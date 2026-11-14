namespace Frostline.FotoInsel
{
    /// <summary>
    /// Gradientenrauschen (Perlin) ohne Unity-API, damit die Höhenberechnung parallel laufen darf.
    /// Werte grob in [-1, 1]; Fbm normiert, Ridged in [0, 1].
    /// </summary>
    public static class FotoNoise
    {
        static readonly int[] P = BuildPermutation(20261114);

        static int[] BuildPermutation(int seed)
        {
            var random = new System.Random(seed);
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int j = random.Next(i + 1);
                int t = p[i]; p[i] = p[j]; p[j] = t;
            }
            var q = new int[512];
            for (int i = 0; i < 512; i++) q[i] = p[i & 255];
            return q;
        }

        static float Fade(float t) { return t * t * t * (t * (t * 6f - 15f) + 10f); }
        static float Lerp(float a, float b, float t) { return a + (b - a) * t; }

        static float Grad(int h, float x, float y)
        {
            switch (h & 7)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                case 3: return -x - y;
                case 4: return x;
                case 5: return -x;
                case 6: return y;
                default: return -y;
            }
        }

        public static float Perlin(float x, float y)
        {
            int xi = (int)System.Math.Floor(x);
            int yi = (int)System.Math.Floor(y);
            float xf = x - xi, yf = y - yi;
            xi &= 255; yi &= 255;
            float u = Fade(xf), v = Fade(yf);
            int a = P[xi] + yi, b = P[xi + 1] + yi;
            float x1 = Lerp(Grad(P[a], xf, yf), Grad(P[b], xf - 1f, yf), u);
            float x2 = Lerp(Grad(P[a + 1], xf, yf - 1f), Grad(P[b + 1], xf - 1f, yf - 1f), u);
            return Lerp(x1, x2, v);
        }

        public static float Fbm(float x, float y, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Perlin(x + i * 17.31f, y - i * 9.73f) * amp;
                norm += amp;
                amp *= gain;
                x *= lacunarity; y *= lacunarity;
            }
            return sum / norm;
        }

        /// <summary>Ridged Multifractal: scharfe Grate, weiche Täler, wie verwittertes Gebirge.</summary>
        public static float Ridged(float x, float y, int octaves, float lacunarity = 2.05f, float gain = 0.5f)
        {
            float sum = 0f, amp = 0.5f, weight = 1f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - System.Math.Abs(Perlin(x + i * 31.7f, y + i * 11.3f));
                n *= n;
                n *= weight;
                weight = n * 2f;
                if (weight > 1f) weight = 1f;
                if (weight < 0f) weight = 0f;
                sum += n * amp;
                norm += amp;
                amp *= gain;
                x *= lacunarity; y *= lacunarity;
            }
            return sum / norm;
        }

        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = (x - edge0) / (edge1 - edge0);
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            return t * t * (3f - 2f * t);
        }

        /// <summary>Deterministischer Hash in [0, 1) für Rasterzellen (Zufall ohne Zustand, threadsicher).</summary>
        public static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }
}
