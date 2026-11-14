using System;
using Frostline.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Frostline.FotoInsel
{
    /// <summary>
    /// Verlassene Insel spielbar machen, ohne sie neu zu bauen: zuerst die Atmosphären-Profile
    /// reparieren (ältere Bauten haben sie leer gespeichert), dann Drohne, Spielsysteme und Kollision.
    /// Terminal: tools/mac/frostline.sh spielbar
    /// </summary>
    public static class FotoSpielbar
    {
        [MenuItem("Frostline/10 · Verlassene Insel spielbar machen", priority = 10)]
        public static void Menu()
        {
            if (!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorUtility.DisplayDialog("Frostline", Build(), "OK");
        }

        public static string Build()
        {
            string repaired = FotoAtmosphaere.RepairProfiles(FotoInselBuilder.GeneratedFolder + "/Verlassen", true);
            string result = repaired + VerlasseneInselSpiel.Build();
            if (!VerlasseneInselSpiel.Report.StartsWith("Spielbar")) return result;
            // Lichtstimmungen in die eben gespeicherte, noch offene Szene.
            Scene scene = SceneManager.GetActiveScene();
            result += FotoLichtstimmungen.Add(scene);
            EditorSceneManager.SaveScene(scene);
            return result;
        }

        /// <summary>Für -executeMethod: Exit-Code 0 = spielbar, 1 = Fehler.</summary>
        public static void Spielbar()
        {
            bool ok;
            try
            {
                Debug.Log("Frostline: " + Build().Trim());
                ok = VerlasseneInselSpiel.Report.StartsWith("Spielbar");
            }
            catch (Exception e)
            {
                Debug.LogError("Frostline: Verlassene Insel spielbar fehlgeschlagen: " + e);
                ok = false;
            }
            Debug.Log("Frostline: Verlassene Insel spielbar" + (ok ? " fertig." : " FEHLGESCHLAGEN."));
            if (Application.isBatchMode) EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
