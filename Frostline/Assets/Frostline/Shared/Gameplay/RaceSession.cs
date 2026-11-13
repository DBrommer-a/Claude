using System;
using System.Collections.Generic;

namespace Frostline.Gameplay
{
    public enum RacePhase
    {
        Idle,
        Countdown,
        Running,
        Finished,
    }

    /// <summary>
    /// Ablauf eines Rennens ohne Unity: Countdown, Gates in Reihenfolge, Runden, Zwischenzeiten,
    /// Bestzeit. Alle Zeiten in Sekunden, von außen übergeben (im Multiplayer: Serverzeit).
    /// </summary>
    public sealed class RaceSession
    {
        public const float CountdownSeconds = 3f;

        readonly List<float> lapTimes = new List<float>();
        readonly List<float> splits = new List<float>();

        public int GateCount { get; private set; }
        public int Laps { get; private set; }
        public RacePhase Phase { get; private set; }
        public double StartTime { get; private set; }
        public double LapStartTime { get; private set; }
        public int NextGate { get; private set; }
        public int CurrentLap { get; private set; }
        public float BestLap { get; private set; }
        public IReadOnlyList<float> LapTimes { get { return lapTimes; } }
        /// <summary>Zeiten der aktuellen Runde an jedem Gate, ab Rundenstart.</summary>
        public IReadOnlyList<float> Splits { get { return splits; } }

        /// <summary>Wird bei jeder abgeschlossenen Runde gemeldet (Rundenzeit, neue Bestzeit?).</summary>
        public event Action<float, bool> LapCompleted;
        public event Action<float> RaceCompleted;

        public RaceSession(int gateCount, int laps, float bestLap = 0f)
        {
            if (gateCount < 1) throw new ArgumentOutOfRangeException(nameof(gateCount));
            GateCount = gateCount;
            Laps = Math.Max(1, laps);
            BestLap = bestLap;
            Phase = RacePhase.Idle;
        }

        /// <summary>Startet den Countdown; das Rennen läuft ab <paramref name="now"/> + 3 s.</summary>
        public void BeginCountdown(double now)
        {
            BeginAt(now + CountdownSeconds);
        }

        /// <summary>Startet zu einem festen Zeitpunkt, z. B. vom Host vorgegeben.</summary>
        public void BeginAt(double startTime)
        {
            StartTime = startTime;
            LapStartTime = startTime;
            NextGate = 0;
            CurrentLap = 1;
            lapTimes.Clear();
            splits.Clear();
            Phase = RacePhase.Countdown;
        }

        public void Abort()
        {
            Phase = RacePhase.Idle;
            splits.Clear();
        }

        /// <summary>Sekunden bis zum Start (nur im Countdown sinnvoll).</summary>
        public float CountdownRemaining(double now)
        {
            return (float)Math.Max(0.0, StartTime - now);
        }

        public void Update(double now)
        {
            if (Phase == RacePhase.Countdown && now >= StartTime) Phase = RacePhase.Running;
        }

        /// <summary>Laufende Rundenzeit.</summary>
        public float CurrentLapTime(double now)
        {
            if (Phase != RacePhase.Running) return 0f;
            return (float)Math.Max(0.0, now - LapStartTime);
        }

        public float TotalTime(double now)
        {
            if (Phase == RacePhase.Finished)
            {
                float sum = 0f;
                foreach (float lap in lapTimes) sum += lap;
                return sum;
            }
            return Phase == RacePhase.Running ? (float)(now - StartTime) : 0f;
        }

        /// <summary>
        /// Meldet einen Durchflug. Zählt nur das nächste Gate in der Reihenfolge.
        /// Rückgabe: true, wenn es das erwartete Gate war.
        /// </summary>
        public bool PassGate(int index, double now)
        {
            Update(now);
            if (Phase != RacePhase.Running || index != NextGate) return false;

            splits.Add((float)(now - LapStartTime));
            NextGate++;
            if (NextGate < GateCount) return true;

            // Letztes Gate = Ziellinie dieser Runde.
            float lap = (float)(now - LapStartTime);
            lapTimes.Add(lap);
            bool best = BestLap <= 0f || lap < BestLap;
            if (best) BestLap = lap;
            LapCompleted?.Invoke(lap, best);

            if (CurrentLap >= Laps)
            {
                Phase = RacePhase.Finished;
                RaceCompleted?.Invoke(TotalTime(now));
            }
            else
            {
                CurrentLap++;
                NextGate = 0;
                LapStartTime = now;
                splits.Clear();
            }
            return true;
        }
    }
}
