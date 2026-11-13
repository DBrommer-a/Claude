using System;
using System.Collections.Generic;
using System.IO;
using Frostline.Flight;
using UnityEngine;

namespace Frostline.Gameplay
{
    /// <summary>
    /// Führt ein Rennen: Drohne an den Start, Countdown, Gates in Reihenfolge, Runden,
    /// Bestzeit mit Ghost der besten Runde. Im Multiplayer gibt der Host Startzeit und Uhr vor.
    /// </summary>
    public sealed class RaceManager : MonoBehaviour
    {
        public DroneController drone;
        public List<RaceTrack> tracks = new List<RaceTrack>();
        public GhostDrone ghost;

        /// <summary>Uhr für Start und Zeitnahme. Im Multiplayer die Serverzeit.</summary>
        public Func<double> Clock = () => Time.timeAsDouble;

        RaceSession session;
        RaceTrack track;
        Vector3 previousPosition;
        bool holding;
        Vector3 holdPosition;
        Quaternion holdRotation;
        readonly ReplayBuffer lapRecording = new ReplayBuffer(60 * 60 * 10);
        float[] bestSplits;
        string message;
        float messageUntil;
        GUIStyle big, medium, small;

        public RaceTrack ActiveTrack { get { return track; } }
        public RaceSession Session { get { return session; } }
        public bool Active { get { return session != null && session.Phase != RacePhase.Idle; } }

        /// <summary>Wird gemeldet, wenn eine Runde fertig ist (Strecke, Zeit). Für den Multiplayer.</summary>
        public event Action<RaceTrack, float> LapReported;

        public static RaceManager Instance { get; private set; }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Awake()
        {
            Instance = this;
            if (drone == null) drone = FindAnyObjectByType<DroneController>();
#if UNITY_6000_4_OR_NEWER
            if (tracks.Count == 0) tracks.AddRange(FindObjectsByType<RaceTrack>());
#else
            if (tracks.Count == 0) tracks.AddRange(FindObjectsByType<RaceTrack>(FindObjectsSortMode.None));
#endif
            tracks.Sort((a, b) => string.Compare(a.trackName, b.trackName, StringComparison.Ordinal));
            foreach (RaceTrack t in tracks) t.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------ Steuerung

        /// <summary>Startet ein Rennen auf der Strecke; startTime &lt; 0 = Countdown ab jetzt.</summary>
        public void StartRace(RaceTrack selected, double startTime = -1.0)
        {
            if (selected == null || drone == null) return;
            StopRace();

            track = selected;
            track.gameObject.SetActive(true);
            IReadOnlyList<RaceGate> gates = track.Gates;
            if (gates.Count == 0)
            {
                Flash("Strecke ohne Gates");
                return;
            }

            session = new RaceSession(gates.Count, track.laps, track.BestLap);
            session.LapCompleted += OnLapCompleted;
            session.RaceCompleted += OnRaceCompleted;
            if (startTime < 0.0) session.BeginCountdown(Clock());
            else session.BeginAt(startTime);

            bestSplits = LoadSplits(track);
            LoadGhost(track);
            MoveToStart();
            UpdateGateStates();
        }

        public void StopRace()
        {
            Release();
            if (session != null)
            {
                session.LapCompleted -= OnLapCompleted;
                session.RaceCompleted -= OnRaceCompleted;
            }
            session = null;
            if (track != null)
            {
                foreach (RaceGate gate in track.Gates) gate.SetState(RaceGate.State.Idle);
                track.gameObject.SetActive(false);
            }
            track = null;
            FrostlineEvents.CurrentLapSeconds = -1f;
            if (ghost != null) ghost.Stop();
        }

        /// <summary>Nach einem Absturz: zurück an das zuletzt durchflogene Gate.</summary>
        public bool TryRespawnAtLastGate()
        {
            if (session == null || track == null || session.Phase != RacePhase.Running) return false;
            int last = session.NextGate - 1;
            Transform target = last >= 0 ? track.Gates[last].transform : track.startPoint;
            if (target == null) return false;

            Vector3 position = last >= 0 ? track.Gates[last].OpeningCenter : target.position;
            Teleport(position, Quaternion.Euler(0f, target.eulerAngles.y, 0f));
            return true;
        }

        void MoveToStart()
        {
            Transform start = track.startPoint != null ? track.startPoint : track.Gates[0].transform;
            holdPosition = start.position;
            holdRotation = Quaternion.Euler(0f, start.eulerAngles.y, 0f);
            Teleport(holdPosition, holdRotation);
            holding = true;
            drone.Body.isKinematic = true;
        }

        void Release()
        {
            if (!holding || drone == null) return;
            holding = false;
            drone.Body.isKinematic = false;
            drone.Body.linearVelocity = Vector3.zero;
            drone.Body.angularVelocity = Vector3.zero;
        }

        void Teleport(Vector3 position, Quaternion rotation)
        {
            Rigidbody body = drone.Body;
            if (!body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            body.position = position;
            body.rotation = rotation;
            drone.transform.SetPositionAndRotation(position, rotation);
            previousPosition = position;
        }

        // ------------------------------------------------------------ Ablauf

        void FixedUpdate()
        {
            if (session == null || drone == null) return;
            double now = Clock();
            session.Update(now);

            if (holding)
            {
                drone.Body.position = holdPosition;
                drone.Body.rotation = holdRotation;
                if (session.Phase == RacePhase.Running)
                {
                    Release();
                    lapRecording.Clear();
                    if (ghost != null && ghost.HasRecording) ghost.Restart();
                    Flash("LOS!");
                }
                previousPosition = holdPosition;
                return;
            }

            Vector3 position = drone.Body.position;
            if (session.Phase == RacePhase.Running)
            {
                RaceGate next = track.Gates[session.NextGate];
                GateCrossing crossing = GateMath.Test(next.Shape, previousPosition.ToNumerics(), position.ToNumerics());
                if (crossing == GateCrossing.Through)
                {
                    int index = session.NextGate;
                    float split = (float)(now - session.LapStartTime);
                    session.PassGate(index, now);
                    FrostlineEvents.RaiseGatePassed(index);
                    ShowSplit(index, split);
                    UpdateGateStates();
                }
                else if (crossing == GateCrossing.Backwards)
                {
                    Flash("Falsche Richtung");
                }

                if (session.Phase == RacePhase.Running)
                {
                    float lapTime = session.CurrentLapTime(now);
                    FrostlineEvents.CurrentLapSeconds = lapTime;
                    RecordLapFrame(lapTime);
                }
            }
            previousPosition = position;
        }

        void RecordLapFrame(float lapTime)
        {
            ReplayFrame frame;
            frame.Time = lapTime;
            frame.Position = drone.transform.position.ToNumerics();
            frame.Rotation = drone.transform.rotation.ToNumerics();
            frame.Throttle = drone.LastInput.Throttle;
            frame.MotorLoad = MotorLoad();
            if (lapRecording.Count == 0 || lapTime - lapRecording.EndTime >= 1f / 60f) lapRecording.Add(frame);
        }

        float MotorLoad()
        {
            var t = drone.Telemetry;
            float sum = 0f;
            for (int i = 0; i < t.MotorRpm.Length; i++) sum += t.MotorRpm[i];
            return t.MaxRpm > 0f ? sum / (t.MotorRpm.Length * t.MaxRpm) : 0f;
        }

        void OnLapCompleted(float lap, bool best)
        {
            FrostlineEvents.RaiseLapFinished(lap);
            LapReported?.Invoke(track, lap);

            if (best)
            {
                track.BestLap = lap;
                SaveSplits(track, session.Splits, lap);
                SaveGhost(track, lapRecording);
                Flash("NEUE BESTZEIT  " + FormatTime(lap));
            }
            else
            {
                Flash("Runde  " + FormatTime(lap) + "   (+" + (lap - session.BestLap).ToString("0.00") + ")");
            }

            lapRecording.Clear();
            if (ghost != null && ghost.HasRecording) ghost.Restart();
        }

        void OnRaceCompleted(float total)
        {
            FrostlineEvents.CurrentLapSeconds = -1f;
            Flash("ZIEL  " + FormatTime(total));
            foreach (RaceGate gate in track.Gates) gate.SetState(RaceGate.State.Idle);
        }

        void UpdateGateStates()
        {
            IReadOnlyList<RaceGate> gates = track.Gates;
            for (int i = 0; i < gates.Count; i++)
            {
                RaceGate.State state = i < session.NextGate ? RaceGate.State.Passed
                    : (i == session.NextGate ? RaceGate.State.Next : RaceGate.State.Idle);
                gates[i].SetState(state);
            }
        }

        void ShowSplit(int index, float split)
        {
            if (bestSplits == null || index >= bestSplits.Length || bestSplits[index] <= 0f) return;
            float delta = split - bestSplits[index];
            Flash((delta <= 0f ? "−" : "+") + Mathf.Abs(delta).ToString("0.00") + " s", 1.2f);
        }

        void Flash(string text, float seconds = 2f)
        {
            message = text;
            messageUntil = Time.unscaledTime + seconds;
        }

        // ------------------------------------------------------------ Bestzeiten und Ghost speichern

        static string GhostPath(RaceTrack t)
        {
            return Path.Combine(Application.persistentDataPath, "Ghosts", Sanitize(t.trackName) + ".frpl");
        }

        static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }

        void SaveGhost(RaceTrack t, ReplayBuffer recording)
        {
            if (recording.Count < 2) return;
            try
            {
                string path = GhostPath(t);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (FileStream stream = File.Create(path)) recording.Write(stream);
                if (ghost != null) ghost.Load(recording.Extract(0f, recording.EndTime));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Frostline: Ghost nicht gespeichert: " + e.Message);
            }
        }

        void LoadGhost(RaceTrack t)
        {
            if (ghost == null) return;
            ghost.Stop();
            string path = GhostPath(t);
            if (!File.Exists(path)) return;
            try
            {
                using (FileStream stream = File.OpenRead(path)) ghost.Load(ReplayBuffer.Read(stream));
            }
            catch (Exception e)
            {
                Debug.LogWarning("Frostline: Ghost nicht lesbar: " + e.Message);
            }
        }

        static void SaveSplits(RaceTrack t, IReadOnlyList<float> splits, float lap)
        {
            var parts = new string[splits.Count];
            for (int i = 0; i < splits.Count; i++) parts[i] = splits[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            // Das letzte Gate ist die Ziellinie und fehlt in Splits nach Rundenende: Rundenzeit anhängen.
            PlayerPrefs.SetString(t.BestLapKey + ".Splits", string.Join(";", parts) + ";" + lap.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        static float[] LoadSplits(RaceTrack t)
        {
            string text = PlayerPrefs.GetString(t.BestLapKey + ".Splits", "");
            if (string.IsNullOrEmpty(text)) return null;
            string[] parts = text.Split(';');
            var values = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out values[i]);
            }
            return values;
        }

        public static string FormatTime(float seconds)
        {
            int minutes = (int)(seconds / 60f);
            float rest = seconds - minutes * 60f;
            return minutes > 0 ? minutes + ":" + rest.ToString("00.00") : rest.ToString("0.00") + " s";
        }

        // ------------------------------------------------------------ Anzeige

        void OnGUI()
        {
            if (session == null) return;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                medium = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                small = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperRight };
                big.normal.textColor = medium.normal.textColor = small.normal.textColor = Color.white;
            }

            float scale = Screen.height / 720f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;
            double now = Clock();

            if (session.Phase == RacePhase.Countdown)
            {
                float remaining = session.CountdownRemaining(now);
                GUI.Label(new Rect(0f, 200f, w, 90f), Mathf.CeilToInt(remaining).ToString(), big);
                GUI.Label(new Rect(0f, 290f, w, 40f), track.trackName, medium);
            }

            if (session.Phase == RacePhase.Running || session.Phase == RacePhase.Finished)
            {
                string lap = session.Phase == RacePhase.Running ? FormatTime(session.CurrentLapTime(now)) : FormatTime(session.TotalTime(now));
                string text = track.trackName + "\nRunde " + session.CurrentLap + "/" + session.Laps + "   " + lap
                    + "\nGate " + Mathf.Min(session.NextGate + 1, session.GateCount) + "/" + session.GateCount
                    + (session.BestLap > 0f ? "\nBestzeit " + FormatTime(session.BestLap) : "");
                GUI.Label(new Rect(w - 420f, 80f, 400f, 120f), text, small);
            }

            if (message != null && Time.unscaledTime < messageUntil)
            {
                GUI.Label(new Rect(0f, 140f, w, 50f), message, medium);
            }
        }
    }
}
