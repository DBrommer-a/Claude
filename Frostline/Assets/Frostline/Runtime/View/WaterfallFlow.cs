using UnityEngine;

namespace Frostline.View
{
    /// <summary>
    /// Lässt einen Wasserfall fließen und rauschen: verschiebt die Schaumtextur des Wasservorhangs
    /// nach unten und spielt ein tiefes Rauschen (zur Laufzeit erzeugt, kein Audio-Asset nötig).
    /// Der Vorhang hat keinen Kollider: Man kann durch den Wasserfall fliegen.
    /// </summary>
    public sealed class WaterfallFlow : MonoBehaviour
    {
        [Tooltip("Renderer des Wasservorhangs (teilen sich ein Material).")]
        public Renderer curtain;
        [Tooltip("Fließgeschwindigkeit der Textur in Wiederholungen pro Sekunde.")]
        public float speed = 1.1f;
        [Tooltip("Fallhöhe in m; lautere, tiefere Wasserfälle bei größerer Höhe.")]
        public float height = 10f;

        static AudioClip rumble;
        static int lastFrame = -1;
        static readonly int BaseMap = Shader.PropertyToID("_BaseColorMap");

        void Start()
        {
            var audio = gameObject.GetComponent<AudioSource>();
            if (audio == null) audio = gameObject.AddComponent<AudioSource>();
            audio.clip = Rumble();
            audio.loop = true;
            audio.spatialBlend = 1f;
            audio.rolloffMode = AudioRolloffMode.Logarithmic;
            audio.minDistance = Mathf.Clamp(height * 0.8f, 6f, 25f);
            audio.maxDistance = 260f;
            audio.dopplerLevel = 0f;
            audio.volume = Mathf.Clamp(0.35f + height / 60f, 0.35f, 0.9f);
            audio.pitch = Mathf.Clamp(1.15f - height / 120f, 0.75f, 1.15f);
            audio.time = Random.value * audio.clip.length;
            audio.Play();
        }

        void Update()
        {
            // Alle Wasserfälle teilen sich ein Material: einmal pro Bild verschieben genügt.
            if (curtain == null || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount;
            Material m = curtain.sharedMaterial;
            if (m != null && m.HasProperty(BaseMap)) m.SetTextureOffset(BaseMap, new Vector2(0f, Time.time * speed));
        }

        /// <summary>Tiefes, breites Rauschen: braunes Rauschen mit langsam schwankender Stärke, nahtlos geloopt.</summary>
        static AudioClip Rumble()
        {
            if (rumble != null) return rumble;
            const int rate = 44100;
            const int seconds = 6;
            int n = rate * seconds;
            var data = new float[n];
            var random = new System.Random(7);
            float brown = 0f, low = 0f;
            for (int i = 0; i < n; i++)
            {
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                brown = Mathf.Clamp(brown + white * 0.02f, -1f, 1f) * 0.998f;
                low += (white - low) * 0.18f;
                float swell = 0.85f + 0.15f * Mathf.Sin(i * Mathf.PI * 2f * 3f / n);
                data[i] = (brown * 2.2f + low * 0.35f) * swell;
            }
            // Ende in den Anfang überblenden, damit die Schleife nicht knackt.
            int fade = rate / 2;
            for (int i = 0; i < fade; i++)
            {
                float t = (float)i / fade;
                data[i] = data[i] * t + data[n - fade + i] * (1f - t);
            }
            var clip = AudioClip.Create("Wasserfall-Rauschen", n - fade, 1, rate, false);
            var trimmed = new float[n - fade];
            System.Array.Copy(data, trimmed, n - fade);
            clip.SetData(trimmed, 0);
            rumble = clip;
            return clip;
        }
    }
}
