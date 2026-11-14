using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Zugriff auf HDRP über Namen statt über feste Typen. Grund: Feld- und Menünamen ändern
    /// sich zwischen Unity-6-Versionen. Fehlt etwas, gibt es eine Warnung in der Konsole und
    /// der Rest der Einrichtung läuft weiter, statt dass gar nichts mehr kompiliert.
    /// </summary>
    static class HdrpAccess
    {
        const string Hd = "UnityEngine.Rendering.HighDefinition.";
        const string Core = "UnityEngine.Rendering.";

        public static Type HdType(string name) { return FindType(Hd + name); }
        public static Type CoreType(string name) { return FindType(Core + name); }

        public static bool IsInstalled { get { return HdType("HDRenderPipelineAsset") != null; } }

        public static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        /// <summary>Fügt einem Volume-Profil eine Komponente hinzu und speichert sie im Profil-Asset.</summary>
        public static Object AddVolumeComponent(Object profile, string hdTypeName)
        {
            Type type = HdType(hdTypeName);
            if (type == null)
            {
                Debug.LogWarning("Frostline: HDRP-Typ „" + hdTypeName + "“ nicht gefunden, wird übersprungen.");
                return null;
            }

            MethodInfo add = profile.GetType().GetMethod("Add", new[] { typeof(Type), typeof(bool) });
            if (add == null)
            {
                Debug.LogWarning("Frostline: VolumeProfile.Add nicht gefunden.");
                return null;
            }

            var component = add.Invoke(profile, new object[] { type, false }) as Object;
            if (component == null) return null;
            component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
            component.name = hdTypeName;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        /// <summary>
        /// Setzt einen Wert an einer Volume-Komponente und schaltet dessen Override ein.
        /// Enum-Werte werden als Name übergeben ("Cloudy").
        /// </summary>
        public static bool SetVolume(Object component, string member, object value)
        {
            if (component == null) return false;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = component.GetType();
            try
            {
                FieldInfo field = type.GetField(member, flags);
                if (field != null)
                {
                    object parameter = field.GetValue(component);
                    if (parameter == null) return Missing(component, member);

                    PropertyInfo overrideState = FindProperty(parameter.GetType(), "overrideState");
                    PropertyInfo valueProperty = FindProperty(parameter.GetType(), "value");
                    if (overrideState == null || valueProperty == null) return Missing(component, member);

                    overrideState.SetValue(parameter, true);
                    valueProperty.SetValue(parameter, Convert(valueProperty.PropertyType, value));
                    return true;
                }

                PropertyInfo property = FindProperty(type, member);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(component, Convert(property.PropertyType, value));
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Frostline: „" + type.Name + "." + member + "“ ließ sich nicht setzen: " + Unwrap(e).Message);
                return false;
            }
            return Missing(component, member);
        }

        /// <summary>Setzt ein öffentliches Feld oder eine Property an einem beliebigen Objekt.</summary>
        public static bool SetMember(object target, string member, object value)
        {
            if (target == null) return false;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
            Type type = target.GetType();
            try
            {
                FieldInfo field = type.GetField(member, flags);
                if (field != null)
                {
                    field.SetValue(target, Convert(field.FieldType, value));
                    return true;
                }
                PropertyInfo property = FindProperty(type, member);
                if (property != null && property.CanWrite)
                {
                    property.SetValue(target, Convert(property.PropertyType, value));
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Frostline: „" + type.Name + "." + member + "“ ließ sich nicht setzen: " + Unwrap(e).Message);
                return false;
            }
            Debug.LogWarning("Frostline: „" + type.Name + "." + member + "“ gibt es in dieser HDRP-Version nicht, wird übersprungen.");
            return false;
        }

        /// <summary>
        /// Trägt alle Streuprofile (Diffusion Profiles) unter Assets/ in das HDRP-Standard-Volume ein und
        /// speichert es. Fehlt ein Profil dort, rendert HDRP Laub, Stroh und Pflanzen mit einem fremden
        /// (hautroten) Profil – in jedem frisch gestarteten Unity und im Build. Die automatische
        /// Registrierung von HDRP markiert das Volume nur als geändert und speichert es nicht.
        /// Rückgabe: Anzahl neu eingetragener Profile, -1 bei Fehler.
        /// </summary>
        public static int RegisterDiffusionProfiles()
        {
            Type profileType = HdType("DiffusionProfileSettings");
            Type settingsType = HdType("HDRPDefaultVolumeProfileSettings");
            if (profileType == null || settingsType == null) return -1;

            MethodInfo getSettings = null;
            foreach (MethodInfo m in typeof(UnityEngine.Rendering.GraphicsSettings).GetMethods(BindingFlags.Public | BindingFlags.Static))
                if (m.Name == "GetRenderPipelineSettings" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0) getSettings = m;
            if (getSettings == null) return -1;
            object settings;
            try { settings = getSettings.MakeGenericMethod(settingsType).Invoke(null, null); }
            catch (Exception e)
            {
                Debug.LogWarning("Frostline: HDRP-Standard-Volume nicht lesbar: " + Unwrap(e).Message);
                return -1;
            }
            PropertyInfo profileProperty = settings != null ? FindProperty(settingsType, "volumeProfile") : null;
            var volumeProfile = profileProperty != null ? profileProperty.GetValue(settings) as Object : null;
            if (volumeProfile == null)
            {
                Debug.LogWarning("Frostline: Kein HDRP-Standard-Volume gesetzt; Streuprofile nicht eingetragen.");
                return -1;
            }

            var wanted = new System.Collections.Generic.List<Object>();
            foreach (string guid in AssetDatabase.FindAssets("t:DiffusionProfileSettings", new[] { "Assets" }))
            {
                Object p = AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), profileType);
                if (p != null) wanted.Add(p);
            }

            // Alle DiffusionProfileList-Komponenten des Profils angleichen (manche Profile enthalten sie doppelt).
            var lists = new System.Collections.Generic.List<Object>();
            FieldInfo componentsField = volumeProfile.GetType().GetField("components", BindingFlags.Public | BindingFlags.Instance);
            var components = componentsField != null ? componentsField.GetValue(volumeProfile) as System.Collections.IList : null;
            if (components != null)
                foreach (object c in components)
                    if (c is Object o && c.GetType().Name == "DiffusionProfileList") lists.Add(o);
            if (lists.Count == 0)
            {
                Object added = AddVolumeComponent(volumeProfile, "DiffusionProfileList");
                if (added == null) return -1;
                lists.Add(added);
            }

            int addedCount = 0;
            foreach (Object list in lists)
            {
                FieldInfo field = list.GetType().GetField("diffusionProfiles", BindingFlags.Public | BindingFlags.Instance);
                object parameter = field != null ? field.GetValue(list) : null;
                PropertyInfo valueProperty = parameter != null ? FindProperty(parameter.GetType(), "value") : null;
                PropertyInfo overrideState = parameter != null ? FindProperty(parameter.GetType(), "overrideState") : null;
                if (valueProperty == null || overrideState == null)
                {
                    Missing(list, "diffusionProfiles");
                    return -1;
                }

                var all = new System.Collections.Generic.List<Object>();
                if (valueProperty.GetValue(parameter) is Array current)
                    foreach (object o in current)
                        if (o is Object u && u != null && !all.Contains(u)) all.Add(u);
                int before = all.Count;
                // HDRP erlaubt 15 eigene Profile; die Projekt-Profile haben Vorrang vor den mitgelieferten.
                foreach (Object p in wanted)
                    if (!all.Contains(p)) all.Insert(0, p);
                while (all.Count > 15) all.RemoveAt(all.Count - 1);
                if (all.Count == before) continue;

                Array array = Array.CreateInstance(profileType, all.Count);
                for (int i = 0; i < all.Count; i++) array.SetValue(all[i], i);
                overrideState.SetValue(parameter, true);
                valueProperty.SetValue(parameter, array);
                EditorUtility.SetDirty(list);
                addedCount = Math.Max(addedCount, all.Count - before);
            }
            if (addedCount > 0)
            {
                EditorUtility.SetDirty(volumeProfile);
                AssetDatabase.SaveAssets();
                Debug.Log("Frostline: " + addedCount + " Streuprofil(e) im HDRP-Standard-Volume eingetragen.");
            }
            return addedCount;
        }

        public static bool InvokeStatic(Type type, string method, params object[] arguments)
        {
            if (type == null) return false;
            MethodInfo info = FindStaticMethod(type, method, arguments);
            if (info == null) return false;
            try
            {
                info.Invoke(null, arguments);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Frostline: " + type.Name + "." + method + " fehlgeschlagen: " + Unwrap(e).Message);
                return false;
            }
        }

        // Sucht die Überladung, deren Parameter zu den Argumenten passen. Ab HDRP 17.6 gibt es z. B.
        // AddHDLight(GameObject, LightType) und die veraltete AddHDLight(GameObject, HDLightTypeAndShape);
        // GetMethod(name) wirft dann eine AmbiguousMatchException.
        static MethodInfo FindStaticMethod(Type type, string name, object[] arguments)
        {
            object[] args = arguments ?? new object[0];
            foreach (MethodInfo candidate in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (candidate.Name != name) continue;
                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters.Length != args.Length) continue;
                bool fits = true;
                for (int i = 0; i < parameters.Length && fits; i++)
                {
                    Type expected = parameters[i].ParameterType;
                    fits = args[i] == null
                        ? !expected.IsValueType || Nullable.GetUnderlyingType(expected) != null
                        : expected.IsInstanceOfType(args[i]);
                }
                if (fits) return candidate;
            }
            return null;
        }

        static PropertyInfo FindProperty(Type type, string name)
        {
            // Von der abgeleiteten Klasse zur Basis suchen; vermeidet Mehrdeutigkeiten bei überschriebenen Properties.
            for (Type t = type; t != null; t = t.BaseType)
            {
                PropertyInfo property = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (property != null) return property;
            }
            return null;
        }

        static object Convert(Type target, object value)
        {
            if (value == null) return null;
            if (target.IsInstanceOfType(value)) return value;
            if (target.IsEnum)
            {
                var text = value as string;
                return text != null ? Enum.Parse(target, text) : Enum.ToObject(target, value);
            }
            return System.Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture);
        }

        static bool Missing(Object component, string member)
        {
            Debug.LogWarning("Frostline: „" + component.GetType().Name + "." + member + "“ gibt es in dieser HDRP-Version nicht, wird übersprungen.");
            return false;
        }

        static Exception Unwrap(Exception e)
        {
            return e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
        }
    }
}
