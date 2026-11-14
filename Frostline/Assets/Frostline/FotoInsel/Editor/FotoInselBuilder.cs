using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Frostline.EditorTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Frostline.FotoInsel
{
    /// <summary>
    /// Menü „Frostline › 7 · Foto-Insel bauen“: eine neue Szene mit einer 8 × 8 km großen Insel für
    /// fotorealistische Bilder – Gebirge mit Schneekappen, Flüsse, Wälder, drei große Industriegebiete,
    /// Hafenstadt. Die Spiel-Insel (Frostline_Insel.unity) bleibt unberührt.
    /// </summary>
    public static class FotoInselBuilder
    {
        public const string GeneratedFolder = "Assets/Frostline/Generated/FotoInsel";
        public const string ScenePath = "Assets/Frostline/Scenes/Foto_Insel.unity";
        public const string VerlassenPath = "Assets/Frostline/Scenes/Verlassene_Insel.unity";
        // Eigene Ordner je Variante, sonst überschreibt die eine Insel Gelände, Bauten und Flüsse der anderen.
        static string TerrainFolder { get { return GeneratedFolder + (FotoTerrain.Verlassen ? "/Terrain_Verlassen" : "/Terrain"); } }
        static string VariantFolder { get { return FotoTerrain.Verlassen ? GeneratedFolder + "/Verlassen" : GeneratedFolder; } }

        [MenuItem("Frostline/9 · Verlassene Insel bauen (Berg über den Wolken)", priority = 9)]
        public static void BuildVerlassenMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build(true);
            EditorUtility.DisplayDialog("Frostline", "Die verlassene Insel ist fertig: " + VerlassenPath + "\n\n" + Report, "OK");
        }

        public static string Report = "";
        static FotoTerrain lastTerrain;

        [MenuItem("Frostline/7 · Foto-Insel bauen (8 × 8 km)", priority = 7)]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
            EditorUtility.DisplayDialog("Frostline", "Die Foto-Insel ist fertig: " + ScenePath + "\n\n" + Report, "OK");
        }

        [MenuItem("Frostline/8 · Foto-Insel: Fotos rendern", priority = 8)]
        public static void RenderMenu()
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Ausgabe/foto_insel"));
            EditorUtility.RevealInFinder(RenderAll(folder, 1920, 1080));
        }

        public static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void Progress(string step, float value)
        {
            EditorUtility.DisplayProgressBar("Foto-Insel", step, value);
        }

        public static string Build(bool verlassen = false)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Report = "";
            FotoTerrain.Verlassen = verlassen;
            FotoTerrain.HeightRange = verlassen ? 3100f : 1150f;
            try
            {
                EnsureFolder(GeneratedFolder);
                EnsureFolder(TerrainFolder);
                EnsureFolder(VariantFolder);
                EnsureFolder(FotoBaeume.Folder);
                EnsureFolder(FotoBauten.Folder);
                EnsureFolder("Assets/Frostline/Scenes");

                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Vorlagen.EnsureImported();

                var t = new FotoTerrain();
                t.Generate(Progress);
                lastTerrain = t;
                Report += t.Log;
                string map = Path.GetFullPath(Path.Combine(Application.dataPath, verlassen ? "../../Ausgabe/verlassene_insel/karte.png" : "../../Ausgabe/foto_insel/karte.png"));
                FotoVorschau.Write(t, map, 1024);

                var world = new GameObject(verlassen ? "Verlassene Insel" : "Foto-Insel").transform;

                Progress("Bäume (Fichte, Kiefer, Buche, Eiche, Birke)", 0.3f);
                List<FotoBaeume.Species> species = FotoBaeume.CreateAll();
                Progress("Gras, Farn, Heide", 0.33f);
                string[] detailNames = { "Grass_A", "Grass_B", "Grass_C", "GrassDry_A", "Heather_A", "Fern_A", "Bush_A" };
                var details = new GameObject[detailNames.Length];
                for (int i = 0; i < detailNames.Length; i++) details[i] = FotoBaeume.DetailPrefab(detailNames[i]);

                TerrainLayer[] layers = CreateLayers();
                CreateTerrains(t, world, layers, species, details);
                Report += "Terrain fertig nach " + watch.ElapsedMilliseconds / 1000 + " s\n";

                Progress("Himmel, Wolken, Ozean, Flüsse", 0.75f);
                FotoAtmosphaere.Create(world, t, VariantFolder);

                if (verlassen)
                {
                    Progress("Materialien der Pakete auf HDRP", 0.78f);
                    foreach (string folder in new[] { "Assets/Viking Village", "Assets/PalmTreePack", "Assets/TirgamesAssets" })
                        if (AssetDatabase.IsValidFolder(folder)) FotoMaterialKonverter.ConvertFolder(folder);
                    Progress("Verlassenes Werk, Brücken, Landmarken", 0.8f);
                    FotoBauten.BuildVerlassen(t, world);
                    Report += FotoBauten.Report;
                    Progress("Wikingerdorf, Rohbau, Traumstrand", 0.88f);
                    FotoVerlassen.Build(t, world);
                    Report += FotoVerlassen.Report;
                    Progress("Fotokameras", 0.95f);
                    CreateCamerasVerlassen(t, world);
                    Progress("Drohne und Spielsysteme", 0.97f);
                    Frostline.EditorTools.VerlasseneInselSpiel.AddGameplay(scene);
                    Report += FotoLichtstimmungen.Add(scene);
                    Report += FotoZusaetze.Add(scene);
                }
                else
                {
                    Progress("Industrie, Stadt, Brücken, Landmarken", 0.8f);
                    FotoBauten.Build(t, world);
                    Report += FotoBauten.Report;
                    Progress("Fotokameras", 0.95f);
                    CreateCameras(t, world);
                }

                AssetDatabase.SaveAssets();
                string scenePath = verlassen ? VerlassenPath : ScenePath;
                EditorSceneManager.SaveScene(scene, scenePath);
                Report += "Szene gespeichert nach " + watch.ElapsedMilliseconds / 1000 + " s: " + scenePath + "\n";
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                FotoTerrain.Verlassen = false;
                FotoTerrain.HeightRange = 1150f;
            }
            return Report;
        }

        // ------------------------------------------------------------ Bodenschichten

        /// <summary>
        /// Eigene Kopie einer Bodenschicht: Die Vorlagen werden auch von der Spiel-Insel benutzt,
        /// Kachelgröße und Farbton der Foto-Insel dürfen sie nicht verändern.
        /// </summary>
        static TerrainLayer CopyLayer(string sourcePath, string name, float tile, Color tint)
        {
            var source = AssetDatabase.LoadAssetAtPath<TerrainLayer>(sourcePath);
            if (source == null) return null;
            string path = TerrainFolder + "/TL_Foto_" + name + ".terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = source.diffuseTexture;
            layer.normalMapTexture = source.normalMapTexture;
            layer.maskMapTexture = source.maskMapTexture;
            layer.normalScale = source.normalScale;
            layer.metallic = source.metallic;
            layer.smoothness = source.smoothness;
            layer.specular = source.specular;
            layer.maskMapRemapMin = source.maskMapRemapMin;
            layer.maskMapRemapMax = source.maskMapRemapMax;
            layer.diffuseRemapMin = source.diffuseRemapMin;
            layer.diffuseRemapMax = new Vector4(tint.r, tint.g, tint.b, 1f);
            layer.tileSize = Vector2.one * (tile > 0f ? tile : source.tileSize.x);
            layer.tileOffset = source.tileOffset;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        static TerrainLayer[] CreateLayers()
        {
            const string pack = AssetStorePakete.TerrainPack + "/TerrainLayers/";
            string Poly(string id) { return Vorlagen.Root + "/Texturen/" + id + "/TL_" + id + ".terrainlayer"; }
            var list = new[]
            {
                CopyLayer(Poly("rocky_terrain_02"), "Wiese", 32f, new Color(0.9f, 0.97f, 0.86f)),            // Luftbild-Grasland
                CopyLayer(pack + "Heather_TerrainLayer.terrainlayer", "Waldboden", 6f, new Color(0.7f, 0.68f, 0.6f)),
                CopyLayer(Poly("aerial_rocks_02"), "Hochlage", 30f, new Color(0.88f, 0.9f, 0.88f)),        // bemooste Felsen
                CopyLayer(pack + "Rock_TerrainLayer.terrainlayer", "Fels", 13f, new Color(0.95f, 0.95f, 0.97f)),
                CopyLayer(Poly("coast_sand_01"), "Sand", 15f, new Color(1f, 0.97f, 0.9f)),
                CopyLayer(pack + "Pebbles_A_TerrainLayer.terrainlayer", "Kies", 5f, Color.white),
                CopyLayer(Poly("snow_field_aerial"), "Schnee", 55f, Color.white),
                CopyLayer(Poly("concrete_panels"), "Beton", 8f, new Color(0.62f, 0.62f, 0.6f)),
            };
            for (int i = 0; i < list.Length; i++)
                if (list[i] == null) Debug.LogWarning("Foto-Insel: Bodenschicht " + i + " fehlt.");
            return list;
        }

        static float[,,] Splat(FotoTerrain t, float originX, float originZ, int res)
        {
            var splat = new float[res, res, 8];
            float step = FotoTerrain.TileSize / res;
            bool verlassen = FotoTerrain.Verlassen;
            Parallel.For(0, res, row =>
            {
                var w = new float[8];
                for (int col = 0; col < res; col++)
                {
                    float x = originX + (col + 0.5f) * step, z = originZ + (row + 0.5f) * step;
                    float h = t.HeightAt(x, z);
                    float slope = t.SlopeAt(x, z);
                    float n = FotoNoise.Fbm(x * 0.021f + 5f, z * 0.021f - 2f, 3);
                    float n2 = FotoNoise.Fbm(x * 0.0032f - 9f, z * 0.0032f + 4f, 3);
                    float zone = t.MaskAt(t.ZoneMask, x, z) / 255f;
                    float road = t.MaskAt(t.RoadMask, x, z) / 255f;
                    byte water = t.MaskAt(t.WaterMask, x, z);
                    for (int k = 0; k < 8; k++) w[k] = 0f;
                    float remaining = 1f;
                    void Take(int layer, float amount)
                    {
                        float a = Mathf.Clamp01(amount) * remaining;
                        w[layer] += a;
                        remaining -= a;
                    }
                    if (zone > 0.99f && road < 0.5f)
                    {
                        // Werksgelände: Beton mit Schotter- und Erdflächen, wie echte Industrieareale.
                        float patch = FotoNoise.Fbm(x * 0.006f + 31f, z * 0.006f - 17f, 3) + n * 0.08f;
                        Take(7, FotoNoise.SmoothStep(-0.22f, 0.12f, patch));
                        Take(5, 0.65f);
                        Take(4, 1f);
                    }
                    else if (verlassen) { Take(5, road * 0.9f); Take(7, zone * 0.8f); }   // alte Schotterwege
                    else Take(7, Mathf.Max(zone * 0.8f, road));
                    if (verlassen && t.Beach != null && h < 9f && t.Beach.Contains(new Vector2(x, z), 60f))
                        Take(4, 1f - FotoNoise.SmoothStep(6.5f, 9f, h));   // Traumstrand
                    if (water == 255) Take(5, 1f);
                    if (h < 0f) { Take(4, 0.75f + n * 0.4f); Take(5, 1f); }
                    Take(3, FotoNoise.SmoothStep(35f, 47f, slope + n * 6f));
                    Take(6, (verlassen ? FotoNoise.SmoothStep(1850f, 2050f, h + n2 * 90f + n * 20f) : FotoNoise.SmoothStep(690f, 790f, h + n2 * 70f + n * 15f)) * (1f - FotoNoise.SmoothStep(42f, 55f, slope)));
                    Take(4, FotoNoise.SmoothStep(3.4f, 1.3f, h + n * 0.9f) * (1f - FotoNoise.SmoothStep(16f, 26f, slope)));
                    if (water > 0) Take(5, water / 200f * 0.85f);
                    Take(5, 0.4f * FotoNoise.SmoothStep(28f, 35f, slope) * FotoNoise.SmoothStep(150f, 320f, h));   // Geröll unter Felswänden
                    Take(2, (verlassen ? FotoNoise.SmoothStep(950f, 1350f, h + n2 * 120f) : FotoNoise.SmoothStep(380f, 560f, h + n2 * 90f)) * FotoNoise.SmoothStep(20f, 32f, slope + n2 * 10f));
                    float forest = t.ForestAt(x, z, h, slope);
                    float village = verlassen && t.Town != null ? (new Vector2(x, z) - t.Town.Center).magnitude : 9999f;
                    forest *= FotoNoise.SmoothStep(120f, 260f, village);   // Dorfwiese statt Waldboden
                    Take(1, forest * 0.92f + FotoNoise.SmoothStep(0.25f, 0.45f, n2) * 0.35f * (village < 220f ? 0.2f : 1f));
                    w[0] += remaining;
                    for (int k = 0; k < 8; k++) splat[row, col, k] = w[k];
                }
            });
            return splat;
        }

        // ------------------------------------------------------------ Bäume und Bewuchs

        static int PickSpecies(float h, float coastal, float stand, float r)
        {
            // 0–2 Fichte, 3–4 Kiefer, 5 Buche, 6 Eiche, 7 Birke
            if (coastal > 0.5f && r < 0.6f) return 3 + (r < 0.3f ? 0 : 1);
            if (h < 130f)
            {
                float beech = 0.3f + stand * 0.25f;
                if (r < beech) return 5;
                if (r < beech + 0.22f) return 6;
                if (r < beech + 0.36f) return 7;
                if (r < beech + 0.5f) return 3;
                return (int)(r * 997f) % 3;
            }
            if (h < 390f)
            {
                if (r < 0.48f + stand * 0.2f) return (int)(r * 997f) % 3;
                if (r < 0.72f) return 3 + ((int)(r * 331f) % 2);
                if (r < 0.9f) return 5;
                return 7;
            }
            if (r < 0.8f) return (int)(r * 997f) % 3;
            if (r < 0.95f) return 3;
            return 7;
        }

        static TreeInstance[] PlaceTrees(FotoTerrain t, float originX, float originZ, List<FotoBaeume.Species> species)
        {
            const float step = 7.2f;
            int n = Mathf.CeilToInt(FotoTerrain.TileSize / step);
            var rows = new List<TreeInstance>[n];
            Parallel.For(0, n, row =>
            {
                var list = new List<TreeInstance>();
                for (int col = 0; col < n; col++)
                {
                    int gx = Mathf.RoundToInt(originX / step) + col, gz = Mathf.RoundToInt(originZ / step) + row;
                    float jx = FotoNoise.Hash(gx, gz, 1), jz = FotoNoise.Hash(gx, gz, 2);
                    float x = originX + (col + jx) * step, z = originZ + (row + jz) * step;
                    float h = t.HeightAt(x, z);
                    if (h < 2.6f) continue;
                    var p = new Vector2(x, z);
                    bool inTown = t.Town != null && t.Town.Contains(p, 20f);
                    bool inIndustry = false;
                    foreach (FotoZone zone in t.Zones)
                        if (zone.Flatten && zone.Contains(p, 25f)) inIndustry = true;
                    bool verlassen = FotoTerrain.Verlassen;
                    if (inIndustry && (!verlassen || FotoNoise.Hash(gx, gz, 11) > 0.04f)) continue;   // verlassen: vereinzelt Bäume im Werk
                    if (verlassen && t.Beach != null && h < 16f && t.Beach.Contains(p, 40f)) continue;   // dort stehen Palmen
                    if (verlassen && t.Town != null && (p - t.Town.Center).magnitude < 230f && FotoNoise.Hash(gx, gz, 12) > 0.03f) continue;
                    if (t.MaskAt(t.RoadMask, x, z) > 0) continue;
                    byte water = t.MaskAt(t.WaterMask, x, z);
                    if (water > 150) continue;
                    float slope = t.SlopeAt(x, z);
                    if (slope > 44f) continue;
                    float forest = t.ForestAt(x, z, h, slope);
                    float riparian = water > 0 ? 0.35f : 0f;
                    // Unregelmäßiges Kronendach: Lücken, dichtere und lichtere Stellen.
                    float canopy = 0.62f + 0.38f * FotoNoise.SmoothStep(-0.35f, 0.35f, FotoNoise.Fbm(x * 0.018f + 4f, z * 0.018f - 9f, 3));
                    float density = forest * 0.95f * canopy + riparian + (inTown ? 0.06f : 0.012f);
                    if (inTown) density = Mathf.Min(density, 0.08f);
                    float chance = FotoNoise.Hash(gx, gz, 3);
                    if (chance > density) continue;
                    float stand = FotoNoise.Fbm(x * 0.006f + 13f, z * 0.006f - 7f, 2) * 0.5f + 0.5f;
                    float coastal = FotoNoise.SmoothStep(40f, 8f, h);
                    int index = riparian > 0f && FotoNoise.Hash(gx, gz, 4) < 0.6f
                        ? (FotoNoise.Hash(gx, gz, 6) < 0.5f ? 7 : 5)
                        : PickSpecies(h, coastal, stand, FotoNoise.Hash(gx, gz, 5));
                    if (inTown) index = 5 + (int)(FotoNoise.Hash(gx, gz, 7) * 3f) % 3;
                    FotoBaeume.Species s = species[index];
                    float size = Mathf.Lerp(s.MinHeight, s.MaxHeight, FotoNoise.Hash(gx, gz, 8));
                    size *= Mathf.Lerp(1f, 0.55f, verlassen ? FotoNoise.SmoothStep(850f, 1100f, h) : FotoNoise.SmoothStep(540f, 740f, h));
                    // Waldränder etwas niedriger, wie bei echtem Mantel und Saum.
                    size *= Mathf.Lerp(0.75f, 1f, FotoNoise.SmoothStep(0.2f, 0.7f, forest + riparian));
                    list.Add(new TreeInstance
                    {
                        position = new Vector3((x - originX) / FotoTerrain.TileSize, 0f, (z - originZ) / FotoTerrain.TileSize),
                        heightScale = size,
                        widthScale = size * Mathf.Lerp(0.88f, 1.12f, FotoNoise.Hash(gx, gz, 9)),
                        rotation = FotoNoise.Hash(gx, gz, 10) * Mathf.PI * 2f,
                        color = Color.white,
                        lightmapColor = Color.white,
                        prototypeIndex = index,
                    });
                }
                rows[row] = list;
            });
            var all = new List<TreeInstance>();
            foreach (var list in rows) all.AddRange(list);
            return all.ToArray();
        }

        static void ApplyDetails(TerrainData data, FotoTerrain t, float originX, float originZ, GameObject[] details)
        {
            var prototypes = new List<DetailPrototype>();
            var indices = new int[details.Length];
            for (int i = 0; i < details.Length; i++)
            {
                indices[i] = -1;
                if (details[i] == null) continue;
                indices[i] = prototypes.Count;
                bool big = details[i].name.Contains("Bush") || details[i].name.Contains("Fern") || details[i].name.Contains("Heather");
                prototypes.Add(new DetailPrototype
                {
                    prototype = details[i],
                    usePrototypeMesh = true,
                    useInstancing = true,
                    renderMode = DetailRenderMode.VertexLit,
                    minWidth = big ? 0.9f : 0.8f,
                    maxWidth = big ? 1.8f : 1.4f,
                    minHeight = big ? 0.9f : 0.8f,
                    maxHeight = big ? 1.9f : 1.5f,
                    noiseSpread = 0.4f,
                    healthyColor = Color.white,
                    dryColor = new Color(0.9f, 0.88f, 0.8f),
                    alignToGround = 0.6f,
                    positionJitter = 1f,
                });
            }
            if (prototypes.Count == 0) return;
            const int res = 512;
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            data.SetDetailResolution(res, 32);
            data.detailPrototypes = prototypes.ToArray();
            var maps = new int[prototypes.Count][,];
            for (int i = 0; i < maps.Length; i++) maps[i] = new int[res, res];
            float step = FotoTerrain.TileSize / res;
            Parallel.For(0, res, row =>
            {
                for (int col = 0; col < res; col++)
                {
                    float x = originX + (col + 0.5f) * step, z = originZ + (row + 0.5f) * step;
                    float h = t.HeightAt(x, z);
                    if (h < 2f || h > (FotoTerrain.Verlassen ? 1500f : 760f)) continue;
                    if (FotoTerrain.Verlassen && t.Beach != null && h < 9f && t.Beach.Contains(new Vector2(x, z), 40f)) continue;
                    if (t.MaskAt(t.ZoneMask, x, z) > 30 || t.MaskAt(t.RoadMask, x, z) > 0 || t.MaskAt(t.WaterMask, x, z) > 120) continue;
                    float slope = t.SlopeAt(x, z);
                    if (slope > 34f) continue;
                    float forest = t.ForestAt(x, z, h, slope);
                    float n = FotoNoise.Fbm(x * 0.05f, z * 0.05f, 2) * 0.5f + 0.5f;
                    float dry = FotoNoise.SmoothStep(0.4f, 0.75f, FotoNoise.Fbm(x * 0.004f + 3f, z * 0.004f, 2) * 0.5f + 0.5f + FotoNoise.SmoothStep(300f, 600f, h) * 0.4f);
                    float r = FotoNoise.Hash(col + (int)originX, row + (int)originZ, 11);
                    int mul = FotoTerrain.Verlassen ? 3 : 1;   // filmreif: dichte Wiesen
                    void Set(int detail, int count) { if (indices[detail] >= 0 && count > 0) maps[indices[detail]][row, col] = count * mul; }
                    if (forest > 0.5f)
                    {
                        Set(5, r < 0.6f ? 2 : 1);
                        if (r > 0.8f) Set(6, 1);
                        if (r < 0.3f) Set(1, 2);
                        continue;
                    }
                    if (h > 420f || dry > 0.6f)
                    {
                        Set(4, n > 0.45f ? 3 : 1);
                        Set(3, 3);
                        continue;
                    }
                    Set(1, n > 0.5f ? 4 : 2);
                    Set(2, n > 0.35f ? 3 : 1);
                    Set(0, 3);
                    if (dry > 0.3f) Set(3, 2);
                    if (forest > 0.15f && r < 0.15f) Set(6, 1);
                }
            });
            for (int i = 0; i < maps.Length; i++) data.SetDetailLayer(0, 0, i, maps[i]);
        }

        static void CreateTerrains(FotoTerrain t, Transform world, TerrainLayer[] layers, List<FotoBaeume.Species> species, GameObject[] details)
        {
            var parent = new GameObject("Terrain (4 × 4 Kacheln à 2 km)").transform;
            parent.SetParent(world, false);
            var prototypes = new TreePrototype[species.Count];
            for (int i = 0; i < species.Count; i++) prototypes[i] = new TreePrototype { prefab = species[i].Prefab };
            var terrains = new Terrain[FotoTerrain.Tiles, FotoTerrain.Tiles];
            int treeCount = 0;
            for (int tz = 0; tz < FotoTerrain.Tiles; tz++)
            {
                for (int tx = 0; tx < FotoTerrain.Tiles; tx++)
                {
                    int index = tz * FotoTerrain.Tiles + tx;
                    Progress("Terrain-Kachel " + (index + 1) + " von 16", 0.35f + index * 0.025f);
                    float originX = tx * FotoTerrain.TileSize, originZ = tz * FotoTerrain.TileSize;
                    string path = TerrainFolder + "/Kachel_" + tx + "_" + tz + ".asset";
                    if (AssetDatabase.LoadAssetAtPath<TerrainData>(path) != null) AssetDatabase.DeleteAsset(path);
                    var data = new TerrainData();
                    AssetDatabase.CreateAsset(data, path);
                    data.heightmapResolution = FotoTerrain.TileRes;
                    data.size = new Vector3(FotoTerrain.TileSize, FotoTerrain.HeightRange, FotoTerrain.TileSize);

                    var heights = new float[FotoTerrain.TileRes, FotoTerrain.TileRes];
                    int baseI = tx * (FotoTerrain.TileRes - 1), baseJ = tz * (FotoTerrain.TileRes - 1);
                    for (int j = 0; j < FotoTerrain.TileRes; j++)
                        for (int i = 0; i < FotoTerrain.TileRes; i++)
                            heights[j, i] = (t.H[(baseJ + j) * FotoTerrain.Res + baseI + i] - FotoTerrain.Bottom) / FotoTerrain.HeightRange;
                    data.SetHeights(0, 0, heights);

                    data.terrainLayers = layers;
                    data.alphamapResolution = 1024;
                    data.SetAlphamaps(0, 0, Splat(t, originX, originZ, 1024));
                    ApplyDetails(data, t, originX, originZ, details);

                    data.treePrototypes = prototypes;
                    TreeInstance[] trees = PlaceTrees(t, originX, originZ, species);
                    data.SetTreeInstances(trees, true);
                    treeCount += trees.Length;
                    EditorUtility.SetDirty(data);

                    GameObject go = Terrain.CreateTerrainGameObject(data);
                    go.name = "Kachel " + tx + "-" + tz;
                    go.transform.SetParent(parent, false);
                    go.transform.position = new Vector3(originX, FotoTerrain.Bottom, originZ);
                    Terrain terrain = go.GetComponent<Terrain>();
                    terrain.heightmapPixelError = 2.5f;
                    terrain.basemapDistance = 3000f;
                    terrain.drawInstanced = true;
                    terrain.treeDistance = 5000f;
                    terrain.treeBillboardDistance = 5000f;
                    terrain.treeMaximumFullLODCount = 100000;
                    terrain.detailObjectDistance = FotoTerrain.Verlassen ? 250f : 160f;
                    terrain.detailObjectDensity = 1f;
                    terrain.shadowCastingMode = ShadowCastingMode.On;
                    terrain.allowAutoConnect = true;
                    terrain.groupingID = 7;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                    terrains[tx, tz] = terrain;
                }
            }
            for (int tz = 0; tz < FotoTerrain.Tiles; tz++)
                for (int tx = 0; tx < FotoTerrain.Tiles; tx++)
                    terrains[tx, tz].SetNeighbors(
                        tx > 0 ? terrains[tx - 1, tz] : null,
                        tz < FotoTerrain.Tiles - 1 ? terrains[tx, tz + 1] : null,
                        tx < FotoTerrain.Tiles - 1 ? terrains[tx + 1, tz] : null,
                        tz > 0 ? terrains[tx, tz - 1] : null);
            Report += "Bäume: " + treeCount + "\n";
        }

        // ------------------------------------------------------------ Kameras und Fotos

        static void Viewpoint(Transform parent, string name, Vector3 position, Vector3 target)
        {
            var go = new GameObject("Blick: " + name);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation(target - position);
        }

        static Vector3 At(FotoTerrain t, Vector2 p, float above)
        {
            return new Vector3(p.x, Mathf.Max(0f, t.HeightAt(p)) + above, p.y);
        }

        static void CreateCameras(FotoTerrain t, Transform world)
        {
            var cameraGo = new GameObject("Fotokamera");
            cameraGo.transform.SetParent(world, false);
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.fieldOfView = 42f;
            camera.nearClipPlane = 0.5f;
            camera.farClipPlane = 30000f;
            var hd = cameraGo.AddComponent<HDAdditionalCameraData>();
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
            hd.TAAQuality = HDAdditionalCameraData.TAAQualityLevel.High;

            var views = new GameObject("Fotostandpunkte").transform;
            views.SetParent(world, false);
            Vector2 center = FotoTerrain.Center;

            Viewpoint(views, "Totale über die Raffinerie", At(t, t.Refinery.Center + new Vector2(-350f, -1500f), 720f), At(t, center + new Vector2(0f, 900f), 120f));
            Vector2 portOut = (t.Port.Center - center).normalized;
            Viewpoint(views, "Containerhafen", At(t, t.Port.Center + portOut * 950f + new Vector2(-portOut.y, portOut.x) * 450f, 230f), At(t, t.Port.Center - portOut * 150f, 0f));
            Viewpoint(views, "Stahlwerk und Kühltürme", At(t, t.Steelworks.Center + new Vector2(-150f, -1250f), 380f), At(t, t.Steelworks.Center + new Vector2(120f, 120f), 20f));
            FotoRiver longest = null;
            foreach (FotoRiver r in t.Rivers) if (longest == null || r.Points.Count > longest.Points.Count) longest = r;
            if (longest != null)
            {
                Vector2 a = longest.Points[(int)(longest.Points.Count * 0.55f)], b = longest.Points[(int)(longest.Points.Count * 0.3f)];
                Vector2 back = (a - b).normalized;
                Viewpoint(views, "Flusstal und Wald", At(t, a + back * 260f, 95f), At(t, b, 40f));
            }
            Viewpoint(views, "Gebirge und Insel von Norden", At(t, center + new Vector2(300f, 4300f), 950f), At(t, center + new Vector2(0f, -600f), 50f));
            Viewpoint(views, "Hafenstadt", At(t, t.Town.Center + new Vector2(-500f, -650f), 210f), At(t, t.Town.Center, 0f));

            Transform first = views.GetChild(0);
            cameraGo.transform.SetPositionAndRotation(first.position, first.rotation);
        }

        /// <summary>Fotostandpunkte der verlassenen Insel: Gipfel über dem Wolkenmeer, Dorf, Werk, Rohbau, Strand.</summary>
        static void CreateCamerasVerlassen(FotoTerrain t, Transform world)
        {
            var cameraGo = new GameObject("Fotokamera");
            cameraGo.transform.SetParent(world, false);
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.AddComponent<Camera>();
            camera.fieldOfView = 40f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 40000f;
            var hd = cameraGo.AddComponent<HDAdditionalCameraData>();
            hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;

            var views = new GameObject("Fotostandpunkte").transform;
            views.SetParent(world, false);
            Vector2 peak = t.MastPoint;
            float peakH = t.HeightAt(peak);
            Vector3 top = new Vector3(peak.x, peakH, peak.y);

            Vector2 d1 = new Vector2(-0.38f, -0.92f).normalized;
            Viewpoint(views, "Gipfel über dem Wolkenmeer", new Vector3(peak.x + d1.x * 5600f, peakH + 120f, peak.y + d1.y * 5600f), top - Vector3.up * 380f);
            Vector2 d2 = new Vector2(0.82f, -0.57f).normalized;
            Viewpoint(views, "Wolkenmeer im Gegenlicht", new Vector3(peak.x + d2.x * 4200f, 2150f, peak.y + d2.y * 4200f), top - Vector3.up * 500f);
            Vector2 d3 = new Vector2(-0.95f, 0.3f).normalized;
            Viewpoint(views, "Nordwand im Abendlicht", new Vector3(peak.x + d3.x * 3200f, peakH - 250f, peak.y + d3.y * 3200f), top - Vector3.up * 350f);

            Vector2 town = t.Town.Center;
            Vector2 outward = (town - FotoTerrain.Center).normalized;
            Viewpoint(views, "Verlassenes Wikingerdorf", At(t, town + outward * 210f + new Vector2(-outward.y, outward.x) * 70f, 38f), At(t, town - outward * 60f, 10f));
            Viewpoint(views, "Dorfgasse", At(t, town + new Vector2(-outward.y, outward.x) * 70f, 2.2f), At(t, town, 4f));

            Vector2 rohbau = t.Rohbau.Center;
            Vector2 steel = t.Steelworks.Center;
            Vector2 side = (rohbau - steel).normalized;
            Viewpoint(views, "Verlassenes Stahlwerk und Rohbau", At(t, rohbau + side * 260f + new Vector2(-side.y, side.x) * 160f, 95f), At(t, Vector2.Lerp(rohbau, steel, 0.45f), 10f));
            Viewpoint(views, "Rohbau", At(t, t.Rohbau.ToWorld(new Vector2(-38f, -34f)), 7f), At(t, rohbau, 8f));

            if (t.MainRiver >= 0)
            {
                // Filmeinstellungen am Hauptfluss: knapp über dem Wasser flussaufwärts zum Berg, und das Dorf am Fluss.
                FotoRiver river = t.Rivers[t.MainRiver];
                int k = (int)(river.Points.Count * 0.72f);
                Vector2 p = river.Points[k], up = river.Points[Mathf.Max(0, k - 60)];
                Vector3 eye = new Vector3(p.x, river.Surface[k] + 1.4f, p.y);
                Viewpoint(views, "Hauptfluss flussaufwärts", eye, new Vector3(up.x, river.Surface[k] + 18f, up.y) + (new Vector3(peak.x, 900f, peak.y) - eye).normalized * 40f);
                int m = (int)(river.Points.Count * 0.9f);
                Vector2 q = river.Points[m], qd = (river.Points[Mathf.Min(river.Points.Count - 1, m + 8)] - river.Points[m - 8]).normalized;
                Vector2 bank = q + new Vector2(-qd.y, qd.x) * (river.HalfWidth[m] + 30f);
                Viewpoint(views, "Dorf am Fluss", At(t, bank, 22f), At(t, Vector2.Lerp(q, town, 0.6f), 4f));
            }

            FotoZone b = t.Beach;
            Vector3 beachA = At(t, b.ToWorld(new Vector2(-b.Half.x * 0.7f, b.Half.y * 0.05f)), 1.8f);
            Vector3 beachB = At(t, b.ToWorld(new Vector2(b.Half.x * 0.6f, -b.Half.y * 0.55f)), 3f);
            Viewpoint(views, "Traumstrand", beachA, beachB);
            Vector3 sea = At(t, b.ToWorld(new Vector2(0f, -b.Half.y - 60f)), 5f);
            sea.y = 5f;
            Viewpoint(views, "Vom Meer zum Berg in den Wolken", sea, new Vector3(peak.x, 1350f, peak.y));

            Transform first = views.GetChild(0);
            cameraGo.transform.SetPositionAndRotation(first.position, first.rotation);
        }

        /// <summary>Rendert alle Fotostandpunkte als PNG (mehrere Bilder pro Standpunkt, damit TAA, Wolken und Nebel einschwingen).</summary>
        public static string RenderAll(string folder, int width, int height, int frames = 14)
        {
            Directory.CreateDirectory(folder);
            GameObject cameraGo = GameObject.Find("Fotokamera");
            GameObject views = GameObject.Find("Fotostandpunkte");
            if (cameraGo == null || views == null) return "Fotokamera oder Fotostandpunkte fehlen – zuerst die Foto-Insel bauen.";
            Camera camera = cameraGo.GetComponent<Camera>();
            int quality = QualitySettings.GetQualityLevel();
            int best = QualitySettings.names.Length - 1;
            QualitySettings.SetQualityLevel(best, true);
            var log = "";
            try
            {
                for (int v = 0; v < views.transform.childCount; v++)
                {
                    Transform view = views.transform.GetChild(v);
                    camera.transform.SetPositionAndRotation(view.position, view.rotation);
                    string name = view.name.Replace("Blick: ", "").Replace(' ', '_');
                    string file = Path.Combine(folder, (v + 1).ToString("00") + "_" + name + ".png");
                    RenderCamera(camera, width, height, frames, file, 2);
                    log += file + "\n";
                }
                Transform firstView = views.transform.GetChild(0);
                camera.transform.SetPositionAndRotation(firstView.position, firstView.rotation);
            }
            finally
            {
                QualitySettings.SetQualityLevel(quality, true);
            }
            return log;
        }

        /// <summary>
        /// Rendert die Kamera mit SMAA in <paramref name="supersample"/>-facher Auflösung und rechnet
        /// herunter. TAA verschmiert bei einzeln gerenderten Bildern, deshalb hier nicht.
        /// </summary>
        public static void RenderCamera(Camera camera, int width, int height, int frames, string file, int supersample = 1)
        {
            int w = width * supersample, h = height * supersample;
            var hd = camera.GetComponent<HDAdditionalCameraData>();
            var previousAa = hd != null ? hd.antialiasing : HDAdditionalCameraData.AntialiasingMode.None;
            if (hd != null)
            {
                hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                hd.SMAAQuality = HDAdditionalCameraData.SMAAQualityLevel.High;
            }
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
            RenderTexture previous = camera.targetTexture;
            camera.targetTexture = rt;
            try
            {
                for (int i = 0; i < frames; i++) camera.Render();
                RenderTexture.active = rt;
                var big = new Texture2D(w, h, TextureFormat.RGB24, false);
                big.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                big.Apply();
                Texture2D image = big;
                if (supersample > 1)
                {
                    Color32[] src = big.GetPixels32();
                    var dst = new Color32[width * height];
                    int n = supersample * supersample;
                    Parallel.For(0, height, y =>
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int r = 0, g = 0, b = 0;
                            for (int sy = 0; sy < supersample; sy++)
                            {
                                int row = (y * supersample + sy) * w + x * supersample;
                                for (int sx = 0; sx < supersample; sx++)
                                {
                                    Color32 c = src[row + sx];
                                    r += c.r; g += c.g; b += c.b;
                                }
                            }
                            dst[y * width + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
                        }
                    });
                    image = new Texture2D(width, height, TextureFormat.RGB24, false);
                    image.SetPixels32(dst);
                    image.Apply();
                    Object.DestroyImmediate(big);
                }
                File.WriteAllBytes(file, image.EncodeToPNG());
                Object.DestroyImmediate(image);
            }
            finally
            {
                camera.targetTexture = previous;
                RenderTexture.active = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                if (hd != null) hd.antialiasing = previousAa;
            }
        }
    }
}
