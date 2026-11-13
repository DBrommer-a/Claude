using System;
using Frostline.Flight;
using UnityEngine;

namespace Frostline.View
{
    public enum Weather
    {
        Clear = 0,
        LowClouds = 1,
        Snowstorm = 2,
    }

    /// <summary>Was im Multiplayer vom Host an alle geht, damit alle dieselbe Welt sehen.</summary>
    [Serializable]
    public struct WorldState : IEquatable<WorldState>
    {
        public Weather Weather;
        /// <summary>Uhrzeit in Stunden, 0..24.</summary>
        public float TimeOfDay;
        public int CloudSeed;

        public bool Equals(WorldState other)
        {
            return Weather == other.Weather && Mathf.Approximately(TimeOfDay, other.TimeOfDay) && CloudSeed == other.CloudSeed;
        }
    }

    /// <summary>
    /// Wetter und Tageszeit: Sonnenstand über Physically Based Sky, drei Wetterlagen als
    /// überblendete Volumes (klar, tiefe Wolken, Schneesturm), Schneefall um die Kamera,
    /// böiger Wind auf die Drohne und Windgeräusch nach Höhe und Wetter.
    /// </summary>
    public sealed class WeatherSystem : MonoBehaviour
    {
        public Light sun;
        public DroneController drone;
        [Tooltip("Volume für tiefe Wolken (wird überblendet).")]
        public Component lowCloudsVolume;
        [Tooltip("Volume für den Schneesturm (wird überblendet).")]
        public Component stormVolume;
        [Tooltip("Basis-Volume mit den normalen Wolken; erhält den Wolken-Seed.")]
        public Component baseVolume;
        public ParticleSystem snow;
        public AudioSource windAudio;

        [Header("Zustand")]
        public Weather weather = Weather.Clear;
        [Range(5f, 21f)] public float timeOfDay = 10f;
        public int cloudSeed = 1;
        [Tooltip("Spielminuten pro Echtsekunde; 0 = Uhrzeit steht.")]
        public float timeScale = 0f;

        [Header("Sonne")]
        [Tooltip("Höchster Sonnenstand im Jahresmittel auf etwa 64° Nord.")]
        public float maxElevation = 38f;
        public float sunrise = 5.5f;
        public float sunset = 20.5f;

        [Header("Wind (m/s)")]
        public float clearWind = 2f;
        public float lowCloudsWind = 6f;
        public float stormWind = 14f;

        float lowCloudsWeight;
        float stormWeight;
        int appliedSeed = int.MinValue;
        float windAngle;

        public static WeatherSystem Instance { get; private set; }

        public WorldState State
        {
            get { return new WorldState { Weather = weather, TimeOfDay = timeOfDay, CloudSeed = cloudSeed }; }
        }

        public event Action<WorldState> StateChangedLocally;

        void Awake()
        {
            Instance = this;
            if (drone == null) drone = FindFirstObjectByType<DroneController>();
            lowCloudsWeight = weather == Weather.LowClouds ? 1f : 0f;
            stormWeight = weather == Weather.Snowstorm ? 1f : 0f;
            windAngle = UnityEngine.Random.Range(0f, 360f);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Vom Spieler geändert (Menü): lokal anwenden und dem Multiplayer melden.</summary>
        public void SetLocal(WorldState state)
        {
            Apply(state);
            StateChangedLocally?.Invoke(State);
        }

        /// <summary>Vom Host empfangen oder lokal gesetzt.</summary>
        public void Apply(WorldState state)
        {
            weather = state.Weather;
            timeOfDay = Mathf.Clamp(state.TimeOfDay, 0f, 24f);
            cloudSeed = state.CloudSeed;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (timeScale > 0f) timeOfDay = Mathf.Repeat(timeOfDay + timeScale * dt / 60f, 24f);

            UpdateSun();
            UpdateBlend(dt);
            UpdateCloudSeed();
            UpdateWind(dt);
            UpdateSnow();
            UpdateWindAudio();
        }

        void UpdateSun()
        {
            if (sun == null) return;
            float day = Mathf.InverseLerp(sunrise, sunset, timeOfDay);
            float elevation = Mathf.Sin(day * Mathf.PI) * maxElevation;
            if (day <= 0f || day >= 1f) elevation = -8f;
            // Aufgang im Nordosten, Mittag im Süden, Untergang im Nordwesten (Sommer im hohen Norden).
            float azimuth = Mathf.Lerp(50f, 310f, day);
            sun.transform.rotation = Quaternion.Euler(Mathf.Max(elevation, -8f), azimuth + 180f, 0f);
        }

        void UpdateBlend(float dt)
        {
            float speed = dt / 6f; // sechs Sekunden Überblendung
            lowCloudsWeight = Mathf.MoveTowards(lowCloudsWeight, weather == Weather.LowClouds ? 1f : 0f, speed);
            stormWeight = Mathf.MoveTowards(stormWeight, weather == Weather.Snowstorm ? 1f : 0f, speed);
            VolumeAccess.SetWeight(lowCloudsVolume, lowCloudsWeight);
            VolumeAccess.SetWeight(stormVolume, stormWeight);
        }

        void UpdateCloudSeed()
        {
            if (cloudSeed == appliedSeed) return;
            appliedSeed = cloudSeed;
            var random = new System.Random(cloudSeed);
            var offset = new Vector3((float)random.NextDouble() * 1000f, 0f, (float)random.NextDouble() * 1000f);
            VolumeAccess.SetParameter(baseVolume, "VolumetricClouds", "shapeOffset", offset);
            VolumeAccess.SetParameter(lowCloudsVolume, "VolumetricClouds", "shapeOffset", offset);
            VolumeAccess.SetParameter(stormVolume, "VolumetricClouds", "shapeOffset", offset);
        }

        float BaseWind
        {
            get
            {
                float clear = 1f - Mathf.Max(lowCloudsWeight, stormWeight);
                return clear * clearWind + lowCloudsWeight * lowCloudsWind + stormWeight * stormWind;
            }
        }

        void UpdateWind(float dt)
        {
            if (drone == null) return;
            windAngle += (Mathf.PerlinNoise(Time.time * 0.01f, 3.1f) - 0.5f) * 10f * dt;
            float gust = 0.7f + 0.6f * Mathf.PerlinNoise(Time.time * 0.35f, 7.7f);
            // Mehr Wind in der Höhe.
            float altitude = Mathf.Clamp01(drone.transform.position.y / 800f);
            float speed = BaseWind * gust * (0.6f + 0.8f * altitude);
            Vector3 direction = Quaternion.Euler(0f, windAngle, 0f) * Vector3.forward;
            float updraft = (Mathf.PerlinNoise(Time.time * 0.5f, 1.3f) - 0.5f) * speed * 0.2f;
            drone.wind = direction * speed + Vector3.up * updraft;
        }

        void UpdateSnow()
        {
            if (snow == null) return;
            Camera cam = Camera.main;
            if (cam != null) snow.transform.position = cam.transform.position + Vector3.up * 8f;
            ParticleSystem.EmissionModule emission = snow.emission;
            emission.rateOverTime = lowCloudsWeight * 400f + stormWeight * 6000f;
            ParticleSystem.VelocityOverLifetimeModule velocity = snow.velocityOverLifetime;
            Vector3 wind = drone != null ? drone.wind : Vector3.zero;
            velocity.x = wind.x;
            velocity.z = wind.z;
        }

        void UpdateWindAudio()
        {
            if (windAudio == null || drone == null) return;
            float altitude = Mathf.Clamp01(drone.transform.position.y / 600f);
            float target = 0.08f + 0.25f * altitude + 0.5f * stormWeight + 0.15f * lowCloudsWeight;
            windAudio.volume = Mathf.MoveTowards(windAudio.volume, target, Time.deltaTime * 0.3f);
        }

        public static string WeatherName(Weather w)
        {
            switch (w)
            {
                case Weather.LowClouds: return "Tiefe Wolken";
                case Weather.Snowstorm: return "Schneesturm";
                default: return "Klar";
            }
        }
    }
}
