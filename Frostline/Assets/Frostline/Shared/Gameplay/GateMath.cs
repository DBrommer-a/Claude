using System;
using System.Numerics;

namespace Frostline.Gameplay
{
    /// <summary>Öffnung eines Gates: Mitte, Blickrichtung (Durchflugrichtung), Breite und Höhe.</summary>
    public struct GateShape
    {
        public Vector3 Center;
        public Quaternion Rotation;
        public float Width;
        public float Height;

        public GateShape(Vector3 center, Quaternion rotation, float width, float height)
        {
            Center = center;
            Rotation = rotation;
            Width = width;
            Height = height;
        }
    }

    public enum GateCrossing
    {
        None,
        /// <summary>Richtig herum durch die Öffnung.</summary>
        Through,
        /// <summary>Durch die Öffnung, aber falsch herum.</summary>
        Backwards,
        /// <summary>Ebene durchquert, aber außerhalb der Öffnung (vorbeigeflogen).</summary>
        Missed,
    }

    public static class GateMath
    {
        /// <summary>
        /// Prüft, ob die Strecke von <paramref name="from"/> nach <paramref name="to"/> (ein Physik-Schritt)
        /// die Gate-Ebene schneidet und wo. Funktioniert auch bei 150 km/h, weil nicht auf Trigger gewartet,
        /// sondern die Bahn zwischen zwei Schritten geprüft wird.
        /// </summary>
        public static GateCrossing Test(in GateShape gate, Vector3 from, Vector3 to, float missMargin = 6f)
        {
            Quaternion inverse = Quaternion.Conjugate(gate.Rotation);
            Vector3 a = Vector3.Transform(from - gate.Center, inverse);
            Vector3 b = Vector3.Transform(to - gate.Center, inverse);

            // Lokales z = Durchflugrichtung. Kein Vorzeichenwechsel = Ebene nicht erreicht.
            if ((a.Z < 0f) == (b.Z < 0f) || Math.Abs(a.Z - b.Z) < 1e-6f) return GateCrossing.None;

            float t = a.Z / (a.Z - b.Z);
            Vector3 hit = a + (b - a) * t;
            float halfWidth = gate.Width * 0.5f;
            float halfHeight = gate.Height * 0.5f;
            bool inside = Math.Abs(hit.X) <= halfWidth && Math.Abs(hit.Y) <= halfHeight;

            if (!inside)
            {
                bool near = Math.Abs(hit.X) <= halfWidth + missMargin && Math.Abs(hit.Y) <= halfHeight + missMargin;
                return near ? GateCrossing.Missed : GateCrossing.None;
            }
            return a.Z < 0f ? GateCrossing.Through : GateCrossing.Backwards;
        }
    }
}
