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
    ///   -frostlineOhne baeume,gras,schatten,nebel,wasser,wolken   Messung zur Fehlersuche ohne diese Teile
    /// Danach beendet sich das Spiel selbst.
    /// </summary>
    public sealed class AutomationRunner : MonoBehaviour
    {
        const float SettleSeconds = 3f;

        string shotsFolder;
        bool benchmark;
        string resultPath;
        string quality;
        string without;
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
            runner.without = Argument(args, "-frostlineOhne");
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

            // Der Multiplayer-Test (-frostlineHost/-frostlineJoin) beendet das Spiel selbst.
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-frostlineHost") >= 0 || Array.IndexOf(args, "-frostlineJoin") >= 0) yield break;

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

            // Zuletzt das Modell von außen (die FPV-Kamera blendet es aus): zeigt die Nase nach vorn?
            // Am Startpunkt, dort ist das Funksignal sauber (weit draußen rauscht das Bild absichtlich).
            DroneRespawn spawn = drone.GetComponent<DroneRespawn>();
            bool hasSpawn = spawn != null && spawn.spawnPoint != null;
            Vector3 modelSpot = hasSpawn ? spawn.spawnPoint.position + Vector3.up * 4f : high;
            Quaternion modelYaw = hasSpawn ? Quaternion.Euler(0f, spawn.spawnPoint.eulerAngles.y, 0f) : Quaternion.identity;
            drone.transform.SetPositionAndRotation(modelSpot, modelYaw);
            yield return ModelShots(drone.transform, index);
            index += 2;
            yield return PropShots(drone.transform, index);
            yield return MoodShots(drone.transform, views);

            body.isKinematic = false;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            drone.enabled = true;
            DroneRespawn respawn = drone.GetComponent<DroneRespawn>();
            if (respawn != null) respawn.Respawn();
        }

        /// <summary>Jede Lichtstimmung von zwei Standpunkten: Dorf am Fluss und über dem Wolkenmeer.</summary>
        IEnumerator MoodShots(Transform drone, List<KeyValuePair<string, Pose>> views)
        {
            Frostline.View.LightingMoods moods = Frostline.View.LightingMoods.Instance;
            if (moods == null || moods.moods.Length == 0 || views.Count == 0) yield break;
            var spots = new List<KeyValuePair<string, Pose>>();
            foreach (KeyValuePair<string, Pose> v in views)
                if (v.Key.Contains("Dorf am Fluss") || v.Key.Contains("Gipfel")) spots.Add(v);
            if (spots.Count == 0) spots.Add(views[0]);
            int start = moods.Current;
            for (int m = 0; m < moods.moods.Length; m++)
            {
                moods.Select(m);
                for (int s = 0; s < spots.Count; s++)
                {
                    drone.SetPositionAndRotation(spots[s].Value.position, spots[s].Value.rotation);
                    yield return new WaitForSecondsRealtime((s == 0 ? moods.blendSeconds : 0f) + SettleSeconds);
                    yield return Capture((30 + m).ToString("00") + (char)('a' + s) + "_Stimmung_" + Safe(moods.moods[m].name));
                }
            }
            moods.Select(start);
        }

        const string ModelChild = "Modell (Higgsfield)";
        static readonly string[] PropNames = { "Leuchtturm", "Funkmast", "Gestrandeter Kutter", "Kranbahn", "Fischerhütte 1" };

        /// <summary>Ein Bild pro Higgsfield-Requisite aus der FPV-Sicht, schräg von vorn oben.</summary>
        IEnumerator PropShots(Transform drone, int index)
        {
            Camera fpv = Camera.main;
            // Die FPV-Kamera ist gegenüber der Drohne nach oben geneigt (Uptilt); das gleichen wir aus.
            Quaternion cameraOffset = fpv != null ? Quaternion.Inverse(drone.rotation) * fpv.transform.rotation : Quaternion.identity;

            // Requisiten über ihr Modell-Kind suchen: Ringe und Checkpoints tragen teils dieselben Namen.
            var props = new Dictionary<string, Transform>();
            foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Include))
                if (t.name == ModelChild && t.parent != null && !props.ContainsKey(t.parent.name)) props[t.parent.name] = t.parent;

            foreach (string name in PropNames)
            {
                Transform prop;
                if (!props.TryGetValue(name, out prop))
                {
                    Log("Requisite " + name + " nicht gefunden");
                    continue;
                }
                Frame(drone, prop, cameraOffset);
                yield return new WaitForSecondsRealtime(SettleSeconds);
                yield return Capture(index.ToString("00") + "_Requisite_" + Safe(name));
                index++;
            }

            // Industrial Set (Asset Store): Halle, Öltank und Container aus der Nähe.
            foreach (string prefix in new[] { "Hangar_", "Oil_tank", "Container" })
            {
                Transform found = null;
                foreach (Transform t in FindObjectsByType<Transform>(FindObjectsInactive.Exclude))
                {
                    if (t.name.StartsWith(prefix) && t.parent != null && t.parent.name.StartsWith("Industriegebiet"))
                    {
                        found = t;
                        break;
                    }
                }
                if (found == null) continue;
                Frame(drone, found, cameraOffset);
                yield return new WaitForSecondsRealtime(SettleSeconds);
                yield return Capture(index.ToString("00") + "_Industrie_" + Safe(prefix.TrimEnd('_')));
                index++;
            }

            // Tiefflug über der Tundra: Gras und Heide sieht man nur aus wenigen Metern Höhe.
            SignalBoundary signal = FindAnyObjectByType<SignalBoundary>();
            DroneRespawn spawn = drone.GetComponent<DroneRespawn>();
            if (signal != null && spawn != null && spawn.spawnPoint != null)
            {
                var center = new Vector3(signal.center.x, 0f, signal.center.y);
                Vector3 inland = center - spawn.spawnPoint.position;
                inland.y = 0f;
                inland.Normalize();
                RaycastHit hit;
                Vector3 probe = spawn.spawnPoint.position + inland * 260f + Vector3.up * 2000f;
                if (Physics.Raycast(probe, Vector3.down, out hit, 4000f, ~0, QueryTriggerInteraction.Ignore))
                {
                    Vector3 eye = hit.point + Vector3.up * 2.5f;
                    Vector3 target = eye + inland * 30f - Vector3.up * 3f;
                    drone.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye) * Quaternion.Inverse(cameraOffset));
                    yield return new WaitForSecondsRealtime(SettleSeconds);
                    yield return Capture(index.ToString("00") + "_Tiefflug_Tundra");
                    index++;
                }
            }

            // Renn-Gates gibt es nur bei laufendem Rennen; für das Bild die erste Strecke kurz einschalten.
            RaceTrack track = FindAnyObjectByType<RaceTrack>(FindObjectsInactive.Include);
            if (track == null) yield break;
            bool wasActive = track.gameObject.activeSelf;
            track.gameObject.SetActive(true);
            RaceGate[] gates = track.GetComponentsInChildren<RaceGate>();
            if (gates.Length > 0)
            {
                gates[0].SetState(RaceGate.State.Next);
                Frame(drone, gates[0].transform, cameraOffset);
                yield return new WaitForSecondsRealtime(SettleSeconds);
                yield return Capture(index.ToString("00") + "_Requisite_RennGate");
                gates[0].SetState(RaceGate.State.Idle);
            }
            track.gameObject.SetActive(wasActive);
        }

        static void Frame(Transform drone, Transform target, Quaternion cameraOffset)
        {
            var bounds = new Bounds(target.position, Vector3.zero);
            foreach (Renderer r in target.GetComponentsInChildren<Renderer>())
                if (r.enabled) bounds.Encapsulate(r.bounds);
            float radius = Mathf.Max(bounds.extents.magnitude, 3f);
            Vector3 direction = (Quaternion.Euler(0f, 35f, 0f) * target.forward + Vector3.up * 0.35f).normalized;
            Vector3 eye = bounds.center + direction * radius * 1.6f;
            drone.SetPositionAndRotation(eye, Quaternion.LookRotation(bounds.center - eye) * Quaternion.Inverse(cameraOffset));
        }

        /// <summary>
        /// Zwei Bilder der Drohne mit einer eigenen Kamera: schräg von vorn und genau von rechts.
        /// Im Seitenbild muss die Nase (Kamera vorn am Rahmen) rechts im Bild liegen.
        /// </summary>
        IEnumerator ModelShots(Transform drone, int index)
        {
            var go = new GameObject("Automatik-Modellkamera");
            Camera cam = go.AddComponent<Camera>();
            cam.nearClipPlane = 0.01f;
            cam.fieldOfView = 40f;
            cam.depth = 100f;
            Vector3 center = drone.position + drone.up * 0.03f;
            var shots = new[]
            {
                new KeyValuePair<string, Vector3>("Drohne_vorn_rechts", drone.forward * 0.45f + drone.right * 0.35f + drone.up * 0.2f),
                new KeyValuePair<string, Vector3>("Drohne_Seite_Nase_rechts", drone.right * 0.6f + drone.up * 0.1f),
            };
            foreach (KeyValuePair<string, Vector3> shot in shots)
            {
                go.transform.position = center + shot.Value;
                go.transform.LookAt(center, Vector3.up);
                yield return new WaitForSecondsRealtime(SettleSeconds);
                yield return Capture(index.ToString("00") + "_" + shot.Key);
                index++;
            }
            Destroy(go);
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
            // Fehlersuche: -frostlineOhne baeume,gras,schatten,nebel,wasser,wolken misst ohne diese Teile.
            yield return null;   // QualityTuner hat dann seine Werte gesetzt
            if (!string.IsNullOrEmpty(without))
            {
                foreach (Terrain terrain in Terrain.activeTerrains)
                {
                    if (without.Contains("baeume")) terrain.treeDistance = 0f;
                    if (without.Contains("gras")) { terrain.detailObjectDistance = 0f; terrain.detailObjectDensity = 0f; }
                }
                if (without.Contains("baeume"))
                    foreach (Frostline.View.TerrainTreeInstancer t in FindObjectsByType<Frostline.View.TerrainTreeInstancer>(FindObjectsSortMode.None)) t.enabled = false;
                if (without.Contains("schatten"))
                    foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                        if (l.type == LightType.Directional) l.shadows = LightShadows.None;
                foreach (Behaviour b in FindObjectsByType<Behaviour>(FindObjectsSortMode.None))
                {
                    string type = b.GetType().Name;
                    if (without.Contains("nebel") && type == "LocalVolumetricFog") b.enabled = false;
                    if (without.Contains("wasser") && type == "WaterSurface") b.enabled = false;
                    if (without.Contains("wolken") && type == "Volume")
                        Frostline.View.VolumeAccess.SetParameter(b, "VolumetricClouds", "enable", false);
                }
                Log("Messung ohne: " + without);
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
