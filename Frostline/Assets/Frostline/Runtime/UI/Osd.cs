using Frostline.Controls;
using Frostline.Diagnostics;
using Frostline.Flight;
using Frostline.Flight.Core;
using UnityEngine;

namespace Frostline.UI
{
    /// <summary>
    /// Einblendung wie in einer FPV-Brille: Akku, Gas, Tempo, Höhe, Modus und Warnungen.
    /// Bewusst schlicht gehalten; das richtige HUD kommt mit dem Look in einer späteren Phase.
    /// </summary>
    public sealed class Osd : MonoBehaviour
    {
        public DroneController drone;
        public StickInput input;
        public BenchmarkRunner benchmark;
        public bool visible = true;

        GUIStyle label;
        GUIStyle centered;
        GUIStyle big;
        float smoothedFrameTime = 1f / 60f;
        float startAltitude;

        void Awake()
        {
            if (drone == null) drone = FindFirstObjectByType<DroneController>();
            if (input == null) input = FindFirstObjectByType<StickInput>();
            if (benchmark == null) benchmark = FindFirstObjectByType<BenchmarkRunner>();
        }

        void Start()
        {
            if (drone != null) startAltitude = drone.transform.position.y;
        }

        void Update()
        {
            smoothedFrameTime = Mathf.Lerp(smoothedFrameTime, Time.unscaledDeltaTime, 0.05f);
        }

        void EnsureStyles()
        {
            if (label != null) return;
            label = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold };
            label.normal.textColor = Color.white;
            centered = new GUIStyle(label) { alignment = TextAnchor.MiddleCenter };
            big = new GUIStyle(centered) { fontSize = 28 };
        }

        void OnGUI()
        {
            if (drone == null) return;
            EnsureStyles();

            float scale = Screen.height / 720f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;
            float h = Screen.height / scale;

            if (benchmark != null && benchmark.Running)
            {
                Shadowed(new Rect(0f, 20f, w, 30f), "BENCHMARK  " + (benchmark.Progress * 100f).ToString("0") + " %   "
                    + (1f / smoothedFrameTime).ToString("0") + " fps", centered);
                return;
            }

            if (benchmark != null && benchmark.LastResult != null)
            {
                DrawResult(benchmark.LastResult, w);
            }

            if (!visible) return;
            Telemetry t = drone.Telemetry;

            string mode = t.Mode == FlightMode.Acro ? "ACRO" : (t.Mode == FlightMode.Angle ? "ANGLE" : "HORIZON");
            Shadowed(new Rect(20f, 16f, 300f, 26f), mode + (t.Armed ? "  ARMED" : "  DISARMED"), label);
            Shadowed(new Rect(0f, 16f, w, 26f), (1f / smoothedFrameTime).ToString("0") + " fps", centered);

            Color batteryColor = t.CellVoltage < 3.5f ? new Color(1f, 0.35f, 0.3f) : Color.white;
            Shadowed(new Rect(w - 260f, 16f, 240f, 26f),
                t.BatteryVoltage.ToString("0.0") + " V  (" + t.CellVoltage.ToString("0.00") + ")", label, batteryColor);
            Shadowed(new Rect(w - 260f, 42f, 240f, 26f),
                t.UsedMah.ToString("0") + " mAh   " + t.CurrentAmps.ToString("0") + " A", label);

            float throttle = drone.LastInput.Throttle * 100f;
            float speed = drone.SpeedMetersPerSecond * 3.6f;
            float altitude = drone.transform.position.y - startAltitude;
            Shadowed(new Rect(20f, h - 66f, 300f, 26f), "GAS " + throttle.ToString("0") + " %", label);
            Shadowed(new Rect(20f, h - 40f, 300f, 26f), speed.ToString("0") + " km/h", label);
            Shadowed(new Rect(w - 260f, h - 40f, 240f, 26f), "HÖHE " + altitude.ToString("0") + " m", label);

            string warning = null;
            if (t.Failsafe) warning = "FAILSAFE – SENDER GETRENNT";
            else if (t.ArmingBlockedByThrottle) warning = "GAS GANZ RUNTER ZUM SCHÄRFEN";
            else if (t.TurtleActive) warning = "TURTLE";
            else if (!t.Armed) warning = "ENTSCHÄRFT";
            if (warning != null) Shadowed(new Rect(0f, h * 0.62f, w, 36f), warning, big);

            string source = input != null ? input.DeviceLabel : "";
            Shadowed(new Rect(0f, h - 30f, w, 24f), source + "   ·   F1 Menü   ·   F9 Benchmark   ·   R Reset", centered,
                new Color(1f, 1f, 1f, 0.6f));
        }

        void DrawResult(BenchmarkResult r, float w)
        {
            Color color = r.Rating == BenchmarkResult.Verdict.Passed ? new Color(0.5f, 1f, 0.6f)
                : (r.Rating == BenchmarkResult.Verdict.Borderline ? new Color(1f, 0.85f, 0.4f) : new Color(1f, 0.45f, 0.4f));
            Shadowed(new Rect(0f, 70f, w, 36f), r.RatingText, big, color);
            Shadowed(new Rect(0f, 106f, w, 26f),
                "Schnitt " + r.AverageFps.ToString("0.0") + " fps   ·   1%-Tief " + r.OnePercentLowFps.ToString("0.0")
                + " fps   ·   " + r.QualityName + "   ·   " + r.Resolution, centered);
            if (r.InEditor)
            {
                Shadowed(new Rect(0f, 132f, w, 26f), "Im Editor gemessen – für das Gate zählt nur der Build.", centered,
                    new Color(1f, 0.85f, 0.4f));
            }
        }

        static void Shadowed(Rect rect, string text, GUIStyle style)
        {
            Shadowed(rect, text, style, Color.white);
        }

        static void Shadowed(Rect rect, string text, GUIStyle style, Color color)
        {
            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.75f * color.a);
            GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), text, style);
            GUI.color = color;
            GUI.Label(rect, text, style);
            GUI.color = previous;
        }
    }
}
