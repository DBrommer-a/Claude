using Frostline.Flight;
using UnityEngine;

namespace Frostline.Gameplay
{
    /// <summary>
    /// Kartenrand wie bei echtem Analog-FPV: Je weiter weg, desto stärker rauscht das Bild,
    /// bis „SIGNAL VERLOREN“ kommt und die Drohne am letzten Checkpoint neu startet.
    /// Unsichtbare Wände gibt es nicht.
    /// </summary>
    public sealed class SignalBoundary : MonoBehaviour
    {
        public DroneController drone;
        public DroneRespawn respawn;
        public Vector2 center = new Vector2(2000f, 2000f);
        public float cleanRadius = 2300f;
        public float lostRadius = 2900f;
        public float cleanAltitude = 2200f;
        public float lostAltitude = 3200f;
        [Tooltip("Auch ohne Kartenrand ein wenig analoges Rauschen, wie bei echter Analog-Brille.")]
        [Range(0f, 0.3f)] public float baseNoise = 0.03f;

        readonly SignalModel model = new SignalModel();
        AnalogNoise noise;
        float lostMessageUntil;
        GUIStyle style;

        public float Noise { get { return model.Noise; } }

        void Awake()
        {
            if (drone == null) drone = FindAnyObjectByType<DroneController>();
            if (respawn == null && drone != null) respawn = drone.GetComponent<DroneRespawn>();
            noise = new AnalogNoise();
        }

        void OnDestroy()
        {
            noise.Dispose();
        }

        void Update()
        {
            if (drone == null) return;
            model.CenterX = center.x;
            model.CenterZ = center.y;
            model.CleanRadius = cleanRadius;
            model.LostRadius = lostRadius;
            model.CleanAltitude = cleanAltitude;
            model.LostAltitude = lostAltitude;

            Vector3 p = drone.transform.position;
            if (model.Update(p.x, p.y, p.z, Time.deltaTime))
            {
                lostMessageUntil = Time.unscaledTime + 2f;
                if (respawn != null) respawn.Respawn();
                model.Reset();
            }

            float amount = Mathf.Max(model.Noise, Time.unscaledTime < lostMessageUntil ? 1f : 0f);
            noise.Update(Mathf.Max(amount, baseNoise));
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            noise.Draw();

            if (Time.unscaledTime < lostMessageUntil || model.Noise >= 1f)
            {
                if (style == null)
                {
                    style = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                    style.normal.textColor = Color.white;
                }
                GUI.Label(new Rect(0f, Screen.height * 0.4f, Screen.width, 60f), "SIGNAL VERLOREN", style);
            }
        }
    }

    /// <summary>
    /// Analoges Videorauschen als Bildschirm-Überlagerung: Schnee, wandernde Störstreifen und
    /// zerrissene Zeilen. Läuft auf der CPU in niedriger Auflösung und wird hochskaliert.
    /// </summary>
    public sealed class AnalogNoise : System.IDisposable
    {
        const int Width = 240;
        const int Height = 136;

        readonly Texture2D texture;
        readonly Color32[] pixels = new Color32[Width * Height];
        uint seed = 0x12345678u;
        float rollOffset;
        float amount;

        public AnalogNoise()
        {
            texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "Analog Noise",
            };
        }

        public void Update(float strength)
        {
            amount = Mathf.Clamp01(strength);
            if (amount <= 0.001f) return;

            rollOffset = (rollOffset + Time.unscaledDeltaTime * 40f) % Height;
            for (int y = 0; y < Height; y++)
            {
                // Waagrechter Störbalken, der durchs Bild läuft.
                float band = Mathf.Abs(((y + rollOffset) % Height) - Height * 0.5f) / (Height * 0.5f);
                float bandBoost = Mathf.Pow(1f - band, 6f) * 0.6f;
                // Einzelne Zeilen reißen bei viel Rauschen komplett aus.
                bool torn = Next() < amount * amount * 0.08f;
                float lineAlpha = Mathf.Clamp01(amount * (0.55f + bandBoost) + (torn ? 0.6f : 0f));
                int row = y * Width;
                for (int x = 0; x < Width; x++)
                {
                    byte v = (byte)(Next() * 255f);
                    byte a = (byte)(Mathf.Clamp01(lineAlpha * (0.6f + 0.4f * Next())) * 255f);
                    pixels[row + x] = new Color32(v, v, v, a);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        public void Draw()
        {
            if (amount <= 0.001f) return;
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), texture, ScaleMode.StretchToFill, true);
            if (amount > 0.6f)
            {
                // Kurz vor dem Abriss wird das Bild zusätzlich dunkler und flackert.
                float dark = (amount - 0.6f) / 0.4f * (0.6f + 0.4f * Next());
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture, ScaleMode.StretchToFill,
                    true, 0f, new Color(0f, 0f, 0f, dark * 0.7f), 0f, 0f);
            }
        }

        float Next()
        {
            seed ^= seed << 13;
            seed ^= seed >> 17;
            seed ^= seed << 5;
            return (seed & 0xFFFFFF) / 16777216f;
        }

        public void Dispose()
        {
            if (texture != null) Object.Destroy(texture);
        }
    }
}
