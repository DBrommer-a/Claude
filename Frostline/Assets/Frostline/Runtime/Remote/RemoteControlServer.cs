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

        // ------------------------------------------------------------ Weg übers Internet (Frostline Relay)

        /// <summary>Unveränderliche Kopplung; wird als Ganzes ersetzt (Empfangs-Thread liest sie).</summary>
        sealed class RelayConfig
        {
            public string Host;
            public int Port;
            public byte[] Secret;
            public byte[] Room;
            public volatile IPEndPoint Endpoint;
        }

        /// <summary>
        /// Server für die Kopplung per QR-Code (Frostline Relay, UDP 47821); leer = nur WLAN. Im Menü änderbar.
        /// Eingerichtet mit tools/relay/einrichten.sh.
        /// </summary>
        public const string DefaultRelayHost = "31.70.130.250";
        const string RelayHostKey = "Frostline.Relay.Host";
        const string RelayPortKey = "Frostline.Relay.Port";
        const string RelaySecretKey = "Frostline.Relay.Secret";

        volatile RelayConfig relay;
        readonly byte[] relayBuffer = new byte[RemoteRelay.HeaderSize + RemoteProtocol.MaxPacketSize];
        readonly byte[] relaySendBuffer = new byte[RemoteRelay.HeaderSize + RemoteProtocol.MaxPacketSize];
        bool clientViaRelay;
        float nextKeepalive;
        long relayAckMs = -100000;
        volatile bool relayPeerPresent;
        bool announcedRelayClient;
        bool relaySendWarned;

        /// <summary>Inhalt des QR-Codes (frostline://pair?…), oder null ohne Server.</summary>
        public string PairingUrl
        {
            get
            {
                RelayConfig r = relay;
                return r == null ? null : RemoteRelay.PairingUrl(r.Host, r.Port, r.Secret, SystemInfo.deviceName);
            }
        }

        public string RelayHost
        {
            get { RelayConfig r = relay; return r == null ? "" : (r.Port == RemoteRelay.DefaultPort ? r.Host : r.Host + ":" + r.Port); }
        }

        public bool RelayReachable { get { return NowMs - Interlocked.Read(ref relayAckMs) < 3500; } }
        public bool ConnectedViaRelay { get { lock (clientGate) return clientViaRelay && IsConnected; } }

        public string RelayStatus
        {
            get
            {
                RelayConfig r = relay;
                if (r == null) return "Kein Server eingetragen – nur im selben WLAN.";
                if (r.Endpoint == null) return "Server " + r.Host + " wird gesucht …";
                if (!RelayReachable) return "Server " + r.Host + " antwortet nicht (UDP-Port " + r.Port + ").";
                if (ConnectedViaRelay) return "Über Server verbunden: " + ClientName;
                return relayPeerPresent ? "Server erreichbar, Handy meldet sich …" : "Server erreichbar – QR-Code mit dem iPhone scannen.";
            }
        }

        /// <summary>Server setzen ("host" oder "host:port"); leer schaltet den Weg übers Internet ab.</summary>
        public void SetRelayHost(string text)
        {
            string host = (text ?? "").Trim();
            int port = RemoteRelay.DefaultPort;
            int colon = host.LastIndexOf(':');
            if (colon > 0 && host.IndexOf(':') == colon && int.TryParse(host.Substring(colon + 1), out int parsed))
            {
                port = parsed;
                host = host.Substring(0, colon);
            }
            PlayerPrefs.SetString(RelayHostKey, host);
            PlayerPrefs.SetInt(RelayPortKey, port);
            PlayerPrefs.Save();
            ConfigureRelay(host, port, LoadSecret());
        }

        /// <summary>Neuer Schlüssel: Das bisher gekoppelte Handy kommt nicht mehr hinein, ein neuer QR-Code entsteht.</summary>
        public void NewPairing()
        {
            byte[] secret = RemoteRelay.NewSecret();
            PlayerPrefs.SetString(RelaySecretKey, RemoteRelay.Base64Url(secret));
            PlayerPrefs.Save();
            RelayConfig r = relay;
            if (r != null) ConfigureRelay(r.Host, r.Port, secret);
        }

        static byte[] LoadSecret()
        {
            byte[] secret = RemoteRelay.FromBase64Url(PlayerPrefs.GetString(RelaySecretKey, ""));
            if (secret == null || secret.Length != RemoteRelay.SecretSize)
            {
                secret = RemoteRelay.NewSecret();
                PlayerPrefs.SetString(RelaySecretKey, RemoteRelay.Base64Url(secret));
                PlayerPrefs.Save();
            }
            return secret;
        }

        void LoadRelay()
        {
            string host = PlayerPrefs.GetString(RelayHostKey, DefaultRelayHost);
            int port = PlayerPrefs.GetInt(RelayPortKey, RemoteRelay.DefaultPort);
            // Zum Testen: -frostlineRelay host:port
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-frostlineRelay");
            if (i >= 0 && i + 1 < args.Length)
            {
                string arg = args[i + 1];
                int colon = arg.LastIndexOf(':');
                host = colon > 0 ? arg.Substring(0, colon) : arg;
                if (colon > 0) int.TryParse(arg.Substring(colon + 1), out port);
            }
            ConfigureRelay(host, port, LoadSecret());
        }

        void ConfigureRelay(string host, int port, byte[] secret)
        {
            if (string.IsNullOrEmpty(host))
            {
                relay = null;
                return;
            }
            var config = new RelayConfig { Host = host, Port = port, Secret = secret, Room = RemoteRelay.RoomId(secret) };
            relay = config;
            lock (clientGate)
            {
                if (clientViaRelay) { client = null; clientName = ""; clientViaRelay = false; link.Clear(); }
            }
            // Namen auflösen, ohne das Spiel anzuhalten.
            new Thread(() =>
            {
                try
                {
                    IPAddress[] addresses = Dns.GetHostAddresses(config.Host);
                    IPAddress chosen = null;
                    foreach (IPAddress a in addresses)
                        if (a.AddressFamily == AddressFamily.InterNetwork) { chosen = a; break; }
                    if (chosen == null && addresses.Length > 0) chosen = addresses[0];
                    // Der Empfangs-Socket ist IPv6 mit IPv4-Mitnutzung; Mono sendet von dort nur an
                    // IPv6-Adressen, IPv4 also als ::ffff:a.b.c.d.
                    Socket s = socket;
                    if (chosen != null && s != null && s.AddressFamily == AddressFamily.InterNetworkV6 && chosen.AddressFamily == AddressFamily.InterNetwork)
                        chosen = chosen.MapToIPv6();
                    if (chosen != null) config.Endpoint = new IPEndPoint(chosen, config.Port);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Frostline: Server " + config.Host + " nicht gefunden: " + e.Message);
                }
            }) { IsBackground = true, Name = "Frostline Relay DNS" }.Start();
        }

        /// <summary>Schickt ein Funkpaket direkt (WLAN) oder signiert über den Server.</summary>
        void SendPacket(byte[] data, int length, EndPoint target, bool viaRelay, byte[] scratch)
        {
            if (!viaRelay)
            {
                socket.SendTo(data, length, SocketFlags.None, target);
                return;
            }
            RelayConfig r = relay;
            if (r == null || r.Endpoint == null) return;
            int size = RemoteRelay.Seal(scratch, RemoteRelay.RoleGame, r.Room, r.Secret, data, length);
            socket.SendTo(scratch, size, SocketFlags.None, r.Endpoint);
        }

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
            // Zum Testen: eigener Port ohne Bonjour, damit ein Handy im WLAN den Testlauf nicht findet.
            string[] args = Environment.GetCommandLineArgs();
            int p = Array.IndexOf(args, "-frostlineFunkPort");
            if (p >= 0 && p + 1 < args.Length) int.TryParse(args[p + 1], out port);
            if (Array.IndexOf(args, "-frostlineOhneBonjour") >= 0) advertiseBonjour = false;
            if (drone == null) drone = FindAnyObjectByType<DroneController>();
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
            LoadRelay();
            Status = "Wartet auf iPhone (Port " + port + (BonjourActive ? ", Bonjour aktiv)" : ")");
        }

        void Close()
        {
            running = false;
            EndPoint target;
            bool viaRelay;
            lock (clientGate) { target = client; viaRelay = clientViaRelay; }
            if (socket != null && target != null)
            {
                try
                {
                    int length = RemoteProtocol.WriteGoodbye(sendBuffer);
                    SendPacket(sendBuffer, length, target, viaRelay, relaySendBuffer);
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
            var buffer = new byte[RemoteRelay.HeaderSize + 256];
            var reply = new byte[RemoteProtocol.MaxPacketSize];
            var relayReply = new byte[RemoteRelay.HeaderSize + RemoteProtocol.MaxPacketSize];
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

                bool viaRelay = false;
                if (RemoteRelay.IsFrame(buffer, length))
                {
                    RelayConfig r = relay;
                    if (r == null) continue;
                    int offset, payloadLength;
                    bool present;
                    RemoteRelay.Kind kind = RemoteRelay.Open(buffer, length, r.Room, r.Secret, RemoteRelay.RolePhone, out offset, out payloadLength, out present);
                    if (kind == RemoteRelay.Kind.ServerAck)
                    {
                        Interlocked.Exchange(ref relayAckMs, NowMs);
                        relayPeerPresent = present;
                        continue;
                    }
                    if (kind != RemoteRelay.Kind.Peer || payloadLength <= 0) continue;
                    // Nur signierte Pakete des gekoppelten Handys; danach wie im WLAN weiter.
                    Buffer.BlockCopy(buffer, offset, relayBuffer, 0, payloadLength);
                    Buffer.BlockCopy(relayBuffer, 0, buffer, 0, payloadLength);
                    length = payloadLength;
                    viaRelay = true;
                    Interlocked.Exchange(ref relayAckMs, NowMs);
                }

                RemoteProtocol.PacketType type;
                if (!RemoteProtocol.TryReadType(buffer, length, out type)) continue;

                switch (type)
                {
                    case RemoteProtocol.PacketType.Control:
                        ControlPacket control;
                        if (!IsCurrentClient(remote, viaRelay)) { ignoredPackets++; break; }
                        if (RemoteProtocol.TryReadControl(buffer, length, out control) && link.Receive(control, NowMs))
                        {
                            lock (clientGate) { client = remote; clientViaRelay = viaRelay; }
                        }
                        break;

                    case RemoteProtocol.PacketType.Hello:
                        uint session;
                        string name;
                        if (RemoteProtocol.TryReadHello(buffer, length, out session, out name) && IsCurrentClient(remote, viaRelay))
                        {
                            lock (clientGate)
                            {
                                client = remote;
                                clientName = name;
                                clientViaRelay = viaRelay;
                            }
                            try
                            {
                                int size = RemoteProtocol.WriteWelcome(reply, session, (ushort)telemetryRateHz, hostName);
                                SendPacket(reply, size, remote, viaRelay, relayReply);
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
                            clientViaRelay = false;
                        }
                        break;
                }
            }
        }

        int ignoredPackets;

        /// <summary>
        /// Ein Handy auf einmal: Solange das verbundene funkt, werden andere ignoriert (sonst würden zwei
        /// Handys abwechselnd steuern). Schweigt es, darf ein anderes – oder dasselbe auf anderem Weg
        /// (z. B. WLAN weg, weiter über Mobilfunk und Server).
        /// </summary>
        bool IsCurrentClient(EndPoint remote, bool viaRelay)
        {
            lock (clientGate)
            {
                if (client == null || !link.IsConnected(NowMs)) return true;
                if (viaRelay != clientViaRelay) return false;
                return viaRelay || remote.Equals(client);
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
            bool relayed = ConnectedViaRelay;
            if (IsConnected) Status = "Verbunden" + (relayed ? " über Server" : "") + ": " + ClientName + "  ·  " + PacketsPerSecond.ToString("0") + " Pakete/s  ·  Verlust " + LossPercent.ToString("0") + " %";
            else Status = "Wartet auf iPhone (Port " + port + (BonjourActive ? ", Bonjour aktiv" : "") + (relay != null ? ", Server " + relay.Host : "") + ")";
            if (relayed && !announcedRelayClient) Debug.Log("Frostline: iPhone über Server verbunden: " + ClientName);
            announcedRelayClient = relayed;

            // Lebenszeichen an den Server: hält den Weg durch Router und Mobilfunk offen.
            RelayConfig r = relay;
            if (r != null && r.Endpoint != null && Time.unscaledTime >= nextKeepalive)
            {
                nextKeepalive = Time.unscaledTime + 1f;
                try
                {
                    int size = RemoteRelay.Seal(relaySendBuffer, RemoteRelay.RoleGame, r.Room, r.Secret, sendBuffer, 0);
                    socket.SendTo(relaySendBuffer, size, SocketFlags.None, r.Endpoint);
                }
                catch (Exception e)
                {
                    if (!relaySendWarned) Debug.LogWarning("Frostline: Senden an den Server " + r.Endpoint + " fehlgeschlagen: " + e.Message);
                    relaySendWarned = true;
                }
            }

            if (Time.unscaledTime < nextTelemetryTime) return;
            nextTelemetryTime = Time.unscaledTime + 1f / Mathf.Max(1, telemetryRateHz);

            EndPoint target;
            bool viaRelay;
            lock (clientGate) { target = client; viaRelay = clientViaRelay; }
            if (target == null || !IsConnected) return;

            try
            {
                TelemetryPacket packet = BuildTelemetry();
                int length = RemoteProtocol.WriteTelemetry(sendBuffer, packet);
                SendPacket(sendBuffer, length, target, viaRelay, relaySendBuffer);
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
