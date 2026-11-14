using System.Collections.Generic;
using Frostline.EditorTools;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Frostline.FotoInsel
{
    /// <summary>
    /// Bauten der Foto-Insel: Containerhafen, Raffinerie, Stahlwerk, Hafenstadt, Brücken, Strommasten,
    /// Windräder, Leuchtturm und Küstenrequisiten. Nimmt die Prefabs des Industrial Set, die Poly-Haven-
    /// Texturen und -Modelle (Vorlagen) und die eigenen Requisiten; große Bauwerke entstehen mit FotoKit.
    /// </summary>
    public static class FotoBauten
    {
        public static string Folder { get { return FotoInselBuilder.GeneratedFolder + (FotoTerrain.Verlassen ? "/Bauten_Verlassen" : "/Bauten"); } }
        const string Requisiten = "Assets/Frostline/Models/Requisiten/";

        static FotoTerrain t;
        static System.Random rnd;
        static readonly Dictionary<GameObject, Bounds> boundsCache = new Dictionary<GameObject, Bounds>();
        static int meshCounter;

        static Material concrete, concreteDark, pavers, rustSheet, rust, metal, brick, stone, planks, siding;
        static Material coal, ore, gravel, glass, white, red, craneBlue, orange, steel, galvanized, hullBlue, hullRed, hullGreen, tankWhite;
        static Material[] plaster, roofs;

        public static string Report = "";
        /// <summary>Verlassene Variante: Rost statt Lack, Dächer teils eingestürzt, keine Autos und Lichter.</summary>
        public static bool Verlassen;

        // ------------------------------------------------------------ Grundlagen

        static float R(float a, float b) { return a + (float)rnd.NextDouble() * (b - a); }
        static bool Chance(float p) { return rnd.NextDouble() < p; }

        static Material Paint(string name, Color color, float smoothness, float metallic = 0f)
        {
            string path = Folder + "/M_" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("HDRP/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetTexture("_BaseColorMap", null);
            m.SetTexture("_NormalMap", null);
            m.SetTexture("_MaskMap", null);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_DoubleSidedEnable", 1f);
            m.SetFloat("_CullMode", 0f);
            m.SetFloat("_CullModeForward", 0f);
            m.enableInstancing = true;
            HDMaterial.ValidateMaterial(m);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Tex(string id, Color tint, Material fallback)
        {
            Material m = Vorlagen.TextureMaterial(id, tint);
            if (m != null) m.enableInstancing = true;
            return m != null ? m : fallback;
        }

        static void CreateMaterials()
        {
            white = Paint("Weiss", new Color(0.82f, 0.83f, 0.82f), 0.45f);
            red = Paint("Rot", new Color(0.6f, 0.09f, 0.07f), 0.45f);
            craneBlue = Paint("Kranblau", new Color(0.1f, 0.27f, 0.5f), 0.4f, 0.2f);
            orange = Paint("Orange", new Color(0.85f, 0.42f, 0.08f), 0.4f);
            steel = Paint("Stahl", new Color(0.3f, 0.31f, 0.32f), 0.45f, 0.75f);
            galvanized = Paint("Verzinkt", new Color(0.55f, 0.57f, 0.58f), 0.55f, 0.85f);
            glass = Paint("Glas", new Color(0.06f, 0.08f, 0.1f), 0.9f, 0.2f);
            hullBlue = Paint("RumpfBlau", new Color(0.06f, 0.1f, 0.18f), 0.5f, 0.3f);
            hullRed = Paint("RumpfRot", new Color(0.42f, 0.07f, 0.05f), 0.4f, 0.2f);
            hullGreen = Paint("RumpfGruen", new Color(0.08f, 0.22f, 0.15f), 0.45f, 0.25f);
            tankWhite = Paint("TankWeiss", new Color(0.78f, 0.79f, 0.78f), 0.5f, 0.3f);

            concrete = Tex("concrete_panels", new Color(0.8f, 0.79f, 0.76f), white);
            concreteDark = Tex("concrete_panels", new Color(0.5f, 0.49f, 0.47f), steel);
            pavers = Tex("concrete_pavers", new Color(0.75f, 0.74f, 0.72f), concrete);
            rustSheet = Tex("rusty_metal_sheet", new Color(0.85f, 0.8f, 0.76f), steel);
            rust = Tex("rusty_metal_02", Color.white, steel);
            metal = Tex("metal_plate", new Color(0.72f, 0.74f, 0.76f), galvanized);
            brick = Tex("brick_wall_02", Color.white, red);
            stone = Tex("stone_wall", Color.white, concrete);
            planks = Tex("weathered_planks", Color.white, steel);
            siding = Tex("weathered_plank_siding", new Color(0.62f, 0.2f, 0.14f), red);
            coal = Tex("gray_rocks", new Color(0.17f, 0.17f, 0.18f), steel);
            ore = Tex("gray_rocks", new Color(0.62f, 0.34f, 0.22f), red);
            gravel = Tex("rock_face_03", new Color(0.75f, 0.72f, 0.68f), concrete);
            plaster = new[]
            {
                Tex("painted_plaster_wall", new Color(0.95f, 0.93f, 0.88f), white),
                Tex("painted_plaster_wall", new Color(0.93f, 0.85f, 0.68f), white),
                Tex("painted_plaster_wall", new Color(0.82f, 0.62f, 0.52f), white),
                Tex("painted_plaster_wall", new Color(0.78f, 0.82f, 0.8f), white),
                Tex("painted_plaster_wall", new Color(0.9f, 0.9f, 0.92f), white),
            };
            roofs = new[]
            {
                Tex("grey_roof_tiles", new Color(0.62f, 0.3f, 0.22f), red),
                Tex("grey_roof_tiles", new Color(0.36f, 0.36f, 0.38f), steel),
                Tex("grey_roof_tiles", new Color(0.5f, 0.24f, 0.18f), red),
            };
        }

        static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static Bounds PrefabBounds(GameObject prefab)
        {
            Bounds b;
            if (boundsCache.TryGetValue(prefab, out b)) return b;
            var instance = (GameObject)Object.Instantiate(prefab);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
            b = renderers.Length > 0 ? renderers[0].bounds : new Bounds(Vector3.zero, Vector3.one);
            foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
            Object.DestroyImmediate(instance);
            boundsCache[prefab] = b;
            return b;
        }

        /// <summary>Stellt ein Prefab mit der Unterkante auf <paramref name="ground"/>.</summary>
        static GameObject Put(GameObject prefab, Transform parent, Vector3 ground, Quaternion rotation, float scale)
        {
            if (prefab == null) return null;
            Bounds b = PrefabBounds(prefab);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            go.transform.rotation = rotation;
            go.transform.localScale = Vector3.one * scale;
            go.transform.position = ground + rotation * new Vector3(-b.center.x, -b.min.y, -b.center.z) * scale;
            foreach (Transform child in go.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        static GameObject Put(string prefabName, Transform parent, Vector3 ground, Quaternion rotation, float scale)
        {
            return Put(AssetStorePakete.Prefab(prefabName), parent, ground, rotation, scale);
        }

        /// <summary>Modell auf Zielhöhe skaliert (Requisiten, Poly Haven).</summary>
        static GameObject PutHeight(GameObject asset, Transform parent, Vector3 ground, float yaw, float height, float sink = 0f)
        {
            if (asset == null) return null;
            Bounds b = PrefabBounds(asset);
            float scale = height / Mathf.Max(0.01f, b.size.y);
            GameObject go = Put(asset, parent, ground - Vector3.up * sink, Quaternion.Euler(0f, yaw, 0f), scale);
            return go;
        }

        static GameObject PutLength(GameObject asset, Transform parent, Vector3 ground, float yaw, float length, float sink = 0f)
        {
            if (asset == null) return null;
            Bounds b = PrefabBounds(asset);
            float scale = length / Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z));
            return Put(asset, parent, ground - Vector3.up * sink, Quaternion.Euler(0f, yaw, 0f), scale);
        }

        static GameObject Model(string id)
        {
            GameObject m = Vorlagen.Model(id);
            if (m == null) m = AssetDatabase.LoadAssetAtPath<GameObject>(Vorlagen.Root + "/Modelle/" + id + "/" + id + "_1k.gltf");
            return m;
        }

        static Matrix4x4 ZoneFrame(FotoZone z, float y)
        {
            return Matrix4x4.TRS(new Vector3(z.Center.x, y, z.Center.y), z.Rotation(), Vector3.one);
        }

        static Vector3 ZoneWorld(FotoZone z, float lx, float ly, float y)
        {
            Vector2 p = z.ToWorld(new Vector2(lx, ly));
            return new Vector3(p.x, y, p.y);
        }

        static GameObject BuildKit(FotoKit kit, string name, Transform parent)
        {
            if (kit.IsEmpty) return null;
            return kit.Build(name + "_" + (meshCounter++), parent, Folder);
        }

        // ------------------------------------------------------------ Einstieg

        static void Init(FotoTerrain terrain)
        {
            t = terrain;
            rnd = new System.Random(4711);
            boundsCache.Clear();
            meshCounter = 0;
            Report = "";
            containerMaterials = null;
            asphalt = null;
            carPaint = null;
            FotoInselBuilder.EnsureFolder(Folder);
            AssetStorePakete.PrepareMaterials();
            CreateMaterials();
        }

        /// <summary>Verlassene Insel: nur das stillgelegte Werk, Brücken, Landmarken und Natur.</summary>
        public static void BuildVerlassen(FotoTerrain terrain, Transform parent)
        {
            Init(terrain);
            Verlassen = true;
            try
            {
                Transform industry = Group("Verlassenes Industriegebiet", parent);
                BuildSteelworks(t.Steelworks, Group(t.Steelworks.Name, industry));
                BuildBridges(Group("Brücken", parent));
                BuildLandmarks(Group("Landmarken", parent));
                ScatterNature(Group("Felsen und Treibholz", parent));
            }
            finally
            {
                Verlassen = false;
            }
            AssetDatabase.SaveAssets();
        }

        public static void Build(FotoTerrain terrain, Transform parent)
        {
            Init(terrain);

            Transform industry = Group("Industrie", parent);
            BuildPort(t.Port, Group(t.Port.Name, industry));
            BuildRefinery(t.Refinery, Group(t.Refinery.Name, industry));
            BuildSteelworks(t.Steelworks, Group(t.Steelworks.Name, industry));
            BuildTown(t.Town, Group(t.Town.Name, parent));
            BuildBridges(Group("Brücken", parent));
            BuildPowerLine(Group("Hochspannung", parent));
            BuildWindFarm(Group("Windpark", parent));
            BuildLandmarks(Group("Landmarken", parent));
            ScatterNature(Group("Felsen und Treibholz", parent));
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------ Gemeinsame Bauteile

        static void QuayWalls(FotoZone z, FotoKit kit)
        {
            float top = z.Elevation + 0.6f;
            float bottom = -24f;
            for (int edge = 0; edge < 4; edge++)
            {
                bool alongX = edge < 2;
                float sign = (edge % 2 == 0) ? -1f : 1f;
                float half = alongX ? z.Half.x : z.Half.y;
                for (float s = -half; s < half; s += 20f)
                {
                    float c = s + 10f;
                    Vector2 local = alongX ? new Vector2(c, sign * z.Half.y) : new Vector2(sign * z.Half.x, c);
                    Vector2 outward = alongX ? new Vector2(0f, sign) : new Vector2(sign, 0f);
                    float outside = t.HeightAt(z.ToWorld(local + outward * 16f));
                    if (outside > -0.3f) continue;
                    Vector3 bottomCenter = new Vector3(local.x - outward.x * 1.5f, bottom - z.Elevation, local.y - outward.y * 1.5f);
                    Vector3 size = alongX ? new Vector3(20.3f, top - bottom, 3f) : new Vector3(3f, top - bottom, 20.3f);
                    kit.Box(concreteDark, bottomCenter, size);
                    // Poller und Fender.
                    Vector3 bollard = new Vector3(local.x - outward.x * 2f, 0.6f, local.y - outward.y * 2f);
                    kit.Cylinder(steel, bollard, 0.35f, 0.8f, 8);
                    kit.Box(steel, new Vector3(local.x + outward.x * 0.2f, -3.5f, local.y + outward.y * 0.2f), alongX ? new Vector3(2.5f, 3f, 0.6f) : new Vector3(0.6f, 3f, 2.5f));
                }
            }
        }

        static void StsCrane(FotoKit k, Vector3 p, Material paint, Material machine)
        {
            float legH = 46f;
            Vector3[] legs =
            {
                p + new Vector3(-9f, 0f, -8f), p + new Vector3(9f, 0f, -8f),
                p + new Vector3(-9f, 0f, 22f), p + new Vector3(9f, 0f, 22f),
            };
            foreach (Vector3 leg in legs)
            {
                k.Beam(paint, leg, leg + Vector3.up * legH, 1.8f);
                k.Box(steel, leg - new Vector3(0f, 0f, 0f), new Vector3(3f, 1.4f, 2.2f));
            }
            foreach (float h in new[] { 13f, legH })
            {
                k.Beam(paint, legs[0] + Vector3.up * h, legs[1] + Vector3.up * h, 1.4f);
                k.Beam(paint, legs[2] + Vector3.up * h, legs[3] + Vector3.up * h, 1.4f);
                k.Beam(paint, legs[0] + Vector3.up * h, legs[2] + Vector3.up * h, 1.4f);
                k.Beam(paint, legs[1] + Vector3.up * h, legs[3] + Vector3.up * h, 1.4f);
            }
            k.Beam(paint, legs[0] + Vector3.up * 13f, legs[2] + Vector3.up * legH, 0.8f);
            k.Beam(paint, legs[1] + Vector3.up * 13f, legs[3] + Vector3.up * legH, 0.8f);
            // Ausleger über das Wasser (−z) und Rückausleger.
            foreach (float side in new[] { -3.6f, 3.6f })
                k.Box(paint, p + new Vector3(side, legH + 1.5f, -12f), new Vector3(2.2f, 3f, 104f));
            for (float z = -62f; z < 38f; z += 8f)
                k.Beam(paint, p + new Vector3(-3.6f, legH + 3f, z), p + new Vector3(3.6f, legH + 3f, z), 0.6f);
            k.Box(machine, p + new Vector3(0f, legH + 4.5f, 26f), new Vector3(13f, 7f, 14f));
            k.Box(white, p + new Vector3(0f, legH + 1f, -24f), new Vector3(8f, 3.2f, 6f));
            Vector3 apexL = p + new Vector3(-3f, 80f, 6f), apexR = p + new Vector3(3f, 80f, 6f);
            k.Beam(paint, legs[0] + Vector3.up * legH, apexL, 1.3f);
            k.Beam(paint, legs[2] + Vector3.up * legH, apexL, 1.3f);
            k.Beam(paint, legs[1] + Vector3.up * legH, apexR, 1.3f);
            k.Beam(paint, legs[3] + Vector3.up * legH, apexR, 1.3f);
            k.Beam(paint, apexL, apexR, 1f);
            foreach (float s in new[] { -3.6f, 3.6f })
            {
                Vector3 apex = s < 0 ? apexL : apexR;
                k.Beam(galvanized, apex, p + new Vector3(s, legH + 3f, -40f), 0.35f);
                k.Beam(galvanized, apex, p + new Vector3(s, legH + 3f, -62f), 0.35f);
                k.Beam(galvanized, apex, p + new Vector3(s, legH + 3f, 36f), 0.35f);
            }
            k.Beam(galvanized, p + new Vector3(-1f, legH, -24f), p + new Vector3(-1f, 24f, -24f), 0.15f);
            k.Beam(galvanized, p + new Vector3(1f, legH, -24f), p + new Vector3(1f, 24f, -24f), 0.15f);
            k.Box(orange, p + new Vector3(0f, 22.5f, -24f), new Vector3(12f, 1.5f, 2.6f));
        }

        // Container: ein Mesh pro Block und Farbe, sonst wären es zehntausend Objekte.
        static Mesh containerMesh;
        static Bounds containerBounds;
        static Material[] containerMaterials;

        static void PrepareContainers()
        {
            if (containerMaterials != null) return;
            GameObject prefab = AssetStorePakete.Prefab("Cargo_container_v1_LD1close");
            MeshFilter filter = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
            Material source = prefab != null ? prefab.GetComponentInChildren<MeshRenderer>().sharedMaterial : null;
            if (filter != null && filter.sharedMesh != null)
            {
                containerMesh = filter.sharedMesh;
                containerBounds = containerMesh.bounds;
            }
            else
            {
                var kit = new FotoKit();
                kit.Box(white, new Vector3(0f, -1.5f, 0f), new Vector3(8f, 3f, 3f));
                GameObject temp = kit.Build("Container_Ersatz", null, Folder);
                containerMesh = temp.GetComponent<MeshFilter>().sharedMesh;
                containerBounds = containerMesh.bounds;
                Object.DestroyImmediate(temp);
            }
            Color[] colors =
            {
                new Color(0.72f, 0.22f, 0.17f), new Color(0.2f, 0.36f, 0.62f), new Color(0.24f, 0.48f, 0.3f),
                new Color(0.88f, 0.5f, 0.16f), new Color(0.6f, 0.6f, 0.6f), new Color(0.9f, 0.9f, 0.87f),
                new Color(0.32f, 0.58f, 0.72f), new Color(0.5f, 0.3f, 0.2f), new Color(0.78f, 0.7f, 0.25f),
            };
            containerMaterials = new Material[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                // Lackierter Stahl ohne Atlas-Textur: die Paket-Textur färbt die Kisten fast schwarz.
                containerMaterials[i] = Paint("Container_" + i, colors[i], 0.32f, 0.25f);
            }
        }

        sealed class ContainerBatch
        {
            readonly Dictionary<int, List<CombineInstance>> lists = new Dictionary<int, List<CombineInstance>>();
            public int Count;

            public void Add(Matrix4x4 frame, Vector3 bottomCenter, float yaw, int color)
            {
                Vector3 offset = new Vector3(-containerBounds.center.x, -containerBounds.min.y, -containerBounds.center.z);
                Matrix4x4 m = frame * Matrix4x4.TRS(bottomCenter, Quaternion.Euler(0f, yaw, 0f), Vector3.one) * Matrix4x4.Translate(offset);
                List<CombineInstance> list;
                if (!lists.TryGetValue(color, out list)) lists[color] = list = new List<CombineInstance>();
                list.Add(new CombineInstance { mesh = containerMesh, transform = m, subMeshIndex = 0 });
                Count++;
            }

            public void Build(string name, Transform parent)
            {
                if (Count == 0) return;
                var combines = new List<CombineInstance>();
                var materials = new List<Material>();
                foreach (var pair in lists)
                {
                    var mesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                    mesh.CombineMeshes(pair.Value.ToArray(), true, true);
                    combines.Add(new CombineInstance { mesh = mesh, transform = Matrix4x4.identity });
                    materials.Add(containerMaterials[pair.Key]);
                }
                var final = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                final.CombineMeshes(combines.ToArray(), false, false);
                final.RecalculateBounds();
                string path = Folder + "/" + name + ".asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(final, path);
                foreach (CombineInstance c in combines) Object.DestroyImmediate(c.mesh);
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = final;
                go.AddComponent<MeshRenderer>().sharedMaterials = materials.ToArray();
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            }
        }

        static void ContainerStack(ContainerBatch batch, Matrix4x4 frame, Vector3 corner, int bays, int rows, int maxTiers, float yaw, float fill)
        {
            for (int r = 0; r < rows; r++)
            {
                int rowColor = rnd.Next(containerMaterials.Length);
                for (int b = 0; b < bays; b++)
                {
                    if (!Chance(fill)) continue;
                    int tiers = 1 + rnd.Next(maxTiers);
                    for (int tier = 0; tier < tiers; tier++)
                    {
                        int color = Chance(0.35f) ? rowColor : rnd.Next(containerMaterials.Length);
                        Vector3 local = corner + Quaternion.Euler(0f, yaw, 0f) * new Vector3(b * 8.5f + 4f, tier * 2.95f, r * 3.2f + 1.5f);
                        batch.Add(frame, local, yaw, color);
                    }
                }
            }
        }

        /// <summary>Schiff mit Rumpf, Brücke und optional Containern an Deck; Mitte auf Wasserhöhe.</summary>
        static void Ship(Transform parent, Vector3 waterCenter, float yaw, float length, float beam, bool containers, Material hullUpper)
        {
            var kit = new FotoKit { Frame = Matrix4x4.TRS(waterCenter, Quaternion.Euler(0f, yaw, 0f), Vector3.one) };
            float half = length * 0.5f;
            float body = length - 55f;
            // Rumpf entlang lokal z: unten rot (Antifouling), oben dunkel.
            kit.Box(hullRed, new Vector3(0f, -9f, -5f), new Vector3(beam, 8.5f, body));
            kit.Box(hullUpper, new Vector3(0f, -0.5f, -5f), new Vector3(beam, 8f, body));
            // Bug: schmaler werdende Scheiben.
            for (int i = 0; i < 6; i++)
            {
                float f = 1f - (i + 1) / 7f;
                float z = body * 0.5f - 5f + i * 5.5f + 2.75f;
                kit.Box(hullRed, new Vector3(0f, -9f + i * 0.4f, z), new Vector3(beam * f, 8.5f - i * 0.4f, 5.5f));
                kit.Box(hullUpper, new Vector3(0f, -0.5f, z), new Vector3(beam * Mathf.Max(f, 0.18f), 8.6f + i * 0.25f, 5.5f));
            }
            // Heck.
            for (int i = 0; i < 3; i++)
            {
                float f = 1f - i * 0.12f;
                float z = -body * 0.5f - 5f - i * 6f - 3f;
                kit.Box(hullRed, new Vector3(0f, -7f + i, z), new Vector3(beam * f, 6.5f - i, 6f));
                kit.Box(hullUpper, new Vector3(0f, -0.5f, z), new Vector3(beam * f, 8f, 6f));
            }
            // Deckshaus am Heck, Brückennock, Schornstein.
            float house = -half + 32f;
            kit.Box(white, new Vector3(0f, 7.5f, house), new Vector3(beam * 0.62f, 22f, 14f));
            kit.Box(white, new Vector3(0f, 29.5f, house), new Vector3(beam + 4f, 3.4f, 9f));
            kit.Box(glass, new Vector3(0f, 30.3f, house + 4.55f), new Vector3(beam * 0.9f, 1.6f, 0.2f));
            kit.Box(hullUpper, new Vector3(0f, 7.5f, house - 14f), new Vector3(6f, 26f, 7f));
            kit.Box(red, new Vector3(0f, 31.5f, house - 14f), new Vector3(6.1f, 2f, 7.1f));
            kit.Box(steel, new Vector3(0f, 33.5f, house), new Vector3(1f, 8f, 1f));
            BuildKit(kit, "Schiff", parent);

            if (!containers) return;
            PrepareContainers();
            var batch = new ContainerBatch();
            Matrix4x4 frame = Matrix4x4.TRS(waterCenter + Vector3.up * 7.5f, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
            int across = Mathf.FloorToInt(beam / 3.2f) - 1;
            for (float z = house + 12f; z < half - 30f; z += 8.8f)
            {
                for (int row = 0; row < across; row++)
                {
                    int tiers = 2 + rnd.Next(4);
                    for (int tier = 0; tier < tiers; tier++)
                    {
                        Vector3 local = new Vector3(-across * 1.6f + row * 3.2f + 1.6f, tier * 2.95f, z);
                        batch.Add(frame, local, 90f, rnd.Next(containerMaterials.Length));
                    }
                }
            }
            batch.Build("Schiffsladung_" + (meshCounter++), parent);
        }

        static void Clutter(Transform parent, Vector3 center, float radius, int count, float yawBase)
        {
            string[] props =
            {
                "Barrel_v2_quadro", "Barrel_v3_quadro", "Barrel_v1_LD3", "Palet_v1_set", "Bags_on_pallet_v1_1", "Bags_on_pallet_v1_2",
                "Wooden_box_v1_LD2", "Wooden_box_v1_LD2square", "Dumpsters_v1_garbadge", "Dumpsters_v1_empty",
                "Electric_box_v3", "Generator_v1", "Conditioner_v1", "Road_block_v1", "Electric_box_v2",
            };
            for (int i = 0; i < count; i++)
            {
                Vector2 off = Random2() * radius;
                Vector3 p = center + new Vector3(off.x, 0f, off.y);
                p.y = t.HeightAt(p.x, p.z);
                Put(props[rnd.Next(props.Length)], parent, p, Quaternion.Euler(0f, yawBase + rnd.Next(4) * 90f + R(-8f, 8f), 0f), 1f);
            }
        }

        static Vector2 Random2()
        {
            float a = R(0f, Mathf.PI * 2f), r = Mathf.Sqrt((float)rnd.NextDouble());
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        static void Fence(FotoZone z, Transform parent, float inset)
        {
            GameObject wall = AssetStorePakete.Prefab("Concrete_fence_v1_wall_set_v2");
            if (wall == null) return;
            float len = PrefabBounds(wall).size.z;
            for (int edge = 0; edge < 4; edge++)
            {
                bool alongX = edge < 2;
                float sign = (edge % 2 == 0) ? -1f : 1f;
                float half = alongX ? z.Half.x : z.Half.y;
                for (float s = -half + len * 0.5f; s < half; s += len)
                {
                    Vector2 local = alongX ? new Vector2(s, sign * (z.Half.y - inset)) : new Vector2(sign * (z.Half.x - inset), s);
                    Vector2 world = z.ToWorld(local);
                    Vector2 outside = z.ToWorld(local + (alongX ? new Vector2(0f, sign) : new Vector2(sign, 0f)) * 30f);
                    if (t.HeightAt(outside) < 0.5f) continue;            // Kaikante
                    if (t.MaskAt(t.RoadMask, world.x, world.y) > 0) continue;
                    bool nearGate = false;
                    foreach (FotoRoad road in t.Roads)
                        foreach (Vector2 rp in road.Points)
                            if ((rp - world).sqrMagnitude < 40f * 40f) { nearGate = true; break; }
                    if (nearGate) continue;
                    float yaw = alongX ? 90f : 0f;
                    Put(wall, parent, new Vector3(world.x, t.HeightAt(world), world.y), z.Rotation(yaw), 1f);
                }
            }
        }

        static void LightMasts(FotoKit kit, FotoZone z, float spacingX, float spacingY, float height)
        {
            if (Verlassen) return;
            for (float x = -z.Half.x + spacingX * 0.5f; x < z.Half.x; x += spacingX)
            {
                for (float y = -z.Half.y + spacingY * 0.5f; y < z.Half.y; y += spacingY)
                {
                    kit.Revolve(galvanized, new Vector3(x, 0f, y), new[] { 0f, height }, new[] { 0.55f, 0.25f }, 8, true);
                    kit.Box(galvanized, new Vector3(x, height, y), new Vector3(4f, 0.6f, 1.2f));
                    kit.Box(white, new Vector3(x, height + 0.6f, y), new Vector3(3.6f, 0.9f, 1f));
                }
            }
        }

        static void Hall(FotoKit k, Vector3 c, float length, float width, float height, Material wall, Material roof, int roofType)
        {
            if (Verlassen && wall != brick && wall != concrete) wall = rustSheet;
            k.Box(wall, c, new Vector3(length, height, width));
            if (Verlassen && Chance(0.4f)) return;   // Dach eingestürzt, Fensterbänder zerbrochen
            k.Box(glass, c + new Vector3(0f, height * 0.62f, width * 0.5f + 0.05f), new Vector3(length * 0.92f, 2.4f, 0.15f));
            k.Box(glass, c + new Vector3(0f, height * 0.62f, -width * 0.5f - 0.05f), new Vector3(length * 0.92f, 2.4f, 0.15f));
            // Tore.
            for (float x = -length * 0.5f + 25f; x < length * 0.5f - 10f; x += 45f)
                k.Box(steel, c + new Vector3(x, 0f, width * 0.5f + 0.08f), new Vector3(7f, Mathf.Min(8f, height * 0.6f), 0.2f));
            Vector3 top = c + Vector3.up * height;
            if (roofType == 0) k.SawtoothRoof(metal, glass, top, length, width, 12f, 4.5f, 0f);
            else if (roofType == 1) k.GableRoof(metal, wall, top, length, width, width * 0.18f, 0.6f, 0f);
            else
            {
                k.Box(metal, top, new Vector3(length + 0.6f, 0.6f, width + 0.6f));
                k.Box(metal, top + Vector3.up * 0.6f, new Vector3(length * 0.85f, 3.5f, width * 0.22f));
                k.Box(glass, top + new Vector3(0f, 1.5f, width * 0.11f + 0.05f), new Vector3(length * 0.83f, 1.6f, 0.1f));
            }
        }

        // ------------------------------------------------------------ Verdichtung der Werksflächen

        static readonly List<Rect> occupied = new List<Rect>();
        static Material asphalt;
        static Material[] carPaint;

        static void Occupy(float cx, float cy, float w, float h) { occupied.Add(new Rect(cx - w * 0.5f, cy - h * 0.5f, w, h)); }

        static bool Free(Rect r)
        {
            foreach (Rect o in occupied) if (o.Overlaps(r)) return false;
            return true;
        }

        static void EnsureFillMaterials()
        {
            if (asphalt == null) asphalt = Paint("Asphalt", new Color(0.11f, 0.11f, 0.115f), 0.25f);
            if (carPaint == null)
                carPaint = new[]
                {
                    Paint("Auto_Weiss", new Color(0.85f, 0.85f, 0.85f), 0.7f, 0.3f), Paint("Auto_Silber", new Color(0.55f, 0.56f, 0.58f), 0.7f, 0.6f),
                    Paint("Auto_Schwarz", new Color(0.04f, 0.04f, 0.045f), 0.75f, 0.4f), Paint("Auto_Grau", new Color(0.3f, 0.31f, 0.32f), 0.7f, 0.4f),
                    Paint("Auto_Blau", new Color(0.1f, 0.18f, 0.4f), 0.7f, 0.4f), Paint("Auto_Rot", new Color(0.5f, 0.06f, 0.05f), 0.7f, 0.4f),
                };
        }

        static void ParkingLot(FotoKit k, Rect r)
        {
            k.Box(asphalt, new Vector3(r.center.x, 0f, r.center.y), new Vector3(r.width, 0.06f, r.height));
            for (float y = r.yMin + 4f; y < r.yMax - 4f; y += 15f)
            {
                for (float x = r.xMin + 2f; x < r.xMax - 2f; x += 2.7f)
                {
                    if (!Chance(0.78f)) continue;
                    for (int side = 0; side < 2; side++)
                    {
                        float cy = y + side * 5.6f;
                        if (cy > r.yMax - 3f || !Chance(0.85f)) continue;
                        Material paint = carPaint[rnd.Next(carPaint.Length)];
                        k.Box(paint, new Vector3(x, 0.25f, cy), new Vector3(1.85f, 0.9f, 4.5f), R(-3f, 3f));
                        k.Box(glass, new Vector3(x, 1.15f, cy - 0.2f), new Vector3(1.6f, 0.5f, 2.4f), R(-3f, 3f));
                    }
                }
            }
        }

        static void Office(FotoKit k, Vector3 c, float w, float d)
        {
            int floors = 3 + rnd.Next(5);
            float h = floors * 3.6f;
            k.Box(Chance(0.5f) ? concrete : white, c, new Vector3(w, h, d));
            for (int f = 0; f < floors; f++)
            {
                float y = f * 3.6f + 1f;
                k.Box(glass, c + new Vector3(0f, y, d * 0.5f + 0.05f), new Vector3(w * 0.94f, 1.7f, 0.12f));
                k.Box(glass, c + new Vector3(0f, y, -d * 0.5f - 0.05f), new Vector3(w * 0.94f, 1.7f, 0.12f));
                k.Box(glass, c + new Vector3(w * 0.5f + 0.05f, y, 0f), new Vector3(0.12f, 1.7f, d * 0.9f));
                k.Box(glass, c + new Vector3(-w * 0.5f - 0.05f, y, 0f), new Vector3(0.12f, 1.7f, d * 0.9f));
            }
            k.Box(steel, c + new Vector3(R(-w * 0.2f, w * 0.2f), h, R(-d * 0.2f, d * 0.2f)), new Vector3(6f, 2.5f, 4f));
        }

        static void TankCluster(FotoKit k, Rect r)
        {
            int count = 3 + rnd.Next(6);
            for (int i = 0; i < count; i++)
            {
                float radius = R(3.5f, 7f), height = R(7f, 15f);
                var c = new Vector3(R(r.xMin + radius, r.xMax - radius), 0f, R(r.yMin + radius, r.yMax - radius));
                k.Cylinder(Chance(0.7f) ? tankWhite : rust, c, radius, height, 24, false);
                k.Revolve(metal, c + Vector3.up * height, new[] { 0f, radius * 0.25f }, new[] { radius, 0.6f }, 24, true);
            }
            k.Box(concrete, new Vector3(r.center.x, 0f, r.center.y), new Vector3(r.width, 0.5f, r.height));
        }

        static void Stockpiles(FotoKit k, Rect r)
        {
            int count = 1 + rnd.Next(3);
            for (int i = 0; i < count; i++)
            {
                float len = R(r.width * 0.4f, r.width * 0.85f), wid = R(14f, 24f);
                var c = new Vector3(r.center.x + R(-5f, 5f), 0f, r.yMin + (i + 0.5f) * r.height / count);
                k.Heap(Chance(0.5f) ? gravel : (Chance(0.5f) ? coal : ore), c, len, Mathf.Min(wid, r.height / count), R(5f, 9f), 0f);
            }
        }

        /// <summary>Füllt freie Flächen einer Werkszone in einem Blockraster mit Asphaltstraßen dazwischen.</summary>
        static void FillZone(FotoZone z, Transform parent, float blockW, float blockD, float street)
        {
            EnsureFillMaterials();
            var kit = new FotoKit { Frame = ZoneFrame(z, z.Elevation) };
            int filled = 0;
            for (float x = -z.Half.x + street; x + blockW <= z.Half.x - street * 0.5f; x += blockW + street)
            {
                for (float y = -z.Half.y + street; y + blockD <= z.Half.y - street * 0.5f; y += blockD + street)
                {
                    var r = new Rect(x, y, blockW, blockD);
                    // Straße unter dem Block (asphaltiert), wenn sie keine Großanlage schneidet.
                    var road = new Rect(x - street, y - street, blockW + street, street);
                    if (Free(road)) kit.Box(asphalt, new Vector3(road.center.x, 0f, road.center.y + 1.5f), new Vector3(road.width, 0.05f, street - 3f));
                    var roadSide = new Rect(x - street, y - street, street, blockD + street);
                    if (Free(roadSide)) kit.Box(asphalt, new Vector3(roadSide.center.x + 1.5f, 0f, roadSide.center.y), new Vector3(street - 3f, 0.05f, roadSide.height));
                    if (!Free(r)) continue;
                    Vector2 world = z.ToWorld(r.center);
                    if (t.MaskAt(t.WaterMask, world.x, world.y) > 0) continue;
                    occupied.Add(r);
                    var c = new Vector3(r.center.x, 0f, r.center.y);
                    int type = rnd.Next(100);
                    if (type < 36)
                        Hall(kit, c, r.width * R(0.72f, 0.95f), r.height * R(0.6f, 0.88f), R(9f, 21f),
                            rnd.Next(3) == 0 ? concrete : (Chance(0.5f) ? rustSheet : metal), Chance(0.5f) ? metal : rust, rnd.Next(3));
                    else if (type < 52) { if (Verlassen) Stockpiles(kit, r); else ParkingLot(kit, r); }
                    else if (type < 64) TankCluster(kit, r);
                    else if (type < 76) Office(kit, c, r.width * R(0.4f, 0.7f), r.height * R(0.4f, 0.7f));
                    else if (type < 88) Clutter(parent, ZoneWorld(z, c.x, c.z, z.Elevation), Mathf.Min(r.width, r.height) * 0.4f, 14, -z.Angle);
                    else Stockpiles(kit, r);
                    filled++;
                    if (kit.VertexCount > 250000) BuildKit(kit, z.Name + "_Bloecke", parent);
                }
            }
            BuildKit(kit, z.Name + "_Bloecke", parent);
            Report += z.Name + ": " + filled + " zusätzliche Werksblöcke\n";
        }

        static void RailLine(FotoKit k, float x0, float x1, float y, int wagons)
        {
            k.Box(gravel, new Vector3((x0 + x1) * 0.5f, 0f, y), new Vector3(x1 - x0, 0.35f, 4.2f));
            k.Box(steel, new Vector3((x0 + x1) * 0.5f, 0.35f, y - 0.72f), new Vector3(x1 - x0, 0.17f, 0.08f));
            k.Box(steel, new Vector3((x0 + x1) * 0.5f, 0.35f, y + 0.72f), new Vector3(x1 - x0, 0.17f, 0.08f));
            float x = x0 + 30f;
            for (int i = 0; i < wagons && x < x1 - 20f; i++)
            {
                Material m = Chance(0.6f) ? rust : (Chance(0.5f) ? hullBlue : red);
                k.Box(steel, new Vector3(x, 0.5f, y), new Vector3(13.5f, 1f, 2.6f));
                k.Box(m, new Vector3(x, 1.5f, y), new Vector3(13f, 2.6f, 2.9f));
                x += 14.2f;
            }
        }

        // ------------------------------------------------------------ Containerhafen

        static void BuildPort(FotoZone z, Transform parent)
        {
            var kit = new FotoKit { Frame = ZoneFrame(z, z.Elevation) };
            QuayWalls(z, kit);
            float quay = -z.Half.y + 18f;
            for (int i = 0; i < 7; i++)
                StsCrane(kit, new Vector3(-540f + i * 175f + R(-20f, 20f), 0f, quay), i % 3 == 2 ? orange : craneBlue, white);
            BuildKit(kit, "Hafen_Kai_und_Kraene", parent);

            // Containerlager in Blöcken mit Fahrgassen.
            PrepareContainers();
            Matrix4x4 frame = ZoneFrame(z, z.Elevation);
            int blocks = 0, boxes = 0;
            for (float y = -z.Half.y + 75f; y < 30f; y += 34f)
            {
                for (float x = -z.Half.x + 30f; x < z.Half.x - 120f; x += 118f)
                {
                    if (Chance(0.07f)) continue;
                    var batch = new ContainerBatch();
                    ContainerStack(batch, frame, new Vector3(x, 0f, y), 12, 6, 4, 0f, 0.84f);
                    batch.Build("Containerblock_" + (meshCounter++), parent);
                    blocks++;
                    boxes += batch.Count;
                }
            }
            Report += "Hafen: " + blocks + " Containerblöcke, " + boxes + " Container\n";

            // Lagerhallen hinten, mit Verwaltung.
            var hallKit = new FotoKit { Frame = ZoneFrame(z, z.Elevation) };
            string[] hangars = { "Hangar_v2", "Hangar_v2_1", "Hangar_v2_6", "Hangar_v2_7", "Hangar_v3" };
            for (float x = -z.Half.x + 60f; x < z.Half.x - 40f; x += R(95f, 125f))
            {
                if (Chance(0.3f))
                    Hall(hallKit, new Vector3(x, 0f, 170f), R(70f, 95f), R(40f, 55f), R(12f, 17f), Chance(0.5f) ? rustSheet : concrete, metal, rnd.Next(3));
                else
                    Put(hangars[rnd.Next(hangars.Length)], parent, ZoneWorld(z, x, 170f, z.Elevation), z.Rotation(Chance(0.5f) ? 0f : 180f), R(1.6f, 2.1f));
                Clutter(parent, ZoneWorld(z, x + R(-30f, 30f), 125f, z.Elevation), 18f, 6, -z.Angle);
            }
            Hall(hallKit, new Vector3(z.Half.x - 70f, 0f, 60f), 50f, 22f, 18f, concrete, metal, 2);
            LightMasts(hallKit, z, 160f, 150f, 32f);
            BuildKit(hallKit, "Hafen_Hallen", parent);
            occupied.Clear();
            Occupy(0f, -z.Half.y * 0.5f - 60f, z.Half.x * 2f, z.Half.y + 200f);
            Occupy(0f, 170f, z.Half.x * 2f, 70f);
            FillZone(z, parent, 64f, 40f, 14f);
            Fence(z, parent, 3f);

            // Frachter am Kai.
            Vector3 berth = ZoneWorld(z, -120f, -z.Half.y - 21f, 0f);
            Ship(parent, berth, -z.Angle + 90f, 235f, 32f, true, hullBlue);
            Vector3 berth2 = ZoneWorld(z, 420f, -z.Half.y - 19f, 0f);
            if (t.HeightAt(berth2.x, berth2.z) < -6f) Ship(parent, berth2, -z.Angle + 90f, 180f, 27f, true, hullGreen);
        }

        // ------------------------------------------------------------ Raffinerie

        static void BuildRefinery(FotoZone z, Transform parent)
        {
            var kit = new FotoKit { Frame = ZoneFrame(z, z.Elevation) };
            QuayWalls(z, kit);

            // Tanklager im Westteil.
            int tanks = 0;
            for (float x = -z.Half.x + 45f; x < -60f; x += 64f)
            {
                for (float y = -z.Half.y + 70f; y < z.Half.y - 40f; y += 64f)
                {
                    if (Chance(0.1f)) continue;
                    float radius = R(14f, 19f);
                    float height = R(14f, 21f);
                    var c = new Vector3(x + R(-3f, 3f), 0f, y + R(-3f, 3f));
                    if (Chance(0.35f))
                    {
                        Put("Oil_tank_v1", parent, ZoneWorld(z, c.x, c.z, z.Elevation), z.Rotation(R(0f, 360f)), radius * 2f / 10.6f);
                    }
                    else
                    {
                        kit.Cylinder(Chance(0.8f) ? tankWhite : rust, c, radius, height, 36, false);
                        kit.Revolve(metal, c + Vector3.up * height, new[] { 0f, radius * 0.12f }, new[] { radius, 1.5f }, 36, true);
                        kit.Revolve(galvanized, c, new[] { 0f, height + 1.2f }, new[] { radius + 0.6f, radius + 0.6f }, 4, false);
                    }
                    // Auffangwanne.
                    float s = 29f;
                    kit.Box(concrete, c + new Vector3(0f, 0f, s), new Vector3(2f * s, 1.6f, 0.6f));
                    kit.Box(concrete, c + new Vector3(0f, 0f, -s), new Vector3(2f * s, 1.6f, 0.6f));
                    kit.Box(concrete, c + new Vector3(s, 0f, 0f), new Vector3(0.6f, 1.6f, 2f * s));
                    kit.Box(concrete, c + new Vector3(-s, 0f, 0f), new Vector3(0.6f, 1.6f, 2f * s));
                    tanks++;
                }
            }
            BuildKit(kit, "Raffinerie_Tanklager", parent);

            // Prozessanlagen im Ostteil: Kolonnen mit Bühnen, Kugeltanks, Reaktoren, Rohrbrücken.
            var plant = new FotoKit { Frame = ZoneFrame(z, z.Elevation) };
            for (int i = 0; i < 16; i++)
            {
                var p = new Vector3(70f + (i % 5) * 100f + R(-15f, 15f), 0f, -z.Half.y + 90f + (i / 5) * 140f + R(-20f, 20f));
                float h = R(32f, 72f), r = R(2.2f, 4.6f);
                plant.Cylinder(Chance(0.5f) ? galvanized : tankWhite, p, r, h, 16, true);
                for (float y = 8f; y < h - 2f; y += R(7f, 10f))
                    plant.Revolve(steel, p + Vector3.up * y, new[] { 0f, 0.35f }, new[] { r + 1.4f, r + 1.4f }, 16, true);
                plant.Beam(steel, p + new Vector3(r + 1.4f, 0f, 0f), p + new Vector3(r + 1.4f, h, 0f), 0.35f);
                // Strukturgerüst daneben.
                var frame = p + new Vector3(r + 9f, 0f, 0f);
                for (int lvl = 0; lvl < 4; lvl++)
                {
                    float y = lvl * 7f;
                    plant.Box(steel, frame + Vector3.up * (y + 6.6f), new Vector3(10f, 0.4f, 10f));
                }
                foreach (var corner in new[] { new Vector3(-5, 0, -5), new Vector3(5, 0, -5), new Vector3(-5, 0, 5), new Vector3(5, 0, 5) })
                    plant.Beam(steel, frame + corner, frame + corner + Vector3.up * 28f, 0.5f);
            }
            for (int i = 0; i < 5; i++)
            {
                var c = new Vector3(110f + i * 38f, 0f, z.Half.y - 70f);
                plant.Sphere(tankWhite, c + Vector3.up * 14f, 10f, 24);
                for (int leg = 0; leg < 8; leg++)
                {
                    float a = leg * Mathf.PI / 4f;
                    plant.Beam(steel, c + new Vector3(Mathf.Cos(a) * 9f, 0f, Mathf.Sin(a) * 9f), c + new Vector3(Mathf.Cos(a) * 9f, 13f, Mathf.Sin(a) * 9f), 0.6f);
                }
            }
            // Fackel und Schornsteine.
            var flare = new Vector3(z.Half.x - 50f, 0f, z.Half.y - 50f);
            for (int b = 0; b < 9; b++)
                plant.Cylinder(b % 2 == 0 ? red : white, flare + Vector3.up * (b * 10f), 1.4f, 10f, 12, b == 8);
            for (int c = 0; c < 2; c++)
            {
                var stack = new Vector3(z.Half.x - 120f - c * 34f, 0f, -z.Half.y + 60f);
                plant.Revolve(concrete, stack, new[] { 0f, 96f }, new[] { 6f, 3.6f }, 24, false);
                plant.Revolve(red, stack + Vector3.up * 96f, new[] { 0f, 5f }, new[] { 3.6f, 3.4f }, 24, false);
                plant.Revolve(white, stack + Vector3.up * 101f, new[] { 0f, 5f }, new[] { 3.4f, 3.2f }, 24, false);
                plant.Revolve(red, stack + Vector3.up * 106f, new[] { 0f, 5f }, new[] { 3.2f, 3.1f }, 24, true);
            }
            BuildKit(plant, "Raffinerie_Prozessanlagen", parent);

            // Rohrbrücken aus dem Industrial Set.
            GameObject rack = AssetStorePakete.Prefab("Pipes_set_v1_H_set_v1");
            if (rack != null)
            {
                float len = PrefabBounds(rack).size.z * 1.4f;
                foreach (float y in new[] { -40f, 75f })
                    for (float x = -40f; x < z.Half.x - 30f; x += len)
                        Put(rack, parent, ZoneWorld(z, x, y, z.Elevation), z.Rotation(90f), 1.4f);
                for (float y = -z.Half.y + 50f; y < z.Half.y - 30f; y += len)
                    Put(rack, parent, ZoneWorld(z, -30f, y, z.Elevation), z.Rotation(0f), 1.4f);
            }
            for (int i = 0; i < 8; i++)
                Put("Industrial_pipe_v1", parent, ZoneWorld(z, R(60f, z.Half.x - 60f), R(-z.Half.y + 60f, z.Half.y - 60f), z.Elevation), z.Rotation(R(0f, 360f)), R(2f, 2.8f));
            for (int i = 0; i < 6; i++)
            {
                Vector3 c = ZoneWorld(z, R(40f, z.Half.x - 80f), R(-z.Half.y + 60f, z.Half.y - 80f), z.Elevation);
                for (int k = 0; k < 6; k++)
                    Put("Oil_tank_v2", parent, c + new Vector3((k % 3) * 6f, 0f, (k / 3) * 6f), Quaternion.identity, 2.2f);
                Put("Pipes_set_v1_U_set_v1", parent, c + new Vector3(0f, 0f, 16f), z.Rotation(0f), 1.6f);
            }
            string[] buildings = { "Hangar_v3", "Hangar_v3_basic", "Hangar_v2_outbuilding3", "Hangar_v4" };
            for (int i = 0; i < 9; i++)
            {
                Vector3 p = ZoneWorld(z, R(-z.Half.x + 40f, z.Half.x - 40f), z.Half.y - R(20f, 45f), z.Elevation);
                Put(buildings[rnd.Next(buildings.Length)], parent, p, z.Rotation(180f), R(1.3f, 1.7f));
                Clutter(parent, p + new Vector3(R(-20f, 20f), 0f, R(-20f, 20f)), 14f, 5, -z.Angle);
            }
            occupied.Clear();
            Occupy(-z.Half.x * 0.5f - 30f, 0f, z.Half.x + 20f, z.Half.y * 2f);
            Occupy(z.Half.x * 0.5f + 20f, -60f, z.Half.x, z.Half.y * 2f - 120f);
            Occupy(z.Half.x - 50f, z.Half.y - 50f, 30f, 30f);
            FillZone(z, parent, 60f, 45f, 14f);
            Fence(z, parent, 3f);

            // Tankerpier mit Tanker.
            var pier = new FotoKit { Frame = ZoneFrame(z, 0f) };
            float px = 180f, start = -z.Half.y, end = -z.Half.y - 330f;
            pier.Box(concrete, new Vector3(px, 4f, (start + end) * 0.5f), new Vector3(10f, 1.2f, start - end));
            pier.Box(concrete, new Vector3(px, 4f, end - 9f), new Vector3(90f, 1.2f, 18f));
            for (float y = start - 12f; y > end - 18f; y -= 22f)
            {
                pier.Box(concreteDark, new Vector3(px - 3.5f, -20f, y), new Vector3(1.6f, 24f, 1.6f));
                pier.Box(concreteDark, new Vector3(px + 3.5f, -20f, y), new Vector3(1.6f, 24f, 1.6f));
            }
            for (int pipe = 0; pipe < 3; pipe++)
                pier.Beam(galvanized, new Vector3(px - 3f + pipe * 1.2f, 5.9f, start), new Vector3(px - 3f + pipe * 1.2f, 5.9f, end), 0.7f);
            BuildKit(pier, "Raffinerie_Pier", parent);
            Ship(parent, ZoneWorld(z, px + 55f, end - 12f, 0f), -z.Angle + 90f, 245f, 40f, false, hullRed);
            Report += "Raffinerie: " + tanks + " Tanks\n";
        }

        // ------------------------------------------------------------ Stahlwerk

        static void BuildSteelworks(FotoZone z, Transform parent)
        {
            var kit = new FotoKit { Frame = ZoneFrame(z, z.Elevation) };
            occupied.Clear();
            Occupy(-130f, -250f, 410f, 60f); Occupy(-130f, -185f, 410f, 50f); Occupy(250f, -230f, 180f, 70f);
            Occupy(-280f, 250f, 240f, 64f); Occupy(-20f, 255f, 150f, 52f);
            Occupy(300f, -95f, 190f, 110f); Occupy(380f, 200f, 95f, 95f); Occupy(470f, 70f, 95f, 95f);
            Occupy(155f, 40f, 90f, 24f); Occupy(-470f, -20f, 64f, 64f); Occupy(-250f, 123f, 400f, 170f);
            Occupy(130f, 160f, 170f, 40f); Occupy(85f, 0f, 300f, 60f); Occupy(0f, -138f, 2000f, 10f);
            RailLine(kit, -z.Half.x + 10f, z.Half.x - 10f, -138f, 26);
            Hall(kit, new Vector3(-130f, 0f, -250f), 400f, 52f, 30f, rustSheet, metal, 0);
            Hall(kit, new Vector3(-130f, 0f, -185f), 400f, 42f, 24f, concrete, metal, 1);
            Hall(kit, new Vector3(250f, 0f, -230f), 170f, 62f, 38f, rustSheet, rust, 2);
            Hall(kit, new Vector3(-280f, 0f, 250f), 230f, 56f, 26f, brick, metal, 1);
            Hall(kit, new Vector3(-20f, 0f, 255f), 140f, 44f, 20f, concrete, metal, 0);

            // Hochöfen mit Winderhitzern (Cowper) und Gichtgasleitung.
            foreach (float hx in new[] { 250f, 360f })
            {
                var p = new Vector3(hx, 0f, -95f);
                kit.Revolve(rust, p, new[] { 0f, 12f, 16f, 40f, 50f, 60f }, new[] { 10f, 10f, 12f, 9f, 5.5f, 3.5f }, 28, true);
                for (int lvl = 0; lvl < 6; lvl++)
                    kit.Box(steel, p + new Vector3(0f, 14f + lvl * 11f, 0f), new Vector3(30f, 0.6f, 30f));
                foreach (var c in new[] { new Vector3(-15, 0, -15), new Vector3(15, 0, -15), new Vector3(-15, 0, 15), new Vector3(15, 0, 15) })
                    kit.Beam(steel, p + c, p + c + Vector3.up * 82f, 1.1f);
                kit.Beam(rust, p + new Vector3(0f, 78f, 0f), p + new Vector3(32f, 36f, 18f), 3.2f);
                kit.Revolve(rust, p + new Vector3(32f, 12f, 18f), new[] { 0f, 6f, 30f, 34f }, new[] { 1.5f, 6.5f, 6.5f, 2f }, 20, true);
                kit.Box(brick, p + new Vector3(0f, 0f, 30f), new Vector3(46f, 22f, 26f));
                for (int c = 0; c < 3; c++)
                {
                    var cowper = p + new Vector3(-38f, 0f, -24f + c * 15f);
                    kit.Cylinder(galvanized, cowper, 6f, 44f, 24, false);
                    kit.Sphere(galvanized, cowper + Vector3.up * 44f, 6f, 20);
                }
            }

            // Kühltürme.
            foreach (var c in new[] { new Vector3(380f, 0f, 200f), new Vector3(470f, 0f, 70f) })
                kit.Revolve(concrete, c, new[] { 0f, 7f, 30f, 60f, 85f, 100f, 112f }, new[] { 42f, 39f, 33f, 28.5f, 27.5f, 28.4f, 30f }, 48, false, 1f);

            // Schornsteine.
            for (int i = 0; i < 3; i++)
            {
                var s = new Vector3(120f + i * 34f, 0f, 40f);
                kit.Revolve(i == 1 ? brick : concrete, s, new[] { 0f, 118f }, new[] { 6.5f, 3.6f }, 24, false);
                kit.Revolve(red, s + Vector3.up * 118f, new[] { 0f, 6f }, new[] { 3.6f, 3.5f }, 24, false);
                kit.Revolve(white, s + Vector3.up * 124f, new[] { 0f, 6f }, new[] { 3.5f, 3.4f }, 24, true);
            }

            // Gasometer.
            var gas = new Vector3(-470f, 0f, -20f);
            kit.Cylinder(metal, gas, 26f, 58f, 40, false);
            kit.Revolve(metal, gas + Vector3.up * 58f, new[] { 0f, 6f }, new[] { 26f, 6f }, 40, true);
            for (int r = 0; r < 24; r++)
            {
                float a = r * Mathf.PI * 2f / 24f;
                Vector3 o = new Vector3(Mathf.Cos(a) * 26.6f, 0f, Mathf.Sin(a) * 26.6f);
                kit.Beam(steel, gas + o, gas + o + Vector3.up * 60f, 0.7f);
            }

            // Erz- und Kohlehalden mit Förderbändern.
            for (int h = 0; h < 4; h++)
                kit.Heap(h % 2 == 0 ? coal : ore, new Vector3(-250f, 0f, 60f + h * 42f), 380f, 34f, 13f, 0f);
            kit.Heap(gravel, new Vector3(130f, 0f, 160f), 160f, 30f, 11f, 0f);
            for (int b = 0; b < 2; b++)
            {
                Vector3 from = new Vector3(-60f, 4f, 80f + b * 84f), to = new Vector3(230f, 46f, -70f + b * 18f);
                kit.Beam(galvanized, from, to, 3f);
                for (int s = 1; s < 8; s++)
                {
                    Vector3 p = Vector3.Lerp(from, to, s / 8f);
                    kit.Beam(steel, new Vector3(p.x, 0f, p.z), p, 1f);
                }
            }
            LightMasts(kit, z, 190f, 170f, 28f);
            BuildKit(kit, "Stahlwerk", parent);

            FillZone(z, parent, 70f, 52f, 16f);
            GameObject gantry = AssetDatabase.LoadAssetAtPath<GameObject>(Requisiten + "Kranbahn.glb");
            for (int i = 0; i < 3; i++)
            {
                GameObject g = PutLength(gantry, parent, ZoneWorld(z, -380f + i * 130f, 120f, z.Elevation), -z.Angle + 90f, 85f);
                if (g != null) g.name = "Portalkran Halde " + (i + 1);
            }
            string[] buildings = { "Hangar_v4", "Hangar_v3", "Hangar_v2_basic", "Hangar_v2_3", "Hangar_v2_outbuilding", "Hangar_v3_outbuilding_1" };
            for (int i = 0; i < 14; i++)
            {
                Vector3 p = ZoneWorld(z, R(-z.Half.x + 40f, z.Half.x - 40f), R(-z.Half.y + 20f, z.Half.y - 20f), z.Elevation);
                Vector2 local = z.ToLocal(new Vector2(p.x, p.z));
                if (Mathf.Abs(local.y + 220f) < 70f || (local.x > 170f && local.y < 0f && local.y > -140f)) continue;
                Put(buildings[rnd.Next(buildings.Length)], parent, p, z.Rotation(rnd.Next(4) * 90f), R(1.2f, 1.8f));
                Clutter(parent, p + new Vector3(R(-25f, 25f), 0f, R(-25f, 25f)), 15f, 6, -z.Angle);
            }
            for (int i = 0; i < 6; i++)
                Put("Industrial_pipe_v1", parent, ZoneWorld(z, R(160f, 420f), R(-180f, -20f), z.Elevation), z.Rotation(R(0f, 360f)), R(1.8f, 2.4f));
            Fence(z, parent, 3f);
            Report += "Stahlwerk: Hallen, 2 Hochöfen, 2 Kühltürme, 3 Schornsteine, Gasometer\n";
        }

        // ------------------------------------------------------------ Hafenstadt

        static void BuildTown(FotoZone z, Transform parent)
        {
            var kit = new FotoKit();
            int houses = 0;
            GameObject lamp = Model("street_lamp_01");
            GameObject hydrant = Model("fire_hydrant");
            GameObject bench = Model("modular_street_seating");
            for (float bx = -z.Half.x + 34f; bx < z.Half.x - 20f; bx += 68f)
            {
                for (float by = -z.Half.y + 30f; by < z.Half.y - 15f; by += 60f)
                {
                    for (int q = 0; q < 4; q++)
                    {
                        if (Chance(0.12f)) continue;
                        float lx = bx + (q % 2 == 0 ? -15f : 15f) + R(-2f, 2f);
                        float ly = by + (q < 2 ? -13f : 13f) + R(-1.5f, 1.5f);
                        if (House(kit, z, lx, ly, q < 2 ? 180f : 0f)) houses++;
                    }
                    if (kit.VertexCount > 200000) BuildKit(kit, "Haeuser", parent);
                    Vector2 corner = z.ToWorld(new Vector2(bx - 34f, by - 30f));
                    if (lamp != null && t.MaskAt(t.WaterMask, corner.x, corner.y) == 0)
                        PutHeight(lamp, parent, new Vector3(corner.x + 4f, t.HeightAt(corner.x + 4f, corner.y + 4f), corner.y + 4f), -z.Angle, 6.5f);
                    if (hydrant != null && Chance(0.25f))
                        PutHeight(hydrant, parent, new Vector3(corner.x + 6f, t.HeightAt(corner.x + 6f, corner.y + 3f), corner.y + 3f), R(0f, 360f), 0.8f);
                }
            }
            BuildKit(kit, "Haeuser", parent);

            // Kirche mit Turm am Marktplatz.
            Vector2 c = z.ToWorld(new Vector2(0f, 0f));
            float g = t.HeightAt(c);
            var church = new FotoKit { Frame = Matrix4x4.TRS(new Vector3(c.x, g - 1.5f, c.y), z.Rotation(), Vector3.one) };
            church.Box(stone, new Vector3(0f, 0f, 0f), new Vector3(30f, 15f, 13f));
            church.GableRoof(roofs[1], stone, new Vector3(0f, 15f, 0f), 30f, 13f, 7f, 0.5f, 0f);
            church.Box(stone, new Vector3(-18f, 0f, 0f), new Vector3(7f, 34f, 7f));
            church.Revolve(roofs[1], new Vector3(-18f, 34f, 0f), new[] { 0f, 15f }, new[] { 5.2f, 0.15f }, 4, false);
            church.Box(glass, new Vector3(0f, 6f, 6.55f), new Vector3(24f, 5f, 0.1f));
            BuildKit(church, "Kirche", parent);
            if (bench != null)
                for (int i = 0; i < 4; i++)
                    PutLength(bench, parent, new Vector3(c.x + 12f + i * 4f, t.HeightAt(c.x + 12f + i * 4f, c.y + 14f), c.y + 14f), -z.Angle, 2f);
            Report += "Stadt: " + houses + " Häuser und Kirche\n";
        }

        static bool House(FotoKit kit, FotoZone z, float lx, float ly, float facing)
        {
            float length = R(10f, 15f), width = R(8f, 11f);
            int floors = rnd.Next(10) < 6 ? 2 : (Chance(0.5f) ? 3 : 1);
            float height = floors * 3f + 0.4f;
            float yaw = facing + (Chance(0.15f) ? 90f : 0f) + R(-2f, 2f);
            Quaternion rot = z.Rotation(yaw);
            Vector2 center = z.ToWorld(new Vector2(lx, ly));
            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 off = rot * new Vector3((i % 2 == 0 ? -1 : 1) * length * 0.5f, 0f, (i < 2 ? -1 : 1) * width * 0.5f);
                Vector2 p = center + new Vector2(off.x, off.z);
                float h = t.HeightAt(p);
                min = Mathf.Min(min, h); max = Mathf.Max(max, h);
                if (t.MaskAt(t.WaterMask, p.x, p.y) > 30 || t.MaskAt(t.RoadMask, p.x, p.y) > 200) return false;
            }
            if (min < 1.2f || max - min > 4.5f) return false;
            float baseY = max + 0.25f;
            kit.Frame = Matrix4x4.TRS(new Vector3(center.x, baseY, center.y), rot, Vector3.one);
            Material wall = plaster[rnd.Next(plaster.Length)];
            Material roof = roofs[rnd.Next(roofs.Length)];
            kit.Box(stone, new Vector3(0f, min - baseY - 0.6f, 0f), new Vector3(length + 0.3f, baseY - min + 0.6f, width + 0.3f));
            kit.Box(wall, Vector3.zero, new Vector3(length, height, width));
            kit.GableRoof(roof, wall, new Vector3(0f, height, 0f), length, width, R(3f, 4.6f), 0.55f, 0f);
            for (int f = 0; f < floors; f++)
            {
                float y = f * 3f + 0.9f;
                int count = Mathf.Max(2, Mathf.FloorToInt(length / 3.1f));
                for (int w = 0; w < count; w++)
                {
                    float x = -length * 0.5f + (w + 0.5f) * length / count;
                    kit.Box(glass, new Vector3(x, y, width * 0.5f + 0.04f), new Vector3(1.15f, 1.45f, 0.1f));
                    kit.Box(glass, new Vector3(x, y, -width * 0.5f - 0.04f), new Vector3(1.15f, 1.45f, 0.1f));
                }
                kit.Box(glass, new Vector3(length * 0.5f + 0.04f, y, 0f), new Vector3(0.1f, 1.45f, 1.15f));
                kit.Box(glass, new Vector3(-length * 0.5f - 0.04f, y, 0f), new Vector3(0.1f, 1.45f, 1.15f));
            }
            kit.Box(planks, new Vector3(R(-length * 0.3f, length * 0.3f), 0f, width * 0.5f + 0.05f), new Vector3(1.1f, 2.1f, 0.12f));
            kit.Box(brick, new Vector3(length * 0.25f, height + 1.2f, width * 0.15f), new Vector3(0.7f, 3.2f, 0.7f));
            kit.Frame = Matrix4x4.identity;
            return true;
        }

        // ------------------------------------------------------------ Brücken

        static void BuildBridges(Transform parent)
        {
            var kit = new FotoKit();
            foreach (FotoBridge b in t.Bridges)
            {
                float yaw = Mathf.Atan2(b.Direction.x, b.Direction.y) * Mathf.Rad2Deg;
                kit.Frame = Matrix4x4.TRS(b.Center, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
                kit.Box(concrete, new Vector3(0f, -1.4f, 0f), new Vector3(b.Width, 1.4f, b.Length));
                kit.Box(concreteDark, new Vector3(b.Width * 0.5f - 0.2f, 0f, 0f), new Vector3(0.35f, 1.1f, b.Length));
                kit.Box(concreteDark, new Vector3(-b.Width * 0.5f + 0.2f, 0f, 0f), new Vector3(0.35f, 1.1f, b.Length));
                foreach (float end in new[] { -1f, 1f })
                {
                    float z = end * (b.Length * 0.5f - 7f);
                    float ground = t.HeightAt(kit.Frame.MultiplyPoint3x4(new Vector3(0f, 0f, z)).x, kit.Frame.MultiplyPoint3x4(new Vector3(0f, 0f, z)).z);
                    float h = b.Center.y - 1.4f - ground + 3f;
                    if (h > 0.5f) kit.Box(concreteDark, new Vector3(0f, -1.4f - h, z), new Vector3(b.Width - 1f, h, 2.2f));
                }
            }
            kit.Frame = Matrix4x4.identity;
            BuildKit(kit, "Bruecken", parent);
            Report += "Brücken: " + t.Bridges.Count + "\n";
        }

        // ------------------------------------------------------------ Strom und Wind

        static void Pylon(FotoKit k, Vector3 p, float yaw, out Vector3[] arms)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            float h = 42f;
            var baseCorners = new Vector3[4];
            var topCorners = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                var c = new Vector3((i % 2 == 0 ? -1 : 1), 0f, (i < 2 ? -1 : 1));
                baseCorners[i] = p + rot * (c * 4.5f) - Vector3.up * 1f;
                topCorners[i] = p + rot * (c * 1f) + Vector3.up * h;
                k.Beam(galvanized, baseCorners[i], topCorners[i], 0.35f);
            }
            for (float y = 0f; y < h - 4f; y += 7f)
            {
                float f0 = y / h, f1 = (y + 7f) / h;
                for (int i = 0; i < 4; i++)
                {
                    int j = i == 0 ? 1 : (i == 1 ? 3 : (i == 3 ? 2 : 0));
                    Vector3 a0 = Vector3.Lerp(baseCorners[i], topCorners[i], f0), b1 = Vector3.Lerp(baseCorners[j], topCorners[j], f1);
                    k.Beam(galvanized, a0, b1, 0.14f);
                }
            }
            arms = new Vector3[6];
            for (int level = 0; level < 3; level++)
            {
                float y = 26f + level * 6.5f;
                float span = level == 1 ? 12f : 9f;
                Vector3 l = p + rot * new Vector3(-span, y, 0f), r = p + rot * new Vector3(span, y, 0f);
                k.Beam(galvanized, l, r, 0.45f);
                arms[level * 2] = l - Vector3.up * 2.5f;
                arms[level * 2 + 1] = r - Vector3.up * 2.5f;
            }
        }

        static void BuildPowerLine(Transform parent)
        {
            FotoRoad road = null;
            foreach (FotoRoad r in t.Roads) if (r.Name == "Inselquerung") road = r;
            if (road == null) return;
            var kit = new FotoKit();
            Vector3[] previous = null;
            int pylons = 0;
            for (int i = 10; i < road.Points.Count - 10; i += 34)
            {
                Vector2 a = road.Points[i], b = road.Points[Mathf.Min(road.Points.Count - 1, i + 2)];
                Vector2 dir = (b - a).normalized;
                Vector2 pos = a + new Vector2(-dir.y, dir.x) * 75f;
                if (t.InAnyZone(pos, 10f) || t.MaskAt(t.WaterMask, pos.x, pos.y) > 0) { previous = null; continue; }
                float yaw = Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg + 90f;
                Vector3[] arms;
                Pylon(kit, new Vector3(pos.x, t.HeightAt(pos), pos.y), yaw, out arms);
                if (previous != null)
                    for (int c = 0; c < 6; c++) kit.Cable(steel, previous[c], arms[c], 7f, 0.07f, 10);
                previous = arms;
                pylons++;
            }
            BuildKit(kit, "Hochspannungsleitung", parent);
            Report += "Strommasten: " + pylons + "\n";
        }

        static void BuildWindFarm(Transform parent)
        {
            var kit = new FotoKit();
            var placed = new List<Vector2>();
            var sw = new Vector2(2300f, 2300f);
            for (int attempt = 0; attempt < 600 && placed.Count < 8; attempt++)
            {
                Vector2 p = sw + Random2() * 950f;
                float h = t.HeightAt(p);
                if (h < 30f || t.SlopeAt(p.x, p.y) > 12f || t.InAnyZone(p, 120f)) continue;
                if (t.MaskAt(t.RoadMask, p.x, p.y) > 0 || t.MaskAt(t.WaterMask, p.x, p.y) > 0) continue;
                bool far = true;
                foreach (Vector2 q in placed) if ((q - p).sqrMagnitude < 380f * 380f) far = false;
                if (!far) continue;
                placed.Add(p);
                var basePoint = new Vector3(p.x, h - 0.5f, p.y);
                float yaw = 235f + R(-6f, 6f);
                kit.Frame = Matrix4x4.TRS(basePoint, Quaternion.Euler(0f, yaw, 0f), Vector3.one);
                kit.Cylinder(concrete, Vector3.zero, 9f, 1.2f, 20);
                kit.Revolve(white, Vector3.zero, new[] { 0f, 100f }, new[] { 2.5f, 1.5f }, 20, true);
                kit.Box(white, new Vector3(0f, 99f, -2f), new Vector3(4.2f, 4.2f, 13f));
                kit.Sphere(white, new Vector3(0f, 101.2f, 5f), 1.8f, 14);
                float spin = R(0f, 120f);
                for (int bl = 0; bl < 3; bl++)
                {
                    Quaternion r = Quaternion.Euler(0f, 0f, spin + bl * 120f);
                    kit.BoxRot(white, new Vector3(0f, 101.2f, 5.6f) + r * new Vector3(0f, 1.5f, 0f), new Vector3(2.6f, 16f, 0.6f), r);
                    kit.BoxRot(white, new Vector3(0f, 101.2f, 5.6f) + r * new Vector3(0f, 17.3f, 0f), new Vector3(1.6f, 38f, 0.4f), r);
                }
            }
            kit.Frame = Matrix4x4.identity;
            BuildKit(kit, "Windpark", parent);
            Report += "Windräder: " + placed.Count + "\n";
        }

        // ------------------------------------------------------------ Landmarken und Natur

        static void BuildLandmarks(Transform parent)
        {
            GameObject Glb(string name) { return AssetDatabase.LoadAssetAtPath<GameObject>(Requisiten + name + ".glb"); }
            Vector3 Ground(Vector2 p) { return new Vector3(p.x, t.HeightAt(p), p.y); }

            PutHeight(Glb("Leuchtturm"), parent, Ground(t.LighthousePoint), R(0f, 360f), 30f, 0.5f);
            PutHeight(Glb("Funkmast"), parent, Ground(t.MastPoint), 0f, 55f, 0.5f);
            PutLength(Glb("Schiffswrack"), parent, Ground(t.WreckPoint), R(0f, 360f), 38f, 1.5f);
            PutHeight(Glb("Fischerhuette"), parent, Ground(t.HutPoint), R(0f, 360f), 6f, 0.2f);
            GameObject ship = Model("dutch_ship_medium");
            if (ship != null) PutLength(ship, parent, new Vector3(t.ShipPoint.x, 0f, t.ShipPoint.y), R(0f, 360f), 30f, 2.2f);

            // Steg an der Fischerhütte.
            var kit = new FotoKit();
            Vector2 outward;
            Vector2 coast = t.CoastPoint(-160f, out outward);
            Vector2 side = new Vector2(-outward.y, outward.x);
            Vector2 s0 = coast - outward * 15f + side * 20f;
            float yaw = Mathf.Atan2(outward.x, outward.y) * Mathf.Rad2Deg;
            kit.Frame = Matrix4x4.TRS(new Vector3(s0.x, 1.4f, s0.y), Quaternion.Euler(0f, yaw, 0f), Vector3.one);
            kit.Box(planks, new Vector3(0f, 0f, 30f), new Vector3(2.6f, 0.25f, 60f));
            for (float z = 4f; z < 60f; z += 5f)
            {
                kit.Cylinder(planks, new Vector3(-1.2f, -6f, z), 0.18f, 6.2f, 6, true);
                kit.Cylinder(planks, new Vector3(1.2f, -6f, z), 0.18f, 6.2f, 6, true);
            }
            kit.Frame = Matrix4x4.identity;
            BuildKit(kit, "Bootssteg", parent);
        }

        static void ScatterNature(Transform parent)
        {
            int Scatter(string id, int count, int attempts, System.Func<Vector2, float, float, bool> accept, float minScale, float maxScale, float sinkFactor, float cull, float tilt)
            {
                GameObject model = Model(id);
                if (model == null) return 0;
                float size = PrefabBounds(model).size.y;
                int placed = 0;
                for (int i = 0; i < attempts && placed < count; i++)
                {
                    var p = new Vector2(R(500f, FotoTerrain.Size - 500f), R(500f, FotoTerrain.Size - 500f));
                    float h = t.HeightAt(p);
                    float slope = t.SlopeAt(p.x, p.y);
                    if (!accept(p, h, slope)) continue;
                    if (t.InAnyZone(p, 30f) || t.MaskAt(t.RoadMask, p.x, p.y) > 0 || t.MaskAt(t.WaterMask, p.x, p.y) == 255) continue;
                    float scale = R(minScale, maxScale);
                    GameObject go = Vorlagen.Place(id, parent, new Vector3(p.x, h, p.y), R(0f, 360f), scale, false, cull, size * scale * sinkFactor, R(-tilt, tilt));
                    if (go == null) go = Put(model, parent, new Vector3(p.x, h - size * scale * sinkFactor, p.y), Quaternion.Euler(R(-tilt, tilt), R(0f, 360f), 0f), scale);
                    if (go != null) placed++;
                }
                return placed;
            }

            int cliffs = Scatter("rock_face_01", 320, 40000, (p, h, s) => h > 90f && s > 28f && s < 60f, 3f, 8f, 0.3f, 0.003f, 15f);
            int coast = Scatter("rock_face_01", 160, 40000, (p, h, s) => h > -3f && h < 8f && s > 12f, 1.6f, 3.5f, 0.35f, 0.004f, 20f);
            int boulders = Scatter("boulder_01", 180, 30000, (p, h, s) => h > 6f && h < 700f && s < 30f, 1.8f, 4.5f, 0.25f, 0.015f, 10f);
            int moss = Scatter("rock_moss_set_01", 200, 40000, (p, h, s) => h > 10f && t.ForestAt(p.x, p.y, h, s) > 0.5f, 1.4f, 2.6f, 0.2f, 0.015f, 5f);
            int drift = Scatter("dead_tree_trunk", 70, 40000, (p, h, s) => h > 0.4f && h < 2.4f && s < 9f, 1f, 2.2f, 0.25f, 0.02f, 3f);
            int stumps = Scatter("tree_stump_01", 90, 40000, (p, h, s) => { float f = t.ForestAt(p.x, p.y, h, s); return f > 0.15f && f < 0.6f; }, 1.4f, 2.4f, 0.1f, 0.02f, 4f);
            Report += "Felsen: " + (cliffs + coast + boulders + moss) + ", Treibholz: " + drift + ", Baumstümpfe: " + stumps + "\n";
        }
    }
}
