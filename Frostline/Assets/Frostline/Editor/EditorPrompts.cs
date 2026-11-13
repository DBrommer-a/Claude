using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Dialoge, die im Hintergrundmodus (Unity -batchmode, z. B. per Skript auf dem Mac)
    /// nicht blockieren: Dort wird nur ins Log geschrieben und „OK“ angenommen.
    /// </summary>
    public static class EditorPrompts
    {
        public static bool Batch { get { return Application.isBatchMode; } }

        public static bool Dialog(string title, string message, string ok)
        {
            if (Batch)
            {
                Debug.Log("[" + title + "] " + message);
                return true;
            }
            return EditorUtility.DisplayDialog(title, message, ok);
        }

        public static bool Dialog(string title, string message, string ok, string cancel)
        {
            if (Batch)
            {
                Debug.Log("[" + title + "] " + message + " → " + ok);
                return true;
            }
            return EditorUtility.DisplayDialog(title, message, ok, cancel);
        }

        public static bool SaveScenes()
        {
            return Batch || EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
        }
    }
}
