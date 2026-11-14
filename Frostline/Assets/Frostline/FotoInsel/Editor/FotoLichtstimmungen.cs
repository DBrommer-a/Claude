using System.Collections.Generic;
using Frostline.View;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

namespace Frostline.FotoInsel
{
    /// <summary>
    /// Lichtstimmungen für die Verlassene Insel: Sonnenaufgang, Morgen, Mittag, Nachmittag, Goldene
    /// Stunde (der Foto-Look) und Sonnenuntergang. Für jede wird der Himmel physikalisch vorberechnet
    /// (FotoHimmel), dazu Sonnenfarbe und -stärke nach dem Weg durch die Atmosphäre und eine passende
    /// Belichtung. Im Spiel schaltet <see cref="LightingMoods"/> zwischen ihnen um.
    /// </summary>
    public static class FotoLichtstimmungen
    {
        public const string RootName = "Lichtstimmungen";
        const float SunLux = 128000f;               // außerhalb der Atmosphäre, wie FotoAtmosphaere
        const float GoldenExposure = 12.6f;          // Belichtung des Foto-Looks (Goldene Stunde)
        const int SkySize = 256;

        struct Definition
        {
            public string Name;
            public float Hour, Elevation, Yaw;
            public bool PhotoLook;
        }

        // Sonnenstand: Höhe über dem Horizont und Richtung, in die das Licht fällt (Yaw 0 = nach Norden,
        // die Sonne steht dann im Süden). Der Foto-Look ist die Goldene Stunde (10°, Licht aus Westen).
        static readonly Definition[] Definitions =
        {
            new Definition { Name = "Sonnenaufgang", Hour = 5.5f, Elevation = 3f, Yaw = 225f },
            new Definition { Name = "Morgen", Hour = 8f, Elevation = 16f, Yaw = 290f },
            new Definition { Name = "Mittag", Hour = 13f, Elevation = 42f, Yaw = 0f },
            new Definition { Name = "Nachmittag", Hour = 16f, Elevation = 28f, Yaw = 45f },
            new Definition { Name = "Goldene Stunde", Hour = 19f, Elevation = 10f, Yaw = 80f, PhotoLook = true },
            new Definition { Name = "Sonnenuntergang", Hour = 20.5f, Elevation = 1.5f, Yaw = 105f },
        };

        public static float DefaultHour { get { return 19f; } }

        /// <summary>Baut die Stimmungen in die offene Szene (vorherige werden ersetzt).</summary>
        public static string Add(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == RootName) Object.DestroyImmediate(root);

            Light sun = null;
            Volume baseVolume = null, lowVolume = null;
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude))
                if (l.type == LightType.Directional && l.gameObject.name.StartsWith("Sonne")) sun = l;
            foreach (Volume v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Exclude))
            {
                if (v.gameObject.name.StartsWith("Atmosphäre")) baseVolume = v;
                if (v.gameObject.name.StartsWith("Wolken in Bodennähe")) lowVolume = v;
            }
            if (sun == null || baseVolume == null || baseVolume.sharedProfile == null)
                return "Lichtstimmungen: Sonne oder Atmosphäre fehlt – übersprungen.\n";

            Texture photoSky = null;
            HDRISky hdri;
            if (baseVolume.sharedProfile.TryGet(out hdri)) photoSky = hdri.hdriSky.value;

            string folder = FotoInselBuilder.GeneratedFolder + "/Verlassen";
            float goldenLight = Illuminance(Find("Goldene Stunde"));
            var moods = new List<LightingMoods.Mood>();
            foreach (Definition d in Definitions)
            {
                Quaternion rotation = Quaternion.Euler(d.Elevation, d.Yaw, 0f);
                Vector3 toSun = -(rotation * Vector3.forward);
                Vector3 t = FotoHimmel.SunColorAtGround(toSun);
                float max = Mathf.Max(t.x, Mathf.Max(t.y, t.z), 1e-5f);

                Texture sky = d.PhotoLook ? photoSky : null;
                if (sky == null)
                    sky = FotoHimmel.Create(folder + "/Himmel_Stimmung_" + d.Name.Replace(' ', '_') + ".asset", toSun, SkySize, true);

                // Belichtung folgt der Helligkeit am Boden, etwas gedämpft: Morgens und abends bleibt es stimmungsvoll dunkler.
                float exposure = GoldenExposure + 0.8f * Mathf.Log(Illuminance(d) / goldenLight, 2f);
                moods.Add(new LightingMoods.Mood
                {
                    name = d.Name,
                    hour = d.Hour,
                    sunEuler = rotation.eulerAngles,
                    // Teilweiser Weißabgleich wie bei einer Kamera auf Tageslicht (wie FotoAtmosphaere.CreateSun).
                    sunColor = Color.Lerp(new Color(t.x / max, t.y / max, t.z / max), Color.white, 0.45f),
                    sunLux = SunLux * (0.2126f * t.x + 0.7152f * t.y + 0.0722f * t.z),
                    sky = sky,
                    exposure = Mathf.Clamp(exposure, 9.5f, 15f),
                });
            }

            var go = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(go, scene);
            var component = go.AddComponent<LightingMoods>();
            component.sun = sun;
            component.baseVolume = baseVolume;
            component.lowVolume = lowVolume;
            component.moods = moods.ToArray();

            // Start im Foto-Look; die Uhrzeit des Wettersystems wählt die Stimmung (und läuft im Multiplayer mit).
            foreach (WeatherSystem w in Object.FindObjectsByType<WeatherSystem>(FindObjectsInactive.Include))
                w.timeOfDay = DefaultHour;

            AssetDatabase.SaveAssets();
            var names = new List<string>();
            foreach (LightingMoods.Mood m in moods) names.Add(m.name + " (EV " + m.exposure.ToString("0.0") + ")");
            return "Lichtstimmungen: " + string.Join(", ", names) + "\n";
        }

        static Definition Find(string name)
        {
            foreach (Definition d in Definitions)
                if (d.Name == name) return d;
            return Definitions[0];
        }

        /// <summary>Beleuchtungsstärke am Boden: direkte Sonne auf die Fläche plus Himmel.</summary>
        static float Illuminance(Definition d)
        {
            Quaternion rotation = Quaternion.Euler(d.Elevation, d.Yaw, 0f);
            Vector3 toSun = -(rotation * Vector3.forward);
            Vector3 t = FotoHimmel.SunColorAtGround(toSun);
            float sunLux = SunLux * (0.2126f * t.x + 0.7152f * t.y + 0.0722f * t.z);
            return Mathf.Max(1f, sunLux * Mathf.Max(0f, toSun.y) + FotoHimmel.HemisphereLux(toSun));
        }
    }
}
