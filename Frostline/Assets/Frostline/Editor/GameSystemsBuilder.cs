using System.Collections.Generic;
using System.Reflection;
using Frostline.Audio;
using Frostline.Controls;
using Frostline.Diagnostics;
using Frostline.Flight;
using Frostline.Gameplay;
using Frostline.Multiplayer;
using Frostline.Remote;
using Frostline.UI;
using Frostline.View;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Frostline.EditorTools
{
    /// <summary>Was die Umgebung erzeugt hat und die Spielsysteme brauchen.</summary>
    public sealed class EnvironmentRefs
    {
        public Light Sun;
        public Component BaseVolume;
        public Component LowCloudsVolume;
        public Component StormVolume;
    }

    /// <summary>Einstellungen, die je Szene verschieden sind.</summary>
    public sealed class SystemsOptions
    {
        public Vector2 IslandCenter = new Vector2(1000f, 1000f);
        public float SignalCleanRadius = 1300f;
        public float SignalLostRadius = 1800f;
        public Vector3 BenchmarkCenter = new Vector3(1000f, 0f, 1000f);
        public float BenchmarkRadius = 750f;
        public float CloudBottom = 650f;
        public float CloudThickness = 1000f;
        public Transform Checkpoints;
        public Transform Collectibles;
        public List<RaceTrack> Tracks = new List<RaceTrack>();
    }

    /// <summary>
    /// Gemeinsame Bausteine für alle Szenen-Builder: Himmel und Wetter, Sonne, Ozean,
    /// Drohne mit FPV-Kamera und alle Spielsysteme.
    /// </summary>
    public static class GameSystemsBuilder
    {
        public const string GeneratedFolder = "Assets/Frostline/Generated";

        // ------------------------------------------------------------ Umgebung

        public static EnvironmentRefs CreateEnvironment(float cloudBottom, float cloudThickness)
        {
            FrostlineProjectSetup.EnsureFolder(GeneratedFolder);
            var refs = new EnvironmentRefs();
            var parent = new GameObject("Umgebung").transform;

            // Basis: klarer Himmel mit einer Wolkenschicht, in die man hineinfliegen kann.
            Object baseProfile = CreateProfile("Env_Basis");
            Object visual = HdrpAccess.AddVolumeComponent(baseProfile, "VisualEnvironment");
            HdrpAccess.SetVolume(visual, "skyType", 4); // Physically Based Sky
            HdrpAccess.SetVolume(visual, "cloudType", 0);
            // Muss World Space bleiben, sonst kann man nicht in die Wolken hineinfliegen.
            HdrpAccess.SetVolume(visual, "renderingSpace", "World");
            HdrpAccess.AddVolumeComponent(baseProfile, "PhysicallyBasedSky");
            AddClouds(baseProfile, "Cloudy", cloudBottom, cloudThickness, 48);
            AddFog(baseProfile, 1800f, 350f);

            Object exposure = HdrpAccess.AddVolumeComponent(baseProfile, "Exposure");
            HdrpAccess.SetVolume(exposure, "mode", "Automatic");
            HdrpAccess.SetVolume(exposure, "limitMin", 4f);
            HdrpAccess.SetVolume(exposure, "limitMax", 16f);

            Object shadows = HdrpAccess.AddVolumeComponent(baseProfile, "HDShadowSettings");
            HdrpAccess.SetVolume(shadows, "maxShadowDistance", 600f);
            HdrpAccess.SetVolume(shadows, "cascadeShadowSplitCount", 3);

            Object water = HdrpAccess.AddVolumeComponent(baseProfile, "WaterRendering");
            HdrpAccess.SetVolume(water, "enable", true);
            Save(baseProfile);

            // Tiefe Wolken: geschlossene Decke ab 250 m, dichter Dunst.
            Object lowProfile = CreateProfile("Env_TiefeWolken");
            AddClouds(lowProfile, "Overcast", 250f, 700f, 48);
            AddFog(lowProfile, 600f, 300f);
            Save(lowProfile);

            // Schneesturm: dunkle Wolken ab 300 m, Sichtweite um 150 m.
            Object stormProfile = CreateProfile("Env_Schneesturm");
            AddClouds(stormProfile, "Stormy", 300f, 900f, 48);
            AddFog(stormProfile, 150f, 2000f);
            Save(stormProfile);

            refs.BaseVolume = CreateVolume("Himmel und Wolken (Basis)", parent, baseProfile, 0f, 1f);
            refs.LowCloudsVolume = CreateVolume("Wetter: Tiefe Wolken", parent, lowProfile, 1f, 0f);
            refs.StormVolume = CreateVolume("Wetter: Schneesturm", parent, stormProfile, 2f, 0f);

            refs.Sun = CreateSun(parent);
            CreateOcean(parent);
            return refs;
        }

        static Object CreateProfile(string name)
        {
            string path = GeneratedFolder + "/" + name + ".asset";
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            ScriptableObject profile = ScriptableObject.CreateInstance(HdrpAccess.CoreType("VolumeProfile"));
            AssetDatabase.CreateAsset(profile, path);
            return profile;
        }

        static void Save(Object profile)
        {
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        static void AddClouds(Object profile, string preset, float bottom, float thickness, int steps)
        {
            Object clouds = HdrpAccess.AddVolumeComponent(profile, "VolumetricClouds");
            if (clouds == null) return;
            HdrpAccess.SetVolume(clouds, "enable", true);
            HdrpAccess.SetVolume(clouds, "cloudControl", "Simple");
            HdrpAccess.SetVolume(clouds, "cloudSimpleMode", "Performance");
            // Preset setzt Dichte und Form; danach alle Werte festhalten und nur die Höhe ändern.
            HdrpAccess.SetMember(clouds, "cloudPreset", preset);
            SetAllOverrides(clouds);
            HdrpAccess.SetMember(clouds, "cloudPreset", "Custom");
            HdrpAccess.SetVolume(clouds, "bottomAltitude", bottom);
            HdrpAccess.SetVolume(clouds, "altitudeRange", thickness);
            // Laut Plan der größte Kostentreiber: wenige Schritte.
            HdrpAccess.SetVolume(clouds, "numPrimarySteps", steps);
            HdrpAccess.SetVolume(clouds, "numLightSteps", 6);
            HdrpAccess.SetVolume(clouds, "shadows", true);
        }

        static void AddFog(Object profile, float meanFreePath, float maximumHeight)
        {
            Object fog = HdrpAccess.AddVolumeComponent(profile, "Fog");
            HdrpAccess.SetVolume(fog, "enabled", true);
            HdrpAccess.SetVolume(fog, "meanFreePath", meanFreePath);
            HdrpAccess.SetVolume(fog, "baseHeight", 0f);
            HdrpAccess.SetVolume(fog, "maximumHeight", maximumHeight);
            HdrpAccess.SetVolume(fog, "enableVolumetricFog", true);
            HdrpAccess.SetVolume(fog, "depthExtent", 300f);
            HdrpAccess.SetVolume(fog, "anisotropy", 0.6f);
        }

        static void SetAllOverrides(Object component)
        {
            if (component == null) return;
            MethodInfo method = component.GetType().GetMethod("SetAllOverridesTo", new[] { typeof(bool) });
            if (method != null) method.Invoke(component, new object[] { true });
        }

        static Component CreateVolume(string name, Transform parent, Object profile, float priority, float weight)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            Component volume = go.AddComponent(HdrpAccess.CoreType("Volume"));
            HdrpAccess.SetMember(volume, "isGlobal", true);
            HdrpAccess.SetMember(volume, "priority", priority);
            HdrpAccess.SetMember(volume, "weight", weight);
            HdrpAccess.SetMember(volume, "sharedProfile", profile);
            return volume;
        }

        static Light CreateSun(Transform parent)
        {
            var go = new GameObject("Sonne");
            go.transform.SetParent(parent, false);
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
            return light;
        }

        static void CreateOcean(Transform parent)
        {
            System.Type surfaceType = HdrpAccess.HdType("WaterSurface");
            if (surfaceType == null)
            {
                Debug.LogWarning("Frostline: HDRP-Wasser nicht gefunden, Ozean fehlt.");
                return;
            }
            var go = new GameObject("Ozean");
            go.transform.SetParent(parent, false);
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

        // ------------------------------------------------------------ Materialien

        /// <summary>HDRP-Lit-Material mit Farbe; wird als Asset unter Generated gespeichert und wiederverwendet.</summary>
        public static Material LitMaterial(string name, Color color, float smoothness, float metallic = 0f, bool doubleSided = false)
        {
            FrostlineProjectSetup.EnsureFolder(GeneratedFolder + "/Materials");
            string path = GeneratedFolder + "/Materials/M_" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("HDRP/Lit");
                material = new Material(shader != null ? shader : Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", metallic);
            if (doubleSided)
            {
                material.SetFloat("_DoubleSidedEnable", 1f);
                material.SetFloat("_CullMode", 0f);
                material.SetFloat("_CullModeForward", 0f);
                material.doubleSidedGI = true;
            }
            // HDRP leitet Shader-Varianten aus den Werten ab; ohne diesen Schritt greift z. B. Double Sided nicht.
            HdrpAccess.InvokeStatic(HdrpAccess.HdType("HDMaterial"), "ValidateMaterial", material);
            EditorUtility.SetDirty(material);
            return material;
        }

        // ------------------------------------------------------------ Drohne

        /// <summary>Ebene für das Modell der eigenen Drohne; die FPV-Kamera blendet sie aus.</summary>
        public const int OwnDroneLayer = 8;
        public const string OwnDroneLayerName = "Eigene Drohne";

        public static DroneController CreateDrone(Transform spawnPoint)
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

            Transform model = BuildDroneModel(go.transform, "Modell", 1f);
            DroneModelSwap.TryUseGeneratedModel(model);

            var cameraGo = new GameObject("FPV-Kamera");
            cameraGo.transform.SetParent(go.transform, false);
            cameraGo.transform.localPosition = new Vector3(0f, 0.045f, 0.075f);
            var camera = cameraGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 30000f;
            cameraGo.AddComponent<AudioListener>();
            cameraGo.tag = "MainCamera";
            ConfigureHdCamera(cameraGo);

            // Das eigene Modell zeigt nur die Verfolgerkamera. Die FPV-Kamera sitzt mitten im Rahmen
            // und hätte sonst Arme und Kamerahalter groß im Bild.
            foreach (Renderer part in go.GetComponentsInChildren<Renderer>(true)) part.gameObject.layer = OwnDroneLayer;
            camera.cullingMask &= ~(1 << OwnDroneLayer);

            var fpv = cameraGo.AddComponent<FpvCamera>();
            fpv.drone = drone;
            fpv.tiltFromDrone = true;
            fpv.fieldOfView = 90f;

            body.mass = drone.config.MassKg;
            return drone;
        }

        static void ConfigureHdCamera(GameObject cameraGo)
        {
            System.Type cameraData = HdrpAccess.HdType("HDAdditionalCameraData");
            if (cameraData == null) return;
            Component data = cameraGo.GetComponent(cameraData) ?? cameraGo.AddComponent(cameraData);
            HdrpAccess.SetMember(data, "antialiasing", "TemporalAntialiasing");
            HdrpAccess.SetMember(data, "allowDynamicResolution", true);
            // Schutz gegen schwarze Pixel bei Wolken und Nebel unter Metal (Risiko im Plan).
            HdrpAccess.SetMember(data, "stopNaNs", true);
        }

        /// <summary>Platzhalter-Drohne aus Grundkörpern, bis das Higgsfield-Modell eingesetzt ist.</summary>
        public static Transform BuildDroneModel(Transform root, string name, float alpha)
        {
            bool ghost = alpha < 0.99f;
            Material frame = LitMaterial(ghost ? "Ghost" : "Drohne_Rahmen", ghost ? new Color(0.4f, 0.9f, 1f) : new Color(0.05f, 0.05f, 0.055f), 0.35f);
            Material accent = LitMaterial(ghost ? "Ghost" : "Drohne_Akzent", ghost ? new Color(0.4f, 0.9f, 1f) : new Color(0.0f, 0.55f, 0.9f), 0.5f);
            Material prop = LitMaterial(ghost ? "Ghost" : "Drohne_Propeller", ghost ? new Color(0.4f, 0.9f, 1f) : new Color(0.9f, 0.45f, 0.1f), 0.6f);

            var model = new GameObject(name).transform;
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
            return model;
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

        // ------------------------------------------------------------ Spielsysteme

        public static void CreateSystems(DroneController drone, EnvironmentRefs env, SystemsOptions options)
        {
            var go = new GameObject("Frostline");

            RemoteControlServer remote = go.AddComponent<RemoteControlServer>();
            remote.drone = drone;

            StickInput input = go.AddComponent<StickInput>();
            input.remote = remote;
            drone.input = input;

            BenchmarkRunner benchmark = go.AddComponent<BenchmarkRunner>();
            benchmark.drone = drone;
            benchmark.center = options.BenchmarkCenter;
            benchmark.radius = options.BenchmarkRadius;
            benchmark.lowAltitude = 40f;
            benchmark.highAltitude = options.CloudBottom + options.CloudThickness * 0.3f;

            Osd osd = go.AddComponent<Osd>();
            osd.drone = drone;
            osd.input = input;
            osd.benchmark = benchmark;

            // Verfolgerkamera für Replays, normalerweise aus.
            var chaseGo = new GameObject("Verfolgerkamera");
            chaseGo.transform.SetParent(go.transform, false);
            Camera chaseCam = chaseGo.AddComponent<Camera>();
            chaseCam.nearClipPlane = 0.05f;
            chaseCam.farClipPlane = 30000f;
            ConfigureHdCamera(chaseGo);
            ChaseCamera chase = chaseGo.AddComponent<ChaseCamera>();
            chaseGo.SetActive(false);

            ReplayDirector replay = go.AddComponent<ReplayDirector>();
            replay.drone = drone;
            replay.chaseCamera = chase;
            replay.fpvCamera = drone.GetComponentInChildren<FpvCamera>().GetComponent<Camera>();

            // Ghost der besten Runde.
            var ghostGo = new GameObject("Ghost");
            ghostGo.transform.SetParent(go.transform, false);
            BuildDroneModel(ghostGo.transform, "Modell", 0.5f);
            GhostDrone ghost = ghostGo.AddComponent<GhostDrone>();
            ghostGo.SetActive(false);

            RaceManager race = go.AddComponent<RaceManager>();
            race.drone = drone;
            race.ghost = ghost;
            race.tracks = options.Tracks;

            SignalBoundary signal = go.AddComponent<SignalBoundary>();
            signal.drone = drone;
            signal.center = options.IslandCenter;
            signal.cleanRadius = options.SignalCleanRadius;
            signal.lostRadius = options.SignalLostRadius;

            WeatherSystem weather = go.AddComponent<WeatherSystem>();
            weather.sun = env.Sun;
            weather.drone = drone;
            weather.baseVolume = env.BaseVolume;
            weather.lowCloudsVolume = env.LowCloudsVolume;
            weather.stormVolume = env.StormVolume;
            weather.snow = CreateSnow(go.transform);
            weather.windAudio = CreateWindAudio(go.transform);

            StartupScreen startup = go.AddComponent<StartupScreen>();
            startup.image = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Frostline/UI/Ladebildschirm.jpg");

            QualityTuner tuner = go.AddComponent<QualityTuner>();
            tuner.volumes = new[] { env.BaseVolume, env.LowCloudsVolume, env.StormVolume };

            if (options.Checkpoints != null)
            {
                Checkpoints checkpoints = options.Checkpoints.gameObject.AddComponent<Checkpoints>();
                checkpoints.drone = drone;
            }

            Collectibles collectibles = null;
            if (options.Collectibles != null)
            {
                collectibles = options.Collectibles.gameObject.AddComponent<Collectibles>();
                collectibles.drone = drone;
            }

            CreateMultiplayer();

            FrostlineMenu menu = go.AddComponent<FrostlineMenu>();
            menu.drone = drone;
            menu.input = input;
            menu.benchmark = benchmark;
            menu.osd = osd;
            menu.fpvCamera = drone.GetComponentInChildren<FpvCamera>();
            menu.motorAudio = drone.GetComponent<MotorAudio>();
            menu.race = race;
            menu.replay = replay;
            menu.weather = weather;
            menu.collectibles = collectibles;
            menu.remote = remote;
        }

        /// <summary>
        /// Netcode for GameObjects mit Unity Transport (Relay kommt über die Sessions), dazu die
        /// Spieler-Drohne als Prefab und die gemeinsame Welt (Wetter, Rennen, Fangen).
        /// </summary>
        static void CreateMultiplayer()
        {
            var go = new GameObject("Multiplayer");
            NetworkManager manager = go.AddComponent<NetworkManager>();
            UnityTransport transport = go.AddComponent<UnityTransport>();
            if (manager.NetworkConfig == null) manager.NetworkConfig = new NetworkConfig();
            manager.NetworkConfig.NetworkTransport = transport;
            manager.NetworkConfig.TickRate = 30;
            manager.NetworkConfig.ConnectionApproval = false;
            manager.NetworkConfig.EnableSceneManagement = true;
            manager.NetworkConfig.PlayerPrefab = CreateNetworkDronePrefab();
            go.AddComponent<MultiplayerSession>();
            go.AddComponent<MultiplayerPanel>();

            var world = new GameObject("Netzwerk-Welt");
            world.AddComponent<NetworkObject>();
            world.AddComponent<NetworkWorld>();
        }

        static GameObject CreateNetworkDronePrefab()
        {
            const string path = GeneratedFolder + "/Netzwerk-Drohne.prefab";
            var go = new GameObject("Netzwerk-Drohne");
            go.AddComponent<NetworkObject>();
            NetworkDrone drone = go.AddComponent<NetworkDrone>();
            Material frame = LitMaterial("Mitspieler", new Color(1f, 0.55f, 0.1f), 0.5f);
            var model = new GameObject("Modell").transform;
            model.SetParent(go.transform, false);
            Part(model, PrimitiveType.Cube, new Vector3(0f, 0.02f, 0f), new Vector3(0.22f, 0.03f, 0.22f), Quaternion.Euler(0f, 45f, 0f), frame);
            Part(model, PrimitiveType.Cube, new Vector3(0f, 0.045f, -0.01f), new Vector3(0.05f, 0.04f, 0.12f), Quaternion.identity, frame);
            go.AddComponent<AudioSource>();
            MotorAudio audio = go.AddComponent<MotorAudio>();
            audio.spatial = true;
            audio.volume = 0.6f;
            drone.model = model;
            drone.motorAudio = audio;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) AssetDatabase.DeleteAsset(path);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        /// <summary>Schneefall in einem Würfel um die Kamera; Menge steuert das Wetter.</summary>
        static ParticleSystem CreateSnow(Transform parent)
        {
            var go = new GameObject("Schneefall");
            go.transform.SetParent(parent, false);
            ParticleSystem snow = go.AddComponent<ParticleSystem>();
            snow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = snow.main;
            main.loop = true;
            main.duration = 5f;
            main.startLifetime = 6f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 40000;
            main.gravityModifier = 0.05f;

            ParticleSystem.EmissionModule emission = snow.emission;
            emission.rateOverTime = 0f;

            ParticleSystem.ShapeModule shape = snow.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(60f, 4f, 60f);

            ParticleSystem.VelocityOverLifetimeModule velocity = snow.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = 0f;
            velocity.y = -1.2f;
            velocity.z = 0f;

            ParticleSystem.NoiseModule noise = snow.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.4f;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            Shader unlit = Shader.Find("HDRP/Unlit");
            var material = new Material(unlit != null ? unlit : Shader.Find("Sprites/Default")) { name = "Schneeflocke" };
            material.SetColor("_UnlitColor", new Color(1f, 1f, 1f, 0.85f));
            string path = GeneratedFolder + "/Materials/M_Schneeflocke.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(material, path);
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            snow.Play();
            return snow;
        }

        static AudioSource CreateWindAudio(Transform parent)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Frostline/Audio/Ambient/Wind_Gletscher.mp3");
            if (clip == null) return null;
            var go = new GameObject("Wind (Ton)");
            go.transform.SetParent(parent, false);
            AudioSource source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = true;
            source.spatialBlend = 0f;
            source.volume = 0.1f;
            return source;
        }

        /// <summary>Ambient-Ton an einer Stelle der Insel, mit 3D-Abfall.</summary>
        public static void CreateAmbient(Transform parent, string clipName, Vector3 position, float radius, float volume)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Frostline/Audio/Ambient/" + clipName + ".mp3");
            if (clip == null) return;
            var go = new GameObject("Ton: " + clipName);
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            AudioSource source = go.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = true;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = radius * 0.3f;
            source.maxDistance = radius;
            source.volume = volume;
            source.dopplerLevel = 0f;
        }

        public static void AddSceneToBuild(string path, bool first)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            if (first) scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            else scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
