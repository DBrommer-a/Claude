using System;
using UnityEngine;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Höhenmodell der ganzen Insel: 4 × 4 km, vier Terrain-Tiles à 2 × 2 km. Die Zonen aus dem
    /// Plan bekommen ebene Flächen (Industriegebiet, Stadt, Dörfer, Hafen), im Norden liegen die
    /// Gletscherberge mit einem Tal, in dem die Eiszunge mit dem Eistunnel liegt.
    /// </summary>
    public static class IslandShape
    {
        public const float Size = 4000f;
        public const float TileSize = 2000f;
        public const float SeabedDepth = -110f;
        public const float HeightRange = 1450f;

        public static readonly Vector2 Center = new Vector2(2000f, 2000f);
        const float CoastRadius = 1650f;

        public struct Zone
        {
            public string Name;
            public Vector2 Position;
            public float Radius;
            public float Height;
            public float Blend;

            public Zone(string name, float x, float z, float radius, float height, float blend)
            {
                Name = name;
                Position = new Vector2(x, z);
                Radius = radius;
                Height = height;
                Blend = blend;
            }

            public Vector3 Ground { get { return new Vector3(Position.x, Height, Position.y); } }
        }

        // Ebene Flächen für die Zonen (Mitte, Radius, Höhe, Übergang).
        public static readonly Zone Industry = new Zone("Industriegebiet", 3100f, 1800f, 270f, 16f, 160f);
        public static readonly Zone City = new Zone("Stadt", 2350f, 1350f, 300f, 18f, 160f);
        public static readonly Zone ValleyVillage = new Zone("Tal-Dorf", 1350f, 1700f, 170f, 32f, 140f);
        public static readonly Zone MountainVillage = new Zone("Bergdorf", 1200f, 2200f, 140f, 280f, 150f);
        public static readonly Zone Harbor = new Zone("Hafen", 3080f, 900f, 200f, 3f, 120f);
        public static readonly Zone FishingVillage = new Zone("Fischerdorf", 3330f, 1120f, 110f, 6f, 90f);

        static readonly Zone[] Zones = { Industry, City, ValleyVillage, MountainVillage, Harbor, FishingVillage };

        // Gletschertal: von hoch im Norden nach Süden hinab.
        public static readonly Vector2 GlacierTop = new Vector2(2050f, 2950f);
        public static readonly Vector2 GlacierFoot = new Vector2(2120f, 2150f);
        public const float GlacierTopHeight = 640f;
        public const float GlacierFootHeight = 110f;
        const float GlacierHalfWidth = 120f;

        public static readonly Vector2 Forest = new Vector2(780f, 1900f);
        public const float ForestRadius = 380f;

        /// <summary>Höhe über dem Meer an einer Stelle der Insel.</summary>
        public static float Height(float x, float z)
        {
            float cx = x - Center.x;
            float cz = z - Center.y;
            float warp = (Fbm(x * 0.0007f + 17.3f, z * 0.0007f - 4.1f, 3) - 0.5f) * 650f;
            float r = Mathf.Sqrt(cx * cx + cz * cz) + warp;
            float d = r / CoastRadius;

            float shore;
            if (d >= 1f)
            {
                shore = Mathf.Lerp(-1.5f, SeabedDepth + 8f, SmoothStep(1f, 1.3f, d));
            }
            else
            {
                float beach = SmoothStep(1f, 0.95f, d) * 5f;
                float inland = SmoothStep(0.95f, 0.4f, d) * 50f;
                shore = -1.5f + beach + inland;
            }

            float landMask = SmoothStep(0.99f, 0.85f, d);
            float hills = (Fbm(x * 0.0028f, z * 0.0028f, 4) - 0.35f) * 110f * landMask;

            // Gebirge im Norden und ein Höhenzug im Westen hinter dem Wald.
            float north = SmoothStep(-150f, 700f, cz);
            float west = SmoothStep(-500f, -1100f, cx) * SmoothStep(-700f, 300f, cz) * 0.45f;
            float mountainMask = SmoothStep(0.95f, 0.5f, d) * Mathf.Max(north, west);
            float ridges = Ridged(x * 0.0012f + 3.7f, z * 0.0012f + 9.2f, 5);
            float mountains = Mathf.Pow(ridges, 1.35f) * 1300f * mountainMask;

            float h = shore + Mathf.Max(hills, -15f) + mountains;
            h = CarveGlacierValley(x, z, h);

            foreach (Zone zone in Zones)
            {
                float dist = Vector2.Distance(new Vector2(x, z), zone.Position);
                float w = SmoothStep(zone.Radius + zone.Blend, zone.Radius, dist);
                if (w > 0f) h = Mathf.Lerp(h, zone.Height, w);
            }
            return Mathf.Clamp(h, SeabedDepth + 1f, SeabedDepth + HeightRange - 1f);
        }

        /// <summary>Talboden des Gletschers an einer Stelle entlang des Tals (0 = oben, 1 = unten).</summary>
        public static float GlacierFloor(float t)
        {
            return Mathf.Lerp(GlacierTopHeight, GlacierFootHeight, t);
        }

        static float CarveGlacierValley(float x, float z, float h)
        {
            Vector2 axis = GlacierFoot - GlacierTop;
            float length = axis.magnitude;
            Vector2 dir = axis / length;
            Vector2 rel = new Vector2(x, z) - GlacierTop;
            float along = Vector2.Dot(rel, dir);
            float t = Mathf.Clamp01(along / length);
            float across = Mathf.Abs(rel.x * dir.y - rel.y * dir.x);
            if (along < -200f || along > length + 200f) return h;

            float floor = GlacierFloor(t);
            // U-förmiges Gletschertal: flacher Boden, steile Flanken.
            float valley = SmoothStep(GlacierHalfWidth * 2.6f, GlacierHalfWidth, across);
            float ends = SmoothStep(-200f, 0f, along) * SmoothStep(length + 200f, length, along);
            float carved = floor + Mathf.Pow(Mathf.Clamp01(across / (GlacierHalfWidth * 2.6f)), 2f) * Mathf.Max(h - floor, 0f);
            return Mathf.Lerp(h, Mathf.Min(h, carved), valley * ends);
        }

        public static float[,] BuildHeightmap(float originX, float originZ, int resolution, Action<float> progress)
        {
            var heights = new float[resolution, resolution];
            float step = TileSize / (resolution - 1);
            for (int row = 0; row < resolution; row++)
            {
                float z = originZ + row * step;
                for (int col = 0; col < resolution; col++)
                {
                    heights[row, col] = (Height(originX + col * step, z) - SeabedDepth) / HeightRange;
                }
                if (progress != null && (row & 63) == 0) progress((float)row / resolution);
            }
            return heights;
        }

        /// <summary>Bodenschichten: 0 Sand, 1 Kies/Gras, 2 Fels, 3 Schnee, 4 Eis.</summary>
        /// <summary>
        /// Gewichte der Bodenschichten: 0 Sand, 1 Grün, 2 Fels, 3 Schnee, 4 Eis; mit
        /// <paramref name="extraLayers"/> zusätzlich 5 Heide (fleckig in der Tundra) und 6 Kies (Strandübergang).
        /// </summary>
        public static float[,,] BuildSplatmap(float[,] heights, float originX, float originZ, int resolution, bool extraLayers = false)
        {
            int heightRes = heights.GetLength(0);
            var splat = new float[resolution, resolution, extraLayers ? 7 : 5];
            float cell = TileSize / (heightRes - 1);

            for (int row = 0; row < resolution; row++)
            {
                for (int col = 0; col < resolution; col++)
                {
                    float u = (float)col / (resolution - 1);
                    float v = (float)row / (resolution - 1);
                    int hc = Mathf.Clamp(Mathf.RoundToInt(u * (heightRes - 1)), 1, heightRes - 2);
                    int hr = Mathf.Clamp(Mathf.RoundToInt(v * (heightRes - 1)), 1, heightRes - 2);

                    float h = heights[hr, hc] * HeightRange + SeabedDepth;
                    float dx = (heights[hr, hc + 1] - heights[hr, hc - 1]) * HeightRange / (2f * cell);
                    float dz = (heights[hr + 1, hc] - heights[hr - 1, hc]) * HeightRange / (2f * cell);
                    float slope = Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;

                    float x = originX + u * TileSize;
                    float z = originZ + v * TileSize;
                    float jitter = (Mathf.PerlinNoise(x * 0.02f, z * 0.02f) - 0.5f) * 6f;

                    float sand = 1f - SmoothStep(3f, 9f, h + jitter);
                    float rock = SmoothStep(30f, 44f, slope + jitter);
                    float snow = SmoothStep(60f, 140f, h + jitter * 4f) * (1f - SmoothStep(32f, 46f, slope));
                    float ice = GlacierIce(x, z);

                    float wSand = sand;
                    float rest = 1f - wSand;
                    float wIce = rest * ice;
                    rest -= wIce;
                    float wRock = rest * rock;
                    rest -= wRock;
                    float wSnow = rest * snow;
                    rest -= wSnow;

                    float wHeather = 0f, wPebbles = 0f;
                    if (extraLayers)
                    {
                        // Kies am Übergang vom Strand ins Grün, Heide in Flecken auf der Tundra unter ~350 m.
                        float pebble = SmoothStep(2f, 6f, h + jitter) * (1f - SmoothStep(9f, 16f, h + jitter)) * 0.85f;
                        wPebbles = rest * pebble;
                        rest -= wPebbles;
                        float patches = SmoothStep(0.42f, 0.68f, Mathf.PerlinNoise(x * 0.006f + 31.7f, z * 0.006f - 12.3f));
                        float heather = patches * (1f - SmoothStep(260f, 380f, h)) * (1f - SmoothStep(20f, 34f, slope));
                        wHeather = rest * heather;
                        rest -= wHeather;
                    }

                    splat[row, col, 0] = wSand;
                    splat[row, col, 1] = rest;
                    splat[row, col, 2] = wRock;
                    splat[row, col, 3] = wSnow;
                    splat[row, col, 4] = wIce;
                    if (extraLayers)
                    {
                        splat[row, col, 5] = wHeather;
                        splat[row, col, 6] = wPebbles;
                    }
                }
            }
            return splat;
        }

        static float GlacierIce(float x, float z)
        {
            Vector2 axis = GlacierFoot - GlacierTop;
            float length = axis.magnitude;
            Vector2 dir = axis / length;
            Vector2 rel = new Vector2(x, z) - GlacierTop;
            float along = Vector2.Dot(rel, dir);
            float across = Mathf.Abs(rel.x * dir.y - rel.y * dir.x);
            return SmoothStep(GlacierHalfWidth * 1.3f, GlacierHalfWidth * 0.8f, across) * SmoothStep(-250f, -50f, along) * SmoothStep(length + 60f, length - 40f, along);
        }

        /// <summary>Erster Punkt an Land, wenn man von außen auf die Insel zufliegt.</summary>
        public static Vector3 CoastPoint(Vector2 from, Vector2 toward, float minHeight = 1.5f)
        {
            Vector2 dir = (toward - from).normalized;
            float distance = Vector2.Distance(from, toward);
            for (float s = 0f; s < distance; s += 2f)
            {
                Vector2 p = from + dir * s;
                float h = Height(p.x, p.y);
                if (h >= minHeight) return new Vector3(p.x, h, p.y);
            }
            return new Vector3(toward.x, Height(toward.x, toward.y), toward.y);
        }

        public static Vector3 Ground(float x, float z)
        {
            return new Vector3(x, Height(x, z), z);
        }

        public static float Fbm(float x, float z, int octaves)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x, z) * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                x = x * 2.03f + 11.7f;
                z = z * 2.03f - 5.3f;
            }
            return sum / norm;
        }

        static float Ridged(float x, float z, int octaves)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float norm = 0f;
            float weight = 1f;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(Mathf.PerlinNoise(x, z) * 2f - 1f);
                n *= n * weight;
                weight = Mathf.Clamp01(n * 1.6f);
                sum += n * amplitude;
                norm += amplitude;
                amplitude *= 0.5f;
                x = x * 2.1f + 7.9f;
                z = z * 2.1f - 3.4f;
            }
            return sum / norm;
        }

        public static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
