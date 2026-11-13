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

                Progress("Himmel, Wolken und Nebel", 0.05f);
                CreateEnvironmentVolume();
                CreateSun();

                Progress("Ozean", 0.1f);
                CreateOcean();

                Terrain terrain = CreateTerrain();

                Progress("Drohne", 0.9f);
                Vector3 spawn = IslandGenerator.FindBeachSpawn(IslandGenerator.TileSize * 0.5f);
                Transform spawnPoint = CreateLaunchPad(spawn);
                CreatePracticeGates(spawn);
                DroneController drone = CreateDrone(spawnPoint);
                CreateSystems(drone, terrain);

                EditorSceneManager.SaveScene(scene, ScenePath);
                AddSceneToBuild(ScenePath);
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

        // ------------------------------------------------------------ Umgebung

        static void CreateEnvironmentVolume()
        {
            Object profile = LoadOrCreateProfile(GeneratedFolder + "/PrototypeEnvironment.asset");

            Object visual = HdrpAccess.AddVolumeComponent(profile, "VisualEnvironment");
            HdrpAccess.SetVolume(visual, "skyType", 4); // Physically Based Sky
            HdrpAccess.SetVolume(visual, "cloudType", 0);
            // Muss World Space bleiben, sonst kann man nicht in die Wolken hineinfliegen.
            HdrpAccess.SetVolume(visual, "renderingSpace", "World");

            HdrpAccess.AddVolumeComponent(profile, "PhysicallyBasedSky");

            Object clouds = HdrpAccess.AddVolumeComponent(profile, "VolumetricClouds");
            if (clouds != null)
            {
                HdrpAccess.SetVolume(clouds, "enable", true);
                HdrpAccess.SetVolume(clouds, "cloudControl", "Simple");
                HdrpAccess.SetVolume(clouds, "cloudSimpleMode", "Performance");
                // Preset „Cloudy“ setzt Dichte und Form; danach alle Werte als Override festhalten
                // und nur die Höhe der Schicht ändern.
                HdrpAccess.SetMember(clouds, "cloudPreset", "Cloudy");
                SetAllOverrides(clouds);
                HdrpAccess.SetMember(clouds, "cloudPreset", "Custom");
                HdrpAccess.SetVolume(clouds, "bottomAltitude", CloudBottom);
                HdrpAccess.SetVolume(clouds, "altitudeRange", CloudThickness);
                // Laut Plan der größte Kostentreiber: wenige Schritte.
                HdrpAccess.SetVolume(clouds, "numPrimarySteps", 48);
                HdrpAccess.SetVolume(clouds, "numLightSteps", 6);
                HdrpAccess.SetVolume(clouds, "shadows", true);
            }

            Object fog = HdrpAccess.AddVolumeComponent(profile, "Fog");
            HdrpAccess.SetVolume(fog, "enabled", true);
            HdrpAccess.SetVolume(fog, "meanFreePath", 1800f);
            HdrpAccess.SetVolume(fog, "baseHeight", 0f);
            HdrpAccess.SetVolume(fog, "maximumHeight", 350f);
            HdrpAccess.SetVolume(fog, "enableVolumetricFog", true);
            HdrpAccess.SetVolume(fog, "depthExtent", 300f);
            HdrpAccess.SetVolume(fog, "anisotropy", 0.6f);

            Object exposure = HdrpAccess.AddVolumeComponent(profile, "Exposure");
            HdrpAccess.SetVolume(exposure, "mode", "Automatic");
            HdrpAccess.SetVolume(exposure, "limitMin", 4f);
            HdrpAccess.SetVolume(exposure, "limitMax", 16f);

            Object shadows = HdrpAccess.AddVolumeComponent(profile, "HDShadowSettings");
            HdrpAccess.SetVolume(shadows, "maxShadowDistance", 600f);
            HdrpAccess.SetVolume(shadows, "cascadeShadowSplitCount", 3);

            Object water = HdrpAccess.AddVolumeComponent(profile, "WaterRendering");
            HdrpAccess.SetVolume(water, "enable", true);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("Umgebung (Himmel, Wolken, Nebel)");
            Component volume = go.AddComponent(HdrpAccess.CoreType("Volume"));
            HdrpAccess.SetMember(volume, "isGlobal", true);
            HdrpAccess.SetMember(volume, "sharedProfile", profile);
        }

        static Object LoadOrCreateProfile(string path)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            ScriptableObject profile = ScriptableObject.CreateInstance(HdrpAccess.CoreType("VolumeProfile"));
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        static void SetAllOverrides(Object component)
        {
            if (component == null) return;
            System.Reflection.MethodInfo method = component.GetType().GetMethod("SetAllOverridesTo", new[] { typeof(bool) });
            if (method != null) method.Invoke(component, new object[] { true });
        }

        static void CreateSun()
        {
            var go = new GameObject("Sonne");
            // Vormittagssonne aus Südosten, 35° über dem Horizont.
            go.transform.rotation = Quaternion.Euler(35f, -40f, 0f);

            Light light = null;
            System.Type extension = HdrpAccess.HdType("GameObjectExtension");
            if (extension != null)
            {
                HdrpAccess.InvokeStatic(extension, "AddHDLight", go, LightType.Directional);
                light = go.GetComponent<Light>();
            }
            if (light == null)
            {
                light = go.AddComponent<Light>();
                light.type = LightType.Directional;
            }

            light.lightUnit = LightUnit.Lux;
            light.intensity = 100000f;
            light.useColorTemperature = true;
            light.colorTemperature = 5600f;
            light.shadows = LightShadows.Soft;
        }

        static void CreateOcean()
        {
            System.Type surfaceType = HdrpAccess.HdType("WaterSurface");
            if (surfaceType == null)
            {
                Debug.LogWarning("Frostline: HDRP-Wasser nicht gefunden, Ozean fehlt.");
                return;
            }

            var go = new GameObject("Ozean");
            go.transform.position = Vector3.zero;
            Component surface = go.AddComponent(surfaceType);
            if (!HdrpAccess.InvokeStatic(HdrpAccess.HdType("WaterSurfacePresets"), "ApplyWaterOceanPreset", surface))
            {
                HdrpAccess.SetMember(surface, "surfaceType", "OceanSeaLake");
                HdrpAccess.SetMember(surface, "geometryType", "Infinite");
            }
            // Kühles, eher dunkles Nordmeer mit etwas Wind.
            HdrpAccess.SetMember(surface, "largeWindSpeed", 35f);
            HdrpAccess.SetMember(surface, "scatteringColor", new Color(0.02f, 0.16f, 0.2f));
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

        static TerrainLayer CreateLayer(string name, Color light, Color dark, float tileSize, float smoothness, float roughness)
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
            Material padMaterial = LitMaterial("Startplatz", new Color(0.18f, 0.18f, 0.2f), 0.3f);
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
            Material gateMaterial = LitMaterial("Gate", new Color(1f, 0.38f, 0.05f), 0.4f);
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

        static Material LitMaterial(string name, Color color, float smoothness)
        {
            string path = GeneratedFolder + "/M_" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("HDRP/Lit");
                material = new Material(shader != null ? shader : Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ------------------------------------------------------------ Drohne

        static DroneController CreateDrone(Transform spawnPoint)
        {
            var go = new GameObject("Drohne");
            go.transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);

            var body = go.AddComponent<Rigidbody>();
            var collider = go.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.22f, 0.05f, 0.22f);
            collider.center = new Vector3(0f, 0.025f, 0f);
            collider.sharedMaterial = new PhysicsMaterial("Drohne")
            {
                bounciness = 0.15f,
                dynamicFriction = 0.6f,
                staticFriction = 0.8f,
                bounceCombine = PhysicsMaterialCombine.Average,
            };

            DroneController drone = go.AddComponent<DroneController>();
            DroneRespawn respawn = go.AddComponent<DroneRespawn>();
            respawn.spawnPoint = spawnPoint;
            respawn.killHeight = -3f;

            go.AddComponent<AudioSource>();
            go.AddComponent<MotorAudio>().drone = drone;

            BuildDroneModel(go.transform);

            // FPV-Kamera vorn am Rahmen.
            var cameraGo = new GameObject("FPV-Kamera");
            cameraGo.transform.SetParent(go.transform, false);
            cameraGo.transform.localPosition = new Vector3(0f, 0.045f, 0.075f);
            var camera = cameraGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 30000f;
            cameraGo.AddComponent<AudioListener>();
            cameraGo.tag = "MainCamera";

            System.Type cameraData = HdrpAccess.HdType("HDAdditionalCameraData");
            if (cameraData != null)
            {
                Component data = cameraGo.AddComponent(cameraData);
                HdrpAccess.SetMember(data, "antialiasing", "TemporalAntialiasing");
                HdrpAccess.SetMember(data, "allowDynamicResolution", true);
                // Schutz gegen schwarze Pixel bei Wolken und Nebel unter Metal (Risiko im Plan).
                HdrpAccess.SetMember(data, "stopNaNs", true);
            }

            var fpv = cameraGo.AddComponent<FpvCamera>();
            fpv.drone = drone;
            fpv.tiltFromDrone = true;
            fpv.fieldOfView = 90f;

            body.mass = drone.config.MassKg;
            return drone;
        }

        /// <summary>Platzhalter-Modell aus Grundkörpern, bis die Higgsfield-Drohne kommt.</summary>
        static void BuildDroneModel(Transform root)
        {
            Material frame = LitMaterial("Drohne_Rahmen", new Color(0.05f, 0.05f, 0.055f), 0.35f);
            Material accent = LitMaterial("Drohne_Akzent", new Color(0.0f, 0.55f, 0.9f), 0.5f);
            Material prop = LitMaterial("Drohne_Propeller", new Color(0.12f, 0.12f, 0.13f), 0.6f);

            var model = new GameObject("Modell").transform;
            model.SetParent(root, false);

            Part(model, PrimitiveType.Cube, new Vector3(0f, 0.02f, 0f), new Vector3(0.06f, 0.02f, 0.14f), Quaternion.identity, frame);
            Part(model, PrimitiveType.Cube, new Vector3(0f, 0.045f, -0.01f), new Vector3(0.04f, 0.03f, 0.1f), Quaternion.identity, accent);

            const float Arm = 0.0795f;
            Vector3[] motors =
            {
                new Vector3(Arm, 0.02f, -Arm), new Vector3(Arm, 0.02f, Arm),
                new Vector3(-Arm, 0.02f, -Arm), new Vector3(-Arm, 0.02f, Arm),
            };
            foreach (Vector3 motor in motors)
            {
                Vector3 mid = motor * 0.5f;
                mid.y = 0.015f;
                Quaternion along = Quaternion.LookRotation(new Vector3(motor.x, 0f, motor.z));
                Part(model, PrimitiveType.Cube, mid, new Vector3(0.016f, 0.006f, Arm * 1.414f), along, frame);
                Part(model, PrimitiveType.Cylinder, motor, new Vector3(0.024f, 0.009f, 0.024f), Quaternion.identity, frame);
                Part(model, PrimitiveType.Cylinder, motor + new Vector3(0f, 0.012f, 0f), new Vector3(0.127f, 0.0008f, 0.127f), Quaternion.identity, prop);
            }
        }

        static void Part(Transform parent, PrimitiveType type, Vector3 position, Vector3 scale, Quaternion rotation, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(part.GetComponent<Collider>());
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localRotation = rotation;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
        }

        // ------------------------------------------------------------ Spiel-Systeme

        static void CreateSystems(DroneController drone, Terrain terrain)
        {
            var go = new GameObject("Frostline");
            StickInput input = go.AddComponent<StickInput>();
            drone.input = input;

            BenchmarkRunner benchmark = go.AddComponent<BenchmarkRunner>();
            benchmark.drone = drone;
            benchmark.center = new Vector3(IslandGenerator.TileSize * 0.5f, 0f, IslandGenerator.TileSize * 0.5f);
            benchmark.radius = 750f;
            benchmark.lowAltitude = 40f;
            benchmark.highAltitude = CloudBottom + CloudThickness * 0.3f;

            Osd osd = go.AddComponent<Osd>();
            osd.drone = drone;
            osd.input = input;
            osd.benchmark = benchmark;

            FrostlineMenu menu = go.AddComponent<FrostlineMenu>();
            menu.drone = drone;
            menu.input = input;
            menu.benchmark = benchmark;
            menu.osd = osd;
            menu.fpvCamera = drone.GetComponentInChildren<FpvCamera>();
            menu.motorAudio = drone.GetComponent<MotorAudio>();
        }

        static void AddSceneToBuild(string path)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
