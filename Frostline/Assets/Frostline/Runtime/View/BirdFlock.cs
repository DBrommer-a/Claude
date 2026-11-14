using UnityEngine;

namespace Frostline.View
{
    /// <summary>
    /// Ein Schwarm Vögel, der um einen Punkt kreist: Möwen über der Küste, Krähen über dem Dorf,
    /// ein Adler am Gipfel. Jeder Vogel fliegt seine eigene, leicht schwankende Bahn, legt sich in
    /// die Kurve und wechselt zwischen Flügelschlag und Gleiten. Kein Kollider, keine Physik.
    /// </summary>
    public sealed class BirdFlock : MonoBehaviour
    {
        public Material material;
        public int count = 8;
        [Tooltip("Spannweite in m.")]
        public float wingspan = 1.2f;
        public float radius = 60f;
        [Tooltip("Höhe der Bahnen über dem Schwarm-Mittelpunkt (min, max) in m.")]
        public Vector2 heightRange = new Vector2(20f, 60f);
        [Tooltip("Fluggeschwindigkeit in m/s.")]
        public float speed = 9f;
        [Tooltip("Flügelschläge pro Sekunde.")]
        public float flapRate = 3f;
        [Tooltip("Anteil der Zeit im Gleitflug (0 = immer schlagen).")]
        [Range(0f, 1f)] public float glide = 0.5f;
        public int seed = 1;

        Transform[] birds;
        Transform[] leftWings;
        Transform[] rightWings;
        float[] angle, orbit, height, phase, wobble;
        float[] direction;

        void Start()
        {
            var random = new System.Random(seed);
            Mesh body = BodyMesh(), wing = WingMesh();
            birds = new Transform[count];
            leftWings = new Transform[count];
            rightWings = new Transform[count];
            angle = new float[count];
            orbit = new float[count];
            height = new float[count];
            phase = new float[count];
            wobble = new float[count];
            direction = new float[count];
            bool clockwise = random.NextDouble() < 0.5;
            for (int i = 0; i < count; i++)
            {
                var bird = new GameObject("Vogel " + (i + 1)).transform;
                bird.SetParent(transform, false);
                bird.localScale = Vector3.one * wingspan * (0.85f + 0.3f * (float)random.NextDouble());
                Part(bird, "Rumpf", body, Vector3.zero, false);
                leftWings[i] = Part(bird, "Flügel links", wing, new Vector3(-0.04f, 0f, 0f), true);
                rightWings[i] = Part(bird, "Flügel rechts", wing, new Vector3(0.04f, 0f, 0f), false);
                birds[i] = bird;
                angle[i] = (float)random.NextDouble() * Mathf.PI * 2f;
                orbit[i] = radius * (0.55f + 0.45f * (float)random.NextDouble());
                height[i] = Mathf.Lerp(heightRange.x, heightRange.y, (float)random.NextDouble());
                phase[i] = (float)random.NextDouble() * 10f;
                wobble[i] = 0.5f + (float)random.NextDouble();
                // Meist kreist der Schwarm gemeinsam in eine Richtung, einzelne andersherum.
                direction[i] = (random.NextDouble() < 0.85 ? clockwise : !clockwise) ? -1f : 1f;
            }
        }

        Transform Part(Transform parent, string name, Mesh mesh, Vector3 offset, bool mirror)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = offset;
            if (mirror) go.transform.localScale = new Vector3(-1f, 1f, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go.transform;
        }

        void Update()
        {
            if (birds == null) return;
            float t = Time.time;
            for (int i = 0; i < birds.Length; i++)
            {
                float r = orbit[i] * (1f + 0.15f * Mathf.Sin(t * 0.07f * wobble[i] + phase[i]));
                angle[i] += direction[i] * speed / Mathf.Max(5f, r) * Time.deltaTime;
                float a = angle[i];
                float h = height[i] + 6f * Mathf.Sin(t * 0.13f * wobble[i] + phase[i]);
                Vector3 p = transform.position + new Vector3(Mathf.Cos(a) * r, h, Mathf.Sin(a) * r);
                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a)) * direction[i];
                // In die Kurve legen, beim Steigen leicht anstellen.
                float climb = Mathf.Cos(t * 0.13f * wobble[i] + phase[i]) * 0.1f;
                Quaternion heading = Quaternion.LookRotation(tangent + Vector3.up * climb, Vector3.up);
                birds[i].SetPositionAndRotation(p, heading * Quaternion.Euler(0f, 0f, direction[i] * 18f));

                // Flügel: Schlagphasen wechseln mit Gleiten (Flügel leicht über der Waagerechten).
                float cycle = Mathf.Repeat(t * 0.25f * wobble[i] + phase[i], 1f);
                bool flapping = cycle > glide;
                float beat = flapping ? Mathf.Sin((t + phase[i]) * flapRate * Mathf.PI * 2f) * 38f : 6f;
                leftWings[i].localRotation = Quaternion.Euler(0f, 0f, -beat);
                rightWings[i].localRotation = Quaternion.Euler(0f, 0f, beat);
            }
        }

        // Einfache, flache Formen; aus der Entfernung zählt die Silhouette.
        static Mesh BodyMesh()
        {
            var m = new Mesh { name = "Vogel_Rumpf" };
            m.vertices = new[]
            {
                new Vector3(0f, 0f, 0.42f), new Vector3(-0.05f, 0.02f, 0f), new Vector3(0.05f, 0.02f, 0f),
                new Vector3(0f, -0.04f, 0.05f), new Vector3(0f, 0f, -0.32f), new Vector3(-0.09f, 0f, -0.4f), new Vector3(0.09f, 0f, -0.4f),
            };
            m.triangles = new[] { 0, 1, 2, 0, 3, 1, 0, 2, 3, 1, 4, 2, 1, 3, 4, 2, 4, 3, 4, 5, 6, 4, 6, 5 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Mesh WingMesh()
        {
            // Flügel nach rechts (+x), Hinterkante geschwungen; beidseitig sichtbar.
            var m = new Mesh { name = "Vogel_Fluegel" };
            Vector3[] v =
            {
                new Vector3(0f, 0f, 0.1f), new Vector3(0.22f, 0.01f, 0.12f), new Vector3(0.5f, 0f, 0.02f),
                new Vector3(0.38f, 0f, -0.12f), new Vector3(0.16f, 0f, -0.14f), new Vector3(0f, 0f, -0.08f),
            };
            m.vertices = v;
            m.triangles = new[] { 0, 1, 5, 1, 4, 5, 1, 2, 4, 2, 3, 4, 5, 1, 0, 5, 4, 1, 4, 2, 1, 4, 3, 2 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
