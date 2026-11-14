using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace Frostline.FotoInsel
{
    /// <summary>
    /// Licht und Luft für den Foto-Look: physikalischer Himmel mit Dunst, Quellwolken mit Schatten,
    /// Höhennebel für die Luftperspektive, tiefe Nachmittagssonne, ACES, leichte Filmkorn-/Vignetten-
    /// Optik. Dazu Ozean und Flüsse als HDRP-Wasser.
    /// </summary>
    public static class FotoAtmosphaere
    {
        public static Light Sun;
        public static VolumeProfile Profile;
        public static float SkyLux;
        /// <summary>Sonne am Nachmittag: 27° über dem Horizont aus Westsüdwest.</summary>
        static Quaternion SunRotation = Quaternion.Euler(27f, 62f, 0f);
        const float SunLux = 128000f;   // außerhalb der Atmosphäre

        static T Add<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component = profile.Add<T>(false);
            component.active = true;
            // Ohne das steht die Komponente nur im Speicher: Nach dem nächsten Laden (Spiel, Build,
            // neuer Editor-Start) ist das Profil leer – kein Himmel, keine Wolken, kein Farblook.
            if (EditorUtility.IsPersistent(profile))
            {
                component.name = typeof(T).Name;
                component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            return component;
        }

        static string MainProfilePath(string folder, bool verlassen)
        {
            return folder + (verlassen ? "/Verlassen_Atmosphaere.asset" : "/FotoInsel_Atmosphaere.asset");
        }

        static string LowCloudProfilePath(string folder) { return folder + "/Verlassen_Bodennaehe.asset"; }

        static Quaternion SunRotationFor(bool verlassen)
        {
            // Verlassene Insel: tiefe Abendsonne aus Westen, goldenes Streiflicht über dem Wolkenmeer.
            return verlassen ? Quaternion.Euler(10f, 80f, 0f) : Quaternion.Euler(27f, 62f, 0f);
        }

        /// <summary>
        /// Füllt vorhandene Atmosphären-Profile neu, ohne ihre GUID zu ändern – die Szene bleibt verknüpft.
        /// Für Inseln, die gebaut wurden, bevor <see cref="Add{T}"/> die Komponenten ins Asset schrieb:
        /// Dort sind die Profile auf der Platte leer und im Spiel fehlen Himmel, Wolken, Nebel und Look.
        /// </summary>
        public static string RepairProfiles(string folder, bool verlassen)
        {
            SunRotation = SunRotationFor(verlassen);
            string report = "";
            var main = AssetDatabase.LoadAssetAtPath<VolumeProfile>(MainProfilePath(folder, verlassen));
            if (main != null)
            {
                Clear(main);
                FillMainProfile(main, folder, verlassen);
                report += "Atmosphäre neu geschrieben: " + main.components.Count + " Einstellungen.\n";
            }
            var low = verlassen ? AssetDatabase.LoadAssetAtPath<VolumeProfile>(LowCloudProfilePath(folder)) : null;
            if (low != null)
            {
                Clear(low);
                FillLowCloudProfile(low);
                report += "Wolken in Bodennähe neu geschrieben.\n";
            }
            AssetDatabase.SaveAssets();
            return report;
        }

        static void Clear(VolumeProfile profile)
        {
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(profile)))
            {
                if (sub == null || sub == profile || !(sub is VolumeComponent)) continue;
                AssetDatabase.RemoveObjectFromAsset(sub);
                Object.DestroyImmediate(sub, true);
            }
            profile.components.Clear();
            profile.isDirty = true;
            EditorUtility.SetDirty(profile);
        }

        public static void Create(Transform parent, FotoTerrain t, string folder)
        {
            bool verlassen = FotoTerrain.Verlassen;
            SunRotation = SunRotationFor(verlassen);
            string path = MainProfilePath(folder, verlassen);
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            FillMainProfile(profile, folder, verlassen);

            var volumeGo = new GameObject("Atmosphäre (Himmel, Wolken, Nebel, Post)");
            volumeGo.transform.SetParent(parent, false);
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;

            if (verlassen) CreateLowCloudVolume(parent, folder);
            if (verlassen) CreateValleyMist(parent, t);
            CreateSun(parent);
            CreateOcean(parent);
            CreateRivers(parent, t, folder);
        }

        static void FillMainProfile(VolumeProfile profile, string folder, bool verlassen)
        {
            Profile = profile;

            // Eigener Streuungs-Himmel als HDRI: HDRPs Physically Based Sky rendert hier schwarz.
            Vector3 sunDirection = -(SunRotation * Vector3.forward);
            Cubemap skyMap = FotoHimmel.Create(folder + (verlassen ? "/Himmel_Abend.asset" : "/Himmel.asset"), sunDirection, 384, true);
            var env = Add<VisualEnvironment>(profile);
            env.skyType.Override((int)SkyType.HDRI);
            env.cloudType.Override(0);
            env.skyAmbientMode.Override(SkyAmbientMode.Dynamic);

            var sky = Add<HDRISky>(profile);
            sky.hdriSky.Override(skyMap);
            sky.skyIntensityMode.Override(SkyIntensityMode.Multiplier);
            sky.multiplier.Override(1f);
            SkyLux = FotoHimmel.UpperHemisphereLux;

            var clouds = Add<VolumetricClouds>(profile);
            clouds.enable.Override(true);
            clouds.cloudPreset = VolumetricClouds.CloudPresets.Sparse;
            if (verlassen)
            {
                // Geschlossenes Wolkenmeer, aus dem der Gipfel herausragt.
                // Geschlossenes Wolkenmeer: Preset-Werte gelten im Volume nur als Overrides.
                clouds.cloudPreset = VolumetricClouds.CloudPresets.Overcast;
                clouds.SetAllOverridesTo(true);
                clouds.bottomAltitude.Override(1150f);
                clouds.altitudeRange.Override(550f);
                clouds.densityMultiplier.Override(1f);
                clouds.shapeFactor.Override(0.55f);
            }
            else
            {
                clouds.bottomAltitude.Override(1800f);
                clouds.altitudeRange.Override(1800f);
            }
            clouds.multiScattering.Override(0.75f);
            clouds.erosionOcclusion.Override(0.1f);
            clouds.shadows.Override(true);
            clouds.shadowOpacity.Override(0.75f);
            clouds.shadowDistance.Override(9000f);
            clouds.numPrimarySteps.Override(96);

            var fog = Add<Fog>(profile);
            fog.enabled.Override(true);
            fog.meanFreePath.Override(4500f);
            fog.baseHeight.Override(0f);
            fog.maximumHeight.Override(verlassen ? 1150f : 700f);
            fog.maxFogDistance.Override(20000f);
            fog.enableVolumetricFog.Override(true);
            fog.depthExtent.Override(400f);
            fog.anisotropy.Override(0.65f);
            fog.albedo.Override(new Color(0.92f, 0.94f, 0.97f));

            var exposure = Add<Exposure>(profile);
            // Feste Tageslicht-Belichtung wie bei einer Kamera („Sunny 16“ minus etwas Reserve):
            // die Automatik lässt sich von der Sonnenspiegelung im Meer zu dunkel ziehen.
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(verlassen ? 12.6f : 13.7f);

            var tonemap = Add<Tonemapping>(profile);
            tonemap.mode.Override(TonemappingMode.ACES);

            var bloom = Add<Bloom>(profile);
            bloom.intensity.Override(0.1f);
            bloom.scatter.Override(0.65f);

            var color = Add<ColorAdjustments>(profile);
            color.contrast.Override(6f);
            color.saturation.Override(-6f);

            var balance = Add<WhiteBalance>(profile);
            balance.temperature.Override(0f);
            balance.tint.Override(2f);

            var ssao = Add<ScreenSpaceAmbientOcclusion>(profile);
            ssao.intensity.Override(1.1f);
            ssao.radius.Override(2.2f);

            var contact = Add<ContactShadows>(profile);
            contact.enable.Override(true);
            contact.length.Override(0.35f);

            var micro = Add<MicroShadowing>(profile);
            micro.enable.Override(true);

            var shadows = Add<HDShadowSettings>(profile);
            shadows.maxShadowDistance.Override(2600f);
            shadows.cascadeShadowSplitCount.Override(4);

            if (verlassen)
            {
                // Filmische Farbstimmung: kühle Schatten, warme Lichter, etwas Kontrast in den Mitten.
                var split = Add<SplitToning>(profile);
                split.shadows.Override(new Color(0.42f, 0.52f, 0.58f));
                split.highlights.Override(new Color(0.62f, 0.52f, 0.4f));
                split.balance.Override(-12f);
                bloom.intensity.Override(0.16f);
                color.contrast.Override(10f);
            }
            var vignette = Add<Vignette>(profile);
            vignette.intensity.Override(0.17f);
            vignette.smoothness.Override(0.55f);

            var grain = Add<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0.1f);

            var chroma = Add<ChromaticAberration>(profile);
            chroma.intensity.Override(0.04f);

            var water = Add<WaterRendering>(profile);
            water.enable.Override(true);

            if (FotoBaeume.FoliageProfile != null)
            {
                var list = Add<DiffusionProfileList>(profile);
                list.diffusionProfiles.Override(new[] { FotoBaeume.FoliageProfile });
            }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Lokales Volumen in Bodennähe: Ist die Kamera unter ~700 m, reißt die Wolkendecke auf und die
        /// Abendsonne erreicht Strand und Dorf. Von oben bleibt das geschlossene Wolkenmeer.
        /// </summary>
        static void CreateLowCloudVolume(Transform parent, string folder)
        {
            string path = LowCloudProfilePath(folder);
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
            FillLowCloudProfile(profile);

            var go = new GameObject("Wolken in Bodennähe (aufgerissen)");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(FotoTerrain.Size * 0.5f, 250f, FotoTerrain.Size * 0.5f);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(FotoTerrain.Size + 4000f, 900f, FotoTerrain.Size + 4000f);
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = false;
            volume.priority = 20f;
            volume.blendDistance = 450f;
            volume.sharedProfile = profile;
        }

        static void FillLowCloudProfile(VolumeProfile profile)
        {
            var clouds = Add<VolumetricClouds>(profile);
            clouds.enable.Override(true);
            clouds.cloudPreset = VolumetricClouds.CloudPresets.Cloudy;
            clouds.SetAllOverridesTo(true);
            clouds.bottomAltitude.Override(1150f);
            clouds.altitudeRange.Override(550f);
            clouds.densityMultiplier.Override(0.5f);
            clouds.shadows.Override(true);
            // Unten im Abendschatten heller belichten, wie ein Kameramann für die Schatten belichtet.
            var exposure = Add<Exposure>(profile);
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(11.7f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        /// <summary>Bodennebel über Dorf und Flusstal: Lichtstrahlen und Tiefenstaffelung wie im Film.</summary>
        static void CreateValleyMist(Transform parent, FotoTerrain t)
        {
            var root = new GameObject("Bodennebel");
            root.transform.SetParent(parent, false);
            void Mist(Vector2 p, Vector3 size, float meanFreePath)
            {
                var go = new GameObject("Nebel");
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(p.x, Mathf.Max(0f, t.HeightAt(p)) + size.y * 0.35f, p.y);
                var fog = go.AddComponent<LocalVolumetricFog>();
                LocalVolumetricFogArtistParameters a = fog.parameters;
                a.size = size;
                a.meanFreePath = meanFreePath;
                a.albedo = new Color(0.95f, 0.93f, 0.9f);
                a.anisotropy = 0.7f;
                a.positiveFade = new Vector3(0.3f, 0.6f, 0.3f);
                a.negativeFade = new Vector3(0.3f, 0.1f, 0.3f);
                fog.parameters = a;
            }
            if (t.Town != null) Mist(t.Town.Center, new Vector3(700f, 40f, 700f), 900f);
            if (t.MainRiver >= 0)
            {
                FotoRiver river = t.Rivers[t.MainRiver];
                for (int i = river.Points.Count / 5; i < river.Points.Count; i += 120)
                    Mist(river.Points[i], new Vector3(420f, 32f, 420f), 1100f);
            }
        }

        static void CreateSun(Transform parent)
        {
            var go = new GameObject(FotoTerrain.Verlassen ? "Sonne (Abend)" : "Sonne (Nachmittag)");
            go.transform.SetParent(parent, false);
            go.transform.rotation = SunRotation;
            HDAdditionalLightData hd = go.AddHDLight(LightType.Directional);
            Light light = go.GetComponent<Light>();
            Vector3 t = FotoHimmel.SunColorAtGround(-(SunRotation * Vector3.forward));
            float max = Mathf.Max(t.x, Mathf.Max(t.y, t.z));
            light.lightUnit = LightUnit.Lux;
            light.useColorTemperature = false;
            // Teilweiser Weißabgleich wie bei einer Kamera auf Tageslicht: warm, aber nicht orange.
            light.color = Color.Lerp(new Color(t.x / max, t.y / max, t.z / max), Color.white, 0.45f);
            light.intensity = SunLux * (0.2126f * t.x + 0.7152f * t.y + 0.0722f * t.z);
            light.shadows = LightShadows.Soft;
            hd.SetShadowResolutionOverride(true);
            hd.SetShadowResolution(4096);
            Sun = light;
        }

        static void ApplyPreset(WaterSurface surface, string method)
        {
            System.Type presets = typeof(WaterSurface).Assembly.GetType("UnityEngine.Rendering.HighDefinition.WaterSurfacePresets");
            MethodInfo m = presets != null ? presets.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public) : null;
            if (m != null) m.Invoke(null, new object[] { surface });
        }

        static void CreateOcean(Transform parent)
        {
            var go = new GameObject("Ozean");
            go.transform.SetParent(parent, false);
            var ocean = go.AddComponent<WaterSurface>();
            ApplyPreset(ocean, "ApplyWaterOceanPreset");
            ocean.surfaceType = WaterSurfaceType.OceanSeaLake;
            ocean.geometryType = WaterGeometryType.Infinite;
            ocean.largeWindSpeed = 22f;
            ocean.largeOrientationValue = 235f;
            ocean.ripplesWindSpeed = 9f;
            ocean.scatteringColor = FotoTerrain.Verlassen ? new Color(0.02f, 0.26f, 0.28f) : new Color(0.02f, 0.2f, 0.24f);
            ocean.refractionColor = FotoTerrain.Verlassen ? new Color(0.1f, 0.62f, 0.6f) : new Color(0.12f, 0.48f, 0.52f);
            ocean.absorptionDistance = FotoTerrain.Verlassen ? 14f : 7f;
            ocean.maxRefractionDistance = 2f;
            ocean.foam = true;
            ocean.simulationFoamAmount = 0.22f;
        }

        static void CreateRivers(Transform parent, FotoTerrain t, string folder)
        {
            var root = new GameObject("Flüsse");
            root.transform.SetParent(parent, false);
            var renderers = new List<MeshRenderer>();
            var mainRenderers = new List<MeshRenderer>();
            Vector2 mainFlow = Vector2.zero;
            for (int r = 0; r < t.Rivers.Count; r++)
            {
                FotoRiver river = t.Rivers[r];
                var vertices = new List<Vector3>();
                var uvs = new List<Vector2>();
                var triangles = new List<int>();
                float along = 0f;
                for (int i = 0; i < river.Points.Count; i++)
                {
                    Vector2 p = river.Points[i];
                    Vector2 prev = river.Points[Mathf.Max(0, i - 1)], next = river.Points[Mathf.Min(river.Points.Count - 1, i + 1)];
                    Vector2 dir = (next - prev).normalized;
                    Vector2 side = new Vector2(-dir.y, dir.x);
                    float hw = river.HalfWidth[i] + 1.4f;
                    float y = river.Surface[i];
                    if (i > 0) along += Vector2.Distance(p, river.Points[i - 1]);
                    vertices.Add(new Vector3(p.x - side.x * hw, y, p.y - side.y * hw));
                    vertices.Add(new Vector3(p.x + side.x * hw, y, p.y + side.y * hw));
                    uvs.Add(new Vector2(0f, along));
                    uvs.Add(new Vector2(hw * 2f, along));
                    if (i > 0)
                    {
                        int a = (i - 1) * 2;
                        triangles.Add(a); triangles.Add(a + 1); triangles.Add(a + 2);
                        triangles.Add(a + 1); triangles.Add(a + 3); triangles.Add(a + 2);
                    }
                }
                var mesh = new Mesh { name = "Fluss_" + r, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                string path = folder + "/Fluss_" + r + ".asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null) AssetDatabase.DeleteAsset(path);
                AssetDatabase.CreateAsset(mesh, path);

                bool main = r == t.MainRiver;
                var go = new GameObject(main ? "Hauptfluss" : "Fluss " + (r + 1));
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.enabled = false;
                if (main)
                {
                    mainRenderers.Add(renderer);
                    mainFlow = river.Points[river.Points.Count - 1] - river.Points[0];
                }
                else renderers.Add(renderer);
            }
            if (mainRenderers.Count > 0)
            {
                // Hauptfluss als eigene Wasserfläche mit Strömung in Fließrichtung, Schaum und Kräuseln.
                var mainGo = new GameObject("Hauptfluss (Strömung)");
                mainGo.transform.SetParent(root.transform, false);
                var main = mainGo.AddComponent<WaterSurface>();
                ApplyPreset(main, "ApplyWaterRiverPreset");
                main.surfaceType = WaterSurfaceType.River;
                main.geometryType = WaterGeometryType.Custom;
                main.meshRenderers = mainRenderers;
                main.largeWindSpeed = 22f;
                main.largeOrientationValue = Mathf.Atan2(mainFlow.y, mainFlow.x) * Mathf.Rad2Deg;
                main.largeCurrentSpeedValue = 1.4f;
                main.ripplesWindSpeed = 9f;
                main.ripplesCurrentSpeedValue = 1.4f;
                main.foam = true;
                main.simulationFoamAmount = 0.35f;
                main.scatteringColor = new Color(0.03f, 0.16f, 0.12f);
                main.refractionColor = new Color(0.3f, 0.5f, 0.38f);
                main.absorptionDistance = 3f;
                main.maxRefractionDistance = 1.2f;
            }
            if (renderers.Count == 0) return;
            var surface = root.AddComponent<WaterSurface>();
            ApplyPreset(surface, "ApplyWaterRiverPreset");
            surface.surfaceType = WaterSurfaceType.River;
            surface.geometryType = WaterGeometryType.Custom;
            surface.meshRenderers = renderers;
            surface.largeWindSpeed = 12f;
            surface.ripplesWindSpeed = 6f;
            surface.scatteringColor = new Color(0.03f, 0.13f, 0.1f);
            surface.refractionColor = new Color(0.25f, 0.4f, 0.3f);
            surface.absorptionDistance = 2.2f;
            surface.maxRefractionDistance = 1f;
        }
    }
}
