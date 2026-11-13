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
