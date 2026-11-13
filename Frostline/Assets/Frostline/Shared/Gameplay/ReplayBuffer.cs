using System;
using System.IO;
using System.Numerics;

namespace Frostline.Gameplay
{
    /// <summary>Ein aufgezeichneter Moment eines Flugs.</summary>
    public struct ReplayFrame
    {
        public float Time;
        public Vector3 Position;
        public Quaternion Rotation;
        public float Throttle;
        /// <summary>Mittlere Motordrehzahl relativ zum Maximum, 0..1 (für den Sound).</summary>
        public float MotorLoad;
    }

    /// <summary>
    /// Ringpuffer für Replays: zeichnet mit fester Rate auf, behält die letzten Minuten und
    /// liefert für jeden Zeitpunkt einen weich interpolierten Zustand.
    /// </summary>
    public sealed class ReplayBuffer
    {
        readonly ReplayFrame[] frames;
        int start;
        int count;

        public ReplayBuffer(int capacity)
        {
            if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
            frames = new ReplayFrame[capacity];
        }

        public int Count { get { return count; } }
        public int Capacity { get { return frames.Length; } }
        public float StartTime { get { return count > 0 ? this[0].Time : 0f; } }
        public float EndTime { get { return count > 0 ? this[count - 1].Time : 0f; } }
        public float Duration { get { return EndTime - StartTime; } }

        public ReplayFrame this[int index]
        {
            get { return frames[(start + index) % frames.Length]; }
        }

        public void Clear()
        {
            start = 0;
            count = 0;
        }

        public void Add(in ReplayFrame frame)
        {
            if (count > 0 && frame.Time <= this[count - 1].Time) return; // Zeit muss steigen
            if (count < frames.Length)
            {
                frames[(start + count) % frames.Length] = frame;
                count++;
            }
            else
            {
                frames[start] = frame;
                start = (start + 1) % frames.Length;
            }
        }

        /// <summary>Zustand zu einem beliebigen Zeitpunkt; außerhalb der Aufnahme der erste bzw. letzte.</summary>
        public ReplayFrame Sample(float time)
        {
            if (count == 0) return default(ReplayFrame);
            if (time <= StartTime) return this[0];
            if (time >= EndTime) return this[count - 1];

            // Binäre Suche nach dem letzten Frame vor time.
            int low = 0;
            int high = count - 1;
            while (high - low > 1)
            {
                int mid = (low + high) / 2;
                if (this[mid].Time <= time) low = mid;
                else high = mid;
            }

            ReplayFrame a = this[low];
            ReplayFrame b = this[high];
            float t = (time - a.Time) / Math.Max(b.Time - a.Time, 1e-6f);

            ReplayFrame result;
            result.Time = time;
            result.Position = Vector3.Lerp(a.Position, b.Position, t);
            result.Rotation = Quaternion.Slerp(a.Rotation, b.Rotation, t);
            result.Throttle = a.Throttle + (b.Throttle - a.Throttle) * t;
            result.MotorLoad = a.MotorLoad + (b.MotorLoad - a.MotorLoad) * t;
            return result;
        }

        /// <summary>Kopiert einen Abschnitt in einen neuen Puffer (z. B. die beste Runde als Ghost).</summary>
        public ReplayBuffer Extract(float fromTime, float toTime)
        {
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                float t = this[i].Time;
                if (t >= fromTime && t <= toTime) n++;
            }
            var result = new ReplayBuffer(Math.Max(2, n));
            for (int i = 0; i < count; i++)
            {
                ReplayFrame frame = this[i];
                if (frame.Time >= fromTime && frame.Time <= toTime)
                {
                    frame.Time -= fromTime;
                    result.Add(frame);
                }
            }
            return result;
        }

        // ------------------------------------------------------------ Speichern

        const uint FileMagic = 0x4C505246; // "FRPL"
        const int FileVersion = 1;

        public void Write(Stream stream)
        {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                writer.Write(FileMagic);
                writer.Write(FileVersion);
                writer.Write(count);
                for (int i = 0; i < count; i++)
                {
                    ReplayFrame f = this[i];
                    writer.Write(f.Time);
                    writer.Write(f.Position.X); writer.Write(f.Position.Y); writer.Write(f.Position.Z);
                    writer.Write(f.Rotation.X); writer.Write(f.Rotation.Y); writer.Write(f.Rotation.Z); writer.Write(f.Rotation.W);
                    writer.Write(f.Throttle);
                    writer.Write(f.MotorLoad);
                }
            }
        }

        public static ReplayBuffer Read(Stream stream)
        {
            using (var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true))
            {
                if (reader.ReadUInt32() != FileMagic) throw new InvalidDataException("Keine Frostline-Replay-Datei.");
                int version = reader.ReadInt32();
                if (version != FileVersion) throw new InvalidDataException("Replay-Version " + version + " wird nicht unterstützt.");
                int n = reader.ReadInt32();
                if (n < 0 || n > 10000000) throw new InvalidDataException("Replay-Datei beschädigt.");
                var buffer = new ReplayBuffer(Math.Max(2, n));
                for (int i = 0; i < n; i++)
                {
                    ReplayFrame f;
                    f.Time = reader.ReadSingle();
                    f.Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    f.Rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    f.Throttle = reader.ReadSingle();
                    f.MotorLoad = reader.ReadSingle();
                    buffer.Add(f);
                }
                return buffer;
            }
        }
    }
}
