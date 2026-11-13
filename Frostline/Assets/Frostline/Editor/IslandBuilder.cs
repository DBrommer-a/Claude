using System.Collections.Generic;
using Frostline.Flight;
using Frostline.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Menü „Frostline > 4 · Insel erstellen“: die ganze Insel aus dem Plan als spielbarer Rohbau.
    /// 4 × 4 km in vier Terrain-Tiles, alle Zonen mit Stellen zum Reinfliegen, drei Rennstrecken,
    /// Checkpoints und Sammelobjekte. Die Formen sind Platzhalter; sie zeigen Maßstab, Wege und
    /// Einfluglöcher, bis die gekauften und kostenlosen Assets sie ersetzen.
    /// </summary>
    public static class IslandBuilder
    {
        const string ScenePath = "Assets/Frostline/Scenes/Frostline_Insel.unity";
        const string Folder = GameSystemsBuilder.GeneratedFolder + "/Insel";
        const string MeshFolder = Folder + "/Meshes";
        const float CloudBottom = 650f;
        const float CloudThickness = 1000f;

        static IslandMaterials mat;
        static System.Random rng;
        static Transform world;
        static Transform rings;
        static Transform checkpoints;
        static readonly List<RaceTrack> tracks = new List<RaceTrack>();
        static int meshCounter;

        // Gemerkte Stellen für Strecken, Sammelobjekte und Checkpoints.
        static Vector3 spawn;
        static Vector3 beachCoast;
        static Vector3 wreckCenter;
        static Quaternion wreckRotation;
        static Vector3 pierStart;
        static Vector3 pierDirection;
        static Vector3 lighthouseBase;
        static Vector3 industry;
        static Vector3 craneBridge;
        static Vector3 chimneyBase;
        static Vector3 tunnelCenter;
        static Vector3 cityCenter;
        static Vector3 churchTower;
        static Vector3 barnCenter;
        static Vector3 mastTop;
        static Vector3 iceTunnelCenter;
        static Quaternion iceTunnelRotation;
        static Vector3 archCenter;

        [MenuItem("Frostline/4 · Insel erstellen (4 × 4 km)", priority = 4)]
        public static void Build()
        {
            if (!HdrpAccess.IsInstalled)
            {
                EditorPrompts.Dialog("Frostline", "HDRP ist noch nicht geladen. Bitte zuerst „Frostline > 1 · Projekt einrichten“ ausführen.", "OK");
                return;
            }
            if (!EditorPrompts.SaveScenes()) return;

            try
            {
                FrostlineProjectSetup.EnsureFolder("Assets/Frostline/Scenes");
                FrostlineProjectSetup.EnsureFolder(MeshFolder);
                meshCounter = 0;
                tracks.Clear();
                rng = new System.Random(20261113);

                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Progress("Himmel, Wetter, Ozean", 0.02f);
                EnvironmentRefs env = GameSystemsBuilder.CreateEnvironment(CloudBottom, CloudThickness);
                mat = IslandMaterials.Create();

                world = new GameObject("Insel").transform;
                rings = new GameObject("Sammelobjekte").transform;
                checkpoints = new GameObject("Checkpoints").transform;

                Terrain[] terrains = CreateTerrains();

                Progress("Strand und Wrack", 0.7f);
                BuildBeach();
                Progress("Hafen und Fischerdorf", 0.72f);
                BuildHarbor();
                Progress("Industriegebiet", 0.75f);
                BuildIndustry();
                Progress("Stadt", 0.8f);
                BuildCity();
                Progress("Dörfer", 0.83f);
                BuildVillages();
                Progress("Gletscher", 0.86f);
                BuildGlacier();
                Progress("Wald und Funkmast", 0.88f);
                BuildForestDetails();
                BuildRadioMast();

                Progress("Rennstrecken", 0.9f);
                BuildTracks();
                BuildCollectibles();
                BuildCheckpoints();
                BuildAmbientSound();

                Progress("Drohne und Spielsysteme", 0.95f);
                Transform spawnPoint = BuildLaunchPad();
                DroneController drone = GameSystemsBuilder.CreateDrone(spawnPoint);
                var options = new SystemsOptions
                {
                    IslandCenter = IslandShape.Center,
                    SignalCleanRadius = 2300f,
                    SignalLostRadius = 2900f,
                    BenchmarkCenter = new Vector3(IslandShape.Center.x, 0f, IslandShape.Center.y),
                    BenchmarkRadius = 1300f,
                    CloudBottom = CloudBottom,
                    CloudThickness = CloudThickness,
                    Checkpoints = checkpoints,
                    Collectibles = rings,
                    Tracks = new List<RaceTrack>(tracks),
                };
                GameSystemsBuilder.CreateSystems(drone, env, options);

                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveScene(scene, ScenePath);
                GameSystemsBuilder.AddSceneToBuild(ScenePath, true);
                Selection.activeGameObject = drone.gameObject;
                SceneView.lastActiveSceneView?.FrameSelected();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorPrompts.Dialog("Frostline",
                "Die Insel ist fertig: Frostline_Insel.unity\n\n"
                + "Start am Strand im Süden. Im Menü (F1 › Spiel) gibt es drei Rennen: Strandlinie, Industrie-Slalom und Gletscher-Dive. "
                + "12 Sammelringe hängen an den Landmarken.\n\n"
                + "Alle Gebäude sind Rohbau-Platzhalter in Originalgröße; sie werden später durch die Assets ersetzt.",
                "OK");
        }

        static void Progress(string step, float value)
        {
            EditorUtility.DisplayProgressBar("Frostline – Insel", step, value);
        }

        static Vector3 Ground(float x, float z)
        {
            return IslandShape.Ground(x, z);
        }

        static GameObject Emit(MeshKit kit, string name, Transform parent, Vector3 position, Quaternion rotation, bool collider = true)
        {
            meshCounter++;
            GameObject go = kit.Build(name.Replace(' ', '_') + "_" + meshCounter, parent, MeshFolder, collider);
            go.name = name;
            go.transform.SetPositionAndRotation(position, rotation);
            return go;
        }

        static Transform Zone(string name)
        {
            var t = new GameObject("Zone: " + name).transform;
            t.SetParent(world, false);
            return t;
        }

        // ------------------------------------------------------------ Terrain

        static Terrain[] CreateTerrains()
        {
            const int HeightResolution = 2049;
            const int SplatResolution = 1024;

            TerrainLayer[] layers =
            {
                PrototypeSceneBuilder.CreateLayer("Insel_" + "Sand", new Color(0.36f, 0.34f, 0.31f), new Color(0.2f, 0.19f, 0.18f), 6f, 0.3f, 0.6f),
                PrototypeSceneBuilder.CreateLayer("Insel_" + "Kies_Gras", new Color(0.33f, 0.34f, 0.3f), new Color(0.2f, 0.22f, 0.18f), 9f, 0.15f, 1.4f),
                PrototypeSceneBuilder.CreateLayer("Insel_" + "Fels", new Color(0.3f, 0.3f, 0.32f), new Color(0.16f, 0.16f, 0.18f), 18f, 0.2f, 2.2f),
                PrototypeSceneBuilder.CreateLayer("Insel_" + "Schnee", new Color(0.86f, 0.88f, 0.92f), new Color(0.74f, 0.78f, 0.86f), 12f, 0.45f, 0.35f),
                PrototypeSceneBuilder.CreateLayer("Insel_" + "Eis", new Color(0.66f, 0.82f, 0.92f), new Color(0.45f, 0.62f, 0.78f), 20f, 0.8f, 0.8f),
            };

            GameObject treePrefab = CreateTreePrefab();
            var terrains = new Terrain[4];
            var parent = new GameObject("Terrain (4 Tiles à 2 × 2 km)").transform;
            parent.SetParent(world, false);

            for (int tz = 0; tz < 2; tz++)
            {
                for (int tx = 0; tx < 2; tx++)
                {
                    int index = tz * 2 + tx;
                    float originX = tx * IslandShape.TileSize;
                    float originZ = tz * IslandShape.TileSize;
                    string label = "Tile " + (tx == 0 ? "West" : "Ost") + "-" + (tz == 0 ? "Süd" : "Nord");

                    // Erst als Asset speichern, dann Schichten setzen: Sonst speichert Unity die Splatmap-
                    // Texturen nicht mit, und beim Laden ist das ganze Terrain nur noch Sand.
                    string dataPath = Folder + "/Terrain_" + tx + "_" + tz + ".asset";
                    if (AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath) != null) AssetDatabase.DeleteAsset(dataPath);
                    var data = new TerrainData();
                    AssetDatabase.CreateAsset(data, dataPath);
                    data.heightmapResolution = HeightResolution;
                    data.size = new Vector3(IslandShape.TileSize, IslandShape.HeightRange, IslandShape.TileSize);
                    float baseProgress = 0.05f + index * 0.15f;
                    float[,] heights = IslandShape.BuildHeightmap(originX, originZ, HeightResolution,
                        p => Progress("Terrain " + label + ": Höhen", baseProgress + p * 0.1f));
                    data.SetHeights(0, 0, heights);

                    Progress("Terrain " + label + ": Bodenschichten", baseProgress + 0.11f);
                    data.terrainLayers = layers;
                    data.alphamapResolution = SplatResolution;
                    data.SetAlphamaps(0, 0, IslandShape.BuildSplatmap(heights, originX, originZ, SplatResolution));

                    Progress("Terrain " + label + ": Bäume", baseProgress + 0.13f);
                    data.treePrototypes = new[] { new TreePrototype { prefab = treePrefab } };
                    data.SetTreeInstances(PlaceTrees(originX, originZ), true);

                    EditorUtility.SetDirty(data);

                    GameObject go = Terrain.CreateTerrainGameObject(data);
                    go.name = label;
                    go.transform.SetParent(parent, false);
                    go.transform.position = new Vector3(originX, IslandShape.SeabedDepth, originZ);

                    Terrain terrain = go.GetComponent<Terrain>();
                    terrain.heightmapPixelError = 8f;
                    terrain.drawInstanced = true;
                    terrain.basemapDistance = 900f;
                    terrain.treeDistance = 1500f;
                    terrain.treeBillboardDistance = 400f;
                    terrain.allowAutoConnect = true;
                    terrain.groupingID = 1;
                    terrain.shadowCastingMode = ShadowCastingMode.On;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                    terrains[index] = terrain;
                }
            }

            // Nachbarn setzen, damit die Detailstufen an den Kanten nahtlos passen.
            terrains[0].SetNeighbors(null, terrains[2], terrains[1], null);
            terrains[1].SetNeighbors(terrains[0], terrains[3], null, null);
            terrains[2].SetNeighbors(null, null, terrains[3], terrains[0]);
            terrains[3].SetNeighbors(terrains[2], null, null, terrains[1]);
            return terrains;
        }

        static GameObject CreateTreePrefab()
        {
            var kit = new MeshKit();
            IslandStructures.PineTree(kit, mat);
            GameObject tree = kit.Build("Kiefer", null, Folder, false, false);
            CapsuleCollider capsule = tree.AddComponent<CapsuleCollider>();
            capsule.radius = 0.4f;
            capsule.height = 10f;
            capsule.center = new Vector3(0f, 5f, 0f);
            string path = Folder + "/Kiefer.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(tree, path);
            Object.DestroyImmediate(tree);
            return prefab;
        }

        /// <summary>Wald im Westen, dazu lockere Bäume an den Dörfern und im Tal.</summary>
        static TreeInstance[] PlaceTrees(float originX, float originZ)
        {
            var list = new List<TreeInstance>();
            var random = new System.Random((int)(originX * 7 + originZ * 13) + 5);
            float tile = IslandShape.TileSize;

            void TryAdd(float x, float z)
            {
                if (x < originX || x >= originX + tile || z < originZ || z >= originZ + tile) return;
                float h = IslandShape.Height(x, z);
                if (h < 4f || h > 420f) return;
                // Nicht auf steilen Hängen und nicht mitten in den ebenen Zonen.
                float slope = Mathf.Abs(IslandShape.Height(x + 3f, z) - h) + Mathf.Abs(IslandShape.Height(x, z + 3f) - h);
                if (slope > 2.8f) return;
                if (InZone(x, z, 30f)) return;
                float scale = 0.8f + (float)random.NextDouble() * 0.7f;
                list.Add(new TreeInstance
                {
                    position = new Vector3((x - originX) / tile, 0f, (z - originZ) / tile),
                    widthScale = scale,
                    heightScale = scale * (0.9f + (float)random.NextDouble() * 0.3f),
                    rotation = (float)random.NextDouble() * Mathf.PI * 2f,
                    color = Color.white,
                    lightmapColor = Color.white,
                    prototypeIndex = 0,
                });
            }

            // Dichter Wald: rund ein Baum pro 140 m².
            int forestCount = 3200;
            for (int i = 0; i < forestCount; i++)
            {
                float a = (float)random.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Sqrt((float)random.NextDouble()) * IslandShape.ForestRadius;
                TryAdd(IslandShape.Forest.x + Mathf.Cos(a) * r, IslandShape.Forest.y + Mathf.Sin(a) * r);
            }
            // Lockere Gruppen über der übrigen Insel.
            for (int i = 0; i < 2500; i++)
            {
                float x = (float)random.NextDouble() * IslandShape.Size;
                float z = (float)random.NextDouble() * IslandShape.Size;
                if (IslandShape.Fbm(x * 0.004f, z * 0.004f, 2) > 0.56f) TryAdd(x, z);
            }
            return list.ToArray();
        }

        static bool InZone(float x, float z, float margin)
        {
            IslandShape.Zone[] zones = { IslandShape.Industry, IslandShape.City, IslandShape.ValleyVillage, IslandShape.MountainVillage, IslandShape.Harbor, IslandShape.FishingVillage };
            foreach (IslandShape.Zone zone in zones)
            {
                if (Vector2.Distance(new Vector2(x, z), zone.Position) < zone.Radius + margin) return true;
            }
            return false;
        }

        // ------------------------------------------------------------ Strand

        static void BuildBeach()
        {
            Transform zone = Zone("Strand");
            beachCoast = IslandShape.CoastPoint(new Vector2(1950f, -50f), new Vector2(1950f, 2000f), 1.5f);
            spawn = Ground(beachCoast.x, beachCoast.z + 35f);

            // Wrack: halb im Wasser an der Küste östlich vom Start, auf der Seite liegend.
            Vector3 wreckCoast = IslandShape.CoastPoint(new Vector2(2330f, -50f), new Vector2(2330f, 2000f), 0.5f);
            wreckCenter = new Vector3(wreckCoast.x, 3.5f, wreckCoast.z - 6f);
            wreckRotation = Quaternion.Euler(0f, 78f, 0f) * Quaternion.Euler(0f, 0f, 62f);
            var kit = new MeshKit();
            IslandStructures.Shipwreck(kit, mat);
            Emit(kit, "Schiffswrack", zone, wreckCenter, wreckRotation);

            // Treibholz
            var drift = new MeshKit();
            for (int i = 0; i < 18; i++)
            {
                float x = beachCoast.x - 300f + (float)rng.NextDouble() * 650f;
                Vector3 p = IslandShape.CoastPoint(new Vector2(x, -50f), new Vector2(x, 2000f), 1.2f) + new Vector3(0f, 0.2f, 4f + (float)rng.NextDouble() * 10f);
                Vector3 d = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f) * Vector3.forward * (2f + (float)rng.NextDouble() * 5f);
                drift.Log(mat.Bark, p - d * 0.5f, p + d * 0.5f, 0.2f + (float)rng.NextDouble() * 0.2f);
            }
            Emit(drift, "Treibholz", zone, Vector3.zero, Quaternion.identity, false);
        }

        static Transform BuildLaunchPad()
        {
            Material padMaterial = GameSystemsBuilder.LitMaterial("Startplatz", new Color(0.18f, 0.18f, 0.2f), 0.3f);
            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "Startplatz";
            pad.transform.SetParent(world, false);
            pad.transform.position = spawn + new Vector3(0f, 0.25f, 0f);
            pad.transform.localScale = new Vector3(2f, 0.5f, 2f);
            pad.GetComponent<Renderer>().sharedMaterial = padMaterial;

            var point = new GameObject("Startpunkt").transform;
            point.SetParent(world, false);
            point.position = spawn + new Vector3(0f, 0.56f, 0f);
            point.rotation = Quaternion.identity; // Blick nach Norden auf die Insel
            return point;
        }

        // ------------------------------------------------------------ Hafen

        static void BuildHarbor()
        {
            Transform zone = Zone("Hafen");
            IslandShape.Zone harbor = IslandShape.Harbor;
            Vector2 outward2 = (harbor.Position - IslandShape.Center).normalized;
            pierDirection = new Vector3(outward2.x, 0f, outward2.y);
            Vector3 side = Vector3.Cross(Vector3.up, pierDirection);

            // Pier beginnt an der Kante der Hafenfläche und führt 120 m aufs Meer.
            Vector3 edge = harbor.Ground + pierDirection * (harbor.Radius - 10f);
            for (float s = 0f; s < 400f; s += 2f)
            {
                Vector3 p = harbor.Ground + pierDirection * s;
                if (IslandShape.Height(p.x, p.z) < 0.5f)
                {
                    edge = p;
                    break;
                }
            }
            pierStart = new Vector3(edge.x, 0f, edge.z) - pierDirection * 8f;
            var kit = new MeshKit();
            IslandStructures.Pier(kit, mat, 120f, 8f);
            Emit(kit, "Pier", zone, pierStart, Quaternion.LookRotation(pierDirection));

            var shed = new MeshKit();
            IslandStructures.Warehouse(shed, mat, 20f, 44f, 10f);
            Emit(shed, "Lagerhalle (offen)", zone, harbor.Ground - side * 70f, Quaternion.LookRotation(pierDirection));

            // Leuchtturm auf der Landspitze neben dem Hafen.
            Vector2 lightTarget = harbor.Position + new Vector2(side.x, side.z) * 260f;
            Vector3 coast = IslandShape.CoastPoint(lightTarget + outward2 * 600f, IslandShape.Center, 2f);
            lighthouseBase = coast - pierDirection * 6f;
            lighthouseBase.y = IslandShape.Height(lighthouseBase.x, lighthouseBase.z);
            var tower = new MeshKit();
            float gallery;
            IslandStructures.Lighthouse(tower, mat, out gallery);
            Emit(tower, "Leuchtturm", zone, lighthouseBase, Quaternion.identity);

            // Boote am Pier
            var boats = new MeshKit();
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = pierStart + pierDirection * (20f + i * 24f) + side * (i % 2 == 0 ? 9f : -9f);
                IslandStructures.Boat(boats, mat, p, Quaternion.LookRotation(pierDirection).eulerAngles.y + (float)rng.NextDouble() * 20f, i % 2 == 0 ? mat.RedPaint : mat.WhitePaint);
            }
            Emit(boats, "Boote", zone, Vector3.zero, Quaternion.identity);

            // Fischerdorf
            Transform village = Zone("Fischerdorf");
            IslandShape.Zone fishing = IslandShape.FishingVillage;
            var huts = new MeshKit();
            for (int i = 0; i < 7; i++)
            {
                float a = i * Mathf.PI * 2f / 7f;
                Vector3 p = fishing.Ground + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (35f + (float)rng.NextDouble() * 25f);
                p.y = IslandShape.Height(p.x, p.z);
                IslandStructures.House(huts, i % 3 == 0 ? mat.WoodOchre : mat.WoodRed, mat, p, -a * Mathf.Rad2Deg + 90f, 5.5f, 7f, 3.2f);
            }
            // Trockengestelle für Fisch
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = fishing.Ground + new Vector3(-20f + i * 9f, 0f, -8f);
                huts.Beam(mat.WoodDark, p + new Vector3(-3f, 0f, 0f), p + new Vector3(-3f, 2.4f, 0f), 0.15f);
                huts.Beam(mat.WoodDark, p + new Vector3(3f, 0f, 0f), p + new Vector3(3f, 2.4f, 0f), 0.15f);
                huts.Beam(mat.WoodDark, p + new Vector3(-3f, 2.3f, 0f), p + new Vector3(3f, 2.3f, 0f), 0.1f);
            }
            Emit(huts, "Fischerhütten", village, Vector3.zero, Quaternion.identity);
        }

        // ------------------------------------------------------------ Industrie

        static void BuildIndustry()
        {
            Transform zone = Zone("Industriegebiet");
            industry = IslandShape.Industry.Ground;

            // Drei Hallen nebeneinander, Längsachse Nord-Süd.
            float[] offsets = { -54f, 0f, 54f };
            for (int i = 0; i < 3; i++)
            {
                var kit = new MeshKit();
                IslandStructures.FactoryHall(kit, mat, 36f, 72f, new System.Random(100 + i));
                Emit(kit, "Fabrikhalle " + (i + 1), zone, industry + new Vector3(offsets[i], 0f, 0f), Quaternion.identity);
            }

            // Silo und Förderband-Tunnel nördlich der Hallen (Tunnel 13 m vor dem Silo offen).
            var silo = new MeshKit();
            IslandStructures.Silo(silo, mat, 7f, 34f);
            Emit(silo, "Silo", zone, industry + new Vector3(125f, 0f, 48f), Quaternion.identity);

            var tunnel = new MeshKit();
            IslandStructures.ConveyorTunnel(tunnel, mat, 32f, 12f);
            tunnelCenter = industry + new Vector3(92f, 0f, 48f);
            Emit(tunnel, "Förderband-Tunnel", zone, tunnelCenter, Quaternion.identity);
            tunnelCenter += new Vector3(0f, 14.25f, 0f);

            var chimney = new MeshKit();
            IslandStructures.Chimney(chimney, mat, 3.2f, 72f);
            chimneyBase = industry + new Vector3(115f, 0f, -50f);
            Emit(chimney, "Schornstein", zone, chimneyBase, Quaternion.identity);

            var crane = new MeshKit();
            IslandStructures.CraneRunway(crane, mat, 150f, 20f, 14f, 10f);
            Vector3 craneCenter = industry + new Vector3(0f, 0f, 75f);
            Emit(crane, "Kranbahn", zone, craneCenter, Quaternion.identity);
            craneBridge = craneCenter + new Vector3(10f, 0f, 0f);

            var rubble = new MeshKit();
            IslandStructures.Rubble(rubble, mat, industry + new Vector3(-110f, 0f, -20f), 18f, 30, rng);
            IslandStructures.Rubble(rubble, mat, industry + new Vector3(80f, 0f, -80f), 12f, 18, rng);
            Emit(rubble, "Schutt", zone, Vector3.zero, Quaternion.identity);
        }

        // ------------------------------------------------------------ Stadt

        static void BuildCity()
        {
            Transform zone = Zone("Stadt");
            cityCenter = IslandShape.City.Ground;

            var plaza = new MeshKit();
            plaza.Box(mat.Plaza, new Vector3(0f, 0.1f, 0f), new Vector3(300f, 0.2f, 300f));
            Emit(plaza, "Stadtboden", zone, cityCenter, Quaternion.identity);

            var shell = new MeshKit();
            IslandStructures.UnfinishedTower(shell, mat, 24f, 16, 3.8f, rng);
            Emit(shell, "Rohbau-Hochhaus", zone, cityCenter, Quaternion.identity);

            // Skyline: acht Glastürme im Raster, 40 m Lücken dazwischen.
            float[] heights = { 92f, 148f, 71f, 120f, 166f, 84f, 135f, 103f };
            int n = 0;
            for (int gx = -1; gx <= 1; gx++)
            {
                for (int gz = -1; gz <= 1; gz++)
                {
                    if (gx == 0 && gz == 0) continue;
                    var tower = new MeshKit();
                    float w = 26f + (n % 3) * 4f;
                    IslandStructures.GlassTower(tower, mat, w, 30f - (n % 2) * 4f, heights[n]);
                    Emit(tower, "Hochhaus " + (n + 1), zone, cityCenter + new Vector3(gx * 72f, 0f, gz * 72f), Quaternion.Euler(0f, (n % 2) * 8f, 0f));
                    n++;
                }
            }
        }

        // ------------------------------------------------------------ Dörfer

        static void BuildVillages()
        {
            BuildVillage(IslandShape.ValleyVillage, 11, true, true);
            BuildVillage(IslandShape.MountainVillage, 7, false, false);
        }

        static void BuildVillage(IslandShape.Zone village, int houses, bool church, bool bridge)
        {
            Transform zone = Zone(village.Name);
            var kit = new MeshKit();
            Material[] colors = { mat.WoodRed, mat.WoodDark, mat.WoodOchre, mat.WoodRed };
            for (int i = 0; i < houses; i++)
            {
                float a = i * Mathf.PI * 2f / houses + (float)rng.NextDouble() * 0.3f;
                float r = village.Radius * (0.45f + (float)rng.NextDouble() * 0.4f);
                Vector3 p = village.Ground + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                p.y = IslandShape.Height(p.x, p.z);
                IslandStructures.House(kit, colors[i % colors.Length], mat, p, -a * Mathf.Rad2Deg, 8f, 10f, 5f);
            }

            // Zwei Scheunen (eine im Bergdorf), offen zum Durchfliegen.
            Vector3 barn = village.Ground + new Vector3(-village.Radius * 0.3f, 0f, village.Radius * 0.1f);
            IslandStructures.Barn(kit, mat.WoodRed, mat, barn, 20f);
            if (!church) barnCenter = barn + Vector3.up * 3f;
            if (church)
            {
                Vector3 second = village.Ground + new Vector3(village.Radius * 0.15f, 0f, -village.Radius * 0.35f);
                IslandStructures.Barn(kit, mat.WoodDark, mat, second, 95f);
                churchTower = village.Ground + new Vector3(0f, 0f, village.Radius * 0.2f);
                IslandStructures.Church(kit, mat, churchTower, 0f);
                // Glockenstuhl liegt am Turm vor dem Kirchenschiff.
                churchTower += new Vector3(0f, 18.5f, 11f + 2.5f);
            }
            Emit(kit, village.Name + " Gebäude", zone, Vector3.zero, Quaternion.identity);

            if (bridge)
            {
                var b = new MeshKit();
                Vector3 p = village.Ground + new Vector3(village.Radius + 40f, 0f, 0f);
                p.y = IslandShape.Height(p.x, p.z);
                IslandStructures.Bridge(b, mat, p, 0f, 22f);
                Emit(b, "Steinbrücke", zone, Vector3.zero, Quaternion.identity);
            }
        }

        // ------------------------------------------------------------ Gletscher

        static Vector3 GlacierPoint(float t, float across = 0f, float above = 0f)
        {
            Vector2 top = IslandShape.GlacierTop;
            Vector2 foot = IslandShape.GlacierFoot;
            Vector2 axis = (foot - top).normalized;
            Vector2 normal = new Vector2(axis.y, -axis.x);
            Vector2 p = Vector2.Lerp(top, foot, t) + normal * across;
            float floor = Mathf.Abs(across) < 1f ? IslandShape.GlacierFloor(t) : IslandShape.Height(p.x, p.y);
            return new Vector3(p.x, floor + above, p.y);
        }

        static Quaternion GlacierDirection()
        {
            Vector3 a = GlacierPoint(0f);
            Vector3 b = GlacierPoint(1f);
            return Quaternion.LookRotation((b - a).normalized);
        }

        static void BuildGlacier()
        {
            Transform zone = Zone("Gletscher");
            Quaternion down = GlacierDirection();

            var tunnel = new MeshKit();
            IslandStructures.IceTunnel(tunnel, mat, 170f);
            iceTunnelCenter = GlacierPoint(0.72f);
            iceTunnelRotation = down;
            Emit(tunnel, "Eistunnel", zone, iceTunnelCenter, iceTunnelRotation);

            // Spalten und Eisblöcke oberhalb und unterhalb des Tunnels.
            foreach (float t in new[] { 0.35f, 0.5f, 0.88f })
            {
                var seracs = new MeshKit();
                IslandStructures.Seracs(seracs, mat, new System.Random((int)(t * 1000)), 90f, 14);
                Emit(seracs, "Eisblöcke", zone, GlacierPoint(t), down);
            }

            // Felsbogen am Talausgang (Strecke führt hindurch) und einer an der Flanke.
            var arch = new MeshKit();
            IslandStructures.RockArch(arch, mat, 26f);
            archCenter = GlacierPoint(0.95f);
            Emit(arch, "Felsbogen", zone, archCenter, Quaternion.Euler(0f, down.eulerAngles.y, 0f));

            var arch2 = new MeshKit();
            IslandStructures.RockArch(arch2, mat, 40f);
            Vector3 flank = GlacierPoint(0.42f, 230f);
            Emit(arch2, "Felsbogen (Flanke)", zone, flank - Vector3.up * 2f, Quaternion.Euler(0f, down.eulerAngles.y + 70f, 0f));
        }

        // ------------------------------------------------------------ Wald, Funkmast

        static void BuildForestDetails()
        {
            Transform zone = Zone("Wald");
            var logs = new MeshKit();
            // Umgestürzte Stämme auf Stümpfen: drunter durchfliegen.
            for (int i = 0; i < 14; i++)
            {
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = (float)rng.NextDouble() * IslandShape.ForestRadius * 0.8f;
                Vector3 p = Ground(IslandShape.Forest.x + Mathf.Cos(a) * r, IslandShape.Forest.y + Mathf.Sin(a) * r);
                Vector3 d = Quaternion.Euler(0f, (float)rng.NextDouble() * 180f, 0f) * Vector3.forward * 9f;
                Vector3 a0 = p - d;
                Vector3 a1 = p + d;
                a0.y = IslandShape.Height(a0.x, a0.z) + 1.6f;
                a1.y = IslandShape.Height(a1.x, a1.z) + 0.4f;
                logs.Log(mat.Bark, a0, a1, 0.45f);
                logs.Box(mat.Bark, new Vector3(a0.x, a0.y - 0.9f, a0.z), new Vector3(0.9f, 1.8f, 0.9f));
            }
            Emit(logs, "Umgestürzte Stämme", zone, Vector3.zero, Quaternion.identity);
        }

        static void BuildRadioMast()
        {
            Transform zone = Zone("Funkmast");
            // Auf einem Hügel zwischen Stadt und Tal-Dorf: höchsten Punkt in der Umgebung suchen.
            Vector3 best = Ground(1700f, 1300f);
            for (float x = 1550f; x <= 1850f; x += 25f)
            {
                for (float z = 1150f; z <= 1450f; z += 25f)
                {
                    Vector3 p = Ground(x, z);
                    if (p.y > best.y && !InZone(x, z, 40f)) best = p;
                }
            }
            var kit = new MeshKit();
            Vector3 top;
            IslandStructures.RadioMast(kit, mat, 84f, out top);
            Emit(kit, "Funkmast", zone, best, Quaternion.identity);
            mastTop = best + top;
        }

        // ------------------------------------------------------------ Strecken

        static RaceTrack NewTrack(string name, int laps, Vector3 start, Vector3 lookAt)
        {
            var go = new GameObject("Strecke: " + name);
            go.transform.SetParent(world, false);
            RaceTrack track = go.AddComponent<RaceTrack>();
            track.trackName = name;
            track.laps = laps;
            var startPoint = new GameObject("Start").transform;
            startPoint.SetParent(go.transform, false);
            startPoint.position = start;
            Vector3 flat = lookAt - start;
            flat.y = 0f;
            startPoint.rotation = Quaternion.LookRotation(flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.forward);
            track.startPoint = startPoint;
            tracks.Add(track);
            return track;
        }

        /// <summary>Gates einer Strecke; jedes zeigt zum nächsten (letztes zum ersten).</summary>
        static void AddGates(RaceTrack track, IList<Vector3> points, float width, float height, IList<Quaternion> fixedRotations = null)
        {
            for (int i = 0; i < points.Count; i++)
            {
                Quaternion rotation;
                // Unitys == für Quaternionen taugt nicht, um „nicht gesetzt“ (0,0,0,0) zu erkennen.
                bool isSet = fixedRotations != null && i < fixedRotations.Count
                    && (fixedRotations[i].x * fixedRotations[i].x + fixedRotations[i].y * fixedRotations[i].y
                        + fixedRotations[i].z * fixedRotations[i].z + fixedRotations[i].w * fixedRotations[i].w) > 0.5f;
                if (isSet)
                {
                    rotation = fixedRotations[i];
                }
                else
                {
                    Vector3 previous = i > 0 ? points[i - 1] : track.startPoint.position;
                    Vector3 next = i + 1 < points.Count ? points[i + 1] : points[i] + (points[i] - previous);
                    Vector3 direction = (next - previous).normalized;
                    rotation = Quaternion.LookRotation(direction.sqrMagnitude > 0.01f ? direction : Vector3.forward);
                }

                var kit = new MeshKit();
                IslandStructures.GateFrame(kit, mat, width, height);
                GameObject gate = Emit(kit, "Gate " + (i + 1), track.transform, points[i], rotation);
                RaceGate rg = gate.AddComponent<RaceGate>();
                rg.width = width;
                rg.height = height;
                rg.openingOffset = Vector3.zero;
            }
        }

        static void BuildTracks()
        {
            // 1 · Strandlinie: am Strand nach Osten, durchs Wrack, unter den Pier wäre zu weit; zurück durchs Hinterland.
            {
                RaceTrack track = NewTrack("Strandlinie", 2, spawn + Vector3.up * 0.6f, spawn + Vector3.right * 100f);
                var points = new List<Vector3>();
                foreach (float x in new[] { 2060f, 2190f })
                {
                    Vector3 c = IslandShape.CoastPoint(new Vector2(x, -50f), new Vector2(x, 2000f), 1.5f);
                    points.Add(c + new Vector3(0f, 3f, 14f));
                }
                points.Add(wreckCenter + wreckRotation * new Vector3(0f, -4f, -36f));
                points.Add(wreckCenter + wreckRotation * new Vector3(0f, -4f, 36f));
                foreach (float x in new[] { 2560f, 2380f, 2150f, 1900f })
                {
                    Vector3 c = IslandShape.CoastPoint(new Vector2(x, -50f), new Vector2(x, 2000f), 1.5f);
                    Vector3 inland = Ground(x, c.z + 90f);
                    points.Add(inland + Vector3.up * 3.5f);
                }
                Vector3 finish = Ground(spawn.x - 20f, spawn.z + 15f) + Vector3.up * 3f;
                points.Add(finish);
                // Die beiden Wrack-Gates zeigen in Längsrichtung des Rumpfs.
                Quaternion along = wreckRotation;
                AddGates(track, points, 5f, 3.5f, new[] { default(Quaternion), default(Quaternion), along, along });
            }

            // 2 · Industrie-Slalom: durch zwei Hallen, unter der Kranbrücke, durch den Förderband-Tunnel.
            {
                Vector3 start = industry + new Vector3(-54f, 0.6f, -110f);
                RaceTrack track = NewTrack("Industrie-Slalom", 2, start, industry + new Vector3(-54f, 0f, 0f));
                var points = new List<Vector3>
                {
                    industry + new Vector3(-54f, 4.5f, -38f),   // Halle 1 Südtor rein
                    industry + new Vector3(-54f, 4.5f, 38f),    // Halle 1 Nordtor raus
                    craneBridge + new Vector3(-20f, 7f, 0f),    // unter der Kranbrücke
                    industry + new Vector3(54f, 4.5f, 38f),     // Halle 3 Nordtor rein
                    industry + new Vector3(54f, 4.5f, -38f),    // Halle 3 Südtor raus
                    chimneyBase + new Vector3(14f, 8f, 0f),     // am Schornstein vorbei
                    tunnelCenter + new Vector3(18f, 0f, 0f),    // vor dem Tunnel
                    tunnelCenter,                               // im Tunnel nach Westen
                    industry + new Vector3(0f, 4.5f, 38f),      // Halle 2 Nordtor rein
                    industry + new Vector3(0f, 4.5f, -38f),     // Halle 2 Südtor raus
                };
                var rotations = new Quaternion[points.Count];
                rotations[0] = Quaternion.LookRotation(Vector3.forward);
                rotations[1] = Quaternion.LookRotation(Vector3.forward);
                rotations[3] = Quaternion.LookRotation(Vector3.back);
                rotations[4] = Quaternion.LookRotation(Vector3.back);
                rotations[6] = Quaternion.LookRotation(Vector3.left);
                rotations[7] = Quaternion.LookRotation(Vector3.left);
                rotations[8] = Quaternion.LookRotation(Vector3.back);
                rotations[9] = Quaternion.LookRotation(Vector3.back);
                AddGates(track, points, 4f, 3.2f, rotations);
            }

            // 3 · Gletscher-Dive: vom Kar oben das Tal hinab, durch den Eistunnel und den Felsbogen.
            {
                Vector3 start = GlacierPoint(0.02f, 0f, 2f);
                RaceTrack track = NewTrack("Gletscher-Dive", 1, start, GlacierPoint(0.2f));
                var points = new List<Vector3>
                {
                    GlacierPoint(0.15f, 0f, 9f),
                    GlacierPoint(0.29f, 30f, 7f),
                    GlacierPoint(0.43f, -28f, 10f),
                    GlacierPoint(0.57f, 0f, 6f),
                    GlacierPoint(0.72f, 0f, 4.5f),   // im Eistunnel
                    GlacierPoint(0.86f, 0f, 6f),
                    archCenter + Vector3.up * 5f,    // durch den Felsbogen
                };
                Quaternion down = GlacierDirection();
                var rotations = new Quaternion[points.Count];
                rotations[4] = down;
                rotations[6] = Quaternion.Euler(0f, down.eulerAngles.y, 0f);
                AddGates(track, points, 6f, 4f, rotations);
            }
        }

        // ------------------------------------------------------------ Sammelobjekte, Checkpoints, Ton

        static void Ring(string name, Vector3 position, float yaw)
        {
            var kit = new MeshKit();
            IslandStructures.CollectRing(kit, mat);
            Emit(kit, name, rings, position, Quaternion.Euler(0f, yaw, 0f), false);
        }

        static void BuildCollectibles()
        {
            Ring("Funkmast-Spitze", mastTop + Vector3.up * 4f, 0f);
            Ring("Schiffswrack", wreckCenter + wreckRotation * new Vector3(0f, -4f, 0f), wreckRotation.eulerAngles.y);
            Ring("Kranbrücke", craneBridge + new Vector3(0f, 9f, -5f), 90f);
            Ring("Leuchtturm-Galerie", lighthouseBase + Vector3.up * 27.8f + Vector3.forward * 6.8f, 90f);
            Ring("Glockenstuhl", churchTower, 0f);
            Ring("Schornstein", chimneyBase + Vector3.up * 40f, 0f);
            Ring("Rohbau-Dach", cityCenter + new Vector3(-5f, 14f * 3.8f + 2f, 0f), 0f);
            Ring("Eistunnel", iceTunnelCenter + iceTunnelRotation * new Vector3(0f, 5f, 0f), iceTunnelRotation.eulerAngles.y);
            Ring("Unter dem Pier", pierStart + pierDirection * 60f + Vector3.up * 2.2f, Quaternion.LookRotation(pierDirection).eulerAngles.y);
            Ring("Scheune Bergdorf", barnCenter, 20f);
            Ring("Förderband", tunnelCenter, 90f);
            Ring("Über den Wolken", new Vector3(IslandShape.Center.x, CloudBottom + CloudThickness + 60f, IslandShape.Center.y), 0f);
        }

        static void Checkpoint(string name, Vector3 ground)
        {
            var t = new GameObject(name).transform;
            t.SetParent(checkpoints, false);
            t.position = ground + Vector3.up * 3f;
            Vector3 toCenter = new Vector3(IslandShape.Center.x, 0f, IslandShape.Center.y) - new Vector3(ground.x, 0f, ground.z);
            t.rotation = Quaternion.LookRotation(toCenter.sqrMagnitude > 1f ? toCenter.normalized : Vector3.forward);
        }

        static void BuildCheckpoints()
        {
            Checkpoint("Strand", spawn);
            Checkpoint("Hafen", IslandShape.Harbor.Ground);
            Checkpoint("Industriegebiet", industry + new Vector3(-110f, 0f, -110f));
            Checkpoint("Stadt", cityCenter + new Vector3(0f, 0f, -170f));
            Checkpoint("Tal-Dorf", IslandShape.ValleyVillage.Ground);
            Checkpoint("Bergdorf", IslandShape.MountainVillage.Ground);
            Checkpoint("Gletscher", GlacierPoint(0.98f));
            Checkpoint("Wald", Ground(IslandShape.Forest.x + IslandShape.ForestRadius + 30f, IslandShape.Forest.y));
            Checkpoint("Funkmast", mastTop - Vector3.up * 84f + new Vector3(12f, 0f, 0f));
        }

        static void BuildAmbientSound()
        {
            var sound = new GameObject("Ambient-Töne").transform;
            sound.SetParent(world, false);
            GameSystemsBuilder.CreateAmbient(sound, "Brandung", beachCoast + new Vector3(0f, 2f, -10f), 450f, 0.9f);
            GameSystemsBuilder.CreateAmbient(sound, "Brandung", wreckCenter, 300f, 0.7f);
            GameSystemsBuilder.CreateAmbient(sound, "Hafen", IslandShape.Harbor.Ground + Vector3.up * 3f, 350f, 0.8f);
            foreach (float x in new[] { -54f, 0f, 54f })
            {
                GameSystemsBuilder.CreateAmbient(sound, "Fabrikhalle", industry + new Vector3(x, 6f, 0f), 70f, 0.8f);
            }
        }
    }
}
