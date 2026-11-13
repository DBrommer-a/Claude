using System;
using System.IO;
using Frostline.Audio;
using Frostline.Flight;
using Frostline.Gameplay;
using Frostline.View;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Frostline.Gameplay
{
    /// <summary>
    /// Zeichnet jeden Flug laufend auf (die letzten 5 Minuten) und spielt ihn aus der
    /// Verfolgerperspektive ab. Taste P: Replay an/aus. Im Replay: Leertaste Pause,
    /// Pfeile links/rechts 5 s zurück/vor, Pfeile hoch/runter Tempo, C Kamera, S speichern.
    /// Gespeicherte Replays sind Material für den Trailer.
    /// </summary>
    public sealed class ReplayDirector : MonoBehaviour
    {
        public DroneController drone;
        public ChaseCamera chaseCamera;
        public Camera fpvCamera;
        [Tooltip("Sichtbares Modell der Drohne; wird im Replay bewegt.")]
        public Transform droneModel;
        public float recordRateHz = 60f;
        public float keepMinutes = 5f;

        ReplayBuffer buffer;
        float nextSample;
        float playTime;
        float speed = 1f;
        bool paused;
        bool previousKey;
        string savedPath;
        float savedUntil;
        GUIStyle style;

        static readonly float[] Speeds = { 0.25f, 0.5f, 1f, 2f };

        public bool Playing { get; private set; }

        void Awake()
        {
            if (drone == null) drone = FindAnyObjectByType<DroneController>();
            int capacity = Mathf.CeilToInt(recordRateHz * 60f * keepMinutes);
            buffer = new ReplayBuffer(Mathf.Max(capacity, 16));
            FrostlineEvents.Respawned += OnRespawned;
        }

        void OnDestroy()
        {
            FrostlineEvents.Respawned -= OnRespawned;
        }

        void OnRespawned()
        {
            // Ein Sprung an den Startpunkt würde im Replay als Flug quer über die Insel erscheinen.
            if (!Playing) buffer.Clear();
        }

        void Update()
        {
            Keyboard keys = Keyboard.current;
            bool key = keys != null && keys.pKey.isPressed;
            if (key && !previousKey) Toggle();
            previousKey = key;

            if (Playing) UpdatePlayback(keys);
            else Record();
        }

        void Record()
        {
            if (drone == null || Time.time < nextSample) return;
            nextSample = Time.time + 1f / recordRateHz;

            ReplayFrame frame;
            frame.Time = Time.time;
            frame.Position = drone.transform.position.ToNumerics();
            frame.Rotation = drone.transform.rotation.ToNumerics();
            frame.Throttle = drone.LastInput.Throttle;
            var t = drone.Telemetry;
            float sum = 0f;
            for (int i = 0; i < t.MotorRpm.Length; i++) sum += t.MotorRpm[i];
            frame.MotorLoad = t.MaxRpm > 0f ? sum / (t.MotorRpm.Length * t.MaxRpm) : 0f;
            buffer.Add(frame);
        }

        public void Toggle()
        {
            if (Playing) Stop();
            else Begin();
        }

        public void Begin()
        {
            if (Playing || buffer.Count < 2 || drone == null) return;
            Playing = true;
            paused = false;
            speed = 1f;
            // Die letzten 30 Sekunden zuerst.
            playTime = Mathf.Max(buffer.StartTime, buffer.EndTime - 30f);

            drone.enabled = false;
            drone.Body.isKinematic = true;
            drone.Body.interpolation = RigidbodyInterpolation.None;
            if (chaseCamera != null)
            {
                chaseCamera.target = drone.transform;
                chaseCamera.gameObject.SetActive(true);
                chaseCamera.SnapToTarget();
            }
            if (fpvCamera != null) fpvCamera.enabled = false;
        }

        public void Stop()
        {
            if (!Playing) return;
            Playing = false;

            // Drohne zurück an den Punkt, an dem das Replay begann.
            ReplayFrame last = buffer.Sample(buffer.EndTime);
            drone.transform.SetPositionAndRotation(last.Position.ToUnity(), last.Rotation.ToUnity());
            drone.Body.position = drone.transform.position;
            drone.Body.rotation = drone.transform.rotation;
            drone.Body.isKinematic = false;
            drone.Body.interpolation = RigidbodyInterpolation.Interpolate;
            drone.Body.linearVelocity = Vector3.zero;
            drone.Body.angularVelocity = Vector3.zero;
            drone.ResetFlightState();
            drone.enabled = true;

            if (chaseCamera != null) chaseCamera.gameObject.SetActive(false);
            if (fpvCamera != null) fpvCamera.enabled = true;
            MotorAudio audio = drone.GetComponent<MotorAudio>();
            if (audio != null) audio.overrideLoad = -1f;
        }

        void UpdatePlayback(Keyboard keys)
        {
            if (keys != null)
            {
                if (keys.spaceKey.wasPressedThisFrame) paused = !paused;
                if (keys.leftArrowKey.wasPressedThisFrame) playTime -= 5f;
                if (keys.rightArrowKey.wasPressedThisFrame) playTime += 5f;
                if (keys.upArrowKey.wasPressedThisFrame) speed = NextSpeed(1);
                if (keys.downArrowKey.wasPressedThisFrame) speed = NextSpeed(-1);
                if (keys.cKey.wasPressedThisFrame && chaseCamera != null) chaseCamera.NextMode();
                if (keys.sKey.wasPressedThisFrame) Save();
            }

            if (!paused) playTime += Time.unscaledDeltaTime * speed;
            if (playTime >= buffer.EndTime)
            {
                playTime = buffer.EndTime;
                paused = true;
            }
            playTime = Mathf.Max(playTime, buffer.StartTime);

            ReplayFrame frame = buffer.Sample(playTime);
            drone.transform.SetPositionAndRotation(frame.Position.ToUnity(), frame.Rotation.ToUnity());

            MotorAudio audio = drone.GetComponent<MotorAudio>();
            if (audio != null) audio.overrideLoad = paused ? 0f : frame.MotorLoad;
        }

        float NextSpeed(int direction)
        {
            int index = Array.IndexOf(Speeds, speed);
            if (index < 0) index = 2;
            index = Mathf.Clamp(index + direction, 0, Speeds.Length - 1);
            return Speeds[index];
        }

        void Save()
        {
            try
            {
                string folder = Path.Combine(Application.persistentDataPath, "Replays");
                Directory.CreateDirectory(folder);
                savedPath = Path.Combine(folder, "replay_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".frpl");
                using (FileStream stream = File.Create(savedPath)) buffer.Write(stream);
                savedUntil = Time.unscaledTime + 4f;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Frostline: Replay nicht gespeichert: " + e.Message);
            }
        }

        void OnGUI()
        {
            if (!Playing) return;
            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerCenter };
                style.normal.textColor = Color.white;
            }
            float scale = Screen.height / 720f;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale;
            float h = Screen.height / scale;

            float position = playTime - buffer.StartTime;
            string text = "REPLAY  " + RaceManager.FormatTime(position) + " / " + RaceManager.FormatTime(buffer.Duration)
                + "   ·   " + speed.ToString("0.##") + "x" + (paused ? "  PAUSE" : "")
                + "\nLeertaste Pause · ←/→ 5 s · ↑/↓ Tempo · C Kamera · S Speichern · P Ende";
            GUI.Label(new Rect(0f, h - 90f, w, 70f), text, style);

            var bar = new Rect(w * 0.2f, h - 100f, w * 0.6f, 4f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, new Color(1f, 1f, 1f, 0.3f), 0f, 0f);
            float fraction = buffer.Duration > 0f ? position / buffer.Duration : 0f;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * fraction, bar.height), Texture2D.whiteTexture);

            if (savedPath != null && Time.unscaledTime < savedUntil)
            {
                GUI.Label(new Rect(0f, 40f, w, 30f), "Gespeichert: " + savedPath, style);
            }
        }
    }
}
