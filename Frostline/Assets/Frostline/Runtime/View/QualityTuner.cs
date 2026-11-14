using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Frostline.View
{
    /// <summary>
    /// Ergänzt die Qualitätsstufen um das, was im HDRP-Asset nicht steht: Wolkenschritte
    /// (laut Plan der größte Kostentreiber), Schatten, Baum- und Grasdistanz. Reagiert,
    /// wenn im Menü die Stufe gewechselt wird. Gras wird zusätzlich mit der Flughöhe ausgeblendet:
    /// Unity zieht die Gras-Sichtweite waagerecht um die Kamera, in 300 m Höhe also eine Scheibe
    /// winziger Halme, die niemand sieht und die die Grafikkarte ein Drittel der Bildzeit kostet.
    /// </summary>
    public sealed class QualityTuner : MonoBehaviour
    {
        [System.Serializable]
        public struct Level
        {
            public int cloudPrimarySteps;
            public int cloudLightSteps;
            public float shadowDistance;
            public float terrainPixelError;
            public float treeDistance;
            [Tooltip("Höchstens so weit Gras und Büsche (m); 0 = wie in der Szene.")]
            public float detailDistance;
            [Tooltip("Höchstens diese Gras-Dichte (0..1); 0 = wie in der Szene.")]
            public float detailDensity;
            [Tooltip("Auflösung der Sonnenschatten in Pixeln; 0 = wie in der Szene.")]
            public int sunShadowResolution;
            [Tooltip("Anzahl Schattenkaskaden der Sonne (1–4); 0 = wie im Volume.")]
            public int shadowCascades;
        }

        // Reihenfolge wie in den Projekt-Einstellungen: Low, Medium, High, Ultra.
        // Gras: Foto-Karten bauen bis 250 m dichtes Gras; im Flug reichen 70–160 m, dahinter trägt die Bodentextur.
        public Level[] levels =
        {
            new Level { cloudPrimarySteps = 32, cloudLightSteps = 4, shadowDistance = 300f, terrainPixelError = 14f, treeDistance = 800f, detailDistance = 70f, detailDensity = 0.35f, sunShadowResolution = 1024, shadowCascades = 2 },
            new Level { cloudPrimarySteps = 40, cloudLightSteps = 5, shadowDistance = 450f, terrainPixelError = 10f, treeDistance = 1100f, detailDistance = 100f, detailDensity = 0.5f, sunShadowResolution = 2048, shadowCascades = 3 },
            new Level { cloudPrimarySteps = 48, cloudLightSteps = 6, shadowDistance = 600f, terrainPixelError = 8f, treeDistance = 1500f, detailDistance = 130f, detailDensity = 0.7f, sunShadowResolution = 2048, shadowCascades = 4 },
            new Level { cloudPrimarySteps = 80, cloudLightSteps = 8, shadowDistance = 900f, terrainPixelError = 4f, treeDistance = 2200f, detailDistance = 160f, detailDensity = 1f, sunShadowResolution = 4096, shadowCascades = 4 },
        };

        [Tooltip("Alle Volumes, deren Wolken und Schatten angepasst werden (Basis und Wetter).")]
        public Component[] volumes;

        [Tooltip("Gras mit der Höhe über Grund ausblenden (sichtbarer Radius = √(Distanz² − Höhe²)).")]
        public bool fadeGrassWithHeight = true;

        // Gras-Werte der Szene, damit ein Wechsel der Stufe nie mehr Gras zeigt als gebaut wurde.
        readonly Dictionary<Terrain, Vector2> sceneDetail = new Dictionary<Terrain, Vector2>();
        int applied = -1;
        float grassDistance;      // Grasdistanz der aktuellen Stufe (vor dem Höhen-Ausblenden)
        float appliedGrass = -1f;

        void Update()
        {
            int level = QualitySettings.GetQualityLevel();
            if (level != applied)
            {
                applied = level;
                Apply(levels[Mathf.Clamp(level, 0, levels.Length - 1)]);
            }
            if (fadeGrassWithHeight) FadeGrass();
        }

        void Apply(Level level)
        {
            if (volumes != null)
            {
                foreach (Component volume in volumes)
                {
                    VolumeAccess.SetParameter(volume, "VolumetricClouds", "numPrimarySteps", level.cloudPrimarySteps);
                    VolumeAccess.SetParameter(volume, "VolumetricClouds", "numLightSteps", level.cloudLightSteps);
                    VolumeAccess.SetParameter(volume, "HDShadowSettings", "maxShadowDistance", level.shadowDistance);
                    if (level.shadowCascades > 0)
                        VolumeAccess.SetParameter(volume, "HDShadowSettings", "cascadeShadowSplitCount", Mathf.Clamp(level.shadowCascades, 1, 4));
                }
            }
            if (level.sunShadowResolution > 0) SetSunShadowResolution(level.sunShadowResolution);

            grassDistance = 0f;
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                terrain.heightmapPixelError = level.terrainPixelError;
                terrain.treeDistance = level.treeDistance;

                Vector2 built;
                if (!sceneDetail.TryGetValue(terrain, out built))
                {
                    built = new Vector2(terrain.detailObjectDistance, terrain.detailObjectDensity);
                    sceneDetail[terrain] = built;
                }
                float distance = level.detailDistance > 0f ? Mathf.Min(built.x, level.detailDistance) : built.x;
                terrain.detailObjectDistance = distance;
                terrain.detailObjectDensity = level.detailDensity > 0f ? Mathf.Min(built.y, level.detailDensity) : built.y;
                grassDistance = Mathf.Max(grassDistance, distance);
            }
            appliedGrass = -1f;
        }

        void FadeGrass()
        {
            Camera cam = Camera.main;
            if (cam == null || grassDistance <= 0f) return;
            Vector3 eye = cam.transform.position;
            float ground = 0f;
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                Vector3 origin = terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                if (eye.x < origin.x || eye.z < origin.z || eye.x > origin.x + size.x || eye.z > origin.z + size.z) continue;
                ground = Mathf.Max(ground, terrain.SampleHeight(eye) + origin.y);
            }
            float height = Mathf.Max(0f, eye.y - ground);
            float visible = height >= grassDistance ? 0f : Mathf.Sqrt(grassDistance * grassDistance - height * height);
            // In 10-m-Schritten, damit das Terrain nicht jedes Bild neu sortiert.
            visible = visible < 10f ? 0f : Mathf.Ceil(visible / 10f) * 10f;
            visible = Mathf.Min(visible, grassDistance);
            if (Mathf.Approximately(visible, appliedGrass)) return;
            if ((visible == 0f) != (appliedGrass == 0f))
                Debug.Log("Frostline: Gras " + (visible > 0f ? "an (" + visible + " m)" : "aus") + " bei " + height.ToString("0") + " m über Grund, Kamera " + cam.name);
            appliedGrass = visible;
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                Vector2 built;
                float max = sceneDetail.TryGetValue(terrain, out built) ? Mathf.Min(built.x, grassDistance) : grassDistance;
                terrain.detailObjectDistance = Mathf.Min(visible, max);
            }
        }

        /// <summary>Sonnenschatten-Auflösung über HDAdditionalLightData (per Name, ohne feste HDRP-Abhängigkeit).</summary>
        static void SetSunShadowResolution(int resolution)
        {
            foreach (Light light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional) continue;
                Component hd = light.GetComponent("HDAdditionalLightData");
                if (hd == null) continue;
                Invoke(hd, "SetShadowResolutionOverride", true);
                Invoke(hd, "SetShadowResolution", resolution);
            }
        }

        static void Invoke(Component target, string method, object argument)
        {
            foreach (MethodInfo m in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name != method) continue;
                ParameterInfo[] p = m.GetParameters();
                if (p.Length != 1 || !p[0].ParameterType.IsInstanceOfType(argument)) continue;
                try { m.Invoke(target, new[] { argument }); }
                catch (System.Exception e) { Debug.LogWarning("Frostline: " + method + " fehlgeschlagen: " + e.Message); }
                return;
            }
        }
    }
}
