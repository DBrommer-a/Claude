using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Frostline.Flight;
using Frostline.Flight.Core;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Frostline.Remote
{
    /// <summary>
    /// Empfängt die Sticks der iPhone-App „Frostline Remote“ per UDP und schickt Telemetrie
    /// zurück. Der Empfang läuft in einem eigenen Thread und schreibt nur den neuesten
    /// Zustand; die Physik liest ihn in jedem Schritt. Fehlen 200 ms lang Pakete, gilt das
    /// wie ein Funkabriss: Failsafe, Motoren aus.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class RemoteControlServer : MonoBehaviour
    {
        public int port = RemoteProtocol.Port;
        [Range(5, 60)] public int telemetryRateHz = 30;
        public bool advertiseBonjour = true;
        public DroneController drone;

        readonly RemoteLink link = new RemoteLink();
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly object clientGate = new object();
        readonly byte[] sendBuffer = new byte[RemoteProtocol.MaxPacketSize];

        Socket socket;
        Thread receiver;
        volatile bool running;
        Bonjour bonjour;
        EndPoint client;
        string clientName = "";
        float nextTelemetryTime;

        byte armEvents, crashEvents, groundEvents, gateEvents;
        bool wasArmed;

        public static RemoteControlServer Instance { get; private set; }

        public long NowMs { get { return clock.ElapsedMilliseconds; } }
        public bool IsConnected { get { return link.IsConnected(NowMs); } }
        public bool IsFailsafe { get { return link.IsFailsafe(NowMs); } }
        public float PacketsPerSecond { get { return link.PacketsPerSecond; } }
        public float LossPercent { get { return link.LossPercent; } }
        public bool BonjourActive { get { return bonjour != null && bonjour.Registered; } }
        public string Status { get; private set; }

        public string ClientName
        {
            get { lock (clientGate) return clientName; }
        }

        void OnEnable()
        {
            Instance = this;
            if (drone == null) drone = FindFirstObjectByType<DroneController>();
            Open();
            FrostlineEvents.Crashed += OnCrashed;
            FrostlineEvents.GroundTouched += OnGroundTouched;
            FrostlineEvents.GateTouched += OnGateTouched;
            FrostlineEvents.GatePassed += OnGatePassed;
        }

        void OnDisable()
        {
            FrostlineEvents.Crashed -= OnCrashed;
            FrostlineEvents.GroundTouched -= OnGroundTouched;
            FrostlineEvents.GateTouched -= OnGateTouched;
            FrostlineEvents.GatePassed -= OnGatePassed;
            Close();
            if (Instance == this) Instance = null;
        }

        void OnCrashed(float speed) { crashEvents++; }
        void OnGroundTouched() { groundEvents++; }
        void OnGateTouched() { gateEvents++; }
        void OnGatePassed(int index) { gateEvents++; }

        // ------------------------------------------------------------ Öffnen / Schließen

        void Open()
        {
            try
            {
                socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.Udp);
                socket.DualMode = true;
                socket.Bind(new IPEndPoint(IPAddress.IPv6Any, port));
            }
            catch (Exception)
            {
                // Ohne IPv6: nur IPv4.
                if (socket != null) socket.Close();
                try
                {
                    socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                    socket.Bind(new IPEndPoint(IPAddress.Any, port));
                }
                catch (Exception e)
                {
                    socket = null;
                    Status = "Port " + port + " belegt: " + e.Message;
                    Debug.LogWarning("Frostline: iPhone-Empfang nicht möglich. " + Status);
                    return;
                }
            }

            socket.ReceiveTimeout = 250;
            running = true;
            receiver = new Thread(ReceiveLoop) { IsBackground = true, Name = "Frostline Remote" };
            receiver.Start();

            if (advertiseBonjour) bonjour = Bonjour.Register(RemoteProtocol.BonjourType, port);
            Status = "Wartet auf iPhone (Port " + port + (BonjourActive ? ", Bonjour aktiv)" : ")");
        }

        void Close()
        {
            running = false;
            EndPoint target;
            lock (clientGate) target = client;
            if (socket != null && target != null)
            {
                try
                {
                    int length = RemoteProtocol.WriteGoodbye(sendBuffer);
                    socket.SendTo(sendBuffer, length, SocketFlags.None, target);
                }
                catch (Exception) { }
            }
            if (socket != null) socket.Close();
            socket = null;
            if (receiver != null && receiver.IsAlive) receiver.Join(500);
            receiver = null;
            if (bonjour != null) bonjour.Dispose();
            bonjour = null;
            link.Clear();
        }

        // ------------------------------------------------------------ Empfang (eigener Thread)

        void ReceiveLoop()
        {
            var buffer = new byte[256];
            var reply = new byte[RemoteProtocol.MaxPacketSize];
            string hostName = SystemInfo.deviceName;

            while (running)
            {
                EndPoint remote = socket.AddressFamily == AddressFamily.InterNetworkV6
                    ? new IPEndPoint(IPAddress.IPv6Any, 0)
                    : new IPEndPoint(IPAddress.Any, 0);
                int length;
                try
                {
                    length = socket.ReceiveFrom(buffer, ref remote);
                }
                catch (SocketException)
                {
                    continue; // Zeitüberschreitung oder ICMP-Fehler: weiter warten
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                RemoteProtocol.PacketType type;
                if (!RemoteProtocol.TryReadType(buffer, length, out type)) continue;

                switch (type)
                {
                    case RemoteProtocol.PacketType.Control:
                        ControlPacket control;
                        if (RemoteProtocol.TryReadControl(buffer, length, out control) && link.Receive(control, NowMs))
                        {
                            lock (clientGate) client = remote;
                        }
                        break;

                    case RemoteProtocol.PacketType.Hello:
                        uint session;
                        string name;
                        if (RemoteProtocol.TryReadHello(buffer, length, out session, out name))
                        {
                            lock (clientGate)
                            {
                                client = remote;
                                clientName = name;
                            }
                            try
                            {
                                int size = RemoteProtocol.WriteWelcome(reply, session, (ushort)telemetryRateHz, hostName);
                                socket.SendTo(reply, size, SocketFlags.None, remote);
                            }
                            catch (Exception) { }
                        }
                        break;

                    case RemoteProtocol.PacketType.Goodbye:
                        link.Clear();
                        lock (clientGate)
                        {
                            client = null;
                            clientName = "";
                        }
                        break;
                }
            }
        }

        // ------------------------------------------------------------ Für StickInput

        public ControlInput ReadInput()
        {
            return link.Read(NowMs);
        }

        public bool ConsumeReset()
        {
            return link.ConsumeReset();
        }

        // ------------------------------------------------------------ Telemetrie

        void Update()
        {
            if (drone != null)
            {
                bool armed = drone.Telemetry.Armed;
                if (armed && !wasArmed) armEvents++;
                wasArmed = armed;
            }

            if (socket == null) return;
            if (IsConnected) Status = "Verbunden: " + ClientName + "  ·  " + PacketsPerSecond.ToString("0") + " Pakete/s  ·  Verlust " + LossPercent.ToString("0") + " %";
            else Status = "Wartet auf iPhone (Port " + port + (BonjourActive ? ", Bonjour aktiv)" : ")");

            if (Time.unscaledTime < nextTelemetryTime) return;
            nextTelemetryTime = Time.unscaledTime + 1f / Mathf.Max(1, telemetryRateHz);

            EndPoint target;
            lock (clientGate) target = client;
            if (target == null || !IsConnected) return;

            try
            {
                TelemetryPacket packet = BuildTelemetry();
                int length = RemoteProtocol.WriteTelemetry(sendBuffer, packet);
                socket.SendTo(sendBuffer, length, SocketFlags.None, target);
            }
            catch (Exception)
            {
                // Netz kurz weg: nächste Runde erneut versuchen.
            }
        }

        TelemetryPacket BuildTelemetry()
        {
            long now = NowMs;
            var packet = new TelemetryPacket
            {
                EchoSequence = link.LastSequence,
                EchoSendTimeMs = link.LastSendTimeMs,
                HoldMs = (ushort)Mathf.Clamp(now - link.LastPacketMs, 0, ushort.MaxValue),
                LapTimeMs = FrostlineEvents.CurrentLapSeconds >= 0f
                    ? (uint)Mathf.RoundToInt(FrostlineEvents.CurrentLapSeconds * 1000f)
                    : RemoteProtocol.NoLapTime,
                ArmEvents = armEvents,
                CrashEvents = crashEvents,
                GroundEvents = groundEvents,
                GateEvents = gateEvents,
            };

            if (drone != null)
            {
                Telemetry t = drone.Telemetry;
                packet.BatteryCentivolts = (ushort)Mathf.Clamp(Mathf.RoundToInt(t.BatteryVoltage * 100f), 0, ushort.MaxValue);
                packet.CellMillivolts = (ushort)Mathf.Clamp(Mathf.RoundToInt(t.CellVoltage * 1000f), 0, ushort.MaxValue);
                packet.BatteryPercent = (byte)Mathf.Clamp(Mathf.RoundToInt(t.BatteryPercent), 0, 100);
                packet.Mode = (byte)t.Mode;
                packet.SpeedKmhTenths = (ushort)Mathf.Clamp(Mathf.RoundToInt(drone.SpeedMetersPerSecond * 36f), 0, ushort.MaxValue);
                packet.AltitudeDecimeters = (short)Mathf.Clamp(Mathf.RoundToInt(drone.transform.position.y * 10f), short.MinValue, short.MaxValue);

                TelemetryFlags flags = TelemetryFlags.None;
                if (t.Armed) flags |= TelemetryFlags.Armed;
                if (t.Failsafe) flags |= TelemetryFlags.Failsafe;
                if (t.TurtleActive) flags |= TelemetryFlags.Turtle;
                if (t.ArmingBlockedByThrottle) flags |= TelemetryFlags.ArmingBlocked;
                if (FrostlineEvents.CurrentLapSeconds >= 0f) flags |= TelemetryFlags.RaceRunning;
                packet.Flags = flags;
            }
            return packet;
        }
    }
}
