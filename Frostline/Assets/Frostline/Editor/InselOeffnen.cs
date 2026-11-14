using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Menü „Frostline › 6 · Insel öffnen“: öffnet die gebaute Insel und zeigt sie im Scene-Fenster
    /// schräg von oben, damit man sofort die ganze Karte sieht (sie liegt bei 0–4000 m, nicht am Ursprung).
    /// Auch beim Start nutzbar: Unity -projectPath Frostline -executeMethod Frostline.EditorTools.InselOeffnen.Open
    /// </summary>
    public static class InselOeffnen
    {
        const string ScenePath = "Assets/Frostline/Scenes/Frostline_Insel.unity";

        [MenuItem("Frostline/6 · Insel öffnen", priority = 6)]
        public static void Open()
        {
            if (!File.Exists(ScenePath))
            {
                EditorUtility.DisplayDialog("Frostline", "Die Insel ist noch nicht gebaut: Frostline › 4 · Insel erstellen.", "OK");
                return;
            }
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            SceneView view = SceneView.lastActiveSceneView;
            if (view == null) view = EditorWindow.GetWindow<SceneView>();
            // Mitte der 4 × 4 km großen Insel, Blick schräg von Südwesten.
            view.LookAt(new Vector3(2000f, 150f, 2000f), Quaternion.Euler(45f, 35f, 0f), 2400f, false, true);
            view.Focus();
            view.Repaint();
        }
    }
}
