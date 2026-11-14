using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Frostline.FotoInsel
{
    /// <summary>Rechteckige Fläche (Industrie, Stadt) mit Drehung; lokale x-Achse entlang der Längsseite.</summary>
    public sealed class FotoZone
    {
        public string Name;
        public Vector2 Center;
        public Vector2 Half;
        public float Angle;          // Grad, gegen den Uhrzeigersinn in der XZ-Ebene
        public float Elevation;
        public float Blend = 70f;
        public bool Flatten = true;  // Industrie: eingeebnet; Stadt: folgt dem Gelände
        public bool Port;            // reicht ins Meer: aufgeschüttet, Kaimauer statt Böschung
        public bool AutoElevation;
        public Vector2 Gate;         // Zufahrt (Weltkoordinaten)

        public Vector2 ToLocal(Vector2 p)
        {
            Vector2 d = p - Center;
            float a = -Angle * Mathf.Deg2Rad;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            return new Vector2(d.x * c - d.y * s, d.x * s + d.y * c);
        }

        public Vector2 ToWorld(Vector2 l)
        {
            float a = Angle * Mathf.Deg2Rad;
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            return Center + new Vector2(l.x * c - l.y * s, l.x * s + l.y * c);
        }

        /// <summary>Abstand außerhalb des Rechtecks (0 innen).</summary>
        public float Outside(Vector2 p)
        {
            Vector2 l = ToLocal(p);
            float dx = Mathf.Max(Mathf.Abs(l.x) - Half.x, 0f);
            float dy = Mathf.Max(Mathf.Abs(l.y) - Half.y, 0f);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        public bool Contains(Vector2 p, float margin = 0f)
        {
            Vector2 l = ToLocal(p);
            return Mathf.Abs(l.x) <= Half.x + margin && Mathf.Abs(l.y) <= Half.y + margin;
        }

        /// <summary>Drehung für Objekte: lokale x/z des Objekts liegen auf lokaler x/y der Zone.</summary>
        public Quaternion Rotation(float yaw = 0f) { return Quaternion.Euler(0f, -Angle + yaw, 0f); }
    }

    public sealed class FotoRiver
    {
        public readonly List<Vector2> Points = new List<Vector2>();
        public readonly List<float> HalfWidth = new List<float>();
        public readonly List<float> Depth = new List<float>();
        public readonly List<float> Surface = new List<float>();
        public bool ReachesSea;
    }

    public sealed class FotoRoad
    {
        public string Name;
        public readonly List<Vector2> Points = new List<Vector2>();
        public readonly List<float> Elevation = new List<float>();
        public float HalfWidth = 4.5f;
    }

    public struct FotoBridge
    {
        public Vector3 Center;
        public Vector2 Direction;
        public float Length;
        public float Width;
    }

    /// <summary>
    /// Höhenfeld der Foto-Insel: 8 × 8 km, Insel rund 6 km. Grundform mit verzerrter Küste und
    /// Gebirgskamm im Norden, Erosion durch Regentropfen, eingeebnete Industrieflächen, Flussnetz aus
    /// der Abflussmenge (Priority-Flood), Straßen mit Dämmen und Brücken.
    /// </summary>
    public sealed class FotoTerrain
    {
        public const float Size = 8000f;
        public const int Tiles = 4;
        public const float TileSize = Size / Tiles;
        public const int TileRes = 1025;
        public const int Res = (TileRes - 1) * Tiles + 1;          // 4097
        public const float Cell = Size / (Res - 1);                // 1,95 m
        public const int CoarseRes = 1025;
        public const float CoarseCell = Size / (CoarseRes - 1);    // 7,8 m
        const int CoarseStep = (Res - 1) / (CoarseRes - 1);        // 4
        public const float Bottom = -80f;
        public static float HeightRange = 1150f;
        /// <summary>Variante „Verlassene Insel“: Hauptgipfel über den Wolken, Wikingerdorf, Traumstrand, verlassenes Werk.</summary>
        public static bool Verlassen;
        public static readonly Vector2 Peak = new Vector2(4250f, 5450f);
        public const float RiverAreaKm2 = 0.6f;

        public static readonly Vector2 Center = new Vector2(4000f, 4150f);
        static readonly Vector2[] Spine =
        {
            new Vector2(2000f, 5500f), new Vector2(2800f, 6300f), new Vector2(3800f, 6650f),
            new Vector2(4900f, 6350f), new Vector2(5650f, 5650f), new Vector2(6000f, 4950f),
        };

        public float[] H;          // Höhe über Meer, Index z * Res + x
        public byte[] ZoneMask;    // 255 in eingeebneten Flächen
        public byte[] RoadMask;    // 255 auf der Fahrbahn
        public byte[] WaterMask;   // 255 im Flussbett, Ufer abfallend
        float[] coarse;

        public readonly List<FotoZone> Zones = new List<FotoZone>();
        public readonly List<FotoRiver> Rivers = new List<FotoRiver>();
        public readonly List<FotoRoad> Roads = new List<FotoRoad>();
        public readonly List<FotoBridge> Bridges = new List<FotoBridge>();
        public FotoZone Port, Refinery, Steelworks, Town, Beach, Rohbau;
        /// <summary>Index des Hauptflusses (verlassene Insel: vom Berg am Dorf vorbei ins Meer), sonst -1.</summary>
        public int MainRiver = -1;
        Vector2 mainMouth;
        public Vector2 LighthousePoint, ShipPoint, WreckPoint, HutPoint, MastPoint;
        public string Log = "";

        // ------------------------------------------------------------ Ablauf

        public void Generate(System.Action<string, float> progress)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            progress("Grundform und Gebirge", 0.02f);
            coarse = new float[CoarseRes * CoarseRes];
            Parallel.For(0, CoarseRes, j =>
            {
                for (int i = 0; i < CoarseRes; i++)
                    coarse[j * CoarseRes + i] = BaseHeight(i * CoarseCell, j * CoarseCell);
            });
            Log += "Grundform " + watch.ElapsedMilliseconds + " ms\n";

            progress("Industrieflächen und Stadt planen", 0.04f);
            PlanZones();
            ShapeLowlands();
            if (Verlassen) ShapeBeach();
            if (Verlassen) CarveMainValley();

            progress("Erosion (Regentropfen)", 0.06f);
            Erode(coarse, CoarseRes, 420000, 7);
            Log += "Erosion " + watch.ElapsedMilliseconds + " ms\n";

            progress("Feines Höhenfeld", 0.12f);
            H = new float[Res * Res];
            Parallel.For(0, Res, j =>
            {
                float v = j / (float)CoarseStep;
                for (int i = 0; i < Res; i++)
                {
                    float u = i / (float)CoarseStep;
                    float h = SampleCubic(coarse, CoarseRes, u, v);
                    float x = i * Cell, z = j * Cell;
                    if (h > 0.5f)
                    {
                        float rough = FotoNoise.SmoothStep(120f, 650f, h);
                        h += FotoNoise.Fbm(x * 0.025f + 3f, z * 0.025f - 8f, 3) * (0.9f + 3.5f * rough);
                        h += (FotoNoise.Ridged(x * 0.0065f, z * 0.0065f, 3) - 0.35f) * 18f * rough;
                    }
                    H[j * Res + i] = Mathf.Clamp(h, Bottom + 1f, Bottom + HeightRange - 2f);
                }
            });
            Log += "Feinfeld " + watch.ElapsedMilliseconds + " ms\n";

            ZoneMask = new byte[Res * Res];
            RoadMask = new byte[Res * Res];
            WaterMask = new byte[Res * Res];

            progress("Industrieflächen einebnen", 0.16f);
            foreach (FotoZone zone in Zones) FlattenZone(zone);
            Log += "Zonen " + watch.ElapsedMilliseconds + " ms\n";

            progress("Flussnetz (Abfluss)", 0.2f);
            TraceRivers();
            if (Verlassen) FindMainRiver();
            Log += "Flüsse " + Rivers.Count + " in " + watch.ElapsedMilliseconds + " ms\n";

            progress("Straßen und Brücken", 0.24f);
            PlanRoads();
            foreach (FotoRoad road in Roads) CarveRoad(road);
            if (!Verlassen) PaintStreets(Town, 68f, 60f, 3.6f);
            progress("Flussbetten", 0.27f);
            foreach (FotoRiver river in Rivers) CarveRiver(river);
            foreach (FotoRiver river in Rivers) RaiseLevees(river);
            PlanLandmarks();
            Log += "fertig " + watch.ElapsedMilliseconds + " ms\n";
        }

        // ------------------------------------------------------------ Grundform

        static float DistanceToSpine(float x, float z)
        {
            float best = float.MaxValue;
            var p = new Vector2(x, z);
            for (int i = 0; i < Spine.Length - 1; i++)
                best = Mathf.Min(best, DistanceToSegment(p, Spine[i], Spine[i + 1], out _));
            return best;
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b, out float t)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            t = len2 > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return (p - (a + ab * t)).magnitude;
        }

        // Inselkörper aus mehreren Lappen (Haupteiland, Halbinsel im Südwesten, Landzunge im Osten).
        static readonly Vector4[] Lobes =
        {
            new Vector4(4050f, 4450f, 2650f, 2350f),
            new Vector4(2350f, 2350f, 1350f, 1000f),
            new Vector4(6250f, 3250f, 1050f, 1250f),
            new Vector4(4300f, 2150f, 1250f, 900f),
        };

        /// <summary>Küstenmaß: &gt; 0 an Land, 0 an der Küste, &lt; 0 im Meer.</summary>
        public static float CoastValue(float x, float z)
        {
            float wx = x + 650f * FotoNoise.Fbm(x * 0.00026f + 3.1f, z * 0.00026f - 1.7f, 5);
            float wz = z + 650f * FotoNoise.Fbm(x * 0.00026f - 8.4f, z * 0.00026f + 5.2f, 5);
            wx += 140f * FotoNoise.Fbm(x * 0.0011f + 9f, z * 0.0011f, 3);
            wz += 140f * FotoNoise.Fbm(x * 0.0011f, z * 0.0011f + 9f, 3);

            // Glattes Maximum der Lappen, damit die Übergänge wie Landengen aussehen.
            float sum = 0f;
            const float k = 9f;
            foreach (Vector4 lobe in Lobes)
            {
                float dx = (wx - lobe.x) / lobe.z, dz = (wz - lobe.y) / lobe.w;
                float c = 1f - Mathf.Sqrt(dx * dx + dz * dz);
                sum += Mathf.Exp(k * c);
            }
            float value = Mathf.Log(sum) / k;

            // Buchten: zwei tiefe Einschnitte, einer im Westen, einer im Süden.
            value -= 0.32f * Mathf.Exp(-((wx - 1250f) * (wx - 1250f) + (wz - 3950f) * (wz - 3950f)) / (2f * 330f * 330f));
            value -= 0.26f * Mathf.Exp(-((wx - 3250f) * (wx - 3250f) + (wz - 1300f) * (wz - 1300f)) / (2f * 300f * 300f));
            return value;
        }

        /// <summary>0 = flache Küste mit Strand, 1 = Steilküste.</summary>
        static float Cliffness(float x, float z)
        {
            return FotoNoise.SmoothStep(0.05f, 0.35f, FotoNoise.Fbm(x * 0.00055f + 51f, z * 0.00055f - 17f, 3));
        }

        static float BaseHeight(float x, float z)
        {
            float c = CoastValue(x, z);
            float cliff = Cliffness(x, z);
            float h;
            if (c >= 0f)
            {
                float inland = FotoNoise.SmoothStep(0f, 0.55f, c);
                h = 1.2f + c * 55f + inland * 40f;
                h += cliff * (16f + 26f * (FotoNoise.Fbm(x * 0.003f, z * 0.003f, 3) * 0.5f + 0.5f)) * FotoNoise.SmoothStep(0f, 0.03f, c);
                float hills = FotoNoise.Fbm(x * 0.0011f + 7.7f, z * 0.0011f - 3.3f, 5) * 0.5f + 0.5f;
                h += Mathf.Pow(Mathf.Clamp01(hills), 1.7f) * 170f * FotoNoise.SmoothStep(0.03f, 0.3f, c);
            }
            else
            {
                h = Mathf.Max(c * (110f + cliff * 160f) - c * c * 900f, -72f);
            }

            float wx = x + 300f * FotoNoise.Fbm(x * 0.0005f + 12f, z * 0.0005f, 3);
            float wz = z + 300f * FotoNoise.Fbm(x * 0.0005f, z * 0.0005f - 12f, 3);
            float ds = DistanceToSpine(wx, wz);
            float m = FotoNoise.SmoothStep(1500f, 220f, ds) * FotoNoise.SmoothStep(-0.03f, 0.13f, c);
            if (m > 0f)
            {
                float ridge = FotoNoise.Ridged(x * 0.00072f + 1.3f, z * 0.00072f + 9.1f, 7);
                float massif = FotoNoise.Fbm(x * 0.00033f - 4.4f, z * 0.00033f + 2.2f, 3) * 0.5f + 0.5f;
                float range = Verlassen ? 0.62f : 1f;   // verlassene Insel: Kette bleibt unter der Wolkendecke
                h += range * (m * (110f + 640f * ridge * (0.55f + 0.7f * massif)) + 260f * m * m * m * massif);
            }
            if (Verlassen)
            {
                // Hauptgipfel: steile Pyramide mit Graten, ragt weit über die Wolkendecke (1.100–1.650 m).
                float pd = Vector2.Distance(new Vector2(wx, wz), Peak);
                float cone = Mathf.Exp(-Mathf.Pow(pd / 980f, 1.3f));
                float ridges = FotoNoise.Ridged(x * 0.0011f + 5f, z * 0.0011f - 3f, 6);
                float land = FotoNoise.SmoothStep(-0.02f, 0.12f, c);
                h += land * (cone * 2150f * (0.82f + 0.3f * ridges) + FotoNoise.SmoothStep(2200f, 500f, pd) * ridges * 220f);
            }

            // Ein paar Schären vor der Küste.
            if (c < 0.02f && c > -0.22f)
            {
                float islet = FotoNoise.Fbm(x * 0.0019f + 21f, z * 0.0019f - 13f, 3) * 0.5f + 0.5f;
                h += FotoNoise.SmoothStep(0.66f, 0.82f, islet) * 46f * FotoNoise.SmoothStep(-0.22f, -0.06f, c);
            }
            return Mathf.Clamp(h, Bottom + 1f, Bottom + HeightRange - 6f);
        }

        // ------------------------------------------------------------ Erosion

        /// <summary>Tropfen-Erosion nach Hans Theobald Beyer (bekannt durch Sebastian Lague), Höhen intern in km.</summary>
        static void Erode(float[] map, int n, int droplets, int seed)
        {
            const int radius = 2;
            const float inertia = 0.05f, capacityFactor = 4f, minCapacity = 0.01f;
            const float erodeSpeed = 0.3f, depositSpeed = 0.3f, evaporate = 0.012f, gravity = 4f;
            const int maxLife = 36;

            var offsets = new List<int>();
            var weights = new List<float>();
            float sum = 0f;
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > radius) continue;
                    float w = 1f - d / radius + 0.15f;
                    offsets.Add(dy * n + dx);
                    weights.Add(w);
                    sum += w;
                }
            }
            int[] off = offsets.ToArray();
            float[] bw = weights.ToArray();
            for (int i = 0; i < bw.Length; i++) bw[i] /= sum;

            for (int i = 0; i < map.Length; i++) map[i] *= 0.001f;
            var random = new System.Random(seed);
            for (int drop = 0; drop < droplets; drop++)
            {
                float px = (float)random.NextDouble() * (n - 6) + 3f;
                float py = (float)random.NextDouble() * (n - 6) + 3f;
                float dirX = 0f, dirY = 0f, speed = 1f, water = 1f, sediment = 0f;
                for (int life = 0; life < maxLife; life++)
                {
                    int nx = (int)px, ny = (int)py;
                    int idx = ny * n + nx;
                    float cx = px - nx, cy = py - ny;
                    float h00 = map[idx], h10 = map[idx + 1], h01 = map[idx + n], h11 = map[idx + n + 1];
                    float height = h00 * (1 - cx) * (1 - cy) + h10 * cx * (1 - cy) + h01 * (1 - cx) * cy + h11 * cx * cy;
                    if (height < 0f) break;
                    float gx = (h10 - h00) * (1 - cy) + (h11 - h01) * cy;
                    float gy = (h01 - h00) * (1 - cx) + (h11 - h10) * cx;

                    dirX = dirX * inertia - gx * (1 - inertia);
                    dirY = dirY * inertia - gy * (1 - inertia);
                    float len = Mathf.Sqrt(dirX * dirX + dirY * dirY);
                    if (len < 1e-9f) break;
                    dirX /= len; dirY /= len;
                    px += dirX; py += dirY;
                    if (px < 3 || px >= n - 4 || py < 3 || py >= n - 4) break;

                    int nx2 = (int)px, ny2 = (int)py;
                    int idx2 = ny2 * n + nx2;
                    float cx2 = px - nx2, cy2 = py - ny2;
                    float newHeight = map[idx2] * (1 - cx2) * (1 - cy2) + map[idx2 + 1] * cx2 * (1 - cy2)
                        + map[idx2 + n] * (1 - cx2) * cy2 + map[idx2 + n + 1] * cx2 * cy2;
                    float dh = newHeight - height;

                    float capacity = Mathf.Max(-dh * speed * water * capacityFactor, minCapacity);
                    if (sediment > capacity || dh > 0f)
                    {
                        float amount = dh > 0f ? Mathf.Min(dh, sediment) : (sediment - capacity) * depositSpeed;
                        sediment -= amount;
                        map[idx] += amount * (1 - cx) * (1 - cy);
                        map[idx + 1] += amount * cx * (1 - cy);
                        map[idx + n] += amount * (1 - cx) * cy;
                        map[idx + n + 1] += amount * cx * cy;
                    }
                    else
                    {
                        float amount = Mathf.Min((capacity - sediment) * erodeSpeed, -dh);
                        for (int b = 0; b < off.Length; b++)
                        {
                            int bi = idx + off[b];
                            if (map[bi] <= 0f) continue;
                            float delta = Mathf.Min(map[bi], amount * bw[b]);
                            map[bi] -= delta;
                            sediment += delta;
                        }
                    }
                    speed = Mathf.Sqrt(Mathf.Max(0f, speed * speed - dh * gravity));
                    water *= 1f - evaporate;
                }
            }
            for (int i = 0; i < map.Length; i++) map[i] *= 1000f;
        }

        static float CatmullRom(float p0, float p1, float p2, float p3, float t)
        {
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
        }

        static float SampleCubic(float[] map, int n, float u, float v)
        {
            int x = Mathf.Clamp((int)u, 0, n - 2), y = Mathf.Clamp((int)v, 0, n - 2);
            float tx = u - x, ty = v - y;
            float r0 = 0f, r1 = 0f, r2 = 0f, r3 = 0f;
            for (int k = -1; k <= 2; k++)
            {
                int yy = Mathf.Clamp(y + k, 0, n - 1) * n;
                float a = map[yy + Mathf.Clamp(x - 1, 0, n - 1)], b = map[yy + x], c = map[yy + Mathf.Clamp(x + 1, 0, n - 1)], d = map[yy + Mathf.Clamp(x + 2, 0, n - 1)];
                float r = CatmullRom(a, b, c, d, tx);
                if (k == -1) r0 = r; else if (k == 0) r1 = r; else if (k == 1) r2 = r; else r3 = r;
            }
            return CatmullRom(r0, r1, r2, r3, ty);
        }

        // ------------------------------------------------------------ Abfragen

        public float HeightAt(float x, float z)
        {
            float u = Mathf.Clamp(x / Cell, 0f, Res - 1.001f), v = Mathf.Clamp(z / Cell, 0f, Res - 1.001f);
            int i = (int)u, j = (int)v;
            float tx = u - i, ty = v - j;
            int k = j * Res + i;
            return Mathf.Lerp(Mathf.Lerp(H[k], H[k + 1], tx), Mathf.Lerp(H[k + Res], H[k + Res + 1], tx), ty);
        }

        public float HeightAt(Vector2 p) { return HeightAt(p.x, p.y); }

        /// <summary>Hangneigung in Grad.</summary>
        public float SlopeAt(float x, float z)
        {
            float d = Cell * 1.5f;
            float gx = (HeightAt(x + d, z) - HeightAt(x - d, z)) / (2f * d);
            float gz = (HeightAt(x, z + d) - HeightAt(x, z - d)) / (2f * d);
            return Mathf.Atan(Mathf.Sqrt(gx * gx + gz * gz)) * Mathf.Rad2Deg;
        }

        public byte MaskAt(byte[] mask, float x, float z)
        {
            int i = Mathf.Clamp(Mathf.RoundToInt(x / Cell), 0, Res - 1);
            int j = Mathf.Clamp(Mathf.RoundToInt(z / Cell), 0, Res - 1);
            return mask[j * Res + i];
        }

        float CoarseHeight(Vector2 p)
        {
            float u = Mathf.Clamp(p.x / CoarseCell, 0f, CoarseRes - 1.001f), v = Mathf.Clamp(p.y / CoarseCell, 0f, CoarseRes - 1.001f);
            int i = (int)u, j = (int)v;
            float tx = u - i, ty = v - j;
            int k = j * CoarseRes + i;
            return Mathf.Lerp(Mathf.Lerp(coarse[k], coarse[k + 1], tx), Mathf.Lerp(coarse[k + CoarseRes], coarse[k + CoarseRes + 1], tx), ty);
        }

        /// <summary>Küstenpunkt in Richtung <paramref name="degrees"/> vom Inselzentrum (erste Stelle unter Wasser).</summary>
        public Vector2 CoastPoint(float degrees, out Vector2 outward)
        {
            outward = new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad));
            Vector2 p = Center;
            for (float r = 300f; r < 4200f; r += 8f)
            {
                p = Center + outward * r;
                if (CoarseHeight(p) < -0.5f) return p;
            }
            return p;
        }

        /// <summary>Wald-Anteil 0–1 an einer Stelle (ohne Zonen, Straßen, Wasser).</summary>
        public float ForestAt(float x, float z, float h, float slope)
        {
            float big = FotoNoise.Fbm(x * 0.00085f + 40f, z * 0.00085f - 12f, 4) * 0.5f + 0.5f;
            float edge = FotoNoise.Fbm(x * 0.0045f - 5f, z * 0.0045f + 33f, 3);
            float f = FotoNoise.SmoothStep(0.43f, 0.53f, big + edge * 0.09f);
            f *= 1f - (Verlassen ? FotoNoise.SmoothStep(950f, 1120f, h + edge * 60f) : FotoNoise.SmoothStep(620f, 760f, h + edge * 60f));
            f *= FotoNoise.SmoothStep(5f, 16f, h);
            f *= 1f - FotoNoise.SmoothStep(36f, 46f, slope);
            return f;
        }

        public bool InAnyZone(Vector2 p, float margin)
        {
            foreach (FotoZone z in Zones) if (z.Contains(p, margin)) return true;
            return false;
        }

        // ------------------------------------------------------------ Zonen

        FotoZone CoastZone(string name, float degrees, Vector2 half, float inland, float elevation, bool port)
        {
            Vector2 outward;
            Vector2 coast = CoastPoint(degrees, out outward);
            var zone = new FotoZone
            {
                Name = name,
                Half = half,
                Angle = Mathf.Atan2(outward.y, outward.x) * Mathf.Rad2Deg + 90f,
                Center = coast - outward * inland,
                Elevation = elevation,
                Port = port,
                Blend = 80f,
            };
            zone.Gate = zone.ToWorld(new Vector2(0f, half.y + 25f));
            return zone;
        }

        /// <summary>Sucht im Winkelbereich den flachsten, niedrigsten Küstenabschnitt für eine Zone.</summary>
        FotoZone BestCoastZone(string name, float fromDeg, float toDeg, Vector2 half, float inland, float elevation, bool port)
        {
            FotoZone best = null;
            float bestScore = float.MaxValue;
            for (float a = fromDeg; a <= toDeg; a += 2.5f)
            {
                FotoZone z = CoastZone(name, a, half, inland, elevation, port);
                float sum = 0f, max = 0f;
                int land = 0, sea = 0;
                for (float lx = -half.x; lx <= half.x; lx += 50f)
                {
                    for (float ly = -half.y; ly <= half.y; ly += 50f)
                    {
                        float h = CoarseHeight(z.ToWorld(new Vector2(lx, ly)));
                        if (h > 0f) { sum += h; max = Mathf.Max(max, h); land++; }
                        else sea++;
                    }
                }
                if (land == 0) continue;
                float score = sum / land + max * 0.35f + (sea > (land + sea) * 0.35f ? 500f : 0f);
                if (score < bestScore) { bestScore = score; best = z; }
            }
            best.Blend = 150f;
            Log += name + ": Winkel " + (best.Angle - 90f).ToString("F0") + "°, Bewertung " + bestScore.ToString("F0") + "\n";
            return best;
        }

        /// <summary>Flache, niedrige Küste im Winkelbereich für eine Fläche, die dem Gelände folgt.</summary>
        FotoZone BestOpenCoast(string name, float fromDeg, float toDeg, float inland, Vector2 half, Vector2 avoid, float minDistance)
        {
            FotoZone best = null;
            float bestScore = float.MaxValue;
            for (float a = fromDeg; a <= toDeg; a += 2.5f)
            {
                Vector2 outward;
                Vector2 coast = CoastPoint(a, out outward);
                var z = new FotoZone
                {
                    Name = name,
                    Center = coast - outward * inland,
                    Half = half,
                    Angle = Mathf.Atan2(outward.y, outward.x) * Mathf.Rad2Deg + 90f,
                    Flatten = false,
                    Blend = 0f,
                };
                if ((z.Center - avoid).magnitude < minDistance) continue;
                float sum = 0f; int n = 0;
                for (float lx = -half.x; lx <= half.x; lx += 40f)
                    for (float ly = -half.y; ly <= half.y; ly += 40f)
                    {
                        float h = CoarseHeight(z.ToWorld(new Vector2(lx, ly)));
                        sum += Mathf.Abs(h - 10f); n++;
                    }
                float score = sum / n + Cliffness(coast.x, coast.y) * 80f;
                if (score < bestScore) { bestScore = score; best = z; }
            }
            best.Gate = best.Center;
            Log += name + ": Winkel " + (best.Angle - 90f).ToString("F0") + "°, Bewertung " + bestScore.ToString("F0") + "\n";
            return best;
        }

        void PlanZonesVerlassen()
        {
            Steelworks = new FotoZone
            {
                Name = "Verlassenes Stahlwerk",
                Center = Center + new Vector2(Mathf.Cos(205f * Mathf.Deg2Rad), Mathf.Sin(205f * Mathf.Deg2Rad)) * 1450f,
                Half = new Vector2(560f, 340f),
                Angle = 18f,
                AutoElevation = true,
                Blend = 110f,
            };
            Steelworks.Gate = Steelworks.ToWorld(new Vector2(Steelworks.Half.x + 25f, 0f));
            Rohbau = new FotoZone
            {
                Name = "Rohbau",
                Center = Steelworks.ToWorld(new Vector2(Steelworks.Half.x * 0.55f, -Steelworks.Half.y - 95f)),
                Half = new Vector2(36f, 28f),
                Angle = Steelworks.Angle,
                AutoElevation = true,
                Blend = 45f,
            };
            Rohbau.Gate = Rohbau.Center;
            Town = BestOpenCoast("Wikingerdorf", -125f, -55f, 300f, new Vector2(200f, 170f), Steelworks.Center, 1250f);
            // Strand an der Westküste: dort steht die Abendsonne über dem Meer.
            Beach = BestOpenCoast("Traumstrand", 150f, 215f, 150f, new Vector2(430f, 170f), Town.Center, 1400f);
            Zones.Add(Steelworks);
            Zones.Add(Rohbau);
            Zones.Add(Town);
            Zones.Add(Beach);
            foreach (FotoZone zone in Zones)
            {
                if (!zone.AutoElevation) continue;
                // Untere Geländehöhe (25-%-Quantil): die Fläche wird eingeschnitten statt aufgeschüttet, keine Dammwände.
                var hs = new List<float>();
                for (float lx = -zone.Half.x; lx <= zone.Half.x; lx += 20f)
                    for (float ly = -zone.Half.y; ly <= zone.Half.y; ly += 20f) hs.Add(CoarseHeight(zone.ToWorld(new Vector2(lx, ly))));
                hs.Sort();
                zone.Elevation = Mathf.Max(6f, hs[hs.Count / 4]);
            }
        }

        /// <summary>
        /// Vorgeformtes, mäandrierendes Tal vom Bergfuß bis zur Küste neben dem Dorf. Es fällt durchgehend
        /// ab, also sammelt die Abflussberechnung dort den Hauptfluss; die Erosion formt es natürlich aus.
        /// </summary>
        void CarveMainValley()
        {
            Vector2 toTown = (Town.Center - Peak).normalized;
            Vector2 start = Peak + toTown * 1250f;
            Vector2 outward;
            Vector2 coast = CoastPoint(Mathf.Atan2((Town.Center - Center).y, (Town.Center - Center).x) * Mathf.Rad2Deg, out outward);
            Vector2 side = new Vector2(-outward.y, outward.x);
            Vector2 end = coast + side * 140f + outward * 60f;
            mainMouth = end;
            float hStart = CoarseHeight(start);
            Vector2 axis = end - start;
            Vector2 normal = new Vector2(-axis.y, axis.x).normalized;
            var path = new List<Vector2>();
            const int n = 90;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float wiggle = Mathf.Sin(t * Mathf.PI * 3.2f + 0.7f) * 150f + FotoNoise.Fbm(t * 6f, 3.3f, 3) * 120f;
                wiggle *= Mathf.Sin(t * Mathf.PI);   // Anfang und Mündung fest
                path.Add(start + axis * t + normal * wiggle);
            }
            const float reach = 260f;
            for (int seg = 0; seg < n; seg++)
            {
                Vector2 a = path[seg], b = path[seg + 1];
                float minX = Mathf.Min(a.x, b.x) - reach, maxX = Mathf.Max(a.x, b.x) + reach;
                float minZ = Mathf.Min(a.y, b.y) - reach, maxZ = Mathf.Max(a.y, b.y) + reach;
                int i0 = Mathf.Max(0, (int)(minX / CoarseCell)), i1 = Mathf.Min(CoarseRes - 1, (int)(maxX / CoarseCell));
                int j0 = Mathf.Max(0, (int)(minZ / CoarseCell)), j1 = Mathf.Min(CoarseRes - 1, (int)(maxZ / CoarseCell));
                for (int j = j0; j <= j1; j++)
                {
                    for (int i = i0; i <= i1; i++)
                    {
                        float tt;
                        float d = DistanceToSegment(new Vector2(i * CoarseCell, j * CoarseCell), a, b, out tt);
                        if (d > reach) continue;
                        float t = (seg + tt) / n;
                        float bed = Mathf.Lerp(hStart, -1.5f, Mathf.Pow(t, 0.8f));
                        float valley = bed + d * Mathf.Lerp(0.35f, 0.08f, t) + d * d * 0.0009f;
                        int k = j * CoarseRes + i;
                        if (coarse[k] > valley) coarse[k] = Mathf.Lerp(coarse[k], valley, 1f - FotoNoise.SmoothStep(reach * 0.55f, reach, d));
                    }
                }
            }
        }

        void FindMainRiver()
        {
            float best = float.MaxValue;
            for (int r = 0; r < Rivers.Count; r++)
            {
                FotoRiver river = Rivers[r];
                if (!river.ReachesSea || river.Points.Count < 200) continue;
                float d = (river.Points[river.Points.Count - 1] - mainMouth).magnitude;
                if (d < best) { best = d; MainRiver = r; }
            }
            Log += "Hauptfluss: " + (MainRiver >= 0 ? Rivers[MainRiver].Points.Count * 5 + " m lang, Mündung " + best.ToString("F0") + " m neben dem Ziel" : "nicht gefunden") + "\n";
        }

        /// <summary>Strand als flach auslaufende Rampe: an der Wasserlinie unter 1 m, landeinwärts sanft steigend.</summary>
        void ShapeBeach()
        {
            if (Beach == null) return;
            const float reach = 160f;
            float r = Beach.Half.magnitude + reach;
            int i0 = Mathf.Max(0, (int)((Beach.Center.x - r) / CoarseCell)), i1 = Mathf.Min(CoarseRes - 1, (int)((Beach.Center.x + r) / CoarseCell));
            int j0 = Mathf.Max(0, (int)((Beach.Center.y - r) / CoarseCell)), j1 = Mathf.Min(CoarseRes - 1, (int)((Beach.Center.y + r) / CoarseCell));
            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    int k = j * CoarseRes + i;
                    float h = coarse[k];
                    var p = new Vector2(i * CoarseCell, j * CoarseCell);
                    Vector2 l = Beach.ToLocal(p);
                    float inland = l.y + Beach.Half.y;
                    float target = 0.6f + Mathf.Max(0f, inland) * 0.028f + FotoNoise.Fbm(p.x * 0.01f, p.y * 0.01f, 2) * 0.6f;
                    float w = 1f - FotoNoise.SmoothStep(0f, reach, Beach.Outside(p));
                    if (w <= 0f) continue;
                    if (h < -0.5f)
                    {
                        // Flaches, türkises Wasser vor dem Strand.
                        float shallow = Mathf.Min(-0.9f, inland * 0.03f);
                        if (shallow > h) coarse[k] = Mathf.Lerp(h, shallow, w);
                        continue;
                    }
                    if (h > target) coarse[k] = Mathf.Lerp(h, target, w);
                }
            }
        }

        void PlanZones()
        {
            if (Verlassen) { PlanZonesVerlassen(); return; }
            Port = BestCoastZone("Containerhafen Ost", -45f, 15f, new Vector2(700f, 270f), 230f, 5.5f, true);
            Refinery = BestCoastZone("Raffinerie Süd", -130f, -70f, new Vector2(620f, 360f), 300f, 7f, true);

            Steelworks = new FotoZone
            {
                Name = "Stahlwerk im Westtal",
                Center = Center + new Vector2(Mathf.Cos(200f * Mathf.Deg2Rad), Mathf.Sin(200f * Mathf.Deg2Rad)) * 1450f,
                Half = new Vector2(560f, 340f),
                Angle = 18f,
                AutoElevation = true,
                Blend = 110f,
            };
            Steelworks.Gate = Steelworks.ToWorld(new Vector2(Steelworks.Half.x + 25f, 0f));

            float portDeg = Port.Angle - 90f, refineryDeg = Refinery.Angle - 90f;
            float bestTown = float.MaxValue;
            for (float a = refineryDeg + 20f; a <= portDeg - 15f; a += 2.5f)
            {
                Vector2 outward;
                Vector2 townCoast = CoastPoint(a, out outward);
                var candidate = new FotoZone
                {
                    Name = "Hafenstadt",
                    Center = townCoast - outward * 430f,
                    Half = new Vector2(380f, 260f),
                    Angle = Mathf.Atan2(outward.y, outward.x) * Mathf.Rad2Deg + 90f,
                    Flatten = false,
                    Blend = 0f,
                };
                if ((candidate.Center - Port.Center).magnitude < 1100f || (candidate.Center - Refinery.Center).magnitude < 1100f) continue;
                float sum = 0f; int n = 0;
                for (float lx = -380f; lx <= 380f; lx += 60f)
                    for (float ly = -260f; ly <= 260f; ly += 60f) { sum += Mathf.Abs(CoarseHeight(candidate.ToWorld(new Vector2(lx, ly))) - 20f); n++; }
                if (sum / n < bestTown) { bestTown = sum / n; Town = candidate; }
            }
            if (Town == null)
            {
                Vector2 outward;
                Vector2 townCoast = CoastPoint((portDeg + refineryDeg) * 0.5f, out outward);
                Town = new FotoZone { Name = "Hafenstadt", Center = townCoast - outward * 430f, Half = new Vector2(380f, 260f), Angle = Mathf.Atan2(outward.y, outward.x) * Mathf.Rad2Deg + 90f, Flatten = false };
            }
            Town.Gate = Town.Center;

            Zones.Add(Port);
            Zones.Add(Refinery);
            Zones.Add(Steelworks);
            Zones.Add(Town);

            foreach (FotoZone zone in Zones)
            {
                if (!zone.AutoElevation) continue;
                double sum = 0; int count = 0;
                for (float lx = -zone.Half.x; lx <= zone.Half.x; lx += 40f)
                {
                    for (float ly = -zone.Half.y; ly <= zone.Half.y; ly += 40f)
                    {
                        sum += CoarseHeight(zone.ToWorld(new Vector2(lx, ly)));
                        count++;
                    }
                }
                zone.Elevation = Mathf.Max(6f, (float)(sum / count));
            }
        }

        /// <summary>
        /// Senkt das Land rund um die Industrieflächen vor der Erosion zu einer Ebene ab, die sanft
        /// ins Hügelland übergeht – wie eine Küsten- oder Talebene, auf der man solche Werke baut.
        /// </summary>
        void ShapeLowlands()
        {
            const float reach = 700f;
            foreach (FotoZone zone in Zones)
            {
                if (!zone.Flatten) continue;
                float r = zone.Half.magnitude + reach;
                int i0 = Mathf.Max(0, (int)((zone.Center.x - r) / CoarseCell)), i1 = Mathf.Min(CoarseRes - 1, (int)((zone.Center.x + r) / CoarseCell));
                int j0 = Mathf.Max(0, (int)((zone.Center.y - r) / CoarseCell)), j1 = Mathf.Min(CoarseRes - 1, (int)((zone.Center.y + r) / CoarseCell));
                for (int j = j0; j <= j1; j++)
                {
                    for (int i = i0; i <= i1; i++)
                    {
                        int k = j * CoarseRes + i;
                        float h = coarse[k];
                        if (h <= 0f) continue;
                        var p = new Vector2(i * CoarseCell, j * CoarseCell);
                        float d = zone.Outside(p);
                        if (d > reach) continue;
                        float target = zone.Elevation + 1.5f + d * 0.05f
                            + FotoNoise.Fbm(p.x * 0.004f + 2f, p.y * 0.004f, 3) * 7f * FotoNoise.SmoothStep(40f, 300f, d);
                        float w = 1f - FotoNoise.SmoothStep(reach * 0.3f, reach, d);
                        if (h > target) coarse[k] = Mathf.Lerp(h, target, w);
                    }
                }
            }
        }

        void FlattenZone(FotoZone zone)
        {
            if (!zone.Flatten) return;
            float reach = Mathf.Max(zone.Half.x, zone.Half.y) * 1.5f + zone.Blend;
            int i0 = Mathf.Max(0, (int)((zone.Center.x - reach) / Cell)), i1 = Mathf.Min(Res - 1, (int)((zone.Center.x + reach) / Cell));
            int j0 = Mathf.Max(0, (int)((zone.Center.y - reach) / Cell)), j1 = Mathf.Min(Res - 1, (int)((zone.Center.y + reach) / Cell));
            Parallel.For(j0, j1 + 1, j =>
            {
                for (int i = i0; i <= i1; i++)
                {
                    var p = new Vector2(i * Cell, j * Cell);
                    float outside = zone.Outside(p);
                    int k = j * Res + i;
                    if (outside <= 0f)
                    {
                        H[k] = zone.Elevation;
                        ZoneMask[k] = 255;
                        continue;
                    }
                    if (outside >= zone.Blend) continue;
                    // Hafen: das Meer bleibt Meer, die Kante wird zur Kaimauer.
                    if (zone.Port && H[k] < 0.5f) continue;
                    float w = 1f - FotoNoise.SmoothStep(0f, zone.Blend, outside);
                    H[k] = Mathf.Lerp(H[k], zone.Elevation, w);
                    ZoneMask[k] = (byte)Mathf.Max(ZoneMask[k], (int)(w * 120f));
                }
            });
        }

        // ------------------------------------------------------------ Flüsse

        sealed class Heap
        {
            float[] keys = new float[1 << 16];
            int[] values = new int[1 << 16];
            public int Count;

            public void Push(float key, int value)
            {
                if (Count == keys.Length)
                {
                    System.Array.Resize(ref keys, Count * 2);
                    System.Array.Resize(ref values, Count * 2);
                }
                int i = Count++;
                while (i > 0)
                {
                    int parent = (i - 1) >> 1;
                    if (keys[parent] <= key) break;
                    keys[i] = keys[parent]; values[i] = values[parent];
                    i = parent;
                }
                keys[i] = key; values[i] = value;
            }

            public int Pop(out float key)
            {
                key = keys[0];
                int result = values[0];
                float lastKey = keys[--Count];
                int lastValue = values[Count];
                int i = 0;
                while (true)
                {
                    int child = i * 2 + 1;
                    if (child >= Count) break;
                    if (child + 1 < Count && keys[child + 1] < keys[child]) child++;
                    if (keys[child] >= lastKey) break;
                    keys[i] = keys[child]; values[i] = values[child];
                    i = child;
                }
                keys[i] = lastKey; values[i] = lastValue;
                return result;
            }
        }

        void TraceRivers()
        {
            int n = CoarseRes;
            var g = new float[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                    g[j * n + i] = H[(j * CoarseStep) * Res + i * CoarseStep];

            var receiver = new int[n * n];
            var order = new int[n * n];
            var visited = new bool[n * n];
            var heap = new Heap();
            for (int k = 0; k < g.Length; k++)
            {
                int i = k % n, j = k / n;
                if (g[k] < 0f || i == 0 || j == 0 || i == n - 1 || j == n - 1)
                {
                    visited[k] = true;
                    receiver[k] = -1;
                    heap.Push(g[k], k);
                }
            }
            int[] dx = { 1, -1, 0, 0, 1, 1, -1, -1 };
            int[] dy = { 0, 0, 1, -1, 1, -1, 1, -1 };
            int count = 0;
            while (heap.Count > 0)
            {
                float level;
                int c = heap.Pop(out level);
                order[count++] = c;
                int ci = c % n, cj = c / n;
                for (int d = 0; d < 8; d++)
                {
                    int ni = ci + dx[d], nj = cj + dy[d];
                    if (ni < 0 || nj < 0 || ni >= n || nj >= n) continue;
                    int nb = nj * n + ni;
                    if (visited[nb]) continue;
                    visited[nb] = true;
                    receiver[nb] = c;
                    float filled = Mathf.Max(g[nb], level + 0.0005f);
                    heap.Push(filled, nb);
                }
            }

            var acc = new float[n * n];
            for (int k = 0; k < g.Length; k++) acc[k] = g[k] >= 0f ? 1f : 0f;
            for (int o = count - 1; o >= 0; o--)
            {
                int c = order[o];
                int r = receiver[c];
                if (r >= 0) acc[r] += acc[c];
            }

            float cellArea = CoarseCell * CoarseCell / 1e6f;
            float threshold = RiverAreaKm2 / cellArea;
            var hasUpstreamRiver = new bool[n * n];
            for (int k = 0; k < g.Length; k++)
            {
                if (acc[k] < threshold || g[k] < 0f) continue;
                int r = receiver[k];
                if (r >= 0) hasUpstreamRiver[r] = true;
            }

            var heads = new List<int>();
            for (int k = 0; k < g.Length; k++)
                if (acc[k] >= threshold && g[k] >= 0f && !hasUpstreamRiver[k]) heads.Add(k);
            // Lange Flüsse zuerst, damit der Hauptstrom durchgehend bleibt.
            heads.Sort((a, b) => g[b].CompareTo(g[a]));

            var owner = new int[n * n];
            for (int k = 0; k < owner.Length; k++) owner[k] = -1;
            foreach (int head in heads)
            {
                var cells = new List<int>();
                int c = head;
                bool sea = false;
                int guard = 0;
                while (c >= 0 && guard++ < 20000)
                {
                    cells.Add(c);
                    if (g[c] < 0f) { sea = true; break; }
                    if (owner[c] >= 0 && cells.Count > 1) break;
                    owner[c] = Rivers.Count;
                    c = receiver[c];
                }
                if (cells.Count < 40) continue;

                var raw = new List<Vector2>();
                var rawAcc = new List<float>();
                foreach (int cell in cells)
                {
                    raw.Add(new Vector2((cell % n) * CoarseCell, (cell / n) * CoarseCell));
                    rawAcc.Add(acc[cell]);
                }
                var river = BuildRiver(raw, rawAcc, cellArea, sea);
                if (river != null) Rivers.Add(river);
            }
        }

        FotoRiver BuildRiver(List<Vector2> raw, List<float> rawAcc, float cellArea, bool sea)
        {
            // Treppen des 8er-Rasters glätten (Chaikin, Enden bleiben fest).
            List<Vector2> pts = raw;
            List<float> accs = rawAcc;
            for (int pass = 0; pass < 3; pass++)
            {
                var np = new List<Vector2> { pts[0] };
                var na = new List<float> { accs[0] };
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    np.Add(Vector2.Lerp(pts[i], pts[i + 1], 0.25f));
                    np.Add(Vector2.Lerp(pts[i], pts[i + 1], 0.75f));
                    na.Add(Mathf.Lerp(accs[i], accs[i + 1], 0.25f));
                    na.Add(Mathf.Lerp(accs[i], accs[i + 1], 0.75f));
                }
                np.Add(pts[pts.Count - 1]);
                na.Add(accs[accs.Count - 1]);
                pts = np; accs = na;
            }

            var river = new FotoRiver { ReachesSea = sea };
            float spacing = 5f, carry = 0f;
            river.Points.Add(pts[0]);
            var resampledAcc = new List<float> { accs[0] };
            for (int i = 0; i < pts.Count - 1; i++)
            {
                float len = Vector2.Distance(pts[i], pts[i + 1]);
                float pos = spacing - carry;
                while (pos <= len)
                {
                    float t = pos / len;
                    river.Points.Add(Vector2.Lerp(pts[i], pts[i + 1], t));
                    resampledAcc.Add(Mathf.Lerp(accs[i], accs[i + 1], t));
                    pos += spacing;
                }
                carry = len - (pos - spacing);
            }
            if (sea && river.Points.Count > 2)
            {
                Vector2 dir = (river.Points[river.Points.Count - 1] - river.Points[river.Points.Count - 3]).normalized;
                Vector2 end = river.Points[river.Points.Count - 1];
                for (int k = 1; k <= 14; k++)
                {
                    river.Points.Add(end + dir * spacing * k);
                    resampledAcc.Add(resampledAcc[resampledAcc.Count - 1]);
                }
            }

            float surface = float.MaxValue;
            for (int i = 0; i < river.Points.Count; i++)
            {
                float area = resampledAcc[i] * cellArea;
                float width = Verlassen ? 4.5f + 3.4f * Mathf.Sqrt(area) : 3f + 2.7f * Mathf.Sqrt(area);
                river.HalfWidth.Add(width * 0.5f);
                river.Depth.Add(0.9f + width * 0.1f);
                float ground = HeightAt(river.Points[i]);
                surface = Mathf.Min(surface, ground - 0.45f);
                river.Surface.Add(Mathf.Max(surface, 0.05f));
            }
            return river.Points.Count > 30 ? river : null;
        }

        void Stamp(Vector2 a, Vector2 b, float reach, System.Action<int, float, float> apply)
        {
            float minX = Mathf.Min(a.x, b.x) - reach, maxX = Mathf.Max(a.x, b.x) + reach;
            float minZ = Mathf.Min(a.y, b.y) - reach, maxZ = Mathf.Max(a.y, b.y) + reach;
            int i0 = Mathf.Max(0, Mathf.FloorToInt(minX / Cell)), i1 = Mathf.Min(Res - 1, Mathf.CeilToInt(maxX / Cell));
            int j0 = Mathf.Max(0, Mathf.FloorToInt(minZ / Cell)), j1 = Mathf.Min(Res - 1, Mathf.CeilToInt(maxZ / Cell));
            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    float t;
                    float d = DistanceToSegment(new Vector2(i * Cell, j * Cell), a, b, out t);
                    if (d <= reach) apply(j * Res + i, d, t);
                }
            }
        }

        void CarveRiver(FotoRiver river)
        {
            for (int s = 0; s < river.Points.Count - 1; s++)
            {
                int s0 = s;
                float hwMax = Mathf.Max(river.HalfWidth[s], river.HalfWidth[s + 1]);
                float bank = 7f + hwMax * 1.6f;
                Stamp(river.Points[s], river.Points[s + 1], hwMax + bank, (k, d, t) =>
                {
                    float hw = Mathf.Lerp(river.HalfWidth[s0], river.HalfWidth[s0 + 1], t);
                    float surface = Mathf.Lerp(river.Surface[s0], river.Surface[s0 + 1], t);
                    float depth = Mathf.Lerp(river.Depth[s0], river.Depth[s0 + 1], t);
                    if (d <= hw)
                    {
                        float e = d / hw;
                        float bed = surface - 0.25f - depth * (1f - e * e);
                        if (H[k] > bed) H[k] = bed;
                        WaterMask[k] = 255;
                    }
                    else
                    {
                        float bw = 7f + hw * 1.6f;
                        if (d > hw + bw) return;
                        float tb = (d - hw) / bw;
                        float target = Mathf.Lerp(surface + 0.35f, H[k], FotoNoise.SmoothStep(0f, 1f, tb));
                        if (H[k] > target) H[k] = target;
                        byte bankValue = (byte)(200f * (1f - tb));
                        if (WaterMask[k] < bankValue) WaterMask[k] = bankValue;
                    }
                });
            }
        }

        void RaiseLevees(FotoRiver river)
        {
            for (int s = 0; s < river.Points.Count - 1; s++)
            {
                int s0 = s;
                float hwMax = Mathf.Max(river.HalfWidth[s], river.HalfWidth[s + 1]);
                Stamp(river.Points[s], river.Points[s + 1], hwMax + 3f, (k, d, t) =>
                {
                    if (WaterMask[k] == 255) return;
                    float hw = Mathf.Lerp(river.HalfWidth[s0], river.HalfWidth[s0 + 1], t);
                    if (d < hw) return;
                    float surface = Mathf.Lerp(river.Surface[s0], river.Surface[s0 + 1], t);
                    if (surface <= 0.2f) return;
                    if (H[k] < surface + 0.25f) H[k] = surface + 0.25f;
                });
            }
        }

        // ------------------------------------------------------------ Straßen

        void PlanRoads()
        {
            if (Verlassen)
            {
                AddRoad("Alte Werksstraße", Town.Center, Center + new Vector2(-600f, -500f), Steelworks.Gate);
                AddRoad("Zufahrt Rohbau", Steelworks.Gate, Rohbau.ToWorld(new Vector2(0f, Rohbau.Half.y + 14f)));
                return;
            }
            Vector2 mid = Center + new Vector2(150f, -350f);
            AddRoad("Küstenstraße", Port.ToWorld(new Vector2(Port.Half.x * 0.55f, Port.Half.y + 25f)), Town.Center, Refinery.Gate);
            AddRoad("Südstraße", Refinery.ToWorld(new Vector2(-Refinery.Half.x * 0.5f, Refinery.Half.y + 25f)), Steelworks.ToWorld(new Vector2(0f, -Steelworks.Half.y - 25f)));
            AddRoad("Inselquerung", Steelworks.Gate, mid, Port.ToWorld(new Vector2(-Port.Half.x * 0.5f, Port.Half.y + 25f)));
        }

        void AddRoad(string name, params Vector2[] waypoints)
        {
            var road = new FotoRoad { Name = name };
            // Catmull-Rom durch die Wegpunkte, dazu ein leichtes Schlängeln.
            var control = new List<Vector2>();
            for (int w = 0; w < waypoints.Length - 1; w++)
            {
                Vector2 a = waypoints[w], b = waypoints[w + 1];
                int parts = Mathf.Max(2, Mathf.RoundToInt(Vector2.Distance(a, b) / 450f));
                Vector2 normal = new Vector2(-(b - a).y, (b - a).x).normalized;
                for (int p = 0; p < parts; p++)
                {
                    float t = p / (float)parts;
                    float wiggle = (p == 0) ? 0f : FotoNoise.Perlin(a.x * 0.01f + p * 1.7f, b.y * 0.01f) * 90f;
                    control.Add(Vector2.Lerp(a, b, t) + normal * wiggle);
                }
            }
            control.Add(waypoints[waypoints.Length - 1]);

            for (int c = 0; c < control.Count - 1; c++)
            {
                Vector2 p0 = control[Mathf.Max(0, c - 1)], p1 = control[c], p2 = control[c + 1], p3 = control[Mathf.Min(control.Count - 1, c + 2)];
                int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(p1, p2) / 8f));
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps;
                    road.Points.Add(new Vector2(CatmullRom(p0.x, p1.x, p2.x, p3.x, t), CatmullRom(p0.y, p1.y, p2.y, p3.y, t)));
                }
            }
            road.Points.Add(control[control.Count - 1]);

            int count = road.Points.Count;
            var raw = new float[count];
            var required = new float[count];
            for (int i = 0; i < count; i++)
            {
                Vector2 p = road.Points[i];
                raw[i] = Mathf.Max(HeightAt(p), 2.5f);
                foreach (FotoZone z in Zones)
                    if (z.Flatten && z.Contains(p, 10f)) raw[i] = z.Elevation;
                required[i] = float.MinValue;
            }

            // Brücken: über Flüssen mindestens 4,5 m über dem Wasser.
            var crossings = new List<int>();
            var crossingRiver = new List<int>();
            for (int r = 0; r < Rivers.Count; r++)
            {
                FotoRiver river = Rivers[r];
                for (int i = 0; i < count; i += 2)
                {
                    for (int s = 0; s < river.Points.Count; s += 3)
                    {
                        if ((river.Points[s] - road.Points[i]).sqrMagnitude > (river.HalfWidth[s] + 6f) * (river.HalfWidth[s] + 6f)) continue;
                        bool near = false;
                        foreach (int c in crossings) if (Mathf.Abs(c - i) < 12) near = true;
                        if (!near)
                        {
                            crossings.Add(i);
                            crossingRiver.Add(r * 100000 + s);
                        }
                    }
                }
            }
            for (int c = 0; c < crossings.Count; c++)
            {
                int r = crossingRiver[c] / 100000, s = crossingRiver[c] % 100000;
                float need = Rivers[r].Surface[s] + 4.5f;
                for (int i = Mathf.Max(0, crossings[c] - 6); i <= Mathf.Min(count - 1, crossings[c] + 6); i++)
                    required[i] = Mathf.Max(required[i], need);
            }

            float[] elev = (float[])raw.Clone();
            for (int pass = 0; pass < 4; pass++)
            {
                for (int i = 0; i < count; i++) elev[i] = Mathf.Max(elev[i], required[i]);
                elev = Smooth(elev, Verlassen ? (pass < 2 ? 7 : 4) : (pass < 2 ? 22 : 8));   // alte Wege folgen dem Gelände
            }
            for (int i = 0; i < count; i++) elev[i] = Mathf.Max(elev[i], required[i]);
            road.Elevation.AddRange(elev);

            for (int c = 0; c < crossings.Count; c++)
            {
                int i = crossings[c];
                int r = crossingRiver[c] / 100000, s = crossingRiver[c] % 100000;
                Vector2 dir = (road.Points[Mathf.Min(count - 1, i + 2)] - road.Points[Mathf.Max(0, i - 2)]).normalized;
                Bridges.Add(new FotoBridge
                {
                    Center = new Vector3(road.Points[i].x, elev[i], road.Points[i].y),
                    Direction = dir,
                    Length = Rivers[r].HalfWidth[s] * 2f + 30f,
                    Width = road.HalfWidth * 2f + 1.6f,
                });
            }
            Roads.Add(road);
        }

        static float[] Smooth(float[] values, int radius)
        {
            var result = new float[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                float sum = 0f; int n = 0;
                for (int k = Mathf.Max(0, i - radius); k <= Mathf.Min(values.Length - 1, i + radius); k++) { sum += values[k]; n++; }
                result[i] = sum / n;
            }
            return result;
        }

        void CarveRoad(FotoRoad road)
        {
            const float shoulder = 22f;
            for (int s = 0; s < road.Points.Count - 1; s++)
            {
                int s0 = s;
                Stamp(road.Points[s], road.Points[s + 1], road.HalfWidth + shoulder, (k, d, t) =>
                {
                    float e = Mathf.Lerp(road.Elevation[s0], road.Elevation[s0 + 1], t);
                    float w = 1f - FotoNoise.SmoothStep(road.HalfWidth + 1f, road.HalfWidth + shoulder, d);
                    // Nur zur Straße hin ziehen, nie über eine nähere Straße hinweg.
                    H[k] = Mathf.Lerp(H[k], e - 0.05f, w);
                    if (d <= road.HalfWidth) RoadMask[k] = 255;
                    else if (d <= road.HalfWidth + 2.5f) RoadMask[k] = (byte)Mathf.Max(RoadMask[k], 140);
                });
            }
        }

        /// <summary>Straßenraster der Stadt (nur Bodenmarkierung, das Gelände bleibt).</summary>
        void PaintStreets(FotoZone zone, float spacingX, float spacingY, float halfWidth)
        {
            float reach = zone.Half.magnitude + 10f;
            int i0 = Mathf.Max(0, (int)((zone.Center.x - reach) / Cell)), i1 = Mathf.Min(Res - 1, (int)((zone.Center.x + reach) / Cell));
            int j0 = Mathf.Max(0, (int)((zone.Center.y - reach) / Cell)), j1 = Mathf.Min(Res - 1, (int)((zone.Center.y + reach) / Cell));
            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    var p = new Vector2(i * Cell, j * Cell);
                    if (!zone.Contains(p)) continue;
                    Vector2 l = zone.ToLocal(p) + zone.Half;
                    float dx = Mathf.Abs(Mathf.Repeat(l.x + spacingX * 0.5f, spacingX) - spacingX * 0.5f);
                    float dy = Mathf.Abs(Mathf.Repeat(l.y + spacingY * 0.5f, spacingY) - spacingY * 0.5f);
                    if (Mathf.Min(dx, dy) <= halfWidth) RoadMask[j * Res + i] = 230;
                }
            }
        }

        // ------------------------------------------------------------ Landmarken

        void PlanLandmarks()
        {
            Vector2 outward;
            LighthousePoint = CoastPoint(150f, out outward) - outward * 45f;
            ShipPoint = CoastPoint(-55f, out outward) + outward * 520f;
            WreckPoint = CoastPoint(-138f, out outward) - outward * 28f;
            HutPoint = CoastPoint(-160f, out outward) - outward * 70f;

            // Funkmast auf dem höchsten Gipfel.
            float best = float.MinValue;
            for (int j = 0; j < Res; j += 16)
            {
                for (int i = 0; i < Res; i += 16)
                {
                    float h = H[j * Res + i];
                    if (h > best) { best = h; MastPoint = new Vector2(i * Cell, j * Cell); }
                }
            }
        }
    }
}
