using System.Collections.Generic;
using Frostline.Audio;
using Frostline.Controls;
using Frostline.Diagnostics;
using Frostline.Flight;
using Frostline.Flight.Core;
using Frostline.Gameplay;
using Frostline.Remote;
using Frostline.View;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Frostline.UI
{
    /// <summary>
    /// Einstellungsmenü des Prototyps (F1 oder Esc): Sender und Kalibrierung, Rates, Drohne,
    /// Grafik, Benchmark. Technisch schlicht (IMGUI), damit es ohne zusätzliche Assets läuft.
    /// </summary>
    public sealed class FrostlineMenu : MonoBehaviour
    {
        public DroneController drone;
        public StickInput input;
        public FpvCamera fpvCamera;
        public MotorAudio motorAudio;
        public BenchmarkRunner benchmark;
        public Osd osd;
        public RaceManager race;
        public ReplayDirector replay;
        public WeatherSystem weather;
        public Collectibles collectibles;
        public RemoteControlServer remote;

        static readonly string[] Tabs = { "Spiel", "Sender", "Rates", "Drohne", "Grafik", "Benchmark" };
        static readonly string[] AxisNames = { "Roll", "Pitch", "Yaw" };

        PilotSettings settings;
        CalibrationWizard wizard;
        QuadConfig[] presets;
        bool open;
        bool dirty;
        int tab;
        Vector2 scroll;
        bool previousMenuKey, previousBenchmarkKey;
        GUIStyle heading, wrapped, bar;

        void Awake()
        {
            if (drone == null) drone = FindAnyObjectByType<DroneController>();
            if (input == null) input = FindAnyObjectByType<StickInput>();
            if (fpvCamera == null) fpvCamera = FindAnyObjectByType<FpvCamera>();
            if (motorAudio == null) motorAudio = FindAnyObjectByType<MotorAudio>();
            if (benchmark == null) benchmark = FindAnyObjectByType<BenchmarkRunner>();
            if (osd == null) osd = FindAnyObjectByType<Osd>();
            if (race == null) race = FindAnyObjectByType<RaceManager>();
            if (replay == null) replay = FindAnyObjectByType<ReplayDirector>();
            if (weather == null) weather = FindAnyObjectByType<WeatherSystem>();
            if (collectibles == null) collectibles = FindAnyObjectByType<Collectibles>();
            if (remote == null) remote = FindAnyObjectByType<RemoteControlServer>();
            presets = QuadConfig.AllPresets();
        }

        void Start()
        {
            settings = PilotSettings.Load();
            if (input != null) wizard = new CalibrationWizard(input);
            ApplyAll();
        }

        void ApplyAll()
        {
            if (drone != null)
            {
                drone.config = settings.config;
                drone.rates = settings.rates;
                drone.ApplySettings();
            }
            if (fpvCamera != null)
            {
                fpvCamera.fieldOfView = settings.fieldOfView;
                fpvCamera.Apply();
            }
            if (motorAudio != null) motorAudio.volume = settings.motorVolume;
            if (osd != null) osd.visible = settings.showOsd;
        }

        void Update()
        {
            Keyboard keys = Keyboard.current;
            if (keys != null)
            {
                bool menuKey = keys.f1Key.isPressed || keys.escapeKey.isPressed;
                if (menuKey && !previousMenuKey) Toggle();
                previousMenuKey = menuKey;

                bool benchmarkKey = keys.f9Key.isPressed;
                if (benchmarkKey && !previousBenchmarkKey && benchmark != null)
                {
                    if (benchmark.Running) benchmark.Abort();
                    else { Close(); benchmark.Begin(); }
                }
                previousBenchmarkKey = benchmarkKey;
            }

            if (wizard != null && wizard.Active) wizard.Update(Time.unscaledDeltaTime);
        }

        void Toggle()
        {
            if (open) Close();
            else open = true;
        }

        void Close()
        {
            if (wizard != null && wizard.Active) wizard.Cancel();
            if (dirty && settings != null)
            {
                settings.Save();
                dirty = false;
            }
            open = false;
        }

        void Changed()
        {
            dirty = true;
            ApplyAll();
        }

        // ------------------------------------------------------------ Zeichnen

        void OnGUI()
        {
            if (!open || settings == null) return;
            EnsureStyles();

            float scale = Screen.height / 760f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;
            float h = Screen.height / scale;
            var area = new Rect((w - 700f) * 0.5f, 40f, 700f, h - 80f);

            GUI.Box(area, GUIContent.none);
            GUI.Box(area, GUIContent.none);
            GUILayout.BeginArea(new Rect(area.x + 16f, area.y + 12f, area.width - 32f, area.height - 24f));
            GUILayout.Label("FROSTLINE – Einstellungen", heading);

            if (wizard != null && wizard.Active)
            {
                DrawWizard();
            }
            else
            {
                tab = GUILayout.Toolbar(tab, Tabs, GUILayout.Height(30f));
                GUILayout.Space(8f);
                scroll = GUILayout.BeginScrollView(scroll);
                switch (tab)
                {
                    case 0: DrawGame(); break;
                    case 1: DrawTransmitter(); break;
                    case 2: DrawRates(); break;
                    case 3: DrawDrone(); break;
                    case 4: DrawGraphics(); break;
                    case 5: DrawBenchmark(); break;
                }
                GUILayout.EndScrollView();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Schließen (F1)", GUILayout.Height(30f))) Close();
            }
            GUILayout.EndArea();
        }

        void EnsureStyles()
        {
            if (heading != null) return;
            heading = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            wrapped = new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 14 };
            bar = new GUIStyle(GUI.skin.box);
        }

        // ------------------------------------------------------------ Spiel

        static readonly string[] WeatherNames = { "Klar", "Tiefe Wolken", "Schneesturm" };
        static readonly string[] DayNames = { "Morgen", "Mittag", "Abend" };
        static readonly float[] DayHours = { 7.5f, 12.5f, 19f };

        void DrawGame()
        {
            GUILayout.Label("Freestyle", heading);
            string progress = collectibles != null ? "Sammelobjekte: " + collectibles.CollectedCount + " / " + collectibles.Total : "";
            GUILayout.Label("Frei fliegen, Landmarken erkunden. " + progress + "   ·   F2: Multiplayer", wrapped);
            GUILayout.BeginHorizontal();
            if (race != null && race.Active && GUILayout.Button("Rennen beenden", GUILayout.Height(30f))) race.StopRace();
            if (replay != null && GUILayout.Button(replay.Playing ? "Replay beenden (P)" : "Replay ansehen (P)", GUILayout.Height(30f)))
            {
                Close();
                replay.Toggle();
                return;
            }
            if (collectibles != null && GUILayout.Button("Sammelfortschritt zurücksetzen", GUILayout.Height(30f))) collectibles.ResetProgress();
            GUILayout.EndHorizontal();

            GUILayout.Space(10f);
            GUILayout.Label("Rennen", heading);
            if (race == null || race.tracks.Count == 0)
            {
                GUILayout.Label("In dieser Szene gibt es keine Strecken. Die ganze Insel hat drei (Frostline › 4 · Insel erstellen).", wrapped);
            }
            else
            {
                foreach (RaceTrack track in race.tracks)
                {
                    GUILayout.BeginHorizontal();
                    float best = track.BestLap;
                    GUILayout.Label(track.trackName + "  ·  " + track.laps + " Runden" + (best > 0f ? "  ·  Bestzeit " + RaceManager.FormatTime(best) : ""), GUILayout.Width(420f));
                    if (GUILayout.Button("Start", GUILayout.Height(28f)))
                    {
                        Close();
                        race.StartRace(track);
                        return;
                    }
                    GUILayout.EndHorizontal();
                }
            }

            if (weather != null)
            {
                GUILayout.Space(10f);
                GUILayout.Label("Wetter und Tageszeit", heading);
                if (weather.lockedByHost) GUILayout.Label("Im Multiplayer bestimmt der Host Wetter und Tageszeit.", wrapped);
                GUI.enabled = !weather.lockedByHost;
                WorldState state = weather.State;
                int w = GUILayout.Toolbar((int)state.Weather, WeatherNames, GUILayout.Height(28f));
                int currentDay = state.TimeOfDay < 10f ? 0 : (state.TimeOfDay < 16f ? 1 : 2);
                int d = GUILayout.Toolbar(currentDay, DayNames, GUILayout.Height(28f));
                float hour = Slider("Uhrzeit", state.TimeOfDay, 5f, 21f, "0.0");
                if (w != (int)state.Weather || d != currentDay || !Mathf.Approximately(hour, state.TimeOfDay))
                {
                    state.Weather = (Weather)w;
                    state.TimeOfDay = d != currentDay ? DayHours[d] : hour;
                    weather.SetLocal(state);
                }
                if (GUILayout.Button("Andere Wolken (neuer Seed)", GUILayout.Height(26f)))
                {
                    state.CloudSeed = Random.Range(1, int.MaxValue);
                    weather.SetLocal(state);
                }
                GUI.enabled = true;
            }

            if (remote != null)
            {
                GUILayout.Space(10f);
                GUILayout.Label("iPhone-App „Frostline Remote“", heading);
                GUILayout.Label(remote.Status, wrapped);
                if (remote.IsConnected && input != null && input.ActiveSource != StickInput.Source.Remote
                    && GUILayout.Button("Mit dem iPhone fliegen", GUILayout.Height(28f)))
                {
                    input.SelectRemote();
                }
            }
        }

        // ------------------------------------------------------------ Sender

        void DrawTransmitter()
        {
            if (input == null)
            {
                GUILayout.Label("Kein Eingabe-Modul in der Szene.", wrapped);
                return;
            }

            GUILayout.Label("Aktiv: " + input.DeviceLabel, heading);
            GUILayout.Label("FPV-Sender per USB anschließen und am Sender „USB Joystick (HID)“ wählen. "
                + "Gamepads funktionieren ebenfalls; ohne Gerät fliegt die Tastatur (W/S Gas, A/D Gier, Pfeile Nick/Roll, Leertaste schärfen).", wrapped);
            GUILayout.Space(6f);

            List<InputDevice> devices = input.ListDevices();
            foreach (InputDevice device in devices)
            {
                bool selected = device == input.Device && input.ActiveSource != StickInput.Source.Remote;
                string kind = device is Gamepad ? "Gamepad" : "Sender";
                if (GUILayout.Toggle(selected, "  " + device.displayName + "  (" + kind + ")", GUI.skin.button, GUILayout.Height(28f)) && !selected)
                {
                    input.SelectDevice(device);
                }
            }
            if (remote != null && remote.IsConnected)
            {
                bool remoteSelected = input.ActiveSource == StickInput.Source.Remote;
                if (GUILayout.Toggle(remoteSelected, "  iPhone: " + remote.ClientName, GUI.skin.button, GUILayout.Height(28f)) && !remoteSelected)
                {
                    input.SelectRemote();
                }
            }
            bool keyboardSelected = input.ActiveSource == StickInput.Source.Keyboard;
            if (GUILayout.Toggle(keyboardSelected, "  Tastatur", GUI.skin.button, GUILayout.Height(28f)) && !keyboardSelected)
            {
                input.SelectDevice(null);
            }

            GUILayout.Space(10f);
            if (input.ActiveSource == StickInput.Source.Transmitter)
            {
                InputProfile profile = input.Profile;
                GUILayout.Label(profile != null && profile.calibrated
                    ? "Dieser Sender ist kalibriert."
                    : "Noch nicht kalibriert – es gilt die Standardbelegung AETR mit Kanal 5 als Arm-Schalter.", wrapped);
                if (GUILayout.Button("Sender kalibrieren", GUILayout.Height(34f))) wizard.Begin();

                if (profile != null)
                {
                    float deadband = Slider("Totzone Stickmitte", profile.deadband, 0f, 0.05f, "0.000");
                    if (!Mathf.Approximately(deadband, profile.deadband))
                    {
                        profile.deadband = deadband;
                        profile.Save();
                    }
                }
            }

            GUILayout.Space(10f);
            GUILayout.Label("Live", heading);
            ControlInput current = input.Current;
            Meter("Roll", current.Roll * 0.5f + 0.5f);
            Meter("Pitch", current.Pitch * 0.5f + 0.5f);
            Meter("Yaw", current.Yaw * 0.5f + 0.5f);
            Meter("Gas", current.Throttle);
            GUILayout.Label("Arm: " + (current.ArmSwitch ? "AN" : "aus") + "     Modus: " + current.Mode
                + "     Turtle: " + (current.TurtleSwitch ? "AN" : "aus"), wrapped);
        }

        void DrawWizard()
        {
            GUILayout.Label("Sender kalibrieren", heading);
            GUILayout.Space(10f);
            GUILayout.Label(wizard.Prompt, new GUIStyle(wrapped) { fontSize = 18 });
            GUILayout.Space(14f);

            if (!wizard.NeedsConfirm) Meter("Halten", wizard.HoldProgress);

            GUILayout.Space(14f);
            GUILayout.BeginHorizontal();
            if (wizard.NeedsConfirm && GUILayout.Button(wizard.Current == CalibrationWizard.Step.Done ? "Fertig" : "Weiter", GUILayout.Height(36f)))
            {
                wizard.Confirm();
            }
            if (wizard.CanSkip && GUILayout.Button("Überspringen", GUILayout.Height(36f))) wizard.Skip();
            if (wizard.Active && wizard.Current != CalibrationWizard.Step.Done && GUILayout.Button("Abbrechen", GUILayout.Height(36f)))
            {
                wizard.Cancel();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(10f);
            GUILayout.Label("Während der Kalibrierung bleibt die Drohne entschärft.", wrapped);
        }

        // ------------------------------------------------------------ Rates

        void DrawRates()
        {
            RateProfile rates = settings.rates;
            GUILayout.Label("Rate-Typ", heading);
            int type = GUILayout.Toolbar((int)rates.Type, new[] { "Betaflight", "Actual" }, GUILayout.Height(28f));
            if (type != (int)rates.Type)
            {
                rates.Type = (RateType)type;
                Changed();
            }
            GUILayout.Label("Die Werte entsprechen denen im Betaflight Configurator. Du kannst die Zahlen deiner echten Drohne übernehmen.", wrapped);

            for (int axis = 0; axis < 3; axis++)
            {
                GUILayout.Space(8f);
                GUILayout.Label(AxisNames[axis] + "   –   maximal " + rates.MaxRate(axis).ToString("0") + " °/s", heading);
                if (rates.Type == RateType.Betaflight)
                {
                    BetaflightRate r = axis == 0 ? rates.BetaflightRoll : (axis == 1 ? rates.BetaflightPitch : rates.BetaflightYaw);
                    BetaflightRate edited = r;
                    edited.RcRate = Slider("RC Rate", r.RcRate, 0.1f, 2.55f, "0.00");
                    edited.SuperRate = Slider("Super Rate", r.SuperRate, 0f, 0.99f, "0.00");
                    edited.Expo = Slider("Expo", r.Expo, 0f, 1f, "0.00");
                    if (edited.RcRate != r.RcRate || edited.SuperRate != r.SuperRate || edited.Expo != r.Expo)
                    {
                        if (axis == 0) rates.BetaflightRoll = edited;
                        else if (axis == 1) rates.BetaflightPitch = edited;
                        else rates.BetaflightYaw = edited;
                        Changed();
                    }
                }
                else
                {
                    ActualRate r = axis == 0 ? rates.ActualRoll : (axis == 1 ? rates.ActualPitch : rates.ActualYaw);
                    ActualRate edited = r;
                    edited.CenterSensitivity = Slider("Center Sensitivity (°/s)", r.CenterSensitivity, 10f, 500f, "0");
                    edited.MaxRate = Slider("Max Rate (°/s)", r.MaxRate, 100f, 1800f, "0");
                    edited.Expo = Slider("Expo", r.Expo, 0f, 1f, "0.00");
                    if (edited.CenterSensitivity != r.CenterSensitivity || edited.MaxRate != r.MaxRate || edited.Expo != r.Expo)
                    {
                        if (axis == 0) rates.ActualRoll = edited;
                        else if (axis == 1) rates.ActualPitch = edited;
                        else rates.ActualYaw = edited;
                        Changed();
                    }
                }
            }
        }

        // ------------------------------------------------------------ Drohne

        void DrawDrone()
        {
            QuadConfig config = settings.config;
            GUILayout.Label("Preset: " + config.Name, heading);
            GUILayout.BeginHorizontal();
            foreach (QuadConfig preset in presets)
            {
                if (GUILayout.Button(preset.Name, GUILayout.Height(30f)))
                {
                    settings.config = preset.Clone();
                    Changed();
                    if (drone != null) drone.ResetFlightState();
                    return;
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            bool changed = false;
            changed |= Edit(ref config.MassKg, "Gewicht (kg)", 0.15f, 1.5f, "0.00");
            changed |= Edit(ref config.ThrustToWeight, "Schub-Gewicht-Verhältnis", 2f, 16f, "0.0");
            changed |= Edit(ref config.MotorKv, "Motor-KV", 1000f, 5000f, "0");
            changed |= Edit(ref config.PropDiameterInch, "Propeller-Durchmesser (Zoll)", 2f, 7f, "0.0");
            changed |= Edit(ref config.PropPitchInch, "Propeller-Steigung (Zoll)", 1.5f, 6f, "0.0");

            float cells = Slider("Akku-Zellen (S)", config.Cells, 2f, 6f, "0");
            int cellCount = Mathf.RoundToInt(cells);
            if (cellCount != config.Cells)
            {
                config.Cells = cellCount;
                changed = true;
            }
            changed |= Edit(ref config.CapacityMah, "Akku-Kapazität (mAh)", 300f, 3000f, "0");

            bool infinite = GUILayout.Toggle(config.InfiniteBattery, "  Akku wird nicht leer (Spannung bricht unter Last trotzdem ein)");
            if (infinite != config.InfiniteBattery)
            {
                config.InfiniteBattery = infinite;
                changed = true;
            }

            GUILayout.Space(8f);
            GUILayout.Label("Kamera", heading);
            changed |= Edit(ref config.CameraTiltDeg, "Kamerawinkel / Uptilt (°)", 0f, 60f, "0");
            changed |= Edit(ref settings.fieldOfView, "Blickwinkel vertikal (°)", 60f, 125f, "0");

            GUILayout.Space(8f);
            GUILayout.Label("Ton", heading);
            changed |= Edit(ref settings.motorVolume, "Motorlautstärke", 0f, 1f, "0.00");

            bool show = GUILayout.Toggle(settings.showOsd, "  OSD anzeigen");
            if (show != settings.showOsd)
            {
                settings.showOsd = show;
                changed = true;
            }

            if (changed) Changed();
        }

        // ------------------------------------------------------------ Grafik

        void DrawGraphics()
        {
            GUILayout.Label("Qualitätsstufe", heading);
            string[] names = QualitySettings.names;
            int current = QualitySettings.GetQualityLevel();
            int chosen = GUILayout.Toolbar(current, names, GUILayout.Height(30f));
            if (chosen != current) QualitySettings.SetQualityLevel(chosen, true);
            GUILayout.Label("Ziel auf dem MacBook Air ist „Medium“ (passiv gekühlt), auf Mac mini oder MacBook Pro „High“. „Ultra“ ist für Screenshots und den Trailer.", wrapped);

            GUILayout.Space(10f);
            bool vsync = GUILayout.Toggle(QualitySettings.vSyncCount > 0, "  VSync (an die Bildwiederholrate des Monitors binden)");
            QualitySettings.vSyncCount = vsync ? 1 : 0;

            GUILayout.Space(10f);
            GUILayout.Label("Auflösung: " + Screen.width + " x " + Screen.height + "   ·   Grafik: " + SystemInfo.graphicsDeviceName, wrapped);
        }

        // ------------------------------------------------------------ Benchmark

        void DrawBenchmark()
        {
            GUILayout.Label("fps-Messung", heading);
            GUILayout.Label("Fliegt 60 Sekunden lang automatisch einen festen Weg über Meer, Insel und durch die Wolken und misst dabei jedes Bild. "
                + "Für die Entscheidung nach dem Prototyp zählt nur die Messung im fertigen Build auf der Zielstufe („Medium“ auf dem MacBook Air).", wrapped);
            GUILayout.Space(8f);

            if (benchmark == null)
            {
                GUILayout.Label("Kein Benchmark-Modul in der Szene.", wrapped);
                return;
            }

            if (GUILayout.Button("Messung starten (F9)", GUILayout.Height(36f)))
            {
                Close();
                benchmark.Begin();
                return;
            }

            BenchmarkResult r = benchmark.LastResult;
            if (r != null)
            {
                GUILayout.Space(10f);
                GUILayout.Label(r.RatingText, heading);
                GUILayout.Label("Schnitt " + r.AverageFps.ToString("0.0") + " fps   ·   1%-Tief " + r.OnePercentLowFps.ToString("0.0")
                    + " fps   ·   längstes Bild " + r.WorstFrameMs.ToString("0.0") + " ms", wrapped);
                GUILayout.Label("Stufe " + r.QualityName + "   ·   " + r.Resolution + (r.InEditor ? "   ·   im Editor gemessen (zählt nicht)" : ""), wrapped);
                if (!string.IsNullOrEmpty(r.ReportPath)) GUILayout.Label("Bericht: " + r.ReportPath, wrapped);
            }
        }

        // ------------------------------------------------------------ Bausteine

        bool Edit(ref float value, string label, float min, float max, string format)
        {
            float edited = Slider(label, value, min, max, format);
            if (Mathf.Approximately(edited, value)) return false;
            value = edited;
            return true;
        }

        float Slider(string label, float value, float min, float max, string format)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(250f));
            float result = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(300f));
            GUILayout.Label(result.ToString(format), GUILayout.Width(70f));
            GUILayout.EndHorizontal();
            return result;
        }

        void Meter(string label, float fraction)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(80f));
            Rect rect = GUILayoutUtility.GetRect(400f, 18f, GUILayout.Width(400f));
            GUI.Box(rect, GUIContent.none, bar);
            float x = rect.x + Mathf.Clamp01(fraction) * (rect.width - 6f);
            GUI.DrawTexture(new Rect(x, rect.y, 6f, rect.height), Texture2D.whiteTexture);
            GUILayout.EndHorizontal();
        }
    }
}
