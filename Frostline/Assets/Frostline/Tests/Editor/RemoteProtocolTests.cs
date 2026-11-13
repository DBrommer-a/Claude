using System;
using Frostline.Flight.Core;
using Frostline.Remote;
using NUnit.Framework;

namespace Frostline.Tests
{
    /// <summary>
    /// Prüft das Funkprotokoll gegen feste Testvektoren. Die Swift-Seite
    /// (FrostlineRemoteKit) prüft gegen genau dieselben Bytes.
    /// </summary>
    public class RemoteProtocolTests
    {
        const string ControlHex = "464c524d010100000403020140e20100004001800000ffff0902";
        const string TelemetryHex = "464c524d010400000403020140e20100070006090a0f4c1101d204e7ff32ef0000030102fa";
        const string HelloHex = "464c524d01020000bebafeca0b54657374206950686f6e65";
        const string WelcomeHex = "464c524d01030000bebafeca1e00084d6163206d696e69";

        static string Hex(byte[] data, int length)
        {
            return BitConverter.ToString(data, 0, length).Replace("-", "").ToLowerInvariant();
        }

        static byte[] Bytes(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return bytes;
        }

        static ControlPacket SampleControl()
        {
            return new ControlPacket
            {
                Sequence = 0x01020304,
                SendTimeMs = 123456,
                Roll = 16384,
                Pitch = -32767,
                Yaw = 0,
                Throttle = 65535,
                Buttons = RemoteButtons.Arm | RemoteButtons.GyroActive,
                Mode = 2,
            };
        }

        static TelemetryPacket SampleTelemetry()
        {
            return new TelemetryPacket
            {
                EchoSequence = 0x01020304,
                EchoSendTimeMs = 123456,
                HoldMs = 7,
                BatteryCentivolts = 2310,
                CellMillivolts = 3850,
                BatteryPercent = 76,
                Flags = TelemetryFlags.Armed | TelemetryFlags.RaceRunning,
                Mode = 1,
                SpeedKmhTenths = 1234,
                AltitudeDecimeters = -25,
                LapTimeMs = 61234,
                ArmEvents = 3,
                CrashEvents = 1,
                GroundEvents = 2,
                GateEvents = 250,
            };
        }

        [Test]
        public void Control_MatchesTestVector()
        {
            var buffer = new byte[RemoteProtocol.MaxPacketSize];
            int length = RemoteProtocol.WriteControl(buffer, SampleControl());
            Assert.That(length, Is.EqualTo(26));
            Assert.That(Hex(buffer, length), Is.EqualTo(ControlHex));

            ControlPacket read;
            byte[] data = Bytes(ControlHex);
            Assert.That(RemoteProtocol.TryReadControl(data, data.Length, out read), Is.True);
            Assert.That(read.Sequence, Is.EqualTo(0x01020304u));
            Assert.That(read.Roll, Is.EqualTo(16384));
            Assert.That(read.Pitch, Is.EqualTo(-32767));
            Assert.That(read.Throttle, Is.EqualTo(65535));
            Assert.That(read.Buttons, Is.EqualTo(RemoteButtons.Arm | RemoteButtons.GyroActive));
            Assert.That(read.Mode, Is.EqualTo(2));
        }

        [Test]
        public void Telemetry_MatchesTestVector()
        {
            var buffer = new byte[RemoteProtocol.MaxPacketSize];
            int length = RemoteProtocol.WriteTelemetry(buffer, SampleTelemetry());
            Assert.That(length, Is.EqualTo(37));
            Assert.That(Hex(buffer, length), Is.EqualTo(TelemetryHex));

            TelemetryPacket read;
            byte[] data = Bytes(TelemetryHex);
            Assert.That(RemoteProtocol.TryReadTelemetry(data, data.Length, out read), Is.True);
            Assert.That(read.AltitudeDecimeters, Is.EqualTo(-25));
            Assert.That(read.LapTimeMs, Is.EqualTo(61234u));
            Assert.That(read.GateEvents, Is.EqualTo(250));
            Assert.That(read.Flags, Is.EqualTo(TelemetryFlags.Armed | TelemetryFlags.RaceRunning));
        }

        [Test]
        public void HelloAndWelcome_MatchTestVectors()
        {
            var buffer = new byte[RemoteProtocol.MaxPacketSize];
            int length = RemoteProtocol.WriteHello(buffer, 0xCAFEBABE, "Test iPhone");
            Assert.That(Hex(buffer, length), Is.EqualTo(HelloHex));
            length = RemoteProtocol.WriteWelcome(buffer, 0xCAFEBABE, 30, "Mac mini");
            Assert.That(Hex(buffer, length), Is.EqualTo(WelcomeHex));

            uint session;
            string name;
            ushort rate;
            byte[] hello = Bytes(HelloHex);
            Assert.That(RemoteProtocol.TryReadHello(hello, hello.Length, out session, out name), Is.True);
            Assert.That(session, Is.EqualTo(0xCAFEBABEu));
            Assert.That(name, Is.EqualTo("Test iPhone"));
            byte[] welcome = Bytes(WelcomeHex);
            Assert.That(RemoteProtocol.TryReadWelcome(welcome, welcome.Length, out session, out rate, out name), Is.True);
            Assert.That(rate, Is.EqualTo(30));
            Assert.That(name, Is.EqualTo("Mac mini"));
        }

        [Test]
        public void LongNames_AreCutWithoutBreakingCharacters()
        {
            var buffer = new byte[RemoteProtocol.MaxPacketSize];
            string longName = new string('ä', 40); // 2 Bytes pro Zeichen
            int length = RemoteProtocol.WriteHello(buffer, 1, longName);
            uint session;
            string name;
            Assert.That(RemoteProtocol.TryReadHello(buffer, length, out session, out name), Is.True);
            Assert.That(name, Is.EqualTo(new string('ä', 16)));
        }

        [Test]
        public void ForeignAndBrokenPackets_AreRejected()
        {
            ControlPacket packet;
            byte[] data = Bytes(ControlHex);
            Assert.That(RemoteProtocol.TryReadControl(data, 20, out packet), Is.False, "zu kurz");
            data[0] = (byte)'X';
            Assert.That(RemoteProtocol.TryReadControl(data, data.Length, out packet), Is.False, "falsche Kennung");
            data = Bytes(ControlHex);
            data[4] = 99;
            Assert.That(RemoteProtocol.TryReadControl(data, data.Length, out packet), Is.False, "falsche Version");
            data = Bytes(TelemetryHex);
            Assert.That(RemoteProtocol.TryReadControl(data, data.Length, out packet), Is.False, "falscher Typ");
            Assert.That(RemoteProtocol.TryReadControl(null, 0, out packet), Is.False);
        }

        [Test]
        public void AxisEncoding_RoundTripsAndClamps()
        {
            Assert.That(RemoteProtocol.DecodeAxis(RemoteProtocol.EncodeAxis(0.5f)), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(RemoteProtocol.DecodeAxis(RemoteProtocol.EncodeAxis(-1f)), Is.EqualTo(-1f));
            Assert.That(RemoteProtocol.EncodeAxis(3f), Is.EqualTo(32767));
            Assert.That(RemoteProtocol.EncodeAxis(float.NaN), Is.EqualTo(0));
            Assert.That(RemoteProtocol.DecodeAxis(short.MinValue), Is.EqualTo(-1f));
            Assert.That(RemoteProtocol.DecodeThrottle(RemoteProtocol.EncodeThrottle(0.25f)), Is.EqualTo(0.25f).Within(1e-4f));
            Assert.That(RemoteProtocol.EncodeThrottle(-1f), Is.EqualTo(0));
        }

        [Test]
        public void SequenceComparison_HandlesWrapAround()
        {
            Assert.That(RemoteProtocol.IsNewer(5, 4), Is.True);
            Assert.That(RemoteProtocol.IsNewer(4, 5), Is.False);
            Assert.That(RemoteProtocol.IsNewer(2, 0xFFFFFFF0u), Is.True);
            Assert.That(RemoteProtocol.IsNewer(7, 7), Is.False);
        }

        // ------------------------------------------------------------ Verbindung

        static ControlPacket Packet(uint sequence, float throttle = 0.5f, RemoteButtons buttons = RemoteButtons.Arm)
        {
            return new ControlPacket
            {
                Sequence = sequence,
                Roll = RemoteProtocol.EncodeAxis(0.25f),
                Throttle = RemoteProtocol.EncodeThrottle(throttle),
                Buttons = buttons,
                Mode = 1,
            };
        }

        [Test]
        public void Link_DeliversLatestInput()
        {
            var link = new RemoteLink();
            Assert.That(link.Read(0).SignalValid, Is.False, "ohne Paket kein Signal");

            link.Receive(Packet(1, 0.3f), 1000);
            ControlInput input = link.Read(1010);
            Assert.That(input.SignalValid, Is.True);
            Assert.That(input.Throttle, Is.EqualTo(0.3f).Within(1e-4f));
            Assert.That(input.Roll, Is.EqualTo(0.25f).Within(1e-4f));
            Assert.That(input.ArmSwitch, Is.True);
            Assert.That(input.Mode, Is.EqualTo(FlightMode.Angle));
        }

        [Test]
        public void Link_IgnoresOldAndDuplicatePackets()
        {
            var link = new RemoteLink();
            Assert.That(link.Receive(Packet(10, 0.6f), 0), Is.True);
            Assert.That(link.Receive(Packet(9, 0.1f), 1), Is.False);
            Assert.That(link.Receive(Packet(10, 0.1f), 2), Is.False);
            Assert.That(link.Read(3).Throttle, Is.EqualTo(0.6f).Within(1e-4f));
        }

        [Test]
        public void Link_AcceptsRestartedApp()
        {
            var link = new RemoteLink();
            link.Receive(Packet(500000, 0.6f), 0);
            Assert.That(link.Receive(Packet(1, 0.2f), 10), Is.True);
            Assert.That(link.Read(11).Throttle, Is.EqualTo(0.2f).Within(1e-4f));
        }

        [Test]
        public void Link_GoesToFailsafeAfter200Milliseconds()
        {
            var link = new RemoteLink();
            link.Receive(Packet(1), 1000);
            Assert.That(link.IsFailsafe(1199), Is.False);
            Assert.That(link.Read(1200).SignalValid, Is.True);
            Assert.That(link.IsFailsafe(1201), Is.True);
            ControlInput input = link.Read(1201);
            Assert.That(input.SignalValid, Is.False);
            Assert.That(input.Throttle, Is.EqualTo(0f));
            Assert.That(input.ArmSwitch, Is.False);
        }

        [Test]
        public void Link_ReportsResetOncePerPress()
        {
            var link = new RemoteLink();
            link.Receive(Packet(1, 0f, RemoteButtons.Reset), 0);
            link.Receive(Packet(2, 0f, RemoteButtons.Reset), 4);
            Assert.That(link.ConsumeReset(), Is.True);
            Assert.That(link.ConsumeReset(), Is.False, "gehalten zählt nicht doppelt");
            link.Receive(Packet(3, 0f, RemoteButtons.None), 8);
            link.Receive(Packet(4, 0f, RemoteButtons.Reset), 12);
            Assert.That(link.ConsumeReset(), Is.True);
        }

        [Test]
        public void Link_MeasuresRateAndLoss()
        {
            var link = new RemoteLink();
            uint sequence = 0;
            // 250 Pakete pro Sekunde, jedes fünfte geht verloren.
            for (int i = 0; i <= 500; i++)
            {
                sequence++;
                if (i % 5 == 4) continue;
                link.Receive(Packet(sequence), i * 4);
            }
            Assert.That(link.PacketsPerSecond, Is.InRange(190f, 210f));
            Assert.That(link.LossPercent, Is.InRange(17f, 23f));
        }
    }
}
