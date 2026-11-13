using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace Frostline.View
{
    /// <summary>
    /// Zugriff auf HDRP-Volumes über Namen, damit die Laufzeit-Skripte nicht fest an eine
    /// HDRP-Version gebunden sind. Felder werden einmal gesucht und dann gemerkt.
    /// </summary>
    public static class VolumeAccess
    {
        static FieldInfo weightField;
        static PropertyInfo profileProperty;

        public static void SetWeight(Component volume, float weight)
        {
            if (volume == null) return;
            if (weightField == null) weightField = volume.GetType().GetField("weight", BindingFlags.Public | BindingFlags.Instance);
            if (weightField != null) weightField.SetValue(volume, Mathf.Clamp01(weight));
        }

        /// <summary>
        /// Setzt einen Wert einer Volume-Komponente (z. B. VolumetricClouds.shapeOffset) auf einer
        /// Laufzeit-Kopie des Profils, damit das Asset im Projekt unverändert bleibt.
        /// </summary>
        public static bool SetParameter(Component volume, string componentType, string parameter, object value)
        {
            if (volume == null) return false;
            if (profileProperty == null) profileProperty = volume.GetType().GetProperty("profile", BindingFlags.Public | BindingFlags.Instance);
            object profile = profileProperty != null ? profileProperty.GetValue(volume) : null;
            if (profile == null) return false;

            FieldInfo componentsField = profile.GetType().GetField("components", BindingFlags.Public | BindingFlags.Instance);
            var components = componentsField != null ? componentsField.GetValue(profile) as IList : null;
            if (components == null) return false;

            foreach (object component in components)
            {
                if (component == null || component.GetType().Name != componentType) continue;
                FieldInfo field = component.GetType().GetField(parameter, BindingFlags.Public | BindingFlags.Instance);
                object param = field != null ? field.GetValue(component) : null;
                if (param == null) return false;
                PropertyInfo overrideState = FindProperty(param.GetType(), "overrideState");
                PropertyInfo valueProperty = FindProperty(param.GetType(), "value");
                if (overrideState == null || valueProperty == null) return false;
                try
                {
                    overrideState.SetValue(param, true);
                    valueProperty.SetValue(param, value);
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
            return false;
        }

        static PropertyInfo FindProperty(Type type, string name)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                PropertyInfo property = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (property != null) return property;
            }
            return null;
        }
    }
}
