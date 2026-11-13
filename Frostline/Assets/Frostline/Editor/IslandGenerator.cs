using UnityEngine;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Höhenfunktion für das Prototyp-Terrain: eine 2 × 2 km große Insel mit Strand im Süden,
    /// Hügeln und einem vergletscherten Gebirgszug im Norden, der in die Wolken reicht.
    /// Platzhalter, bis die echten Gratis-Heightmaps und Megascans-Felsen kommen.
    /// </summary>
    public static class IslandGenerator
    {
        public const float TileSize = 2000f;
        /// <summary>Tiefster Punkt des Meeresbodens (Terrain-Unterkante).</summary>
        public const float SeabedDepth = -100f;
        /// <summary>Höhenspanne des Terrains.</summary>
        public const float HeightRange = 1150f;
        public const float CoastRadius = 760f;

        static readonly Vector2 Center = new Vector2(TileSize * 0.5f, TileSize * 0.5f);

        /// <summary>Höhe über dem Meeresspiegel (y = 0) an einer Stelle des Tiles, in Metern.</summary>
        public static float Height(float x, float z)
        {
            float cx = x - Center.x;
            float cz = z - Center.y;

            // Küstenlinie mit Buchten: Radius wird von Rauschen verbogen.
            float warp = (Fbm(x * 0.0011f + 17.3f, z * 0.0011f - 4.1f, 3) - 0.5f) * 420f;
            float r = Mathf.Sqrt(cx * cx + cz * cz) + warp;
            float d = r / CoastRadius; // 0 Mitte, 1 Küste

            // Grundform: flacher Strand, dann sanft ansteigendes Land, außen der Meeresboden.
            float shore;
            if (d >= 1f)
            {
                shore = Mathf.Lerp(-1.5f, SeabedDepth + 8f, SmoothStep(1f, 1.45f, d));
            }
            else
            {
                float beach = SmoothStep(1f, 0.93f, d) * 5f;
                float inland = SmoothStep(0.93f, 0.35f, d) * 45f;
                shore = -1.5f + beach + inland;
            }

            // Hügel überall an Land.
            float landMask = SmoothStep(0.98f, 0.8f, d);
            float hills = (Fbm(x * 0.004f, z * 0.004f, 4) - 0.35f) * 70f * landMask;

            // Gebirge im Norden: Grate aus „ridged“ Rauschen, nach Süden auslaufend.
            float north = SmoothStep(-350f, 450f, cz);
            float mountainMask = SmoothStep(0.95f, 0.5f, d) * north;
            float ridges = Ridged(x * 0.0019f + 3.7f, z * 0.0019f + 9.2f, 5);
            float mountains = Mathf.Pow(ridges, 1.3f) * 1000f * mountainMask;

            float h = shore + Mathf.Max(hills, -10f) + mountains;
            return Mathf.Clamp(h, SeabedDepth + 1f, SeabedDepth + HeightRange - 1f);
        }

        /// <summary>Höhenfeld für TerrainData.SetHeights (Zeile = z, Spalte = x), normiert auf 0..1.</summary>
        public static float[,] BuildHeightmap(int resolution, System.Action<float> progress)
        {
            var heights = new float[resolution, resolution];
            float step = TileSize / (resolution - 1);
            for (int row = 0; row < resolution; row++)
            {
                float z = row * step;
                for (int col = 0; col < resolution; col++)
                {
                    float h = Height(col * step, z);
                    heights[row, col] = (h - SeabedDepth) / HeightRange;
                }
                if (progress != null && (row & 63) == 0) progress((float)row / resolution);
            }
            return heights;
        }

        /// <summary>
        /// Verteilt vier Bodenschichten nach Höhe und Steilheit:
        /// 0 Sand, 1 Kies und Geröll, 2 Fels, 3 Schnee.
        /// </summary>
        public static float[,,] BuildSplatmap(float[,] heights, int resolution)
        {
            int heightRes = heights.GetLength(0);
            var splat = new float[resolution, resolution, 4];
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

                    float x = u * TileSize;
                    float z = v * TileSize;
                    float jitter = (Mathf.PerlinNoise(x * 0.02f, z * 0.02f) - 0.5f) * 6f;

                    float sand = 1f - SmoothStep(3f, 9f, h + jitter);
                    float rock = SmoothStep(28f, 42f, slope + jitter);
                    float snow = SmoothStep(25f, 70f, h + jitter * 4f) * (1f - SmoothStep(30f, 45f, slope));
                    float gravel = 1f;

                    // Reihenfolge der Vorrangs: Sand am Wasser, darüber Fels an Steilhängen, sonst Schnee.
                    float wSand = sand;
                    float rest = 1f - wSand;
                    float wRock = rest * rock;
                    rest -= wRock;
                    float wSnow = rest * snow;
                    rest -= wSnow;
                    float wGravel = rest * gravel;

                    splat[row, col, 0] = wSand;
                    splat[row, col, 1] = wGravel;
                    splat[row, col, 2] = wRock;
                    splat[row, col, 3] = wSnow;
                }
            }
            return splat;
        }

        /// <summary>Sucht vom Südrand aus den ersten Punkt am Strand, der sicher über dem Wasser liegt.</summary>
        public static Vector3 FindBeachSpawn(float x)
        {
            for (float z = 0f; z < TileSize; z += 1f)
            {
                float h = Height(x, z);
                if (h > 2.5f)
                {
                    // Ein Stück weiter ins Land, damit die Drohne nicht direkt an der Wasserkante steht.
                    float inland = z + 25f;
                    return new Vector3(x, Height(x, inland), inland);
                }
            }
            return new Vector3(x, 5f, TileSize * 0.5f);
        }

        static float Fbm(float x, float z, int octaves)
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

        static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
