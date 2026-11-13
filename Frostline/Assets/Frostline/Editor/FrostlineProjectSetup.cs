using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Menü „Frostline > 1 · Projekt einrichten“: stellt alles ein, was man sonst von Hand in
    /// den Project Settings zusammenklicken müsste. Kann beliebig oft erneut ausgeführt werden.
    /// </summary>
    public static class FrostlineProjectSetup
    {
        public const string SettingsFolder = "Assets/Frostline/Settings";

        struct Level
        {
            public string Name;
            public float RenderScalePercent;
            public bool AmbientOcclusion;
            public bool ScreenSpaceReflection;
            public float LodBias;
            public int TextureBudgetMb;
        }

        // Laut Plan: intern mit rund 67 % rendern und mit STP hochskalieren; High ist das Ziel auf dem M4.
        static readonly Level[] Levels =
        {
            new Level { Name = "Low", RenderScalePercent = 50f, AmbientOcclusion = false, ScreenSpaceReflection = false, LodBias = 0.7f, TextureBudgetMb = 1024 },
            new Level { Name = "Medium", RenderScalePercent = 59f, AmbientOcclusion = false, ScreenSpaceReflection = false, LodBias = 1.0f, TextureBudgetMb = 1536 },
            new Level { Name = "High", RenderScalePercent = 67f, AmbientOcclusion = true, ScreenSpaceReflection = false, LodBias = 1.5f, TextureBudgetMb = 2048 },
            new Level { Name = "Ultra", RenderScalePercent = 100f, AmbientOcclusion = true, ScreenSpaceReflection = true, LodBias = 2.0f, TextureBudgetMb = 4096 },
        };

        const int DefaultLevel = 2; // High

        [MenuItem("Frostline/1 · Projekt einrichten", priority = 1)]
        public static void Run()
        {
            if (!HdrpAccess.IsInstalled)
            {
                EditorUtility.DisplayDialog("Frostline",
                    "Das HDRP-Paket ist noch nicht geladen. Warte, bis Unity die Pakete fertig importiert hat, und starte die Einrichtung dann erneut.", "OK");
                return;
            }

            EnsureFolder(SettingsFolder);

            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.productName = "Frostline";
            // Physik mit 500 Hz. Das Spiel setzt den Wert beim Start zusätzlich selbst.
            Time.fixedDeltaTime = FrostlineBootstrap.PhysicsDeltaTime;

            var assets = new List<RenderPipelineAsset>();
            foreach (Level level in Levels) assets.Add(CreatePipelineAsset(level));
            ConfigureQualityLevels(assets);
            GraphicsSettings.defaultRenderPipeline = assets[DefaultLevel];

            bool restartNeeded = EnableBothInputBackends();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string message = "Fertig eingerichtet:\n\n"
                + "• HDRP mit vier Qualitätsstufen (Low, Medium, High, Ultra), Wasser und Volumenwolken aktiv\n"
                + "• Interne Auflösung 67 % auf „High“, hochskaliert mit STP\n"
                + "• Physik mit 500 Hz\n"
                + "• Farbraum Linear\n\n"
                + "Falls Unity jetzt Shader neu kompiliert, kurz warten. Danach: Frostline > 2 · Prototyp-Szene erstellen.";
            if (restartNeeded)
            {
                message += "\n\nWICHTIG: Die Eingabe-Einstellung wurde geändert. Bitte Unity einmal schließen und das Projekt neu öffnen, bevor du Play drückst.";
            }
            EditorUtility.DisplayDialog("Frostline", message, "OK");
        }

        // ------------------------------------------------------------ HDRP-Asset je Stufe

        static RenderPipelineAsset CreatePipelineAsset(Level level)
        {
            string path = SettingsFolder + "/HDRP_" + level.Name + ".asset";
            var asset = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(path);
            if (asset == null)
            {
                asset = (RenderPipelineAsset)ScriptableObject.CreateInstance(HdrpAccess.HdType("HDRenderPipelineAsset"));
                AssetDatabase.CreateAsset(asset, path);
            }

            var serialized = new SerializedObject(asset);
            SerializedProperty settings = serialized.FindProperty("m_RenderPipelineSettings");
            if (settings == null)
            {
                Debug.LogWarning("Frostline: Einstellungen im HDRP-Asset nicht gefunden; bitte Wasser und Wolken von Hand im Asset aktivieren.");
                return asset;
            }

            // Nur was der Plan braucht ist an; alles andere kostet auf dem M4 unnötig Leistung.
            SetBool(settings, "supportWater", true);
            SetBool(settings, "supportVolumetricClouds", true);
            SetBool(settings, "supportVolumetrics", true);
            SetBool(settings, "supportMotionVectors", true);
            SetBool(settings, "supportDecals", true);
            SetBool(settings, "supportSSAO", level.AmbientOcclusion);
            SetBool(settings, "supportSSR", level.ScreenSpaceReflection);
            SetBool(settings, "supportSSRTransparent", false);
            SetBool(settings, "supportSSGI", false);
            SetBool(settings, "supportSubsurfaceScattering", false);
            SetBool(settings, "supportRayTracing", false);

            SerializedProperty dynamic = settings.FindPropertyRelative("dynamicResolutionSettings");
            if (dynamic != null)
            {
                bool scaled = level.RenderScalePercent < 99.5f;
                SetBool(dynamic, "enabled", scaled);
                SetBool(dynamic, "forceResolution", scaled);
                SetFloat(dynamic, "forcedPercentage", level.RenderScalePercent);
                SetFloat(dynamic, "minPercentage", level.RenderScalePercent);
                SetFloat(dynamic, "maxPercentage", 100f);
                PreferStpUpscaler(dynamic);
            }
            else
            {
                Debug.LogWarning("Frostline: Dynamic-Resolution-Einstellungen nicht gefunden.");
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        /// <summary>STP als Upscaler eintragen. Je nach Unity-6-Version ist das eine Namens- oder eine Enum-Liste.</summary>
        static void PreferStpUpscaler(SerializedProperty dynamic)
        {
            SerializedProperty names = dynamic.FindPropertyRelative("advancedUpscalerNames");
            if (names != null && names.isArray)
            {
                names.arraySize = 1;
                names.GetArrayElementAtIndex(0).stringValue = "STP";
            }

            SerializedProperty byPriority = dynamic.FindPropertyRelative("advancedUpscalersByPriority");
            if (byPriority != null && byPriority.isArray)
            {
                const int Stp = 2; // AdvancedUpscalers.STP
                byPriority.arraySize = 1;
                SerializedProperty element = byPriority.GetArrayElementAtIndex(0);
                if (element.propertyType == SerializedPropertyType.Enum) element.intValue = Stp;
                else if (element.propertyType == SerializedPropertyType.Integer) element.intValue = Stp;
            }

            if (names == null && byPriority == null)
            {
                Debug.LogWarning("Frostline: STP-Upscaler ließ sich nicht automatisch eintragen. Bitte im HDRP-Asset unter Dynamic Resolution „STP“ wählen.");
            }
        }

        // ------------------------------------------------------------ Qualitätsstufen

        static void ConfigureQualityLevels(List<RenderPipelineAsset> assets)
        {
            Object[] loaded = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
            if (loaded == null || loaded.Length == 0)
            {
                Debug.LogWarning("Frostline: QualitySettings nicht gefunden.");
                return;
            }

            var quality = new SerializedObject(loaded[0]);
            SerializedProperty levels = quality.FindProperty("m_QualitySettings");
            if (levels == null || !levels.isArray)
            {
                Debug.LogWarning("Frostline: Qualitätsstufen ließen sich nicht einrichten.");
                return;
            }

            levels.arraySize = Levels.Length;
            for (int i = 0; i < Levels.Length; i++)
            {
                SerializedProperty entry = levels.GetArrayElementAtIndex(i);
                SetString(entry, "name", Levels[i].Name);
                SetObject(entry, "customRenderPipeline", assets[i]);
                SetInt(entry, "vSyncCount", 1);
                SetFloat(entry, "lodBias", Levels[i].LodBias);
                SetBool(entry, "streamingMipmapsActive", true);
                SetFloat(entry, "streamingMipmapsMemoryBudget", Levels[i].TextureBudgetMb);
            }

            SerializedProperty current = quality.FindProperty("m_CurrentQuality");
            if (current != null) current.intValue = DefaultLevel;

            SerializedProperty perPlatform = quality.FindProperty("m_PerPlatformDefaultQuality");
            if (perPlatform != null && perPlatform.isArray)
            {
                for (int i = 0; i < perPlatform.arraySize; i++)
                {
                    SerializedProperty second = perPlatform.GetArrayElementAtIndex(i).FindPropertyRelative("second");
                    if (second != null) second.intValue = DefaultLevel;
                }
            }

            quality.ApplyModifiedPropertiesWithoutUndo();
            QualitySettings.SetQualityLevel(DefaultLevel, true);
        }

        // ------------------------------------------------------------ Eingabe

        /// <summary>
        /// Stellt „Active Input Handling“ auf „Both“. Der Sender läuft über das neue Input
        /// System; das Prototyp-Menü (IMGUI) bekommt Mausklicks aber nur über das alte.
        /// </summary>
        static bool EnableBothInputBackends()
        {
            Object[] loaded = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (loaded == null || loaded.Length == 0) return false;

            var player = new SerializedObject(loaded[0]);
            SerializedProperty handler = player.FindProperty("activeInputHandler");
            if (handler == null)
            {
                Debug.LogWarning("Frostline: Bitte unter Project Settings > Player > Active Input Handling „Both“ wählen.");
                return false;
            }

            const int Both = 2;
            if (handler.intValue == Both) return false;
            handler.intValue = Both;
            player.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        // ------------------------------------------------------------ Helfer

        public static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string leaf = Path.GetFileName(assetPath);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        static SerializedProperty Find(SerializedProperty parent, string name)
        {
            SerializedProperty property = parent.FindPropertyRelative(name);
            if (property == null) Debug.LogWarning("Frostline: Einstellung „" + name + "“ gibt es in dieser Unity-Version nicht, wird übersprungen.");
            return property;
        }

        static void SetBool(SerializedProperty parent, string name, bool value)
        {
            SerializedProperty property = Find(parent, name);
            if (property != null && property.propertyType == SerializedPropertyType.Boolean) property.boolValue = value;
        }

        static void SetFloat(SerializedProperty parent, string name, float value)
        {
            SerializedProperty property = Find(parent, name);
            if (property == null) return;
            if (property.propertyType == SerializedPropertyType.Float) property.floatValue = value;
            else if (property.propertyType == SerializedPropertyType.Integer) property.intValue = Mathf.RoundToInt(value);
        }

        static void SetInt(SerializedProperty parent, string name, int value)
        {
            SerializedProperty property = Find(parent, name);
            if (property != null && property.propertyType == SerializedPropertyType.Integer) property.intValue = value;
        }

        static void SetString(SerializedProperty parent, string name, string value)
        {
            SerializedProperty property = Find(parent, name);
            if (property != null && property.propertyType == SerializedPropertyType.String) property.stringValue = value;
        }

        static void SetObject(SerializedProperty parent, string name, Object value)
        {
            SerializedProperty property = Find(parent, name);
            if (property != null && property.propertyType == SerializedPropertyType.ObjectReference) property.objectReferenceValue = value;
        }
    }
}
