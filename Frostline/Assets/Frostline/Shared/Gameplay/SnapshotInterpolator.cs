using System;
using System.Numerics;

namespace Frostline.Gameplay
{
    /// <summary>Zustand einer fremden Drohne, wie er im Multiplayer übertragen wird.</summary>
    public struct DroneSnapshot
    {
        /// <summary>Serverzeit in Sekunden, zu der der Zustand galt.</summary>
        public double Time;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public float Throttle;
    }

    /// <summary>
    /// Spielt fremde Drohnen mit etwa 100 ms Verzögerung weich ab. Zwischen zwei Zuständen wird
    /// mit Hermite-Kurven interpoliert, die auch die Geschwindigkeit berücksichtigen; bleiben
    /// Pakete kurz aus, wird höchstens 250 ms vorausgerechnet.
    /// </summary>
    public sealed class SnapshotInterpolator
    {
        public double Delay = 0.1;
        public double MaxExtrapolation = 0.25;

        readonly DroneSnapshot[] buffer;
        int count;
        int head; // Index des neuesten

        public SnapshotInterpolator(int capacity = 32)
        {
            buffer = new DroneSnapshot[Math.Max(4, capacity)];
        }

        public int Count { get { return count; } }

        public void Clear()
        {
            count = 0;
            head = 0;
        }

        public void Add(in DroneSnapshot snapshot)
        {
            if (count > 0)
            {
                DroneSnapshot newest = buffer[head];
                if (snapshot.Time <= newest.Time) return; // alt oder doppelt (UDP)
            }
            head = (head + 1) % buffer.Length;
            buffer[head] = snapshot;
            if (count < buffer.Length) count++;
        }

        DroneSnapshot At(int age)
        {
            int index = (head - age) % buffer.Length;
            if (index < 0) index += buffer.Length;
            return buffer[index];
        }

        public bool TrySample(double now, out DroneSnapshot result)
        {
            result = default(DroneSnapshot);
            if (count == 0) return false;

            double renderTime = now - Delay;
            DroneSnapshot newest = At(0);

            if (count == 1 || renderTime >= newest.Time)
            {
                double ahead = Math.Min(renderTime - newest.Time, MaxExtrapolation);
                if (ahead < 0) ahead = 0;
                result = newest;
                result.Time = renderTime;
                result.Position = newest.Position + newest.Velocity * (float)ahead;
                return true;
            }

            for (int age = 1; age < count; age++)
            {
                DroneSnapshot older = At(age);
                if (older.Time <= renderTime)
                {
                    DroneSnapshot newer = At(age - 1);
                    result = Interpolate(older, newer, renderTime);
                    return true;
                }
            }

            result = At(count - 1); // älter als alles im Puffer
            return true;
        }

        static DroneSnapshot Interpolate(in DroneSnapshot a, in DroneSnapshot b, double time)
        {
            float span = (float)(b.Time - a.Time);
            float t = span > 1e-6f ? (float)((time - a.Time) / span) : 1f;
            t = Math.Max(0f, Math.Min(1f, t));

            float t2 = t * t;
            float t3 = t2 * t;
            float h00 = 2f * t3 - 3f * t2 + 1f;
            float h10 = t3 - 2f * t2 + t;
            float h01 = -2f * t3 + 3f * t2;
            float h11 = t3 - t2;

            DroneSnapshot r;
            r.Time = time;
            r.Position = a.Position * h00 + a.Velocity * (h10 * span) + b.Position * h01 + b.Velocity * (h11 * span);
            r.Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t);
            r.Velocity = Vector3.Lerp(a.Velocity, b.Velocity, t);
            r.Throttle = a.Throttle + (b.Throttle - a.Throttle) * t;
            return r;
        }
    }
}
