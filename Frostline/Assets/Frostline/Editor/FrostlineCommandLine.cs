using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Einstiegspunkte für Unity im Hintergrundmodus, damit sich alles per Terminal steuern lässt
    /// (siehe tools/mac/frostline.sh). Beispiel:
    /// Unity -batchmode -quit -projectPath Frostline -executeMethod Frostline.EditorTools.FrostlineCommandLine.Setup
    /// Exit-Code 0 = Erfolg, 1 = Fehler.
    /// </summary>
    public static class FrostlineCommandLine
    {
        public static void Setup()
        {
            Run("Projekt einrichten", () =>
            {
                FrostlineProjectSetup.Run();
                return HdrpAccess.IsInstalled;
            });
        }

        public static void BuildPrototype()
        {
            Run("Prototyp-Szene", () =>
            {
                PrototypeSceneBuilder.Build();
                return File.Exists("Assets/Frostline/Scenes/Prototype.unity");
            });
        }

        public static void BuildIsland()
        {
            Run("Insel", () =>
            {
                IslandBuilder.Build();
                return File.Exists("Assets/Frostline/Scenes/Frostline_Insel.unity");
            });
        }

        /// <summary>Optional: -frostlineOutput Pfad/zur/Frostline.app</summary>
        public static void BuildMac()
        {
            string output = Argument("-frostlineOutput", "Builds/Frostline.app");
            Run("Mac-Build", () => FrostlineBuild.BuildMac(output));
        }

        static void Run(string name, Func<bool> action)
        {
            bool ok;
            try
            {
                Debug.Log("Frostline: " + name + " …");
                ok = action();
            }
            catch (Exception e)
            {
                Debug.LogError("Frostline: " + name + " fehlgeschlagen: " + e);
                ok = false;
            }
            Debug.Log("Frostline: " + name + (ok ? " fertig." : " FEHLGESCHLAGEN."));
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }

        static string Argument(string name, string fallback)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return fallback;
        }
    }
}
