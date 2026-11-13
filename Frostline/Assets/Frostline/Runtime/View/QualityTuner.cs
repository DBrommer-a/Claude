using UnityEngine;

namespace Frostline.View
{
    /// <summary>
    /// Ergänzt die Qualitätsstufen um das, was im HDRP-Asset nicht steht: Wolkenschritte
    /// (laut Plan der größte Kostentreiber), Schattendistanz und Terrain-Detail. Reagiert,
    /// wenn im Menü die Stufe gewechselt wird.
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
        }

        // Reihenfolge wie in den Projekt-Einstellungen: Low, Medium, High, Ultra.
        public Level[] levels =
        {
            new Level { cloudPrimarySteps = 32, cloudLightSteps = 4, shadowDistance = 300f, terrainPixelError = 14f, treeDistance = 800f },
            new Level { cloudPrimarySteps = 40, cloudLightSteps = 5, shadowDistance = 450f, terrainPixelError = 10f, treeDistance = 1100f },
            new Level { cloudPrimarySteps = 48, cloudLightSteps = 6, shadowDistance = 600f, terrainPixelError = 8f, treeDistance = 1500f },
            new Level { cloudPrimarySteps = 80, cloudLightSteps = 8, shadowDistance = 900f, terrainPixelError = 4f, treeDistance = 2200f },
        };

        [Tooltip("Alle Volumes, deren Wolken und Schatten angepasst werden (Basis und Wetter).")]
        public Component[] volumes;

        int applied = -1;

        void Update()
        {
            int level = QualitySettings.GetQualityLevel();
            if (level == applied) return;
            applied = level;
            Apply(levels[Mathf.Clamp(level, 0, levels.Length - 1)]);
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
                }
            }

            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                terrain.heightmapPixelError = level.terrainPixelError;
                terrain.treeDistance = level.treeDistance;
            }
        }
    }
}
