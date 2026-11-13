using System;
using Frostline.Flight;
using Frostline.Flight.Core;
using UnityEngine;

namespace Frostline.Audio
{
    /// <summary>
    /// Motorsound ohne Aufnahme: Jeder der vier Motoren wird aus seiner aktuellen Drehzahl
    /// erzeugt (Blattfrequenz plus Obertöne plus Rauschen für den Luftstrom). Weil die Motoren
    /// nie exakt gleich drehen, entsteht die typische Schwebung einer Drohne von selbst.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class MotorAudio : MonoBehaviour
    {
        public DroneController drone;
        [Range(2, 4)] public int bladeCount = 3;
        [Range(0f, 1f)] public float volume = 0.5f;

        /// <summary>Für Replays: feste Motorlast 0..1 statt der Telemetrie; negativ = aus.</summary>
        [System.NonSerialized] public float overrideLoad = -1f;

        [Tooltip("3D-Ton (für Drohnen anderer Spieler); aus = Ton im Kopfhörer (eigene Drohne).")]
        public bool spatial = false;

        /// <summary>Höchstdrehzahl, wenn keine Drohne zugeordnet ist (Mitspieler).</summary>
        public float fallbackMaxRpm = 28000f;

        const int Motors = QuadSim.MotorCount;

        // Vom Hauptthread geschrieben, vom Audio-Thread gelesen.
        readonly float[] targetFrequency = new float[Motors];
        readonly float[] targetLevel = new float[Motors];
        volatile float targetVolume;

        // Nur Audio-Thread.
        readonly float[] frequency = new float[Motors];
        readonly float[] level = new float[Motors];
        readonly double[] phase = new double[Motors];
        float sampleRate = 48000f;
        float noiseState;
        float currentVolume;
        uint noiseSeed = 0x9E3779B9u;

        void Awake()
        {
            sampleRate = AudioSettings.outputSampleRate;
            if (drone == null) drone = GetComponentInParent<DroneController>();

            AudioSource source = GetComponent<AudioSource>();
            source.clip = null;
            source.loop = true;
            source.spatialBlend = spatial ? 1f : 0f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = 3f;
            source.maxDistance = 400f;
            source.playOnAwake = true;
            source.Play();
        }

        void Update()
        {
            if (drone == null && overrideLoad < 0f) return;
            Telemetry t = drone != null ? drone.Telemetry : null;
            float maxRpm = Mathf.Max(t != null ? t.MaxRpm : fallbackMaxRpm, 1f);
            for (int i = 0; i < Motors; i++)
            {
                // Im Replay laufen die Motoren leicht unterschiedlich, damit die Schwebung bleibt.
                float rpm = overrideLoad >= 0f ? overrideLoad * maxRpm * (1f + 0.01f * i) : t.MotorRpm[i];
                targetFrequency[i] = rpm / 60f * bladeCount;
                float load = Mathf.Clamp01(rpm / maxRpm);
                targetLevel[i] = load * Mathf.Sqrt(load);
            }
            targetVolume = AudioListener.pause ? 0f : volume;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (channels <= 0) return;
            int frames = data.Length / channels;
            float smoothing = 1f - Mathf.Exp(-1f / (0.012f * sampleRate));
            const double TwoPi = Math.PI * 2.0;

            for (int n = 0; n < frames; n++)
            {
                currentVolume += (targetVolume - currentVolume) * smoothing;

                float tone = 0f;
                float wash = 0f;
                for (int i = 0; i < Motors; i++)
                {
                    frequency[i] += (targetFrequency[i] - frequency[i]) * smoothing;
                    level[i] += (targetLevel[i] - level[i]) * smoothing;

                    phase[i] += frequency[i] / sampleRate;
                    if (phase[i] >= 1.0) phase[i] -= Math.Floor(phase[i]);

                    double p = phase[i] * TwoPi;
                    float voice = (float)(Math.Sin(p) + 0.45 * Math.Sin(2.0 * p) + 0.22 * Math.Sin(3.0 * p) + 0.3 * Math.Sin(p / bladeCount));
                    tone += voice * level[i];
                    wash += level[i];
                }

                // Rauschen für den Luftstrom, leicht gefiltert, lauter bei mehr Schub.
                noiseSeed = noiseSeed * 1664525u + 1013904223u;
                float white = (noiseSeed >> 8) * (1f / 8388608f) - 1f;
                noiseState += (white - noiseState) * 0.25f;

                float sample = (tone * 0.11f + noiseState * wash * 0.06f) * currentVolume;
                // Weiche Begrenzung statt hartem Übersteuern.
                sample = sample / (1f + Mathf.Abs(sample));

                int offset = n * channels;
                for (int c = 0; c < channels; c++) data[offset + c] += sample;
            }
        }
    }
}
