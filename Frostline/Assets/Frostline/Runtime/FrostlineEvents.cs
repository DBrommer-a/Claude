using System;

namespace Frostline
{
    /// <summary>
    /// Zentrale Spiel-Ereignisse. Wer etwas auslöst (Drohne, Rennen), muss nicht wissen,
    /// wer zuhört (iPhone-Haptik, Sounds, Replay, Multiplayer).
    /// </summary>
    public static class FrostlineEvents
    {
        /// <summary>Drohne wurde geschärft.</summary>
        public static event Action Armed;
        /// <summary>Harter Aufprall; Wert = Aufprallgeschwindigkeit in m/s.</summary>
        public static event Action<float> Crashed;
        /// <summary>Leichter Bodenkontakt.</summary>
        public static event Action GroundTouched;
        /// <summary>Drohne hat ein Gate berührt (nicht durchflogen).</summary>
        public static event Action GateTouched;
        /// <summary>Gate durchflogen; Wert = Index des Gates.</summary>
        public static event Action<int> GatePassed;
        /// <summary>Runde beendet; Wert = Rundenzeit in Sekunden.</summary>
        public static event Action<float> LapFinished;
        /// <summary>Drohne wurde zurückgesetzt.</summary>
        public static event Action Respawned;

        /// <summary>Laufende Rundenzeit in Sekunden, negativ wenn kein Rennen läuft.</summary>
        public static float CurrentLapSeconds = -1f;

        public static void RaiseArmed() { Armed?.Invoke(); }
        public static void RaiseCrashed(float speed) { Crashed?.Invoke(speed); }
        public static void RaiseGroundTouched() { GroundTouched?.Invoke(); }
        public static void RaiseGateTouched() { GateTouched?.Invoke(); }
        public static void RaiseGatePassed(int index) { GatePassed?.Invoke(index); }
        public static void RaiseLapFinished(float seconds) { LapFinished?.Invoke(seconds); }
        public static void RaiseRespawned() { Respawned?.Invoke(); }
    }
}
