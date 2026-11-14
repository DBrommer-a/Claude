using System;
using UnityEngine;

namespace Frostline.View
{
    /// <summary>
    /// Lichtstimmungen für Karten mit vorberechnetem Himmel (Verlassene Insel): Sonnenaufgang bis
    /// Sonnenuntergang. Jede Stimmung hat ihren eigenen Himmel (HDRI), Sonnenstand, Sonnenfarbe und
    /// Belichtung, im Editor physikalisch vorberechnet. Gewählt wird über die Uhrzeit des Wettersystems:
    /// Die gilt im Multiplayer für alle (der Host bestimmt sie), deshalb sehen alle dasselbe Licht.
    /// Wechsel werden über einige Sekunden überblendet.
    /// </summary>
    public sealed class LightingMoods : MonoBehaviour
    {
        [Serializable]
        public struct Mood
        {
            public string name;
            [Tooltip("Uhrzeit, für die diese Stimmung steht (h). Die nächstliegende Stimmung gewinnt.")]
            public float hour;
            public Vector3 sunEuler;
            public Color sunColor;
            [Tooltip("Sonne in Lux (Einheit des Lichts).")]
            public float sunLux;
            public Texture sky;
            [Tooltip("Feste Belichtung (EV100) im Basis-Volume.")]
            public float exposure;
        }

        public Light sun;
        [Tooltip("Globales Volume mit Himmel (HDRISky) und Belichtung.")]
        public Component baseVolume;
        [Tooltip("Lokales Volume in Bodennähe mit eigener Belichtung (optional).")]
        public Component lowVolume;
        [Tooltip("Belichtung in Bodennähe relativ zur Basis.")]
        public float lowExposureOffset = -0.9f;
        public float blendSeconds = 2.5f;
        public Mood[] moods = new Mood[0];

        public static LightingMoods Instance { get; private set; }
        public int Current { get; private set; } = -1;

        int from = -1;
        float blend = 1f;
        bool skySwapped = true;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            int start = Nearest(CurrentHour());
            if (start >= 0)
            {
                Current = start;
                from = start;
                blend = 1f;
                ApplyLight(moods[start], moods[start], 1f);
                ApplySky(moods[start]);
            }
        }

        /// <summary>Stimmung wählen. Läuft über die Uhrzeit, damit der Multiplayer sie an alle verteilt.</summary>
        public void Select(int index)
        {
            if (index < 0 || index >= moods.Length) return;
            WeatherSystem weather = WeatherSystem.Instance;
            if (weather != null)
            {
                if (weather.lockedByHost) return;
                WorldState state = weather.State;
                state.TimeOfDay = moods[index].hour;
                weather.SetLocal(state);
            }
            else
            {
                Begin(index);
            }
        }

        public void Next()
        {
            if (moods.Length > 0) Select((Mathf.Max(Current, 0) + 1) % moods.Length);
        }

        public string CurrentName { get { return Current >= 0 && Current < moods.Length ? moods[Current].name : ""; } }

        float CurrentHour()
        {
            WeatherSystem weather = WeatherSystem.Instance;
            return weather != null ? weather.timeOfDay : (moods.Length > 0 ? moods[moods.Length - 1].hour : 12f);
        }

        int Nearest(float hour)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < moods.Length; i++)
            {
                float d = Mathf.Abs(moods[i].hour - hour);
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
            return best;
        }

        void Begin(int index)
        {
            if (index == Current) return;
            from = Current >= 0 ? Current : index;
            Current = index;
            blend = 0f;
            skySwapped = false;
        }

        void Update()
        {
            if (moods.Length == 0) return;
            int wanted = Nearest(CurrentHour());
            if (wanted >= 0 && wanted != Current) Begin(wanted);
            if (blend >= 1f) return;

            blend = Mathf.Min(1f, blend + Time.unscaledDeltaTime / Mathf.Max(0.01f, blendSeconds));
            float t = Mathf.SmoothStep(0f, 1f, blend);
            ApplyLight(moods[from], moods[Current], t);
            // Der Himmel lässt sich nicht überblenden; er wechselt in der Mitte, wenn das Licht halb da ist.
            if (!skySwapped && t >= 0.5f)
            {
                ApplySky(moods[Current]);
                skySwapped = true;
            }
        }

        void ApplyLight(Mood a, Mood b, float t)
        {
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Slerp(Quaternion.Euler(a.sunEuler), Quaternion.Euler(b.sunEuler), t);
                sun.color = Color.Lerp(a.sunColor, b.sunColor, t);
                sun.intensity = Mathf.Lerp(a.sunLux, b.sunLux, t);
            }
            float exposure = Mathf.Lerp(a.exposure, b.exposure, t);
            VolumeAccess.SetParameter(baseVolume, "Exposure", "fixedExposure", exposure);
            if (lowVolume != null) VolumeAccess.SetParameter(lowVolume, "Exposure", "fixedExposure", exposure + lowExposureOffset);
        }

        void ApplySky(Mood mood)
        {
            if (mood.sky != null) VolumeAccess.SetParameter(baseVolume, "HDRISky", "hdriSky", mood.sky);
        }
    }
}
