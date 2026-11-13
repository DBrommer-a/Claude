import Foundation
import Network
import UIKit
#if canImport(FrostlineRemoteKit)
import FrostlineRemoteKit
#endif

/// Verbindung zum Spiel auf dem Mac: findet ihn per Bonjour (oder über die IP-Adresse),
/// schickt 250-mal pro Sekunde die Sticks und empfängt Telemetrie.
@MainActor
final class RemoteConnection: ObservableObject {
    enum Status: Equatable {
        case idle
        case searching
        case connecting(String)
        case connected(String)
        case failed(String)

        var label: String {
            switch self {
            case .idle: return "Getrennt"
            case .searching: return "Suche Mac …"
            case .connecting(let name): return "Verbinde mit \(name) …"
            case .connected(let name): return name
            case .failed(let reason): return reason
            }
        }

        var isConnected: Bool {
            if case .connected = self { return true }
            return false
        }
    }

    struct FoundHost: Identifiable, Hashable {
        let id: String
        let name: String
        let endpoint: NWEndpoint
    }

    @Published private(set) var status: Status = .idle
    @Published private(set) var hosts: [FoundHost] = []
    @Published private(set) var telemetry = TelemetryPacket()
    @Published private(set) var hasTelemetry = false
    @Published private(set) var roundTripMs: Double?

    let control = ControlState()
    var onEvents: (([EventTracker.Event]) -> Void)?

    private var browser: NWBrowser?
    private var link: UdpLink?
    private var currentHost: String?
    private var events = EventTracker()
    private var latency = LatencyMeter()
    private var lastPublish: UInt32 = 0
    private var lastTelemetryMs: UInt32 = 0
    private var watchdog: Timer?

    // MARK: Suchen

    func start() {
        startBrowsing()
        watchdog?.invalidate()
        watchdog = Timer.scheduledTimer(withTimeInterval: 0.5, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.checkTimeout() }
        }
    }

    func stop() {
        watchdog?.invalidate()
        watchdog = nil
        browser?.cancel()
        browser = nil
        disconnect()
        status = .idle
    }

    private func startBrowsing() {
        browser?.cancel()
        let browser = NWBrowser(for: .bonjour(type: RemoteProtocol.bonjourType, domain: nil), using: .udp)
        browser.browseResultsChangedHandler = { [weak self] results, _ in
            let found: [FoundHost] = results.compactMap { result in
                guard case let .service(name, _, _, _) = result.endpoint else { return nil }
                return FoundHost(id: name, name: name, endpoint: result.endpoint)
            }.sorted { $0.name < $1.name }
            Task { @MainActor in self?.updateHosts(found) }
        }
        browser.stateUpdateHandler = { [weak self] state in
            if case .failed = state {
                Task { @MainActor in self?.status = .failed("Bonjour-Suche fehlgeschlagen – IP-Adresse in den Einstellungen eingeben") }
            }
        }
        browser.start(queue: .main)
        self.browser = browser
        if link == nil { status = .searching }
    }

    private func updateHosts(_ found: [FoundHost]) {
        hosts = found
        // Automatisch mit dem ersten gefundenen Mac verbinden.
        if link == nil, let first = found.first, !UserDefaults.standard.bool(forKey: Settings.Keys.useManualHost) {
            connect(to: first.endpoint, name: first.name)
        }
    }

    // MARK: Verbinden

    func connect(to host: FoundHost) {
        UserDefaults.standard.set(false, forKey: Settings.Keys.useManualHost)
        connect(to: host.endpoint, name: host.name)
    }

    func connectManual(address: String) {
        let trimmed = address.trimmingCharacters(in: .whitespaces)
        guard !trimmed.isEmpty, let port = NWEndpoint.Port(rawValue: RemoteProtocol.port) else { return }
        UserDefaults.standard.set(true, forKey: Settings.Keys.useManualHost)
        UserDefaults.standard.set(trimmed, forKey: Settings.Keys.manualHost)
        connect(to: .hostPort(host: NWEndpoint.Host(trimmed), port: port), name: trimmed)
    }

    private func connect(to endpoint: NWEndpoint, name: String) {
        disconnect()
        currentHost = name
        status = .connecting(name)
        events.reset()
        latency.reset()
        hasTelemetry = false

        let link = UdpLink(endpoint: endpoint, control: control, deviceName: Settings.pilotName)
        link.onTelemetry = { [weak self] packet in
            Task { @MainActor in self?.receive(packet) }
        }
        link.onWelcome = { [weak self] welcome in
            Task { @MainActor in self?.status = .connected(welcome.name.isEmpty ? name : welcome.name) }
        }
        link.onFailure = { [weak self] reason in
            Task { @MainActor in
                self?.status = .failed(reason)
                self?.disconnect()
            }
        }
        link.start()
        self.link = link
        UIApplication.shared.isIdleTimerDisabled = true
    }

    func disconnect() {
        link?.stop()
        link = nil
        hasTelemetry = false
        UIApplication.shared.isIdleTimerDisabled = false
    }

    func reconnectSaved() {
        if UserDefaults.standard.bool(forKey: Settings.Keys.useManualHost),
           let host = UserDefaults.standard.string(forKey: Settings.Keys.manualHost) {
            connectManual(address: host)
        }
    }

    // MARK: Empfang

    private func receive(_ packet: TelemetryPacket) {
        let now = RemoteClock.nowMs()
        lastTelemetryMs = now
        latency.add(nowMs: now, echoSendTimeMs: packet.echoSendTimeMs, holdMs: packet.holdMs)
        let fired = events.update(packet)
        if !fired.isEmpty { onEvents?(fired) }
        if case .connecting(let name) = status { status = .connected(name) }

        // Anzeige höchstens 20-mal pro Sekunde aktualisieren.
        if now &- lastPublish >= 50 || !hasTelemetry {
            lastPublish = now
            telemetry = packet
            hasTelemetry = true
            roundTripMs = latency.roundTripMs
        }
    }

    private func checkTimeout() {
        guard link != nil, hasTelemetry else { return }
        if RemoteClock.nowMs() &- lastTelemetryMs > 1500 {
            hasTelemetry = false
            if let host = currentHost { status = .connecting(host) }
        }
    }
}

/// UDP-Verbindung auf eigener Warteschlange: Sende-Takt mit 250 Hz, Empfang, Anmeldung.
final class UdpLink {
    var onTelemetry: ((TelemetryPacket) -> Void)?
    var onWelcome: ((WelcomePacket) -> Void)?
    var onFailure: ((String) -> Void)?

    private let connection: NWConnection
    private let control: ControlState
    private let queue = DispatchQueue(label: "frostline.remote.udp", qos: .userInteractive)
    private let session = UInt32.random(in: 1...UInt32.max)
    private let deviceName: String
    private var timer: DispatchSourceTimer?
    private var sequence: UInt32 = 0
    private var ticks = 0
    private var welcomed = false

    init(endpoint: NWEndpoint, control: ControlState, deviceName: String) {
        let parameters = NWParameters.udp
        parameters.serviceClass = .interactiveVoice // niedrige Latenz im WLAN
        connection = NWConnection(to: endpoint, using: parameters)
        self.control = control
        self.deviceName = deviceName
    }

    func start() {
        connection.stateUpdateHandler = { [weak self] state in
            guard let self else { return }
            switch state {
            case .ready:
                self.sendHello()
                self.receive()
                self.startTimer()
            case .failed(let error):
                self.onFailure?("Verbindung fehlgeschlagen: \(error.localizedDescription)")
            case .waiting(let error):
                self.onFailure?("Netz nicht erreichbar: \(error.localizedDescription)")
            default:
                break
            }
        }
        connection.start(queue: queue)
    }

    func stop() {
        queue.sync {
            timer?.cancel()
            timer = nil
        }
        connection.send(content: RemoteProtocol.goodbye(), completion: .idempotent)
        connection.cancel()
    }

    private func startTimer() {
        let timer = DispatchSource.makeTimerSource(flags: .strict, queue: queue)
        // 250 Pakete pro Sekunde wie geplant.
        timer.schedule(deadline: .now(), repeating: .milliseconds(4), leeway: .microseconds(500))
        timer.setEventHandler { [weak self] in self?.tick() }
        timer.resume()
        self.timer = timer
    }

    private func tick() {
        sequence &+= 1
        let packet = control.packet(sequence: sequence, nowMs: RemoteClock.nowMs())
        connection.send(content: packet.encoded(), completion: .idempotent)
        ticks += 1
        // Anmeldung wiederholen: alle 0,5 s bis zur Antwort, danach jede Sekunde.
        if ticks % (welcomed ? 250 : 125) == 0 { sendHello() }
    }

    private func sendHello() {
        let hello = HelloPacket(session: session, name: deviceName)
        connection.send(content: hello.encoded(), completion: .idempotent)
    }

    private func receive() {
        connection.receiveMessage { [weak self] data, _, _, error in
            guard let self else { return }
            if let data {
                switch RemoteProtocol.packetType(of: data) {
                case .telemetry:
                    if let packet = TelemetryPacket.decode(data) { self.onTelemetry?(packet) }
                case .welcome:
                    if let welcome = WelcomePacket.decode(data), welcome.session == self.session {
                        self.welcomed = true
                        self.onWelcome?(welcome)
                    }
                case .goodbye:
                    self.onFailure?("Spiel beendet")
                default:
                    break
                }
            }
            if error == nil { self.receive() }
        }
    }
}
