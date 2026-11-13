using System.Collections.Generic;
using Frostline.Gameplay;
using UnityEngine;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Ausstattung der Insel aus freien Poly-Haven-Vorlagen (CC0): Felsen und Treibholz an der Küste,
    /// Felswände in den Bergen, Findlinge und Baumstümpfe in den Tälern, Schiffe und Fässer im Hafen,
    /// Container, Flutlicht und Zäune im Industriegebiet, Laternen, Bänke und Hydranten in der Stadt.
    ///
    /// Gesetzt wird nur auf Bodenhöhe (Raycast von oben, nicht auf Dächer oder Brücken, nicht ins
    /// Wasser) und mit Abstand zu Gates, deren Flugroute, Sammelringen, Checkpoints und dem Start.
    /// Läuft nach den Strecken, damit diese bekannt sind. Fehlen die Vorlagen, passiert nichts.
    /// </summary>
    public static class IslandDressing
    {
        const float PointClearance = 30f;
        const float LineClearance = 18f;

        static readonly List<Vector3> keepClear = new List<Vector3>();
        static readonly List<Vector3> lineFrom = new List<Vector3>();
        static readonly List<Vector3> lineTo = new List<Vector3>();
        static System.Random rng;
        static int placed;
        static int rejected;

        static readonly IslandShape.Zone[] Zones =
        {
            IslandShape.Industry, IslandShape.City, IslandShape.ValleyVillage,
            IslandShape.MountainVillage, IslandShape.Harbor, IslandShape.FishingVillage,
        };

        public static void Build(Transform world, IList<RaceTrack> tracks, Transform rings, Transform checkpoints,
            Vector3 spawn, Vector3 pierStart, Vector3 pierDirection)
        {
            if (!Vorlagen.Available) return;
            rng = new System.Random(1311);
            placed = 0;
            rejected = 0;
            CollectClearance(tracks, rings, checkpoints, spawn);
            Physics.SyncTransforms();

            var root = new GameObject("Ausstattung (Poly Haven, CC0)").transform;
            root.SetParent(world, false);

            Coast(Group(root, "Küste: Felsen und Treibholz"));
            Mountains(Group(root, "Berge: Felswände"));
            Meadows(Group(root, "Täler: Findlinge und Baumstümpfe"));
            Harbor(Group(root, "Hafen: Schiffe und Fässer"), pierStart, pierDirection);
            Industry(Group(root, "Industriegebiet: Container, Licht, Zäune"));
            City(Group(root, "Stadt: Laternen, Bänke, Hydranten"));
            Village(Group(root, "Fischerdorf"), IslandShape.FishingVillage);
            Village(Group(root, "Tal-Dorf"), IslandShape.ValleyVillage);
            Village(Group(root, "Bergdorf"), IslandShape.MountainVillage);

            Debug.Log("Frostline: Ausstattung aus Vorlagen: " + placed + " Modelle gesetzt, " + rejected + " Stellen verworfen.");
        }

        // ------------------------------------------------------------ Bereiche

        static void Coast(Transform parent)
        {
            const int Steps = 140;
            for (int i = 0; i < Steps; i++)
            {
                float a = i * Mathf.PI * 2f / Steps + Range(-0.01f, 0.01f);
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                Vector3 coast = IslandShape.CoastPoint(IslandShape.Center + dir * 2300f, IslandShape.Center, 0.8f);
                if (NearZone(coast.x, coast.z, 50f)) continue;
                float inland = Range(4f, 24f);
                float x = coast.x - dir.x * inland;
                float z = coast.z - dir.y * inland;
                switch (i % 5)
                {
                    case 0:
                    case 2:
                        TryPlace("rock_moss_set_01", parent, x, z, Range(0.9f, 2f), 8f, 0.008f, 40f, 0.4f);
                        break;
                    case 1:
                    case 3:
                        TryPlace("boulder_01", parent, x, z, Range(1.6f, 3.4f), 3f, 0.02f, 40f, 0.3f);
                        break;
                    default:
                        TryPlace("dead_tree_trunk", parent, x, z, Range(1f, 1.7f), 3f, 0.02f, 25f, 0.05f);
                        break;
                }
            }
        }

        static void Mountains(Transform parent)
        {
            int count = 0;
            for (int i = 0; i < 6000 && count < 150; i++)
            {
                float x = Range(150f, 3850f);
                float z = Range(150f, 3850f);
                float h = IslandShape.Height(x, z);
                if (h < 120f || h > 1150f || Slope(x, z) < 24f || NearZone(x, z, 60f)) continue;
                if (TryPlace("rock_face_01", parent, x, z, Range(1.4f, 3.8f), 12f, 0.006f, 80f, 1.5f, 2f) != null) count++;
            }
        }

        static void Meadows(Transform parent)
        {
            int boulders = 0, stumps = 0;
            for (int i = 0; i < 6000 && (boulders < 60 || stumps < 40); i++)
            {
                float x = Range(150f, 3850f);
                float z = Range(150f, 3850f);
                float h = IslandShape.Height(x, z);
                if (h < 6f || h > 320f || Slope(x, z) > 18f || NearZone(x, z, 40f)) continue;
                if (boulders < 60 && h < 180f)
                {
                    if (TryPlace("boulder_01", parent, x, z, Range(1.5f, 3.6f), 3f, 0.02f, 18f, 0.3f) != null) boulders++;
                }
                else if (stumps < 40)
                {
                    if (TryPlace("tree_stump_01", parent, x, z, Range(0.9f, 1.4f), 2f, 0.03f, 18f, 0.1f) != null) stumps++;
                }
            }
        }

        static void Harbor(Transform parent, Vector3 pierStart, Vector3 pierDirection)
        {
            if (pierDirection.sqrMagnitude > 0.01f)
            {
                // Zwei Segelschiffe liegen links und rechts neben dem Pier im Wasser.
                Vector3 side = Vector3.Cross(Vector3.up, pierDirection);
                float yaw = Quaternion.LookRotation(pierDirection).eulerAngles.y;
                for (int i = 0; i < 2; i++)
                {
                    Vector3 p = pierStart + pierDirection * (45f + i * 50f) + side * (i == 0 ? 21f : -21f);
                    p.y = 0f;
                    if (!IsClear(p, 14f))
                    {
                        rejected++;
                        continue;
                    }
                    if (Vorlagen.Place("dutch_ship_medium", parent, p, yaw + Range(-6f, 6f), 1f, true, 0.004f, 2.2f) != null) placed++;
                }
            }
            Clusters(parent, IslandShape.Harbor, "Barrel_01", 7, 4, 1f, 1.15f, 3f, 1f, 0.03f);
            Clusters(parent, IslandShape.Harbor, "industrial_pastic_container", 5, 3, 1.6f, 2.2f, 3f, 1f, 0.03f);
        }

        static void Industry(Transform parent)
        {
            IslandShape.Zone zone = IslandShape.Industry;
            Clusters(parent, zone, "Barrel_01", 9, 4, 1f, 1.15f, 3f, 1f, 0.03f);
            Clusters(parent, zone, "industrial_pastic_container", 7, 3, 1.6f, 2.4f, 3f, 1f, 0.03f);
            Scatter(parent, zone, "utility_box_01", 12, 1.3f, 1.6f, 1f, 0.03f);
            Scatter(parent, zone, "flood_lights", 8, 1.2f, 1.5f, 3f, 0.01f);
            // Poly Havens Zaun und Bänke sind Baukästen mit ausgelegten Einzelteilen, keine fertigen Objekte;
            // sie lägen flach am Boden und werden deshalb nicht verwendet.
        }

        static void City(Transform parent)
        {
            IslandShape.Zone zone = IslandShape.City;
            foreach (float r in new[] { 60f, 120f, 180f, 240f })
            {
                int n = Mathf.RoundToInt(2f * Mathf.PI * r / 30f);
                float offset = Range(0f, 1f);
                for (int i = 0; i < n; i++)
                {
                    float a = (i + offset) * Mathf.PI * 2f / n;
                    TryPlace("street_lamp_01", parent, zone.Position.x + Mathf.Cos(a) * r, zone.Position.y + Mathf.Sin(a) * r,
                        1f, 1.5f, 0.01f, 10f, 0.05f, 0f, -a * Mathf.Rad2Deg, 0.6f);
                }
            }
            Scatter(parent, zone, "fire_hydrant", 12, 1f, 1f, 1f, 0.04f);
            Scatter(parent, zone, "utility_box_01", 10, 1.3f, 1.6f, 1f, 0.03f);
        }

        static void Village(Transform parent, IslandShape.Zone zone)
        {
            Clusters(parent, zone, "Barrel_01", 3, 3, 1f, 1.15f, 2.5f, 1f, 0.03f);
            Scatter(parent, zone, "tree_stump_01", 6, 0.9f, 1.3f, 2f, 0.03f);
            Scatter(parent, zone, "street_lamp_01", 6, 1f, 1f, 1.5f, 0.01f);
        }

        // ------------------------------------------------------------ Hilfen

        static void Scatter(Transform parent, IslandShape.Zone zone, string id, int count, float scaleMin, float scaleMax,
            float radius, float cull)
        {
            for (int i = 0; i < count; i++)
            {
                float a = Range(0f, Mathf.PI * 2f);
                float d = Mathf.Sqrt(Range(0f, 1f)) * zone.Radius * 0.92f;
                TryPlace(id, parent, zone.Position.x + Mathf.Cos(a) * d, zone.Position.y + Mathf.Sin(a) * d,
                    Range(scaleMin, scaleMax), radius, cull, 12f, 0.03f, 0f, float.NaN, 0.6f);
            }
        }

        static void Clusters(Transform parent, IslandShape.Zone zone, string id, int clusters, int perCluster,
            float scaleMin, float scaleMax, float spread, float radius, float cull)
        {
            for (int c = 0; c < clusters; c++)
            {
                float a = Range(0f, Mathf.PI * 2f);
                float d = Mathf.Sqrt(Range(0f, 1f)) * zone.Radius * 0.9f;
                float cx = zone.Position.x + Mathf.Cos(a) * d;
                float cz = zone.Position.y + Mathf.Sin(a) * d;
                for (int i = 0; i < perCluster; i++)
                {
                    TryPlace(id, parent, cx + Range(-spread, spread), cz + Range(-spread, spread),
                        Range(scaleMin, scaleMax), radius, cull, 12f, 0.02f, 0f, float.NaN, 0.6f);
                }
            }
        }

        /// <summary>Setzt ein Modell, wenn die Stelle Boden ist, flach genug und frei von Flugrouten.</summary>
        static GameObject TryPlace(string id, Transform parent, float x, float z, float scale, float radius, float cull,
            float maxSlope, float sink = 0.1f, float tilt = 0f, float yaw = float.NaN, float maxAboveTerrain = 1.5f)
        {
            Vector3 point;
            float slope;
            if (!GroundAt(x, z, maxAboveTerrain, out point, out slope) || slope > maxSlope || !IsClear(point, radius * scale))
            {
                rejected++;
                return null;
            }
            GameObject go = Vorlagen.Place(id, parent, point, float.IsNaN(yaw) ? Range(0f, 360f) : yaw, scale, true, cull, sink, tilt);
            if (go == null)
            {
                rejected++;
                return null;
            }
            placed++;
            // Folgende Raycasts sollen das neue Modell sehen (nichts übereinander stapeln).
            Physics.SyncTransforms();
            return go;
        }

        /// <summary>Boden an (x, z): erster Treffer von oben, höchstens knapp über dem Gelände, nicht im Wasser.</summary>
        static bool GroundAt(float x, float z, float maxAboveTerrain, out Vector3 point, out float slope)
        {
            point = Vector3.zero;
            slope = 90f;
            RaycastHit hit;
            if (!Physics.Raycast(new Vector3(x, 3000f, z), Vector3.down, out hit, 4000f, ~0, QueryTriggerInteraction.Ignore)) return false;
            if (hit.point.y < 0.6f) return false;
            if (hit.point.y - IslandShape.Height(x, z) > maxAboveTerrain) return false;
            point = hit.point;
            slope = Vector3.Angle(hit.normal, Vector3.up);
            return true;
        }

        static void CollectClearance(IList<RaceTrack> tracks, Transform rings, Transform checkpoints, Vector3 spawn)
        {
            keepClear.Clear();
            lineFrom.Clear();
            lineTo.Clear();
            keepClear.Add(spawn);
            foreach (RaceTrack track in tracks)
            {
                if (track == null) continue;
                Vector3 previous = track.startPoint != null ? track.startPoint.position : track.transform.position;
                keepClear.Add(previous);
                foreach (Transform child in track.transform)
                {
                    if (child.GetComponent<RaceGate>() == null) continue;
                    keepClear.Add(child.position);
                    lineFrom.Add(previous);
                    lineTo.Add(child.position);
                    previous = child.position;
                }
            }
            if (rings != null) foreach (Transform r in rings) keepClear.Add(r.position);
            if (checkpoints != null) foreach (Transform c in checkpoints) keepClear.Add(c.position);
        }

        static bool IsClear(Vector3 p, float radius)
        {
            float point = PointClearance + radius;
            foreach (Vector3 k in keepClear)
                if ((k - p).sqrMagnitude < point * point) return false;
            float line = LineClearance + radius;
            for (int i = 0; i < lineFrom.Count; i++)
                if (DistanceToSegment(p, lineFrom[i], lineTo[i]) < line) return false;
            return true;
        }

        static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 1e-4f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            return Vector3.Distance(p, a + ab * t);
        }

        static bool NearZone(float x, float z, float margin)
        {
            foreach (IslandShape.Zone zone in Zones)
                if (Vector2.Distance(new Vector2(x, z), zone.Position) < zone.Radius + margin) return true;
            return false;
        }

        static float Slope(float x, float z)
        {
            const float d = 3f;
            float dx = (IslandShape.Height(x + d, z) - IslandShape.Height(x - d, z)) / (2f * d);
            float dz = (IslandShape.Height(x, z + d) - IslandShape.Height(x, z - d)) / (2f * d);
            return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
        }

        static Transform Group(Transform root, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(root, false);
            return t;
        }

        static float Range(float min, float max)
        {
            return min + (float)rng.NextDouble() * (max - min);
        }
    }
}
