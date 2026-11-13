using UnityEngine;

namespace Frostline.UI
{
    /// <summary>
    /// Ladebildschirm beim Start: deckt die ersten Bilder ab, in denen Terrain, Wolken und
    /// Shader noch hochfahren, und blendet dann weich aus.
    /// </summary>
    public sealed class StartupScreen : MonoBehaviour
    {
        public Texture2D image;
        public float minimumSeconds = 2.5f;
        public float fadeSeconds = 0.8f;

        float start;
        int framesAfterMinimum;
        GUIStyle title, hint;

        void Start()
        {
            start = Time.realtimeSinceStartup;
        }

        void Update()
        {
            if (Time.realtimeSinceStartup - start > minimumSeconds) framesAfterMinimum++;
        }

        void OnGUI()
        {
            float elapsed = Time.realtimeSinceStartup - start;
            // Erst ausblenden, wenn auch nach der Mindestzeit ein paar Bilder ohne Hänger kamen.
            float fade = framesAfterMinimum > 10 ? Mathf.Clamp01((elapsed - minimumSeconds) / fadeSeconds) : 0f;
            if (fade >= 1f)
            {
                enabled = false;
                return;
            }
            if (title == null)
            {
                title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.09f), fontStyle = FontStyle.Bold };
                title.normal.textColor = Color.white;
                hint = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(Screen.height * 0.025f) };
                hint.normal.textColor = new Color(1f, 1f, 1f, 0.8f);
            }

            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 1f - fade);
            GUI.depth = -1000;
            var full = new Rect(0f, 0f, Screen.width, Screen.height);
            GUI.DrawTexture(full, Texture2D.whiteTexture, ScaleMode.StretchToFill, false, 0f, new Color(0.02f, 0.03f, 0.05f, 1f - fade), 0f, 0f);
            if (image != null) GUI.DrawTexture(full, image, ScaleMode.ScaleAndCrop);
            float x = Screen.width * 0.06f;
            GUI.Label(new Rect(x, Screen.height * 0.36f, Screen.width * 0.5f, Screen.height * 0.12f), "FROSTLINE", title);
            GUI.Label(new Rect(x, Screen.height * 0.49f, Screen.width * 0.5f, Screen.height * 0.05f), "Insel wird geladen …", hint);
            GUI.color = previous;
        }
    }
}
