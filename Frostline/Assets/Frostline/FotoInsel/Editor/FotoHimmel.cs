using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Frostline.FotoInsel
{
    /// <summary>
    /// Physikalischer Himmel als HDR-Cubemap: Einfachstreuung nach Nishita (Rayleigh + Mie) über einer
    /// Erdkugel. Ersatz für HDRPs Physically Based Sky, der auf diesem Mac (Metal) schwarz rendert.
    /// Liefert auch die Farbe des Sonnenlichts nach dem Weg durch die Atmosphäre.
    /// </summary>
    public static class FotoHimmel
    {
        const float EarthRadius = 6360000f;
        const float AtmosphereRadius = 6420000f;
        const float RayleighHeight = 8000f;
        const float MieHeight = 1200f;
        const float MieG = 0.76f;
        static readonly Vector3 BetaR = new Vector3(5.802e-6f, 13.558e-6f, 33.1e-6f);
        // Etwas dunstiger als Normalatmosphäre: Sommerdunst über dem Meer.
        const float BetaM = 5e-6f;
        const float CameraHeight = 250f;

        public static float UpperHemisphereLux;

        static bool RaySphere(Vector3 origin, Vector3 dir, float radius, out float tNear, out float tFar)
        {
            float b = Vector3.Dot(origin, dir);
            float c = Vector3.Dot(origin, origin) - radius * radius;
            float d = b * b - c;
            tNear = tFar = 0f;
            if (d < 0f) return false;
            float s = Mathf.Sqrt(d);
            tNear = -b - s;
            tFar = -b + s;
            return true;
        }

        static Vector3 Exp(Vector3 v) { return new Vector3(Mathf.Exp(v.x), Mathf.Exp(v.y), Mathf.Exp(v.z)); }
        static Vector3 Mul(Vector3 a, Vector3 b) { return new Vector3(a.x * b.x, a.y * b.y, a.z * b.z); }

        /// <summary>Durchlässigkeit der Atmosphäre von <paramref name="p"/> in Richtung Sonne (0 im Erdschatten).</summary>
        static Vector3 SunTransmittance(Vector3 p, Vector3 sun, int steps)
        {
            float n0, n1;
            if (RaySphere(p, sun, EarthRadius - 10f, out n0, out n1) && n0 > 0f) return Vector3.zero;
            RaySphere(p, sun, AtmosphereRadius, out n0, out n1);
            float ds = n1 / steps, odR = 0f, odM = 0f;
            for (int i = 0; i < steps; i++)
            {
                float h = (p + sun * (ds * (i + 0.5f))).magnitude - EarthRadius;
                odR += Mathf.Exp(-h / RayleighHeight) * ds;
                odM += Mathf.Exp(-h / MieHeight) * ds;
            }
            return Exp(-(BetaR * odR + Vector3.one * (BetaM * 1.1f * odM)));
        }

        /// <summary>Farbe und Stärke der Sonne am Boden (relativ zu 1 außerhalb der Atmosphäre).</summary>
        public static Vector3 SunColorAtGround(Vector3 sunDirection)
        {
            return SunTransmittance(new Vector3(0f, EarthRadius + CameraHeight, 0f), sunDirection.normalized, 32);
        }

        public const float Turbidity = 2.8f;

        static float Perez(float theta, float gamma, float A, float B, float C, float D, float E)
        {
            float cosTheta = Mathf.Max(Mathf.Cos(theta), 0.02f);
            float cg = Mathf.Cos(gamma);
            return (1f + A * Mathf.Exp(B / cosTheta)) * (1f + C * Mathf.Exp(D * gamma) + E * cg * cg);
        }

        static float Zenith(float[,] m, float T, float ts)
        {
            float t2 = T * T, s3 = ts * ts * ts, s2 = ts * ts;
            float r0 = m[0, 0] * s3 + m[0, 1] * s2 + m[0, 2] * ts + m[0, 3];
            float r1 = m[1, 0] * s3 + m[1, 1] * s2 + m[1, 2] * ts + m[1, 3];
            float r2 = m[2, 0] * s3 + m[2, 1] * s2 + m[2, 2] * ts + m[2, 3];
            return t2 * r0 + T * r1 + r2;
        }

        static readonly float[,] Mx = { { 0.00166f, -0.00375f, 0.00209f, 0f }, { -0.02903f, 0.06377f, -0.03202f, 0.00394f }, { 0.11693f, -0.21196f, 0.06052f, 0.25886f } };
        static readonly float[,] My = { { 0.00275f, -0.00610f, 0.00317f, 0f }, { -0.04214f, 0.08970f, -0.04153f, 0.00516f }, { 0.15346f, -0.26756f, 0.06670f, 0.26688f } };

        /// <summary>Himmelsleuchtdichte nach Preetham (cd/m², lineares sRGB) für eine Blickrichtung über dem Horizont.</summary>
        static Vector3 Preetham(Vector3 dir, Vector3 sun)
        {
            float T = Turbidity;
            float thetaS = Mathf.Acos(Mathf.Clamp(sun.y, -1f, 1f));
            float theta = Mathf.Acos(Mathf.Clamp(dir.y, 0.001f, 1f));
            float gamma = Mathf.Acos(Mathf.Clamp(Vector3.Dot(dir, sun), -1f, 1f));

            float chi = (4f / 9f - T / 120f) * (Mathf.PI - 2f * thetaS);
            float Yz = ((4.0453f * T - 4.9710f) * Mathf.Tan(chi) - 0.2155f * T + 2.4192f) * 1000f;
            float xz = Zenith(Mx, T, thetaS), yz = Zenith(My, T, thetaS);

            float AY = 0.1787f * T - 1.4630f, BY = -0.3554f * T + 0.4275f, CY = -0.0227f * T + 5.3251f, DY = 0.1206f * T - 2.5771f, EY = -0.0670f * T + 0.3703f;
            float Ax = -0.0193f * T - 0.2592f, Bx = -0.0665f * T + 0.0008f, Cx = -0.0004f * T + 0.2125f, Dx = -0.0641f * T - 0.8989f, Ex = -0.0033f * T + 0.0452f;
            float Ay = -0.0167f * T - 0.2608f, By = -0.0950f * T + 0.0092f, Cy = -0.0079f * T + 0.2102f, Dy = -0.0441f * T - 1.6537f, Ey = -0.0109f * T + 0.0529f;

            float Y = Yz * Perez(theta, gamma, AY, BY, CY, DY, EY) / Perez(0f, thetaS, AY, BY, CY, DY, EY);
            float x = xz * Perez(theta, gamma, Ax, Bx, Cx, Dx, Ex) / Perez(0f, thetaS, Ax, Bx, Cx, Dx, Ex);
            float y = yz * Perez(theta, gamma, Ay, By, Cy, Dy, Ey) / Perez(0f, thetaS, Ay, By, Cy, Dy, Ey);

            float X = x / y * Y, Z = (1f - x - y) / y * Y;
            float r = 3.2406f * X - 1.5372f * Y - 0.4986f * Z;
            float g = -0.9689f * X + 1.8758f * Y + 0.0415f * Z;
            float b = 0.0557f * X - 0.2040f * Y + 1.0570f * Z;
            return new Vector3(Mathf.Max(r, 0f), Mathf.Max(g, 0f), Mathf.Max(b, 0f));
        }

        /// <summary>Leuchtdichte (cd/m²) in Blickrichtung: Preetham-Himmel, Sonnenscheibe, darunter dunstiger Boden.</summary>
        public static Color Radiance(Vector3 dir, Vector3 sun)
        {
            Vector3 L;
            if (dir.y >= 0f)
            {
                L = Preetham(dir, sun);
                float mu = Vector3.Dot(dir, sun);
                if (mu > 0.99996f) L += SunColorAtGround(sun) * 60000f;
            }
            else
            {
                // Unter dem Horizont: dunkle Land-/Meeroberfläche, die nach unten aus dem Dunst auftaucht.
                Vector3 horizon = Preetham(new Vector3(dir.x, 0.002f, dir.z).normalized, sun);
                Vector3 sunAtGround = SunColorAtGround(sun) * 128000f;
                var albedo = new Vector3(0.07f, 0.085f, 0.075f);
                Vector3 ground = Mul(albedo, sunAtGround * Mathf.Max(0f, sun.y) + Vector3.one * 15000f) / Mathf.PI;
                L = Vector3.Lerp(horizon, ground, FotoNoise.SmoothStep(0f, 0.12f, -dir.y));
            }
            L.x = Mathf.Min(L.x, 60000f); L.y = Mathf.Min(L.y, 60000f); L.z = Mathf.Min(L.z, 60000f);
            return new Color(L.x, L.y, L.z, 1f);
        }

        static Vector3 FaceDirection(CubemapFace face, float s, float t)
        {
            switch (face)
            {
                case CubemapFace.PositiveX: return new Vector3(1f, t, -s);
                case CubemapFace.NegativeX: return new Vector3(-1f, t, s);
                case CubemapFace.PositiveY: return new Vector3(s, 1f, -t);
                case CubemapFace.NegativeY: return new Vector3(s, -1f, t);
                case CubemapFace.PositiveZ: return new Vector3(s, t, 1f);
                default: return new Vector3(-s, t, -1f);
            }
        }

        /// <summary>Erzeugt die Himmels-Cubemap für die Sonnenrichtung (Richtung zur Sonne hin).</summary>

        /// <summary>Beleuchtungsstärke (lx) der oberen Himmelshalbkugel für einen Sonnenstand (ohne direkte Sonne).</summary>
        public static float HemisphereLux(Vector3 sunDirection)
        {
            Vector3 sun = sunDirection.normalized;
            // Beleuchtungsstärke der oberen Halbkugel (Luminanz · cos θ · dω), für die Lux-Kalibrierung.
            double lux = 0;
            const int nTheta = 48, nPhi = 96;
            for (int i = 0; i < nTheta; i++)
            {
                float theta = (i + 0.5f) / nTheta * Mathf.PI * 0.5f;
                for (int j = 0; j < nPhi; j++)
                {
                    float phi = (j + 0.5f) / nPhi * Mathf.PI * 2f;
                    var d = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    Color c = Radiance(d, sun);
                    float lum = 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
                    lux += lum * Mathf.Cos(theta) * Mathf.Sin(theta) * (Mathf.PI * 0.5f / nTheta) * (Mathf.PI * 2f / nPhi);
                }
            }
            return (float)lux;
        }

        public static Cubemap Create(string path, Vector3 sunDirection, int size, bool flipRows)
        {
            Vector3 sun = sunDirection.normalized;
            var cube = new Cubemap(size, TextureFormat.RGBAHalf, true);
            for (int f = 0; f < 6; f++)
            {
                var face = (CubemapFace)f;
                var pixels = new Color[size * size];
                Parallel.For(0, size, y =>
                {
                    for (int x = 0; x < size; x++)
                    {
                        float s = 2f * (x + 0.5f) / size - 1f;
                        float t = 2f * (y + 0.5f) / size - 1f;
                        if (flipRows) t = -t;
                        pixels[y * size + x] = Radiance(FaceDirection(face, s, t).normalized, sun);
                    }
                });
                cube.SetPixels(pixels, face);
            }
            cube.Apply(true);

            UpperHemisphereLux = HemisphereLux(sun);

            if (AssetDatabase.LoadAssetAtPath<Cubemap>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(cube, path);
            return cube;
        }
    }
}
