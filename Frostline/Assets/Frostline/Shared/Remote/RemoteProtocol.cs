using System;
using System.Buffers.Binary;
using System.Text;

namespace Frostline.Remote
{
    /// <summary>
    /// Funkprotokoll zwischen der iPhone-App „Frostline Remote“ und dem Spiel auf dem Mac.
    /// UDP, Little Endian, jedes Paket beginnt mit „FLRM“, Version und Typ.
    /// Dieselbe Definition steht in Swift in FrostlineRemoteKit/RemoteProtocol.swift;
    /// beide Seiten prüfen gegen dieselben Testvektoren.
    /// </summary>
    public static class RemoteProtocol
    {
        public const int Port = 47820;
        public const string BonjourType = "_frostline._udp";
        public const byte Version = 1;
        public const int HeaderSize = 8;
        public const int MaxPacketSize = 128;
        public const int MaxNameBytes = 32;

        /// <summary>Bleiben so lange Steuerpakete aus, geht die Drohne in Failsafe (Motoren aus).</summary>
        public const int FailsafeMilliseconds = 200;

        public const uint NoLapTime = 0xFFFFFFFFu;

        static readonly byte[] Magic = { (byte)'F', (byte)'L', (byte)'R', (byte)'M' };

        public enum PacketType : byte
        {
            Control = 1,
            Hello = 2,
            Welcome = 3,
            Telemetry = 4,
            Goodbye = 5,
        }

        public const int ControlSize = HeaderSize + 18;
        public const int TelemetrySize = HeaderSize + 29;

        // ------------------------------------------------------------ Kopf

        public static void WriteHeader(byte[] buffer, PacketType type)
        {
            buffer[0] = Magic[0];
            buffer[1] = Magic[1];
            buffer[2] = Magic[2];
            buffer[3] = Magic[3];
            buffer[4] = Version;
            buffer[5] = (byte)type;
            buffer[6] = 0;
            buffer[7] = 0;
        }

        /// <summary>Liest den Pakettyp; false bei fremden, kaputten oder zu neuen Paketen.</summary>
        public static bool TryReadType(byte[] data, int length, out PacketType type)
        {
            type = 0;
            if (data == null || length < HeaderSize) return false;
            if (data[0] != Magic[0] || data[1] != Magic[1] || data[2] != Magic[2] || data[3] != Magic[3]) return false;
            if (data[4] != Version) return false;
            type = (PacketType)data[5];
            return true;
        }

        // ------------------------------------------------------------ Steuerung (App -> Spiel)

        public static int WriteControl(byte[] buffer, in ControlPacket packet)
        {
            WriteHeader(buffer, PacketType.Control);
            var span = new Span<byte>(buffer, HeaderSize, ControlSize - HeaderSize);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0), packet.Sequence);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), packet.SendTimeMs);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(8), packet.Roll);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(10), packet.Pitch);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(12), packet.Yaw);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(14), packet.Throttle);
            span[16] = (byte)packet.Buttons;
            span[17] = packet.Mode;
            return ControlSize;
        }

        public static bool TryReadControl(byte[] data, int length, out ControlPacket packet)
        {
            packet = default(ControlPacket);
            PacketType type;
            if (!TryReadType(data, length, out type) || type != PacketType.Control || length < ControlSize) return false;
            var span = new ReadOnlySpan<byte>(data, HeaderSize, ControlSize - HeaderSize);
            packet.Sequence = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0));
            packet.SendTimeMs = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4));
            packet.Roll = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(8));
            packet.Pitch = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(10));
            packet.Yaw = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(12));
            packet.Throttle = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(14));
            packet.Buttons = (RemoteButtons)span[16];
            packet.Mode = span[17];
            return true;
        }

        // ------------------------------------------------------------ Telemetrie (Spiel -> App)

        public static int WriteTelemetry(byte[] buffer, in TelemetryPacket packet)
        {
            WriteHeader(buffer, PacketType.Telemetry);
            var span = new Span<byte>(buffer, HeaderSize, TelemetrySize - HeaderSize);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(0), packet.EchoSequence);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4), packet.EchoSendTimeMs);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(8), packet.HoldMs);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(10), packet.BatteryCentivolts);
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(12), packet.CellMillivolts);
            span[14] = packet.BatteryPercent;
            span[15] = (byte)packet.Flags;
            span[16] = packet.Mode;
            BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(17), packet.SpeedKmhTenths);
            BinaryPrimitives.WriteInt16LittleEndian(span.Slice(19), packet.AltitudeDecimeters);
            BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(21), packet.LapTimeMs);
            span[25] = packet.ArmEvents;
            span[26] = packet.CrashEvents;
            span[27] = packet.GroundEvents;
            span[28] = packet.GateEvents;
            return TelemetrySize;
        }

        public static bool TryReadTelemetry(byte[] data, int length, out TelemetryPacket packet)
        {
            packet = default(TelemetryPacket);
            PacketType type;
            if (!TryReadType(data, length, out type) || type != PacketType.Telemetry || length < TelemetrySize) return false;
            var span = new ReadOnlySpan<byte>(data, HeaderSize, TelemetrySize - HeaderSize);
            packet.EchoSequence = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0));
            packet.EchoSendTimeMs = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(4));
            packet.HoldMs = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(8));
            packet.BatteryCentivolts = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(10));
            packet.CellMillivolts = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(12));
            packet.BatteryPercent = span[14];
            packet.Flags = (TelemetryFlags)span[15];
            packet.Mode = span[16];
            packet.SpeedKmhTenths = BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(17));
            packet.AltitudeDecimeters = BinaryPrimitives.ReadInt16LittleEndian(span.Slice(19));
            packet.LapTimeMs = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(21));
            packet.ArmEvents = span[25];
            packet.CrashEvents = span[26];
            packet.GroundEvents = span[27];
            packet.GateEvents = span[28];
            return true;
        }

        // ------------------------------------------------------------ Hallo / Willkommen

        /// <summary>App meldet sich an: Sitzungsnummer und Gerätename.</summary>
        public static int WriteHello(byte[] buffer, uint session, string name)
        {
            WriteHeader(buffer, PacketType.Hello);
            BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(buffer, HeaderSize, 4), session);
            return HeaderSize + 4 + WriteName(buffer, HeaderSize + 4, name);
        }

        public static bool TryReadHello(byte[] data, int length, out uint session, out string name)
        {
            session = 0;
            name = "";
            PacketType type;
            if (!TryReadType(data, length, out type) || type != PacketType.Hello || length < HeaderSize + 5) return false;
            session = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(data, HeaderSize, 4));
            return TryReadName(data, length, HeaderSize + 4, out name);
        }

        /// <summary>Spiel antwortet: Sitzungsnummer zurück, Telemetrie-Rate und Name des Macs.</summary>
        public static int WriteWelcome(byte[] buffer, uint session, ushort telemetryRateHz, string name)
        {
            WriteHeader(buffer, PacketType.Welcome);
            BinaryPrimitives.WriteUInt32LittleEndian(new Span<byte>(buffer, HeaderSize, 4), session);
            BinaryPrimitives.WriteUInt16LittleEndian(new Span<byte>(buffer, HeaderSize + 4, 2), telemetryRateHz);
            return HeaderSize + 6 + WriteName(buffer, HeaderSize + 6, name);
        }

        public static bool TryReadWelcome(byte[] data, int length, out uint session, out ushort telemetryRateHz, out string name)
        {
            session = 0;
            telemetryRateHz = 0;
            name = "";
            PacketType type;
            if (!TryReadType(data, length, out type) || type != PacketType.Welcome || length < HeaderSize + 7) return false;
            session = BinaryPrimitives.ReadUInt32LittleEndian(new ReadOnlySpan<byte>(data, HeaderSize, 4));
            telemetryRateHz = BinaryPrimitives.ReadUInt16LittleEndian(new ReadOnlySpan<byte>(data, HeaderSize + 4, 2));
            return TryReadName(data, length, HeaderSize + 6, out name);
        }

        public static int WriteGoodbye(byte[] buffer)
        {
            WriteHeader(buffer, PacketType.Goodbye);
            return HeaderSize;
        }

        static int WriteName(byte[] buffer, int offset, string name)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(name ?? "");
            int count = Math.Min(bytes.Length, MaxNameBytes);
            // Nicht mitten in einem UTF-8-Zeichen abschneiden.
            while (count > 0 && count < bytes.Length && (bytes[count] & 0xC0) == 0x80) count--;
            buffer[offset] = (byte)count;
            Array.Copy(bytes, 0, buffer, offset + 1, count);
            return 1 + count;
        }

        static bool TryReadName(byte[] data, int length, int offset, out string name)
        {
            name = "";
            if (length < offset + 1) return false;
            int count = data[offset];
            if (count > MaxNameBytes || length < offset + 1 + count) return false;
            name = Encoding.UTF8.GetString(data, offset + 1, count);
            return true;
        }

        // ------------------------------------------------------------ Umrechnung

        public static short EncodeAxis(float value)
        {
            if (float.IsNaN(value)) return 0;
            float clamped = value < -1f ? -1f : (value > 1f ? 1f : value);
            return (short)Math.Round(clamped * 32767f);
        }

        public static float DecodeAxis(short value)
        {
            float v = value / 32767f;
            return v < -1f ? -1f : v;
        }

        public static ushort EncodeThrottle(float value)
        {
            if (float.IsNaN(value)) return 0;
            float clamped = value < 0f ? 0f : (value > 1f ? 1f : value);
            return (ushort)Math.Round(clamped * 65535f);
        }

        public static float DecodeThrottle(ushort value)
        {
            return value / 65535f;
        }

        /// <summary>true, wenn <paramref name="a"/> neuer ist als <paramref name="b"/>; berücksichtigt den Überlauf.</summary>
        public static bool IsNewer(uint a, uint b)
        {
            return (int)(a - b) > 0;
        }
    }

    [Flags]
    public enum RemoteButtons : byte
    {
        None = 0,
        Arm = 1,
        Turtle = 2,
        Reset = 4,
        GyroActive = 8,
    }

    [Flags]
    public enum TelemetryFlags : byte
    {
        None = 0,
        Armed = 1,
        Failsafe = 2,
        Turtle = 4,
        ArmingBlocked = 8,
        RaceRunning = 16,
    }

    public struct ControlPacket
    {
        public uint Sequence;
        /// <summary>Uhr der App in Millisekunden; kommt in der Telemetrie zurück, damit die App die Laufzeit misst.</summary>
        public uint SendTimeMs;
        public short Roll;
        public short Pitch;
        public short Yaw;
        public ushort Throttle;
        public RemoteButtons Buttons;
        /// <summary>0 Acro, 1 Angle, 2 Horizon.</summary>
        public byte Mode;
    }

    public struct TelemetryPacket
    {
        public uint EchoSequence;
        public uint EchoSendTimeMs;
        /// <summary>Wie lange das zuletzt empfangene Steuerpaket schon im Spiel lag, bevor diese Telemetrie rausging.</summary>
        public ushort HoldMs;
        public ushort BatteryCentivolts;
        public ushort CellMillivolts;
        public byte BatteryPercent;
        public TelemetryFlags Flags;
        public byte Mode;
        public ushort SpeedKmhTenths;
        public short AltitudeDecimeters;
        public uint LapTimeMs;
        // Zähler statt einmaliger Ereignisse: geht ein Paket verloren, merkt die App die Änderung beim nächsten.
        public byte ArmEvents;
        public byte CrashEvents;
        public byte GroundEvents;
        public byte GateEvents;
    }
}
