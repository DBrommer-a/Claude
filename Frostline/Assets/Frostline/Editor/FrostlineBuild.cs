using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Menü „Frostline > 3 · Mac-Build erstellen“: baut das Spiel für Apple Silicon.
    /// Nur im Build ist die fps-Messung aussagekräftig; der Editor bremst auf demselben Mac.
    /// </summary>
    public static class FrostlineBuild
    {
        const string OutputPath = "Builds/Frostline.app";

        [MenuItem("Frostline/3 · Mac-Build erstellen", priority = 3)]
        public static void BuildMac()
        {
            var scenes = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && File.Exists(scene.path)) scenes.Add(scene.path);
            }
            if (scenes.Count == 0)
            {
                EditorUtility.DisplayDialog("Frostline", "Keine Szene im Build. Bitte zuerst „Frostline > 2 · Prototyp-Szene erstellen“ ausführen.", "OK");
                return;
            }

            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneOSX)
            {
                bool switchTarget = EditorUtility.DisplayDialog("Frostline",
                    "Die Build-Plattform wird auf macOS umgestellt. Das kann beim ersten Mal einige Minuten dauern.", "Weiter", "Abbrechen");
                if (!switchTarget) return;
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Standalone, BuildTarget.StandaloneOSX);
            }

            PreferAppleSilicon();

            var options = new BuildPlayerOptions
            {
                scenes = scenes.ToArray(),
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneOSX,
                // Kein Development Build: der misst langsamer und verfälscht das 60-fps-Gate.
                options = BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                string full = Path.GetFullPath(OutputPath);
                EditorUtility.DisplayDialog("Frostline",
                    "Build fertig (" + (summary.totalSize / (1024 * 1024)) + " MB, " + summary.totalTime.TotalMinutes.ToString("0.0") + " min).\n\n"
                    + full + "\n\nStarten, Qualitätsstufe „High“ lassen, F9 drücken und 60 Sekunden warten. "
                    + "Der Bericht liegt danach im Ordner Benchmarks (Pfad steht im Menü unter „Benchmark“).", "OK");
                EditorUtility.RevealInFinder(full);
            }
            else
            {
                EditorUtility.DisplayDialog("Frostline", "Build fehlgeschlagen: " + summary.result + ". Details stehen in der Konsole.", "OK");
            }
        }

        /// <summary>
        /// Stellt den Mac-Build auf Apple Silicon (ARM64). Die Einstellung liegt in einem
        /// Plattform-Modul, das nur mit installiertem Mac-Build-Support geladen ist.
        /// </summary>
        static void PreferAppleSilicon()
        {
            Type settings = HdrpAccess.FindType("UnityEditor.OSXStandalone.UserBuildSettings");
            PropertyInfo architecture = settings != null ? settings.GetProperty("architecture", BindingFlags.Public | BindingFlags.Static) : null;
            if (architecture == null)
            {
                Debug.Log("Frostline: Mac-Architektur nicht gefunden; Unity nimmt die Voreinstellung.");
                return;
            }
            try
            {
                architecture.SetValue(null, Enum.Parse(architecture.PropertyType, "ARM64"));
            }
            catch (Exception e)
            {
                Debug.Log("Frostline: Mac-Architektur nicht gesetzt: " + e.Message);
            }
        }
    }
}
