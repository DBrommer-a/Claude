using System.IO;
using Frostline.Audio;
using Frostline.Controls;
using Frostline.Diagnostics;
using Frostline.Flight;
using Frostline.UI;
using Frostline.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Frostline.EditorTools
{
    /// <summary>
    /// Menü „Frostline > 2 · Prototyp-Szene erstellen“: baut die Szene für Woche 1 und 2 –
    /// Ozean, Volumenwolken, ein Terrain-Tile, Drohne mit FPV-Kamera – komplett per Skript.
    /// Erneut ausführen überschreibt die Szene und die erzeugten Assets.
    /// </summary>
    public static class PrototypeSceneBuilder
    {
        const string ScenePath = "Assets/Frostline/Scenes/Prototype.unity";
        const string GeneratedFolder = "Assets/Frostline/Generated";
        const string TextureFolder = GeneratedFolder + "/Textures";

        // Wolkenschicht so tief, dass die Gipfel hineinragen und man hineinfliegen kann.
        const float CloudBottom = 650f;
        const float CloudThickness = 1000f;

        [MenuItem("Frostline/2 · Prototyp-Szene erstellen", priority = 2)]
        public static void Build()
        {
            if (!HdrpAccess.IsInstalled)
            {
                EditorUtility.DisplayDialog("Frostline", "HDRP ist noch nicht geladen. Bitte zuerst „Frostline > 1 · Projekt einrichten“ ausführen.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            try
            {
                FrostlineProjectSetup.EnsureFolder("Assets/Frostline/Scenes");
                FrostlineProjectSetup.EnsureFolder(TextureFolder);

                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

                Progress("Himmel, Wolken, Wetter, Ozean", 0.05f);
                EnvironmentRefs env = GameSystemsBuilder.CreateEnvironment(CloudBottom, CloudThickness);

                CreateTerrain();

                Progress("Drohne", 0.9f);
                Vector3 spawn = IslandGenerator.FindBeachSpawn(IslandGenerator.TileSize * 0.5f);
                Transform spawnPoint = CreateLaunchPad(spawn);
                CreatePracticeGates(spawn);
                DroneController drone = GameSystemsBuilder.CreateDrone(spawnPoint);

                var options = new SystemsOptions
                {
                    IslandCenter = new Vector2(IslandGenerator.TileSize * 0.5f, IslandGenerator.TileSize * 0.5f),
                    SignalCleanRadius = 1300f,
                    SignalLostRadius = 1800f,
                    BenchmarkCenter = new Vector3(IslandGenerator.TileSize * 0.5f, 0f, IslandGenerator.TileSize * 0.5f),
                    BenchmarkRadius = 750f,
                    CloudBottom = CloudBottom,
                    CloudThickness = CloudThickness,
                };
                GameSystemsBuilder.CreateSystems(drone, env, options);
                GameSystemsBuilder.CreateAmbient(null, "Brandung", spawn + new Vector3(0f, 0f, -30f), 250f, 0.8f);

                EditorSceneManager.SaveScene(scene, ScenePath);
                GameSystemsBuilder.AddSceneToBuild(ScenePath, false);
                Selection.activeGameObject = drone.gameObject;
                SceneView.lastActiveSceneView?.FrameSelected();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            EditorUtility.DisplayDialog("Frostline",
                "Die Prototyp-Szene ist fertig und gespeichert.\n\n"
                + "Play drücken. Sender per USB anschließen (EdgeTX: „USB Joystick (HID)“), dann F1 → „Sender kalibrieren“.\n\n"
                + "Ohne Sender: W/S Gas, A/D Gier, Pfeiltasten Nick/Roll, Leertaste schärfen, M Modus, R Reset.\n\n"
                + "F9 startet die fps-Messung. Für das Gate zählt nur die Messung im Build (Frostline > 3 · Mac-Build erstellen).",
                "OK");
        }

        static void Progress(string step, float value)
        {
            EditorUtility.DisplayProgressBar("Frostline – Prototyp-Szene", step, value);
        }

        // ------------------------------------------------------------ Terrain

        static Terrain CreateTerrain()
        {
            const int HeightResolution = 2049; // laut Plan
            const int SplatResolution = 1024;

            string dataPath = GeneratedFolder + "/IslandTile.asset";
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath) != null) AssetDatabase.DeleteAsset(dataPath);

            var data = new TerrainData();
            data.heightmapResolution = HeightResolution;
            data.size = new Vector3(IslandGenerator.TileSize, IslandGenerator.HeightRange, IslandGenerator.TileSize);

            float[,] heights = IslandGenerator.BuildHeightmap(HeightResolution, p => Progress("Terrain: Höhen", 0.15f + p * 0.45f));
            data.SetHeights(0, 0, heights);

            Progress("Terrain: Bodenschichten", 0.65f);
            data.terrainLayers = new[]
            {
                CreateLayer("Sand", new Color(0.62f, 0.57f, 0.48f), new Color(0.48f, 0.44f, 0.37f), 6f, 0.25f, 0.6f),
                CreateLayer("Geroell", new Color(0.36f, 0.36f, 0.36f), new Color(0.22f, 0.22f, 0.23f), 9f, 0.15f, 1.4f),
                CreateLayer("Fels", new Color(0.30f, 0.30f, 0.32f), new Color(0.16f, 0.16f, 0.18f), 18f, 0.2f, 2.2f),
                CreateLayer("Schnee", new Color(0.86f, 0.88f, 0.92f), new Color(0.74f, 0.78f, 0.86f), 12f, 0.45f, 0.35f),
            };
            data.alphamapResolution = SplatResolution;
            data.SetAlphamaps(0, 0, IslandGenerator.BuildSplatmap(heights, SplatResolution));

            Progress("Terrain: speichern", 0.85f);
            AssetDatabase.CreateAsset(data, dataPath);

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Insel (Terrain-Tile 2 × 2 km)";
            go.transform.position = new Vector3(0f, IslandGenerator.SeabedDepth, 0f);

            var terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 8f;   // höherer Pixel Error: weniger Dreiecke in der Ferne
            terrain.drawInstanced = true;
            terrain.basemapDistance = 900f;
            terrain.shadowCastingMode = ShadowCastingMode.On;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return terrain;
        }

        internal static TerrainLayer CreateLayer(string name, Color light, Color dark, float tileSize, float smoothness, float roughness)
        {
            string texturePath = TextureFolder + "/" + name + ".png";
            string layerPath = GeneratedFolder + "/Layer_" + name + ".terrainlayer";

            File.WriteAllBytes(texturePath, NoiseTexture(light, dark, roughness, name.GetHashCode()).EncodeToPNG());
            AssetDatabase.ImportAsset(texturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.anisoLevel = 4;
            importer.SaveAndReimport();

            if (AssetDatabase.LoadAssetAtPath<TerrainLayer>(layerPath) != null) AssetDatabase.DeleteAsset(layerPath);
            var layer = new TerrainLayer
            {
                diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath),
                tileSize = new Vector2(tileSize, tileSize),
                smoothness = smoothness,
                metallic = 0f,
            };
            AssetDatabase.CreateAsset(layer, layerPath);
            return layer;
        }

        /// <summary>Einfache, kachelbare Rausch-Textur als Platzhalter bis zu den Megascans.</summary>
        static Texture2D NoiseTexture(Color light, Color dark, float contrast, int seed)
        {
            const int Size = 512;
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true);
            var random = new System.Random(seed);
            float ox = (float)random.NextDouble() * 100f;
            float oy = (float)random.NextDouble() * 100f;
            var pixels = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float n = 0f;
                    float amplitude = 0.5f;
                    float frequency = 4f;
                    for (int o = 0; o < 5; o++)
                    {
                        n += TileableNoise(x, y, Size, frequency, ox + o * 13f, oy - o * 7f) * amplitude;
                        amplitude *= 0.5f;
                        frequency *= 2f;
                    }
                    float t = Mathf.Clamp01(0.5f + (n - 0.48f) * contrast);
                    pixels[y * Size + x] = Color.Lerp(dark, light, t);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        static float TileableNoise(int x, int y, int size, float frequency, float ox, float oy)
        {
            // Auf einem Torus abgetastet, damit die Textur nahtlos kachelt.
            float u = (float)x / size * Mathf.PI * 2f;
            float v = (float)y / size * Mathf.PI * 2f;
            float r = frequency / (Mathf.PI * 2f);
            float a = Mathf.PerlinNoise(ox + Mathf.Cos(u) * r, oy + Mathf.Sin(u) * r);
            float b = Mathf.PerlinNoise(ox + 50f + Mathf.Cos(v) * r, oy + 50f + Mathf.Sin(v) * r);
            return (a + b) * 0.5f;
        }

        // ------------------------------------------------------------ Startplatz und Gates

        static Transform CreateLaunchPad(Vector3 ground)
        {
            Material padMaterial = GameSystemsBuilder.LitMaterial("Startplatz", new Color(0.18f, 0.18f, 0.2f), 0.3f);
            GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "Startplatz";
            pad.transform.position = ground + new Vector3(0f, 0.25f, 0f);
            pad.transform.localScale = new Vector3(2f, 0.5f, 2f);
            pad.GetComponent<Renderer>().sharedMaterial = padMaterial;

            var spawn = new GameObject("Startpunkt").transform;
            spawn.position = ground + new Vector3(0f, 0.56f, 0f);
            // Blick nach Norden auf die Insel.
            spawn.rotation = Quaternion.identity;
            return spawn;
        }

        static void CreatePracticeGates(Vector3 start)
        {
            Material gateMaterial = GameSystemsBuilder.LitMaterial("Gate", new Color(1f, 0.38f, 0.05f), 0.4f);
            var parent = new GameObject("Uebungs-Gates").transform;
            Vector3[] offsets =
            {
                new Vector3(0f, 0f, 30f),
                new Vector3(18f, 0f, 70f),
                new Vector3(-12f, 0f, 115f),
                new Vector3(5f, 0f, 165f),
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                Vector3 position = start + offsets[i];
                position.y = Mathf.Max(IslandGenerator.Height(position.x, position.z), 0.5f);
                Vector3 next = i + 1 < offsets.Length ? start + offsets[i + 1] : position + Vector3.forward;
                Vector3 direction = next - position;
                direction.y = 0f;
                Quaternion rotation = Quaternion.LookRotation(direction.sqrMagnitude > 0.01f ? direction : Vector3.forward);
                CreateGate(parent, "Gate " + (i + 1), position, rotation, gateMaterial);
            }
        }

        static void CreateGate(Transform parent, string name, Vector3 groundPosition, Quaternion rotation, Material material)
        {
            const float Width = 3f;
            const float Height = 2f;
            const float Bar = 0.15f;
            const float Clearance = 0.3f;

            var gate = new GameObject(name).transform;
            gate.SetParent(parent);
            gate.SetPositionAndRotation(groundPosition, rotation);

            Bar_(gate, new Vector3(-Width * 0.5f, Clearance + Height * 0.5f, 0f), new Vector3(Bar, Height + Clearance * 2f, Bar), material);
            Bar_(gate, new Vector3(Width * 0.5f, Clearance + Height * 0.5f, 0f), new Vector3(Bar, Height + Clearance * 2f, Bar), material);
            Bar_(gate, new Vector3(0f, Clearance * 2f + Height, 0f), new Vector3(Width + Bar, Bar, Bar), material);
        }

        static void Bar_(Transform parent, Vector3 localPosition, Vector3 scale, Material material)
        {
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.transform.SetParent(parent, false);
            bar.transform.localPosition = localPosition;
            bar.transform.localScale = scale;
            bar.GetComponent<Renderer>().sharedMaterial = material;
        }


    }
}
