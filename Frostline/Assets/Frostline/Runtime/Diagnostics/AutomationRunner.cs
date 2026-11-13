using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Frostline.Flight;
using Frostline.Gameplay;
using UnityEngine;

namespace Frostline.Diagnostics
{
    /// <summary>
    /// Steuerung des fertigen Spiels per Kommandozeile, damit sich Messungen und Bildkontrollen
    /// ohne Klicken wiederholen lassen (tools/mac/frostline.sh):
    ///   -frostlineShots &lt;Ordner&gt;    Screenshots von allen Checkpoints und über den Wolken
    ///   -frostlineBenchmark          fps-Messung (60 s), Ergebnis als JSON
    ///   -frostlineResult &lt;Datei&gt;     wohin das JSON geschrieben wird
    ///   -frostlineQuality &lt;Name&gt;     Qualitätsstufe, z. B. High
    /// Danach beendet sich das Spiel selbst.
    /// </summary>
    public sealed class AutomationRunner : MonoBehaviour
    {
        const float SettleSeconds = 3f;

        string shotsFolder;
        bool benchmark;
        string resultPath;
        string quality;
        readonly List<string> written = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            string[] args = Environment.GetCommandLineArgs();
            bool any = false;
            foreach (string arg in args) any |= arg.StartsWith("-frostline", StringComparison.Ordinal);
            if (!any) return;

            var go = new GameObject("Frostline Automation");
            DontDestroyOnLoad(go);
            AutomationRunner runner = go.AddComponent<AutomationRunner>();
            runner.shotsFolder = Argument(args, "-frostlineShots");
            runner.benchmark = Array.IndexOf(args, "-frostlineBenchmark") >= 0;
            runner.resultPath = Argument(args, "-frostlineResult");
            runner.quality = Argument(args, "-frostlineQuality");
        }

        static string Argument(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return null;
        }

        IEnumerator Start()
        {
            Log("Automatisierung gestartet");
            if (!string.IsNullOrEmpty(quality)) SetQuality(quality);

            // Ladebildschirm, Shader und Wolken hochfahren lassen.
            yield return new WaitForSecondsRealtime(6f);

            if (!string.IsNullOrEmpty(shotsFolder)) yield return Screenshots();
            if (benchmark) yield return Benchmark();

            Log("Automatisierung fertig");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        static void SetQuality(string name)
        {
            string[] names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    QualitySettings.SetQualityLevel(i, true);
                    Log("Qualitätsstufe " + names[i]);
                    return;
                }
            }
            Log("Qualitätsstufe „" + name + "“ unbekannt, bleibe bei " + names[QualitySettings.GetQualityLevel()]);
        }

        // ------------------------------------------------------------ Screenshots

        IEnumerator Screenshots()
        {
            Directory.CreateDirectory(shotsFolder);
            DroneController drone = FindAnyObjectByType<DroneController>();
            if (drone == null)
            {
                Log("Keine Drohne in der Szene, keine Screenshots");
                yield break;
            }

            // Zuerst die normale FPV-Sicht am Start.
            yield return new WaitForSecondsRealtime(1f);
            yield return Capture("00_Start_FPV");

            var views = new List<KeyValuePair<string, Pose>>();
            Checkpoints checkpoints = FindAnyObjectByType<Checkpoints>();
            if (checkpoints != null)
            {
                foreach (Transform point in checkpoints.transform)
                {
                    Vector3 eye = point.position + Vector3.up * 25f - point.forward * 40f;
                    Vector3 target = point.position + point.forward * 80f;
                    views.Add(new KeyValuePair<string, Pose>(point.name, new Pose(eye, Quaternion.LookRotation(target - eye))));
                }
            }
            SignalBoundary signal = FindAnyObjectByType<SignalBoundary>();
            Vector3 center = signal != null ? new Vector3(signal.center.x, 0f, signal.center.y) : Vector3.zero;
            Vector3 high = center + new Vector3(0f, 1800f, -2600f);
            views.Add(new KeyValuePair<string, Pose>("Ueber_den_Wolken", new Pose(high, Quaternion.LookRotation(center + Vector3.up * 400f - high))));

            Rigidbody body = drone.Body;
            drone.enabled = false;
            body.isKinematic = true;
            body.interpolation = RigidbodyInterpolation.None;

            int index = 1;
            foreach (KeyValuePair<string, Pose> view in views)
            {
                drone.transform.SetPositionAndRotation(view.Value.position, view.Value.rotation);
                yield return new WaitForSecondsRealtime(SettleSeconds);
                yield return Capture(index.ToString("00") + "_" + Safe(view.Key));
                index++;
            }

            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            drone.enabled = true;
            DroneRespawn respawn = drone.GetComponent<DroneRespawn>();
            if (respawn != null) respawn.Respawn();
        }

        IEnumerator Capture(string name)
        {
            string path = Path.Combine(shotsFolder, name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            yield return new WaitForEndOfFrame();
            yield return new WaitForSecondsRealtime(0.5f);
            written.Add(path);
            Log("Screenshot " + path + "  (" + (1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f)).ToString("0") + " fps)");
        }

        static string Safe(string name)
        {
            var builder = new StringBuilder();
            foreach (char c in name) builder.Append(char.IsLetterOrDigit(c) ? c : '_');
            return builder.ToString();
        }

        // ------------------------------------------------------------ Benchmark

        IEnumerator Benchmark()
        {
            BenchmarkRunner runner = FindAnyObjectByType<BenchmarkRunner>();
            if (runner == null)
            {
                Log("Kein Benchmark in der Szene");
                yield break;
            }
            runner.Begin();
            while (runner.Running) yield return null;

            BenchmarkResult r = runner.LastResult;
            if (r == null) yield break;
            Log("Benchmark: " + r.RatingText + " | Schnitt " + r.AverageFps.ToString("0.0", CultureInfo.InvariantCulture)
                + " fps | 1%-Tief " + r.OnePercentLowFps.ToString("0.0", CultureInfo.InvariantCulture) + " fps");

            if (string.IsNullOrEmpty(resultPath)) yield break;
            CultureInfo inv = CultureInfo.InvariantCulture;
            string json = "{\n"
                + "  \"rating\": \"" + r.Rating + "\",\n"
                + "  \"averageFps\": " + r.AverageFps.ToString("0.00", inv) + ",\n"
                + "  \"onePercentLowFps\": " + r.OnePercentLowFps.ToString("0.00", inv) + ",\n"
                + "  \"medianFrameMs\": " + r.MedianFrameMs.ToString("0.000", inv) + ",\n"
                + "  \"p99FrameMs\": " + r.P99FrameMs.ToString("0.000", inv) + ",\n"
                + "  \"worstFrameMs\": " + r.WorstFrameMs.ToString("0.000", inv) + ",\n"
                + "  \"frames\": " + r.FrameCount + ",\n"
                + "  \"quality\": \"" + r.QualityName + "\",\n"
                + "  \"resolution\": \"" + r.Resolution + "\",\n"
                + "  \"device\": \"" + SystemInfo.deviceModel + "\",\n"
                + "  \"gpu\": \"" + SystemInfo.graphicsDeviceName + "\",\n"
                + "  \"report\": \"" + (r.ReportPath ?? "").Replace("\\", "/") + "\"\n"
                + "}\n";
            string folder = Path.GetDirectoryName(resultPath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllText(resultPath, json);
            Log("Ergebnis geschrieben: " + resultPath);
        }

        static void Log(string message)
        {
            // Eindeutiges Präfix, damit Skripte die Zeilen im Player-Log finden.
            Debug.Log("FROSTLINE_AUTOMATION " + message);
        }
    }
}
