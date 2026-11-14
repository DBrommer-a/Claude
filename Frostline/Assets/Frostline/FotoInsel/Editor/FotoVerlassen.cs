using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Frostline.EditorTools;

namespace Frostline.FotoInsel
{
    /// <summary>
    /// Ausstattung der verlassenen Insel: organisch gewachsenes Wikingerdorf (Viking Village), ein
    /// begehbarer Betonrohbau mit Oberflächen und Schutt aus dem Fab-Paket „Unfinished Building“,
    /// Palmen am Traumstrand. Alles verlassen: keine Feuer, lückenhafte Palisade, gestrandete Boote.
    /// </summary>
    public static class FotoVerlassen
    {
        const string Viking = "Assets/Viking Village/Prefabs/";
        const string FabRoot = "Assets/Fab/UnfinishedBuilding";
        static FotoTerrain t;
        static System.Random rnd;
        static readonly Dictionary<GameObject, Bounds> bounds = new Dictionary<GameObject, Bounds>();
        public static string Report = "";

        static float R(float a, float b) { return a + (float)rnd.NextDouble() * (b - a); }
        static bool Chance(float p) { return rnd.NextDouble() < p; }

        static GameObject Load(string path) { return AssetDatabase.LoadAssetAtPath<GameObject>(path); }

        static Bounds BoundsOf(GameObject asset)
        {
            Bounds b;
            if (bounds.TryGetValue(asset, out b)) return b;
            var inst = (GameObject)Object.Instantiate(asset);
            inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Renderer[] rs = inst.GetComponentsInChildren<Renderer>();
            b = rs.Length > 0 ? rs[0].bounds : new Bounds(Vector3.zero, Vector3.one);
            foreach (Renderer r in rs) b.Encapsulate(r.bounds);
            Object.DestroyImmediate(inst);
            bounds[asset] = b;
            return b;
        }

        /// <summary>Setzt ein Asset mit der Unterkante auf <paramref name="ground"/>; entfernt fehlende Skripte (aus dem URP-Beispielprojekt).</summary>
        static GameObject Put(GameObject asset, Transform parent, Vector3 ground, Quaternion rotation, float scale, Material material = null)
        {
            if (asset == null) return null;
            Bounds b = BoundsOf(asset);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            if (go == null) go = (GameObject)Object.Instantiate(asset, parent);
            go.transform.rotation = rotation;
            go.transform.localScale = Vector3.one * scale;
            go.transform.position = ground + rotation * new Vector3(-b.center.x, -b.min.y, -b.center.z) * scale;
            foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
            {
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);
                foreach (ParticleSystem ps in child.GetComponents<ParticleSystem>())
                {
                    var emission = ps.emission;
                    emission.enabled = false;   // verlassen: kein Feuer, kein Rauch
                }
                foreach (Light l in child.GetComponents<Light>()) l.enabled = false;   // verlassen: kein Feuer, kein Licht
                foreach (Renderer r in child.GetComponents<Renderer>())
                {
                    // Materialien mit fehlendem URP-Shader (pink) durch ein HDRP-Pflanzenmaterial ersetzen.
                    Material[] current = r.sharedMaterials;
                    bool changed = false;
                    for (int i = 0; i < current.Length; i++)
                        if (current[i] == null || current[i].shader == null || current[i].shader.name.Contains("InternalError"))
                        {
                            current[i] = Fallback();
                            changed = true;
                        }
                    if (changed) r.sharedMaterials = current;
                }
                if (material != null)
                    foreach (Renderer r in child.GetComponents<Renderer>())
                    {
                        var mats = new Material[r.sharedMaterials.Length];
                        for (int i = 0; i < mats.Length; i++) mats[i] = material;
                        r.sharedMaterials = mats;
                    }
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            }
            return go;
        }

        static Material fallback;

        static Material Fallback()
        {
            if (fallback != null) return fallback;
            fallback = FotoBaeume.NewLit("Ersatz_Pflanze");
            fallback.SetColor("_BaseColor", new Color(0.22f, 0.32f, 0.13f));
            fallback.SetFloat("_Smoothness", 0.2f);
            HDMaterial.ValidateMaterial(fallback);
            return fallback;
        }

        static float GroundMin(Vector2 c, float radius)
        {
            float min = t.HeightAt(c);
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                min = Mathf.Min(min, t.HeightAt(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius));
            }
            return min;
        }

        static float GroundRange(Vector2 c, float radius)
        {
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                float h = t.HeightAt(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
                min = Mathf.Min(min, h); max = Mathf.Max(max, h);
            }
            return max - min;
        }

        public static void Build(FotoTerrain terrain, Transform parent)
        {
            t = terrain;
            rnd = new System.Random(1066);
            bounds.Clear();
            fallback = null;
            Report = "";
            BuildVillage(new GameObject("Wikingerdorf (verlassen)").transform, parent);
            BuildRohbau(new GameObject("Rohbau (Beton)").transform, parent);
            BuildBeach(new GameObject("Traumstrand").transform, parent);
            BuildRiverBanks(new GameObject("Ufer am Hauptfluss").transform, parent);
        }

        // ------------------------------------------------------------ Ufer

        /// <summary>Farne, Gräser, Büsche und Steine entlang des Hauptflusses, einige Steine im Wasser.</summary>
        static void BuildRiverBanks(Transform root, Transform parent)
        {
            root.SetParent(parent, false);
            if (t.MainRiver < 0) return;
            FotoRiver river = t.Rivers[t.MainRiver];
            var plants = new List<GameObject>();
            foreach (string f in new[] { "Ferns", "MeadowGrass_01", "BroadleafShrub_01", "GreenBush", "Plant_Perennials" })
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Viking Village/Book of the Dead/Vegetation/" + f }))
                {
                    GameObject g = Load(AssetDatabase.GUIDToAssetPath(guid));
                    if (g != null) plants.Add(g);
                }
            var stones = new List<GameObject>();
            foreach (string name in new[] { "pf_boulder_01_040", "pf_boulder_02_025" })
            {
                GameObject g = Load(Viking + "Props/" + name + ".prefab");
                if (g != null) stones.Add(g);
            }
            GameObject moss = Vorlagen.Model("rock_moss_set_01");
            if (moss != null) stones.Add(moss);
            int nPlants = 0, nStones = 0;
            for (int i = 2; i < river.Points.Count - 16; i += 2)
            {
                Vector2 p = river.Points[i];
                Vector2 dir = (river.Points[i + 1] - river.Points[i - 1]).normalized;
                Vector2 side = new Vector2(-dir.y, dir.x);
                float hw = river.HalfWidth[i];
                foreach (int s in new[] { -1, 1 })
                {
                    if (plants.Count > 0 && Chance(0.85f))
                    {
                        Vector2 q = p + side * s * (hw + R(1.2f, 7f)) + dir * R(-3f, 3f);
                        float h = t.HeightAt(q);
                        if (h > river.Surface[i] + 0.1f)
                        {
                            Put(plants[rnd.Next(plants.Count)], root, new Vector3(q.x, h - 0.05f, q.y), Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.9f, 1.6f));
                            nPlants++;
                        }
                    }
                    if (stones.Count > 0 && Chance(0.12f))
                    {
                        // Steine am Rand, manche halb im Wasser.
                        Vector2 q = p + side * s * (hw + R(-1.5f, 2.5f));
                        float h = Mathf.Max(t.HeightAt(q), river.Surface[i] - 0.6f);
                        Put(stones[rnd.Next(stones.Count)], root, new Vector3(q.x, h - 0.3f, q.y), Quaternion.Euler(R(-12f, 12f), R(0f, 360f), R(-12f, 12f)), R(0.8f, 2.2f));
                        nStones++;
                    }
                }
            }
            Report += "Hauptfluss: " + nPlants + " Uferpflanzen, " + nStones + " Steine\n";
        }

        // ------------------------------------------------------------ Wikingerdorf

        struct Placed { public Vector2 P; public float R; }

        static bool Free(List<Placed> placed, Vector2 p, float r)
        {
            foreach (Placed q in placed) if ((q.P - p).sqrMagnitude < (q.R + r) * (q.R + r)) return false;
            return true;
        }

        static bool Buildable(Vector2 p, float radius)
        {
            float h = t.HeightAt(p);
            if (h < 2.2f || h > 120f) return false;
            if (t.MaskAt(t.WaterMask, p.x, p.y) > 40) return false;
            if (t.SlopeAt(p.x, p.y) > 16f) return false;
            return GroundRange(p, radius) < 4.5f;
        }

        static void BuildVillage(Transform root, Transform parent)
        {
            root.SetParent(parent, false);
            FotoZone z = t.Town;
            // Mittelpunkt: flachste Stelle nahe der Zonenmitte.
            Vector2 center = z.Center;
            float best = float.MaxValue;
            for (int i = 0; i < 200; i++)
            {
                Vector2 p = z.Center + new Vector2(R(-140f, 140f), R(-120f, 120f));
                float h = t.HeightAt(p);
                if (h < 4f) continue;
                float score = t.SlopeAt(p.x, p.y) * 3f + GroundRange(p, 14f) * 4f + Mathf.Abs(h - 14f) * 0.2f;
                if (score < best) { best = score; center = p; }
            }

            string[] houses =
            {
                "pf_build_small_house_01", "pf_build_small_house_straw_roof_01", "pf_build_small_house_tall_roof_01",
                "pf_build_small_house_01", "pf_build_small_house_straw_roof_01", "pf_build_bighouse_02",
                "pf_build_bighouse_01", "pf_build_barracks_single_01", "pf_build_storage_01", "pf_build_barracks_01",
            };
            var placed = new List<Placed>();
            int count = 0;

            // Langhaus in der Mitte, Giebel zum Meer.
            Vector2 toSea = (t.CoastPoint(Mathf.Atan2((center - FotoTerrain.Center).y, (center - FotoTerrain.Center).x) * Mathf.Rad2Deg, out _) - center).normalized;
            float seaYaw = Mathf.Atan2(toSea.x, toSea.y) * Mathf.Rad2Deg;
            GameObject hall = Load(Viking + "Buildings/pf_build_big_storage_01.prefab");
            if (hall != null)
            {
                Put(hall, root, new Vector3(center.x, GroundMin(center, 12f) - 0.3f, center.y), Quaternion.Euler(0f, seaYaw + R(-8f, 8f), 0f), 1f);
                placed.Add(new Placed { P = center, R = 15f });
                count++;
            }

            // Wege: vom Platz aus, dem Gelände folgend (geringste Steigung), leicht mäandernd.
            var paths = new List<List<Vector2>>();
            float baseAngle = Mathf.Atan2(toSea.y, toSea.x);
            for (int w = 0; w < 6; w++)
            {
                float heading = baseAngle + Mathf.PI * (0.3f + w * 0.28f) + R(-0.2f, 0.2f);
                var path = new List<Vector2> { center + new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * 18f };
                for (int step = 0; step < 8; step++)
                {
                    Vector2 last = path[path.Count - 1];
                    float bestH = 0f, bestScore = float.MaxValue;
                    for (int c = -2; c <= 2; c++)
                    {
                        float hd = heading + c * 0.28f + R(-0.08f, 0.08f);
                        Vector2 p = last + new Vector2(Mathf.Cos(hd), Mathf.Sin(hd)) * 15f;
                        float score = Mathf.Abs(t.HeightAt(p) - t.HeightAt(last)) + (t.HeightAt(p) < 2.5f ? 100f : 0f) + Mathf.Abs(c) * 0.6f;
                        if (score < bestScore) { bestScore = score; bestH = hd; }
                    }
                    heading = bestH;
                    Vector2 next = last + new Vector2(Mathf.Cos(heading), Mathf.Sin(heading)) * 15f;
                    if (t.HeightAt(next) < 2.5f || (next - center).magnitude > 170f) break;
                    path.Add(next);
                }
                paths.Add(path);
            }

            // Häuser entlang der Wege, mal links, mal rechts, längs zum Weg gedreht.
            foreach (List<Vector2> path in paths)
            {
                for (int i = 1; i < path.Count; i++)
                {
                    Vector2 dir = (path[i] - path[i - 1]).normalized;
                    Vector2 side = new Vector2(-dir.y, dir.x);
                    foreach (int s in new[] { -1, 1 })
                    {
                        if (!Chance(0.85f)) continue;
                        GameObject asset = Load(Viking + "Buildings/" + houses[rnd.Next(houses.Length)] + ".prefab");
                        if (asset == null) continue;
                        Bounds b = BoundsOf(asset);
                        float radius = Mathf.Max(b.size.x, b.size.z) * 0.5f + 1.5f;
                        Vector2 p = path[i] + side * s * (radius + R(2.5f, 6f)) + dir * R(-4f, 4f);
                        if (!Free(placed, p, radius) || !Buildable(p, radius * 0.8f)) continue;
                        bool onPath = false;
                        foreach (List<Vector2> other in paths)
                            foreach (Vector2 q in other)
                                if ((q - p).sqrMagnitude < (radius + 2f) * (radius + 2f)) onPath = true;
                        if (onPath) continue;
                        float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg + (Chance(0.5f) ? 90f : -90f) + R(-14f, 14f);
                        Put(asset, root, new Vector3(p.x, GroundMin(p, radius * 0.7f) - 0.35f, p.y), Quaternion.Euler(0f, yaw, 0f), 1f);
                        placed.Add(new Placed { P = p, R = radius });
                        count++;
                        Props(root, p, radius, placed);
                    }
                }
            }

            // Schmiede und Wachtürme am Rand.
            foreach (string special in new[] { "pf_build_blacksmith_01", "pf_build_tower_01", "pf_build_tower_01", "pf_build_crane_01" })
            {
                GameObject asset = Load(Viking + "Buildings/" + special + ".prefab");
                if (asset == null) continue;
                Bounds b = BoundsOf(asset);
                float radius = Mathf.Max(b.size.x, b.size.z) * 0.5f + 1.5f;
                for (int attempt = 0; attempt < 60; attempt++)
                {
                    float a = R(0f, Mathf.PI * 2f);
                    Vector2 p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(60f, 150f);
                    if (!Free(placed, p, radius) || !Buildable(p, radius * 0.8f)) continue;
                    Put(asset, root, new Vector3(p.x, GroundMin(p, radius * 0.7f) - 0.3f, p.y), Quaternion.Euler(0f, R(0f, 360f), 0f), 1f);
                    placed.Add(new Placed { P = p, R = radius });
                    count++;
                    break;
                }
            }

            int wall = Palisade(root, center, paths, placed);
            int boats = Boats(root, center, toSea);
            int plants = Overgrowth(root, center, placed, paths);
            Report += "Wikingerdorf: " + count + " Gebäude, " + wall + " Palisadenteile, " + boats + " Boote, " + plants + " Pflanzen\n";
        }

        static void Props(Transform root, Vector2 house, float radius, List<Placed> placed)
        {
            string[] props =
            {
                "pf_barrels_02", "pf_logpile_01", "pf_plankpile_01", "pf_plankpile_02", "pf_plankpile_03", "pf_fence_01",
                "pf_fence_01_double", "pf_shed_01", "pf_shed_02", "pf_buckets_01", "pf_wall_logs_04", "pf_fish_stick_01",
                "pf_plank_01", "pf_boulder_01_040", "pf_boulder_02_025",
            };
            int n = rnd.Next(1, 4);
            for (int i = 0; i < n; i++)
            {
                GameObject asset = Load(Viking + "Props/" + props[rnd.Next(props.Length)] + ".prefab");
                if (asset == null) continue;
                float a = R(0f, Mathf.PI * 2f);
                Vector2 p = house + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (radius + R(1.5f, 5f));
                if (!Free(placed, p, 1.2f)) continue;
                // Verlassen: manches umgekippt.
                Quaternion rot = Quaternion.Euler(Chance(0.2f) ? R(-25f, 25f) : 0f, R(0f, 360f), Chance(0.15f) ? R(-20f, 20f) : 0f);
                Put(asset, root, new Vector3(p.x, t.HeightAt(p) - 0.1f, p.y), rot, 1f);
            }
        }

        static int Palisade(Transform root, Vector2 center, List<List<Vector2>> paths, List<Placed> placed)
        {
            GameObject panel = Load(Viking + "Buildings/pf_build_wall_panel_01.prefab");
            GameObject gate = Load(Viking + "Buildings/pf_build_gate_01.prefab");
            if (panel == null) return 0;
            float len = Mathf.Max(BoundsOf(panel).size.x, BoundsOf(panel).size.z) * 0.92f;
            float radius = 0f;
            foreach (Placed p in placed) radius = Mathf.Max(radius, (p.P - center).magnitude + p.R);
            radius += 14f;
            int n = 0;
            float step = len / radius;
            for (float a = 0f; a < Mathf.PI * 2f; a += step)
            {
                Vector2 p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * (1f + 0.06f * Mathf.Sin(a * 3f + 1f));
                float h = t.HeightAt(p);
                if (h < 2.5f || t.SlopeAt(p.x, p.y) > 24f || t.MaskAt(t.WaterMask, p.x, p.y) > 0) continue;
                bool pathCross = false;
                foreach (List<Vector2> path in paths)
                    foreach (Vector2 q in path)
                        if ((q - p).sqrMagnitude < 36f) pathCross = true;
                Vector2 tangent = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));
                float yaw = Mathf.Atan2(tangent.x, tangent.y) * Mathf.Rad2Deg + 90f;
                if (pathCross)
                {
                    if (gate != null && Chance(0.6f)) Put(gate, root, new Vector3(p.x, h - 0.4f, p.y), Quaternion.Euler(0f, yaw, 0f), 1f);
                    continue;
                }
                if (Chance(0.22f)) continue;   // eingefallen
                float lean = Chance(0.2f) ? R(-22f, 22f) : R(-3f, 3f);
                Put(panel, root, new Vector3(p.x, h - 0.6f, p.y), Quaternion.Euler(lean, yaw, 0f), 1f);
                n++;
            }
            return n;
        }

        static int Boats(Transform root, Vector2 center, Vector2 toSea)
        {
            int n = 0;
            float angle = Mathf.Atan2(toSea.y, toSea.x) * Mathf.Rad2Deg;
            foreach (string name in new[] { "pf_build_boat_02", "pf_build_boat_01", "pf_build_boat_01" })
            {
                GameObject boat = Load(Viking + "Buildings/" + name + ".prefab");
                if (boat == null) continue;
                Vector2 outward;
                Vector2 shore = t.CoastPoint(angle + R(-6f, 6f), out outward);
                Vector2 p = shore - outward * R(4f, 12f) + new Vector2(-outward.y, outward.x) * R(-25f, 25f);
                float yaw = Mathf.Atan2(outward.x, outward.y) * Mathf.Rad2Deg + R(-30f, 30f);
                // Gestrandet und zur Seite gekippt.
                Put(boat, root, new Vector3(p.x, Mathf.Max(0.1f, t.HeightAt(p)) - 0.4f, p.y), Quaternion.Euler(0f, yaw, R(8f, 24f)), 1f);
                n++;
            }
            return n;
        }

        static int Overgrowth(Transform root, Vector2 center, List<Placed> placed, List<List<Vector2>> paths)
        {
            string[] folders = { "Ferns", "MeadowGrass_01", "BroadleafShrub_01", "GreenBush", "Plant_Perennials" };
            var plants = new List<GameObject>();
            foreach (string f in folders)
                foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Viking Village/Book of the Dead/Vegetation/" + f }))
                {
                    GameObject g = Load(AssetDatabase.GUIDToAssetPath(guid));
                    if (g != null) plants.Add(g);
                }
            if (plants.Count == 0) return 0;
            int n = 0;
            for (int i = 0; i < 1400 && n < 650; i++)
            {
                float a = R(0f, Mathf.PI * 2f);
                Vector2 p = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Mathf.Sqrt((float)rnd.NextDouble()) * 190f;
                float h = t.HeightAt(p);
                if (h < 2f || t.SlopeAt(p.x, p.y) > 30f) continue;
                bool blocked = false;
                foreach (Placed q in placed) if ((q.P - p).sqrMagnitude < (q.R * 0.75f) * (q.R * 0.75f)) blocked = true;
                if (blocked) continue;
                Put(plants[rnd.Next(plants.Count)], root, new Vector3(p.x, h - 0.05f, p.y), Quaternion.Euler(0f, R(0f, 360f), 0f), R(0.8f, 1.4f));
                n++;
            }
            return n;
        }

        // ------------------------------------------------------------ Rohbau

        static Material FabMaterial(string id, float tile)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(FabRoot + "/" + id + "/M_" + id + ".mat");
            if (m == null) return null;
            m.SetTextureScale("_BaseColorMap", Vector2.one / tile);
            m.SetFloat("_DoubleSidedEnable", 1f);
            m.SetFloat("_CullMode", 0f);
            m.SetFloat("_CullModeForward", 0f);
            m.enableInstancing = true;
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Rust()
        {
            string path = FotoBauten.Folder + "/M_Bewehrung.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("HDRP/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", new Color(0.33f, 0.17f, 0.09f));
            m.SetFloat("_Smoothness", 0.25f);
            m.SetFloat("_Metallic", 0.6f);
            HDMaterial.ValidateMaterial(m);
            return m;
        }

        static void BuildRohbau(Transform root, Transform parent)
        {
            root.SetParent(parent, false);
            FotoZone z = t.Rohbau;
            if (z == null) return;
            Material column = FabMaterial("ug3sbcady", 2.5f) ?? FabMaterial("ugtmdgvew", 2.5f);
            Material slab = FabMaterial("ugkjec3ew", 3f) ?? column;
            Material slabUnder = FabMaterial("uftnbcofw", 2.5f) ?? slab;
            Material blocks = FabMaterial("ugsifevlw", 2f) ?? column;
            Material core = FabMaterial("ui1obbtew", 2.5f) ?? column;
            Material rebar = Rust();
            if (column == null) { Report += "Rohbau: Fab-Materialien fehlen\n"; return; }

            // Auf den tatsächlichen Boden stellen (Wege und Absenkungen können ihn verschoben haben).
            float ground = t.HeightAt(z.Center);
            for (int c = 0; c < 4; c++) ground = Mathf.Min(ground, t.HeightAt(z.ToWorld(new Vector2(c % 2 == 0 ? -12f : 12f, c < 2 ? -7f : 7f))));
            var kit = new FotoKit { Frame = Matrix4x4.TRS(new Vector3(z.Center.x, ground + 0.05f, z.Center.y), z.Rotation(), Vector3.one) };
            const float floorH = 3.3f, slabT = 0.3f;
            const int floors = 5;
            float[] xs = { -12f, -4f, 4f, 12f };
            float[] zs = { -7f, 0f, 7f };
            // Treppenloch: x 6,4–12, z 4,4–7 (Lauf entlang +x).
            var stairHole = new Rect(6.4f, 4.4f, 5.6f, 2.6f);

            kit.Box(slab, new Vector3(0f, -3f, 0f), new Vector3(25.6f, 3.05f, 15.6f));   // Bodenplatte mit Fundament ins Erdreich
            for (int f = 0; f < floors; f++)
            {
                float y = f * floorH;
                bool top = f == floors - 1;
                foreach (float x in xs)
                {
                    foreach (float zz in zs)
                    {
                        if (top && Chance(0.45f))
                        {
                            // Nur Anschlusseisen ragen heraus.
                            for (int r = 0; r < 4; r++)
                                kit.Beam(rebar, new Vector3(x + (r % 2 == 0 ? -0.15f : 0.15f), y, zz + (r < 2 ? -0.15f : 0.15f)),
                                    new Vector3(x + (r % 2 == 0 ? -0.15f : 0.15f) + R(-0.25f, 0.25f), y + R(1.2f, 1.9f), zz + (r < 2 ? -0.15f : 0.15f) + R(-0.25f, 0.25f)), 0.025f);
                            continue;
                        }
                        kit.Box(column, new Vector3(x, y, zz), new Vector3(0.45f, floorH - slabT, 0.45f));
                        if (top)
                            for (int r = 0; r < 4; r++)
                                kit.Beam(rebar, new Vector3(x + (r % 2 == 0 ? -0.15f : 0.15f), y + floorH - slabT, zz + (r < 2 ? -0.15f : 0.15f)),
                                    new Vector3(x + (r % 2 == 0 ? -0.15f : 0.15f), y + floorH - slabT + R(0.8f, 1.6f), zz + (r < 2 ? -0.15f : 0.15f)), 0.025f);
                    }
                }

                // Decke darüber (oberste Decke nur zur Hälfte betoniert), mit Treppenloch.
                if (f < floors - 1)
                {
                    float sy = y + floorH - slabT;
                    float xEnd = f == floors - 2 ? 2f : 12.3f;
                    Slab(kit, slab, slabUnder, -12.3f, xEnd, -7.3f, 7.3f, sy, slabT, stairHole);
                    // Treppe von dieser Etage zur nächsten.
                    const int steps = 15;
                    float run = stairHole.width / steps, rise = floorH / steps;
                    for (int s = 0; s < steps; s++)
                        kit.Box(core, new Vector3(stairHole.xMin + (s + 0.5f) * run, y, stairHole.center.y), new Vector3(run, rise * (s + 1), stairHole.height - 0.2f));
                }

                // Ausfachung aus Betonsteinen mit Fensteröffnungen, oben immer lückenhafter.
                float infill = f == 0 ? 0.75f : (f == 1 ? 0.6f : (f == 2 ? 0.3f : 0.08f));
                for (int i = 0; i < xs.Length - 1; i++)
                {
                    foreach (float zz in new[] { -7f, 7f })
                        if (Chance(infill)) Wall(kit, blocks, new Vector3((xs[i] + xs[i + 1]) * 0.5f, y, zz), xs[i + 1] - xs[i] - 0.45f, floorH - slabT, true, Chance(0.75f));
                }
                for (int i = 0; i < zs.Length - 1; i++)
                {
                    foreach (float xx in new[] { -12f, 12f })
                        if (Chance(infill)) Wall(kit, blocks, new Vector3(xx, y, (zs[i] + zs[i + 1]) * 0.5f), zs[i + 1] - zs[i] - 0.45f, floorH - slabT, false, Chance(0.75f));
                }
            }
            // Treppenhauswand als Kern bis oben.
            kit.Box(core, new Vector3(9.2f, 0f, 7.35f), new Vector3(5.8f, floorH * (floors - 1), 0.25f));
            BuildKitAsset(kit, "Rohbau_Beton", root);

            // Schutt, Bretter, Eisen aus dem Fab-Paket auf den Etagen und drumherum.
            var debris = new List<KeyValuePair<GameObject, Material>>();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { FabRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string id = System.IO.Path.GetFileNameWithoutExtension(path);
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var mat = AssetDatabase.LoadAssetAtPath<Material>(System.IO.Path.GetDirectoryName(path) + "/M_" + id + ".mat");
                if (asset != null) debris.Add(new KeyValuePair<GameObject, Material>(asset, mat));
            }
            int pieces = 0;
            Matrix4x4 frame = kit.Frame;
            for (int i = 0; i < 140 && debris.Count > 0; i++)
            {
                var pick = debris[rnd.Next(debris.Count)];
                int f = rnd.Next(-1, floors - 1);   // -1 = um das Gebäude herum
                Vector3 local = f < 0
                    ? new Vector3(R(-30f, 30f), 0f, R(-24f, 24f))
                    : new Vector3(R(-11.5f, 11.5f), f * floorH, R(-6.8f, 6.8f));
                if (f < 0 && Mathf.Abs(local.x) < 13.5f && Mathf.Abs(local.z) < 8.5f) continue;
                if (f >= 0 && stairHole.Contains(new Vector2(local.x, local.z))) continue;
                Vector3 world = frame.MultiplyPoint3x4(local);
                if (f < 0) world.y = t.HeightAt(world.x, world.z);
                Put(pick.Key, root, world, Quaternion.Euler(0f, R(0f, 360f), 0f), 1f, pick.Value);
                pieces++;
            }
            Report += "Rohbau: 5 Geschosse begehbar (Treppen), " + pieces + " Schutt- und Bauteile aus dem Fab-Paket\n";
        }

        static void Slab(FotoKit kit, Material top, Material under, float x0, float x1, float z0, float z1, float y, float thickness, Rect hole)
        {
            // Decke als bis zu vier Platten um das Treppenloch herum.
            void Part(float a0, float a1, float b0, float b1)
            {
                if (a1 - a0 < 0.05f || b1 - b0 < 0.05f) return;
                kit.Box(top, new Vector3((a0 + a1) * 0.5f, y, (b0 + b1) * 0.5f), new Vector3(a1 - a0, thickness, b1 - b0), 0f, false);
            }
            float hx0 = Mathf.Clamp(hole.xMin, x0, x1), hx1 = Mathf.Clamp(hole.xMax, x0, x1);
            Part(x0, x1, z0, hole.yMin);
            Part(x0, x1, hole.yMax, z1);
            Part(x0, hx0, hole.yMin, hole.yMax);
            Part(hx1, x1, hole.yMin, hole.yMax);
        }

        static void Wall(FotoKit kit, Material m, Vector3 bottomCenter, float length, float height, bool alongX, bool window)
        {
            const float thick = 0.24f;
            Vector3 Size(float l, float h) { return alongX ? new Vector3(l, h, thick) : new Vector3(thick, h, l); }
            Vector3 Offset(float d) { return alongX ? new Vector3(d, 0f, 0f) : new Vector3(0f, 0f, d); }
            if (!window)
            {
                kit.Box(m, bottomCenter, Size(length, height));
                return;
            }
            float w = Mathf.Min(2.2f, length * 0.4f), sill = 0.95f, lintel = 2.3f;
            float side = (length - w) * 0.5f;
            kit.Box(m, bottomCenter + Offset(-(w + side) * 0.5f), Size(side, height));
            kit.Box(m, bottomCenter + Offset((w + side) * 0.5f), Size(side, height));
            kit.Box(m, bottomCenter, Size(w, sill));
            kit.Box(m, bottomCenter + Vector3.up * lintel, Size(w, height - lintel));
        }

        static void BuildKitAsset(FotoKit kit, string name, Transform parent)
        {
            GameObject go = kit.Build(name, parent, FotoBauten.Folder);
            go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
        }

        // ------------------------------------------------------------ Traumstrand

        static void BuildBeach(Transform root, Transform parent)
        {
            root.SetParent(parent, false);
            FotoZone z = t.Beach;
            if (z == null) return;
            var palms = new List<GameObject>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/PalmTreePack" }))
            {
                GameObject g = Load(AssetDatabase.GUIDToAssetPath(guid));
                if (g != null && BoundsOf(g).size.y < 30f) palms.Add(g);
            }
            int n = 0;
            for (int i = 0; i < 3000 && n < 90 && palms.Count > 0; i++)
            {
                Vector2 p = z.ToWorld(new Vector2(R(-z.Half.x, z.Half.x), R(-z.Half.y, z.Half.y + 60f)));
                float h = t.HeightAt(p);
                if (h < 1.6f || h > 14f || t.SlopeAt(p.x, p.y) > 14f) continue;
                if (t.MaskAt(t.RoadMask, p.x, p.y) > 0 || t.MaskAt(t.WaterMask, p.x, p.y) > 0) continue;
                // Zum Meer geneigt, wie echte Strandpalmen.
                Vector2 sea = z.ToWorld(new Vector2(0f, -1000f)) - z.Center;
                float yaw = Mathf.Atan2(sea.x, sea.y) * Mathf.Rad2Deg + R(-35f, 35f);
                Put(palms[rnd.Next(palms.Count)], root, new Vector3(p.x, h - 0.2f, p.y), Quaternion.Euler(0f, yaw, 0f) * Quaternion.Euler(R(3f, 14f), 0f, 0f), R(0.8f, 1.25f));
                n++;
            }
            Report += "Traumstrand: " + n + " Palmen\n";
        }
    }
}
