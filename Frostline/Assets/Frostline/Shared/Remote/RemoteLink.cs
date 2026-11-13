using Frostline.Flight.Core;

namespace Frostline.Remote
{
    /// <summary>
    /// Zustand der Verbindung zur iPhone-App, ohne Netzwerk-Code: nimmt empfangene
    /// Steuerpakete entgegen, verwirft alte und doppelte, zählt Verluste und entscheidet
    /// über Failsafe. Zeit wird von außen übergeben, damit sich alles testen lässt.
    /// </summary>
    public sealed class RemoteLink
    {
        readonly object gate = new object();

        ControlPacket latest;
        bool hasPacket;
        long lastPacketMs;
        bool resetLatched;
        bool resetWasDown;

        // Statistik über ein gleitendes Fenster von einer Sekunde.
        long windowStartMs;
        int windowReceived;
        uint windowFirstSequence;
        bool windowHasFirst;
        uint windowLastSequence;

        public float PacketsPerSecond { get; private set; }
        public float LossPercent { get; private set; }
        public uint LastSequence { get { lock (gate) return latest.Sequence; } }
        public uint LastSendTimeMs { get { lock (gate) return latest.SendTimeMs; } }
        public long LastPacketMs { get { lock (gate) return lastPacketMs; } }

        /// <summary>Von der Empfangs-Routine aufrufen. false = Paket war alt oder doppelt.</summary>
        public bool Receive(in ControlPacket packet, long nowMs)
        {
            lock (gate)
            {
                if (hasPacket && !RemoteProtocol.IsNewer(packet.Sequence, latest.Sequence))
                {
                    // Ein deutlich kleinerer Zähler heißt: App wurde neu gestartet.
                    bool restarted = latest.Sequence - packet.Sequence > 10000u;
                    if (!restarted) return false;
                    windowHasFirst = false;
                }

                latest = packet;
                hasPacket = true;
                lastPacketMs = nowMs;

                bool resetDown = (packet.Buttons & RemoteButtons.Reset) != 0;
                if (resetDown && !resetWasDown) resetLatched = true;
                resetWasDown = resetDown;

                if (!windowHasFirst)
                {
                    windowHasFirst = true;
                    windowFirstSequence = packet.Sequence;
                    windowStartMs = nowMs;
                    windowReceived = 0;
                }
                windowReceived++;
                windowLastSequence = packet.Sequence;

                long span = nowMs - windowStartMs;
                if (span >= 1000)
                {
                    uint expected = windowLastSequence - windowFirstSequence + 1;
                    PacketsPerSecond = windowReceived * 1000f / span;
                    LossPercent = expected > 0 ? MathUtil.Clamp(100f * (1f - windowReceived / (float)expected), 0f, 100f) : 0f;
                    windowFirstSequence = packet.Sequence;
                    windowStartMs = nowMs;
                    windowReceived = 1;
                }
                return true;
            }
        }

        public bool IsConnected(long nowMs)
        {
            lock (gate) return hasPacket && nowMs - lastPacketMs < 2000;
        }

        public bool IsFailsafe(long nowMs)
        {
            lock (gate) return !hasPacket || nowMs - lastPacketMs > RemoteProtocol.FailsafeMilliseconds;
        }

        /// <summary>Liefert true genau einmal pro Druck auf „Reset“ in der App.</summary>
        public bool ConsumeReset()
        {
            lock (gate)
            {
                bool value = resetLatched;
                resetLatched = false;
                return value;
            }
        }

        /// <summary>Aktuelle Eingabe für die Flugphysik.</summary>
        public ControlInput Read(long nowMs)
        {
            lock (gate)
            {
                ControlInput input = default(ControlInput);
                input.SignalValid = hasPacket && nowMs - lastPacketMs <= RemoteProtocol.FailsafeMilliseconds;
                if (!input.SignalValid) return input;

                input.Roll = RemoteProtocol.DecodeAxis(latest.Roll);
                input.Pitch = RemoteProtocol.DecodeAxis(latest.Pitch);
                input.Yaw = RemoteProtocol.DecodeAxis(latest.Yaw);
                input.Throttle = RemoteProtocol.DecodeThrottle(latest.Throttle);
                input.ArmSwitch = (latest.Buttons & RemoteButtons.Arm) != 0;
                input.TurtleSwitch = (latest.Buttons & RemoteButtons.Turtle) != 0;
                input.Mode = latest.Mode <= 2 ? (FlightMode)latest.Mode : FlightMode.Acro;
                return input;
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                hasPacket = false;
                windowHasFirst = false;
                resetLatched = false;
                resetWasDown = false;
                PacketsPerSecond = 0f;
                LossPercent = 0f;
            }
        }
    }
}
