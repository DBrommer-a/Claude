using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Frostline.Flight;
using UnityEngine;

namespace Frostline.Diagnostics
{
    /// <summary>Ergebnis einer fps-Messung.</summary>
    public sealed class BenchmarkResult
    {
        public float AverageFps;
        public float OnePercentLowFps;
        public float WorstFrameMs;
        public float MedianFrameMs;
        public float P95FrameMs;
        public float P99FrameMs;
        public int FrameCount;
        public float Seconds;
        public float TargetFps;
        public bool InEditor;
        public string QualityName;
        public string Resolution;
        public string ReportPath;

        public enum Verdict { Passed, Borderline, Failed }

        public Verdict Rating
        {
            get
            {
                if (OnePercentLowFps >= TargetFps) return Verdict.Passed;
                return AverageFps >= TargetFps ? Verdict.Borderline : Verdict.Failed;
            }
        }

        public string RatingText
        {
            get
            {
                switch (Rating)
                {
                    case Verdict.Passed: return "BESTANDEN – stabil über " + TargetFps.ToString("0") + " fps";
                    case Verdict.Borderline: return "KNAPP – Schnitt reicht, aber es gibt Einbrüche";
                    default: return "NICHT ERREICHT – Schnitt unter " + TargetFps.ToString("0") + " fps";
                }
            }
        }
    }

    /// <summary>
    /// Misst die Bildrate auf einem festen Kameraflug über Meer, Insel und durch die Wolken.
    /// Immer derselbe Weg, damit Messungen vergleichbar sind. Start mit F9.
    /// Zählt nur im fertigen Build; der Editor ist auf demselben Rechner deutlich langsamer.
    /// </summary>
    public sealed class BenchmarkRunner : MonoBehaviour
    {
        public DroneController drone;

        [Header("Messung")]
        public float durationSeconds = 60f;
        public float warmupSeconds = 3f;
        public float targetFps = 60f;

        [Header("Flugweg")]
        public Vector3 center = new Vector3(1000f, 0f, 1000f);
        public float radius = 700f;
        public float lowAltitude = 60f;
        [Tooltip("Oberster Punkt des Flugs. In die Wolkenschicht legen.")]
        public float highAltitude = 800f;
        [Tooltip("Mindestabstand zum Boden, damit der Flug nicht durch Berge führt.")]
        public float minimumClearance = 40f;

        readonly List<float> frameMs = new List<float>(8192);
        float elapsed;
        int savedVSync;
        int savedTargetFrameRate;
        bool savedKinematic;
        RigidbodyInterpolation savedInterpolation;

        public bool Running { get; private set; }
        public float Progress { get { return Running ? Mathf.Clamp01(elapsed / (durationSeconds + warmupSeconds)) : 0f; } }
        public BenchmarkResult LastResult { get; private set; }

        void Awake()
        {
            if (drone == null) drone = FindAnyObjectByType<DroneController>();
        }

        public void Begin()
        {
            if (Running || drone == null) return;
            Running = true;
            elapsed = 0f;
            frameMs.Clear();
            LastResult = null;

            // Ohne Begrenzung messen, sonst deckelt der Bildschirm das Ergebnis.
            savedVSync = QualitySettings.vSyncCount;
            savedTargetFrameRate = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;

            Rigidbody body = drone.Body;
            savedKinematic = body.isKinematic;
            savedInterpolation = body.interpolation;
            drone.enabled = false;
            body.interpolation = RigidbodyInterpolation.None;
            body.isKinematic = true;
        }

        public void Abort()
        {
            if (!Running) return;
            Restore();
        }

        void Update()
        {
            if (!Running) return;

            float dt = Time.unscaledDeltaTime;
            elapsed += dt;
            float total = durationSeconds + warmupSeconds;

            Vector3 position, forward;
            EvaluatePath(Mathf.Clamp01(elapsed / total), out position, out forward);
            drone.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, Vector3.up));

            if (elapsed > warmupSeconds) frameMs.Add(dt * 1000f);
            if (elapsed >= total) Finish();
        }

        /// <summary>Eine Runde um die Inselmitte, dabei einmal hoch in die Wolken und wieder herunter.</summary>
        void EvaluatePath(float t, out Vector3 position, out Vector3 forward)
        {
            position = PathPoint(t);
            Vector3 ahead = PathPoint(Mathf.Min(t + 0.004f, 1f)) - PathPoint(Mathf.Max(t - 0.004f, 0f));
            forward = ahead.sqrMagnitude > 1e-6f ? ahead.normalized : Vector3.forward;
        }

        Vector3 PathPoint(float t)
        {
            float angle = t * Mathf.PI * 2f;
            float r = radius * (0.75f + 0.25f * Mathf.Cos(angle * 3f));
            float height = Mathf.Lerp(lowAltitude, highAltitude, 0.5f - 0.5f * Mathf.Cos(angle));
            Vector3 point = center + new Vector3(Mathf.Cos(angle) * r, height, Mathf.Sin(angle) * r);
            point.y = Mathf.Max(point.y, GroundHeight(point) + minimumClearance);
            return point;
        }

        static float GroundHeight(Vector3 point)
        {
            float ground = 0f; // Meeresspiegel
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                Vector3 origin = terrain.transform.position;
                Vector3 size = terrain.terrainData.size;
                bool inside = point.x >= origin.x && point.x <= origin.x + size.x && point.z >= origin.z && point.z <= origin.z + size.z;
                if (inside) ground = Mathf.Max(ground, terrain.SampleHeight(point) + origin.y);
            }
            return ground;
        }

        void Finish()
        {
            BenchmarkResult result = Analyze();
            Restore();
            LastResult = result;
            try
            {
                result.ReportPath = WriteReport(result);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Frostline: Benchmark-Bericht konnte nicht gespeichert werden. " + e.Message);
            }
            Debug.Log("Frostline Benchmark: " + result.RatingText + " | Schnitt " + result.AverageFps.ToString("0.0")
                + " fps, 1%-Tief " + result.OnePercentLowFps.ToString("0.0") + " fps");
        }

        void Restore()
        {
            Running = false;
            QualitySettings.vSyncCount = savedVSync;
            Application.targetFrameRate = savedTargetFrameRate;

            Rigidbody body = drone.Body;
            body.isKinematic = savedKinematic;
            body.interpolation = savedInterpolation;
            drone.enabled = true;

            DroneRespawn respawn = drone.GetComponent<DroneRespawn>();
            if (respawn != null) respawn.Respawn();
        }

        BenchmarkResult Analyze()
        {
            var result = new BenchmarkResult();
            result.TargetFps = targetFps;
            result.InEditor = Application.isEditor;
            result.QualityName = QualitySettings.names.Length > 0 ? QualitySettings.names[QualitySettings.GetQualityLevel()] : "?";
            result.Resolution = Screen.width + " x " + Screen.height;
            result.FrameCount = frameMs.Count;
            if (frameMs.Count == 0) return result;

            double sum = 0.0;
            for (int i = 0; i < frameMs.Count; i++) sum += frameMs[i];
            result.Seconds = (float)(sum / 1000.0);
            result.AverageFps = (float)(frameMs.Count / (sum / 1000.0));

            var sorted = new List<float>(frameMs);
            sorted.Sort();
            result.MedianFrameMs = Percentile(sorted, 0.50f);
            result.P95FrameMs = Percentile(sorted, 0.95f);
            result.P99FrameMs = Percentile(sorted, 0.99f);
            result.WorstFrameMs = sorted[sorted.Count - 1];

            // 1%-Tief: Bildrate der langsamsten 1 % aller Bilder.
            int worstCount = Mathf.Max(1, sorted.Count / 100);
            double worstSum = 0.0;
            for (int i = sorted.Count - worstCount; i < sorted.Count; i++) worstSum += sorted[i];
            result.OnePercentLowFps = (float)(1000.0 / (worstSum / worstCount));
            return result;
        }

        static float Percentile(List<float> sorted, float fraction)
        {
            int index = Mathf.Clamp(Mathf.RoundToInt(fraction * (sorted.Count - 1)), 0, sorted.Count - 1);
            return sorted[index];
        }

        string WriteReport(BenchmarkResult r)
        {
            CultureInfo inv = CultureInfo.InvariantCulture;
            string folder = Path.Combine(Application.persistentDataPath, "Benchmarks");
            Directory.CreateDirectory(folder);
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", inv);
            string reportPath = Path.Combine(folder, "benchmark_" + stamp + ".txt");

            var text = new StringBuilder();
            text.AppendLine("Frostline Benchmark " + DateTime.Now.ToString("yyyy-MM-dd HH:mm", inv));
            text.AppendLine("Ergebnis: " + r.RatingText);
            if (r.InEditor) text.AppendLine("ACHTUNG: im Editor gemessen. Für das Gate zählt nur die Messung im Build.");
            text.AppendLine();
            text.AppendLine("Schnitt:        " + r.AverageFps.ToString("0.0", inv) + " fps");
            text.AppendLine("1%-Tief:        " + r.OnePercentLowFps.ToString("0.0", inv) + " fps");
            text.AppendLine("Median:         " + r.MedianFrameMs.ToString("0.00", inv) + " ms");
            text.AppendLine("95. Perzentil:  " + r.P95FrameMs.ToString("0.00", inv) + " ms");
            text.AppendLine("99. Perzentil:  " + r.P99FrameMs.ToString("0.00", inv) + " ms");
            text.AppendLine("Längstes Bild:  " + r.WorstFrameMs.ToString("0.00", inv) + " ms");
            text.AppendLine("Bilder:         " + r.FrameCount + " in " + r.Seconds.ToString("0.0", inv) + " s");
            text.AppendLine();
            text.AppendLine("Qualitätsstufe: " + r.QualityName);
            text.AppendLine("Auflösung:      " + r.Resolution);
            text.AppendLine("Gerät:          " + SystemInfo.deviceModel);
            text.AppendLine("Prozessor:      " + SystemInfo.processorType + " (" + SystemInfo.processorCount + " Kerne)");
            text.AppendLine("Grafik:         " + SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType);
            text.AppendLine("Speicher:       " + SystemInfo.systemMemorySize + " MB");
            text.AppendLine("Betriebssystem: " + SystemInfo.operatingSystem);
            text.AppendLine("Unity:          " + Application.unityVersion);
            File.WriteAllText(reportPath, text.ToString());

            var csv = new StringBuilder("frame,ms\n");
            for (int i = 0; i < frameMs.Count; i++)
            {
                csv.Append(i).Append(',').Append(frameMs[i].ToString("0.000", inv)).Append('\n');
            }
            File.WriteAllText(Path.Combine(folder, "benchmark_" + stamp + "_frames.csv"), csv.ToString());
            return reportPath;
        }
    }
}
