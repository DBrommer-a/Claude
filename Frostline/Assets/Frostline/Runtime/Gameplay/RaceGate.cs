using Frostline.Gameplay;
using UnityEngine;

namespace Frostline.Gameplay
{
    /// <summary>
    /// Ein Gate. Die Öffnung liegt in der lokalen x/y-Ebene um die Gate-Mitte, durchflogen wird
    /// in Blickrichtung (lokal z). Ob die Drohne hindurch ist, prüft der RaceManager anhand der
    /// Flugbahn, nicht mit Triggern, damit auch schnelle Durchflüge sicher zählen.
    /// </summary>
    public sealed class RaceGate : MonoBehaviour
    {
        [Tooltip("Lichte Breite der Öffnung in m.")]
        public float width = 3f;
        [Tooltip("Lichte Höhe der Öffnung in m.")]
        public float height = 2f;
        [Tooltip("Mitte der Öffnung relativ zum Gate-Ursprung.")]
        public Vector3 openingOffset = new Vector3(0f, 1.3f, 0f);

        public enum State { Idle, Next, Passed }

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int EmissiveColor = Shader.PropertyToID("_EmissiveColor");
        // Higgsfield-Gates kommen über glTFast; dessen Materialien heißen anders.
        static readonly int GltfBaseColor = Shader.PropertyToID("baseColorFactor");
        static readonly int GltfEmissive = Shader.PropertyToID("emissiveFactor");

        Renderer[] renderers;
        MaterialPropertyBlock block;
        State state = State.Idle;

        public GateShape Shape
        {
            get
            {
                Vector3 center = transform.TransformPoint(openingOffset);
                return new GateShape(center.ToNumerics(), transform.rotation.ToNumerics(), width, height);
            }
        }

        public Vector3 OpeningCenter { get { return transform.TransformPoint(openingOffset); } }

        void Awake()
        {
            renderers = GetComponentsInChildren<Renderer>();
            block = new MaterialPropertyBlock();
            Apply();
        }

        public void SetState(State value)
        {
            if (state == value) return;
            state = value;
            Apply();
        }

        void Apply()
        {
            if (renderers == null) return;
            Color color;
            Color glow;
            switch (state)
            {
                case State.Next:
                    color = new Color(0.1f, 1f, 0.35f);
                    glow = color * 6f;
                    break;
                case State.Passed:
                    color = new Color(0.25f, 0.25f, 0.28f);
                    glow = Color.black;
                    break;
                default:
                    color = new Color(1f, 0.38f, 0.05f);
                    glow = color * 1.5f;
                    break;
            }
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(block);
                block.SetColor(BaseColor, color);
                block.SetColor(EmissiveColor, glow);
                // glTFast multipliziert die Farbe mit der Textur: im Grundzustand weiß, damit die
                // orange-schwarzen Streifen des Modells bleiben; „nächstes Gate“ grün, durchflogen grau.
                block.SetColor(GltfBaseColor, state == State.Idle ? Color.white : color);
                block.SetColor(GltfEmissive, state == State.Idle ? Color.black : glow);
                r.SetPropertyBlock(block);
            }
        }

        void OnDrawGizmos()
        {
            Gizmos.matrix = Matrix4x4.TRS(transform.TransformPoint(openingOffset), transform.rotation, Vector3.one);
            Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(width, height, 0.05f));
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * 2f);
        }
    }
}
