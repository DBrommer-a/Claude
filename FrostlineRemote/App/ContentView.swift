import SwiftUI
#if canImport(FrostlineRemoteKit)
import FrostlineRemoteKit
#endif

/// Hauptbildschirm im Querformat, aufgebaut wie ein FPV-Sender: links und rechts feste Knüppel,
/// dazwischen die Konsole mit Kippschaltern, Statusanzeige und Kanalmonitor. Alles einfarbig dunkel.
struct ContentView: View {
    @EnvironmentObject private var connection: RemoteConnection
    @Environment(\.scenePhase) private var scenePhase

    @AppStorage(Settings.Keys.gimbalSize) private var gimbalSize = 205.0
    @AppStorage(Settings.Keys.fingerTravel) private var fingerTravel = 0.85
    @AppStorage(Settings.Keys.stickMode) private var stickMode = 2
    @AppStorage(Settings.Keys.absoluteGrip) private var absoluteGrip = false
    @AppStorage(Settings.Keys.detentHaptics) private var detentHaptics = true
    @AppStorage(Settings.Keys.gyroMaxTilt) private var gyroMaxTilt = 30.0
    @AppStorage(Settings.Keys.haptics) private var hapticsOn = true
    @AppStorage(Settings.Keys.deadband) private var deadband = 0.02

    @State private var armed = false
    @State private var mode: FlightMode = .acro
    @State private var gyroOn = false
    @State private var showSettings = false
    @State private var turtleHeld = false

    @StateObject private var devices = DeviceServices()
    @StateObject private var channels = ChannelValues()

    var body: some View {
        GeometryReader { geo in
            let size = min(CGFloat(gimbalSize), geo.size.height - 84, (geo.size.width - 300) / 2)
            ZStack {
                background
                VStack(spacing: 10) {
                    TelemetryBar()
                    HStack(alignment: .bottom, spacing: 12) {
                        gimbal(left: true).frame(width: size, height: size + GimbalUIView.labelHeight)
                        Spacer(minLength: 8)
                        console
                        Spacer(minLength: 8)
                        gimbal(left: false).frame(width: size, height: size + GimbalUIView.labelHeight)
                    }
                    .frame(maxHeight: .infinity, alignment: .bottom)
                }
                .padding(.horizontal, 18)
                .padding(.top, 8)
                .padding(.bottom, 12)
            }
        }
        .statusBarHidden(true)
        .persistentSystemOverlays(.hidden)
        .defersSystemGestures(on: .all)
        .sheet(isPresented: $showSettings) {
            SettingsView().environmentObject(connection)
        }
        .onAppear {
            let haptics = devices.haptics
            connection.onEvents = { events in haptics.play(events) }
            haptics.enabled = hapticsOn
            haptics.detentsEnabled = detentHaptics
            devices.gyro.onAxes = { roll, pitch in
                connection.control.setSticks(roll: roll, pitch: pitch)
                channels.roll = roll
                channels.pitch = pitch
            }
            connection.start()
            connection.reconnectSaved()
        }
        .onChange(of: scenePhase) { _, phase in
            switch phase {
            case .active:
                connection.start()
                connection.reconnectSaved()
            case .background:
                // Im Hintergrund nichts senden: das Spiel geht nach 200 ms in Failsafe.
                setGyro(false)
                connection.stop()
            default:
                break
            }
        }
        .onChange(of: hapticsOn) { _, value in devices.haptics.enabled = value }
        .onChange(of: detentHaptics) { _, value in devices.haptics.detentsEnabled = value }
        .onChange(of: stickMode) { _, _ in
            // Beim Umstellen der Belegung erst einmal alles neutral: Gas aus, Knüppel mittig.
            connection.control.setSticks(roll: 0, pitch: 0, yaw: 0, throttle: 0)
            channels.reset()
        }
        .onChange(of: connection.telemetry.flags) { _, flags in
            // Das Spiel entschärft z. B. nach einem Absturz oder Failsafe: Schalter nachziehen.
            if armed && !flags.contains(.armed) && !flags.contains(.armingBlocked) && connection.hasTelemetry && flags.contains(.failsafe) {
                armed = false
                connection.control.setArmed(false)
            }
        }
        .tint(.white)
        .preferredColorScheme(.dark)
    }

    private var background: some View {
        ZStack {
            LinearGradient(colors: [Color(white: 0.055), Color(white: 0.11)], startPoint: .top, endPoint: .bottom)
            RadialGradient(colors: [Color.white.opacity(0.05), .clear], center: .center, startRadius: 10, endRadius: 420)
        }
        .ignoresSafeArea()
    }

    // MARK: Knüppel

    private var modeTwo: Bool { stickMode != 1 }

    private func gimbal(left: Bool) -> some View {
        // Mode 2: links Gas/Gier, rechts Nick/Roll. Mode 1: links Nick/Gier, rechts Gas/Roll.
        let throttleHere = left == modeTwo
        let role = GimbalRole(horizontalName: left ? "GIER" : "ROLL",
                              verticalName: throttleHere ? "GAS" : "NICK",
                              verticalIsThrottle: throttleHere)
        // Im Gyro-Modus kommen Nick und Roll aus der Lage des Telefons.
        let disabled = gyroOn && !left && modeTwo
        return GimbalStickView(role: role,
                               grip: absoluteGrip ? .absolute : .relative,
                               fingerTravel: CGFloat(fingerTravel),
                               deadband: Float(deadband),
                               detents: detentHaptics,
                               enabled: !disabled,
                               disabledLabel: "GYRO",
                               onChange: { x, y in send(left: left, throttleHere: throttleHere, x: x, y: y) },
                               onDetent: { devices.haptics.detent() })
    }

    private func send(left: Bool, throttleHere: Bool, x: Float, y: Float) {
        if left {
            connection.control.setSticks(yaw: x)
            channels.yaw = x
        } else if !gyroOn {
            connection.control.setSticks(roll: x)
            channels.roll = x
        }
        if throttleHere {
            connection.control.setSticks(throttle: y)
            channels.throttle = y
        } else if !gyroOn {
            connection.control.setSticks(pitch: y)
            channels.pitch = y
        }
    }

    // MARK: Konsole

    private var console: some View {
        VStack(spacing: 10) {
            HStack(alignment: .top, spacing: 14) {
                RCToggleSwitch(title: "ARM", isOn: armed) {
                    armed.toggle()
                    connection.control.setArmed(armed)
                    devices.haptics.tap()
                }
                RCModeSwitch(selection: mode) { value in
                    mode = value
                    connection.control.setMode(value)
                    devices.haptics.tap()
                }
                HoldSwitch(title: "TURTLE", held: turtleHeld) { down in
                    turtleHeld = down
                    connection.control.setTurtle(down)
                    if down { devices.haptics.tap() }
                }
            }
            StatusScreen(armed: armed, channels: channels)
            HStack(spacing: 10) {
                ConsoleButton(symbol: "arrow.counterclockwise", label: "Reset") {
                    connection.control.pressReset()
                    devices.haptics.tap()
                }
                ConsoleButton(symbol: "gyroscope", label: "Gyro", active: gyroOn) { setGyro(!gyroOn) }
                ConsoleButton(symbol: "slider.horizontal.3", label: "Setup") { showSettings = true }
            }
        }
        .frame(maxWidth: 330)
    }

    private func setGyro(_ on: Bool) {
        gyroOn = on && devices.gyro.isAvailable
        connection.control.setGyro(gyroOn)
        if gyroOn {
            devices.gyro.start(maxTilt: gyroMaxTilt)
        } else {
            devices.gyro.stop()
            connection.control.setSticks(roll: 0, pitch: 0)
            channels.roll = 0
            channels.pitch = 0
        }
    }
}

/// Haptik und Gyro leben so lange wie der Bildschirm, nicht nur so lange wie ein View-Wert.
final class DeviceServices: ObservableObject {
    let haptics = Haptics()
    let gyro = GyroController()
}

/// Kanalwerte für den Monitor in der Konsole.
final class ChannelValues: ObservableObject {
    @Published var throttle: Float = 0
    @Published var yaw: Float = 0
    @Published var pitch: Float = 0
    @Published var roll: Float = 0

    func reset() {
        throttle = 0; yaw = 0; pitch = 0; roll = 0
    }
}

// MARK: - Schalter wie am Sender

/// Zweistufiger Kippschalter (hebel oben = an).
struct RCToggleSwitch: View {
    let title: String
    let isOn: Bool
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            VStack(spacing: 5) {
                Text(title).font(.caption2.weight(.heavy)).tracking(1.5)
                    .foregroundStyle(.white.opacity(isOn ? 1 : 0.6))
                ZStack(alignment: isOn ? .top : .bottom) {
                    RoundedRectangle(cornerRadius: 9)
                        .fill(Color.black.opacity(0.55))
                        .overlay(RoundedRectangle(cornerRadius: 9).stroke(Color.white.opacity(0.18), lineWidth: 1))
                    Capsule()
                        .fill(isOn ? Color.white : Color(white: 0.45))
                        .frame(width: 22, height: 26)
                        .shadow(color: .black.opacity(0.6), radius: 3, y: 2)
                        .padding(4)
                }
                .frame(width: 34, height: 60)
                Text(isOn ? "SCHARF" : "AUS").font(.system(size: 9, weight: .bold)).foregroundStyle(.white.opacity(0.55))
            }
        }
        .buttonStyle(.plain)
        .accessibilityLabel(title)
        .accessibilityValue(isOn ? "an" : "aus")
    }
}

/// Dreistufiger Schalter für den Flugmodus.
struct RCModeSwitch: View {
    let selection: FlightMode
    let onSelect: (FlightMode) -> Void
    private let order: [FlightMode] = [.acro, .horizon, .angle]

    var body: some View {
        VStack(spacing: 5) {
            Text("MODUS").font(.caption2.weight(.heavy)).tracking(1.5).foregroundStyle(.white.opacity(0.6))
            HStack(spacing: 0) {
                ForEach(order) { m in
                    Button { onSelect(m) } label: {
                        Text(m.label.uppercased())
                            .font(.system(size: 10, weight: .heavy))
                            .frame(width: 58, height: 34)
                            .foregroundStyle(m == selection ? Color.black : Color.white.opacity(0.65))
                            .background(m == selection ? Color.white : Color.clear, in: RoundedRectangle(cornerRadius: 7))
                    }
                    .buttonStyle(.plain)
                }
            }
            .padding(3)
            .background(Color.black.opacity(0.55), in: RoundedRectangle(cornerRadius: 9))
            .overlay(RoundedRectangle(cornerRadius: 9).stroke(Color.white.opacity(0.18), lineWidth: 1))
            Text("Stufe \(order.firstIndex(of: selection).map { $0 + 1 } ?? 1)")
                .font(.system(size: 9, weight: .bold)).foregroundStyle(.white.opacity(0.55))
        }
    }
}

/// Taster, der nur gedrückt gehalten wirkt (Turtle).
struct HoldSwitch: View {
    let title: String
    let held: Bool
    let onChange: (Bool) -> Void

    var body: some View {
        VStack(spacing: 5) {
            Text(title).font(.caption2.weight(.heavy)).tracking(1.5).foregroundStyle(.white.opacity(held ? 1 : 0.6))
            Circle()
                .fill(held ? Color.white : Color(white: 0.16))
                .overlay(Circle().stroke(Color.white.opacity(0.25), lineWidth: 1))
                .overlay(Image(systemName: "tortoise.fill").font(.system(size: 15)).foregroundStyle(held ? Color.black : Color.white.opacity(0.7)))
                .frame(width: 46, height: 46)
                .padding(.vertical, 7)
                .gesture(DragGesture(minimumDistance: 0)
                    .onChanged { _ in if !held { onChange(true) } }
                    .onEnded { _ in onChange(false) })
            Text("HALTEN").font(.system(size: 9, weight: .bold)).foregroundStyle(.white.opacity(0.55))
        }
        .accessibilityLabel(title)
    }
}

struct ConsoleButton: View {
    let symbol: String
    let label: String
    var active = false
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            HStack(spacing: 6) {
                Image(systemName: symbol).font(.system(size: 13, weight: .semibold))
                Text(label).font(.caption.weight(.semibold))
            }
            .frame(maxWidth: .infinity, minHeight: 36)
            .foregroundStyle(active ? Color.black : Color.white)
            .background(active ? Color.white : Color.white.opacity(0.07), in: RoundedRectangle(cornerRadius: 10))
            .overlay(RoundedRectangle(cornerRadius: 10).stroke(Color.white.opacity(0.14), lineWidth: 1))
        }
        .buttonStyle(.plain)
    }
}

// MARK: - Statusanzeige

/// Display wie am Sender: Scharf-Status, Warnungen und Kanalmonitor (Gas, Gier, Nick, Roll).
struct StatusScreen: View {
    @EnvironmentObject private var connection: RemoteConnection
    let armed: Bool
    @ObservedObject var channels: ChannelValues

    var body: some View {
        let flags = connection.telemetry.flags
        VStack(spacing: 7) {
            HStack {
                Text(headline(flags))
                    .font(.system(size: 15, weight: .heavy, design: .monospaced))
                    .foregroundStyle(.white)
                Spacer()
                Text(connection.hasTelemetry ? "LINK" : "KEIN LINK")
                    .font(.system(size: 10, weight: .bold, design: .monospaced))
                    .foregroundStyle(.white.opacity(connection.hasTelemetry ? 0.8 : 0.4))
            }
            ChannelBar(name: "GAS", value: channels.throttle * 2 - 1, unipolar: channels.throttle)
            ChannelBar(name: "GIER", value: channels.yaw)
            ChannelBar(name: "NICK", value: channels.pitch)
            ChannelBar(name: "ROLL", value: channels.roll)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 9)
        .background(Color.black.opacity(0.55), in: RoundedRectangle(cornerRadius: 12))
        .overlay(RoundedRectangle(cornerRadius: 12).stroke(Color.white.opacity(0.14), lineWidth: 1))
    }

    private func headline(_ flags: TelemetryFlags) -> String {
        if connection.hasTelemetry {
            if flags.contains(.failsafe) { return "FAILSAFE" }
            if flags.contains(.armingBlocked) { return "GAS RUNTER" }
            if flags.contains(.turtle) { return "TURTLE" }
            if flags.contains(.armed) { return "SCHARF" }
        }
        return armed ? "SCHARF…" : "ENTSCHÄRFT"
    }
}

struct ChannelBar: View {
    let name: String
    let value: Float
    var unipolar: Float? = nil

    var body: some View {
        HStack(spacing: 8) {
            Text(name).font(.system(size: 9, weight: .bold, design: .monospaced)).foregroundStyle(.white.opacity(0.6))
                .frame(width: 34, alignment: .leading)
            GeometryReader { geo in
                let w = geo.size.width
                ZStack(alignment: .leading) {
                    Capsule().fill(Color.white.opacity(0.08))
                    if let u = unipolar {
                        Capsule().fill(Color.white.opacity(0.85)).frame(width: max(3, w * CGFloat(u)))
                    } else {
                        let v = CGFloat(max(-1, min(1, value)))
                        Rectangle().fill(Color.white.opacity(0.25)).frame(width: 1).offset(x: w / 2)
                        Capsule().fill(Color.white.opacity(0.85))
                            .frame(width: max(3, abs(v) * w / 2))
                            .offset(x: v >= 0 ? w / 2 : w / 2 + v * w / 2)
                    }
                }
            }
            .frame(height: 6)
            Text(unipolar != nil ? String(format: "%3.0f%%", (unipolar ?? 0) * 100) : String(format: "%+4.0f", value * 100))
                .font(.system(size: 9, weight: .semibold, design: .monospaced)).foregroundStyle(.white.opacity(0.7))
                .frame(width: 36, alignment: .trailing)
        }
    }
}

/// Telemetrie vom Mac: Verbindung, Laufzeit, Akku, Tempo, Höhe, Rundenzeit.
struct TelemetryBar: View {
    @EnvironmentObject private var connection: RemoteConnection

    var body: some View {
        let t = connection.telemetry
        HStack(spacing: 18) {
            HStack(spacing: 6) {
                Circle()
                    .fill(Color.white.opacity(connection.hasTelemetry ? 1 : (connection.status.isConnected ? 0.5 : 0.18)))
                    .frame(width: 8, height: 8)
                Text(connection.status.label).lineLimit(1)
            }
            if connection.hasTelemetry {
                if let rtt = connection.roundTripMs {
                    Label("\(Int(rtt.rounded())) ms", systemImage: "wifi")
                }
                Label(String(format: "%.1f V  %d %%", t.batteryVolts, t.batteryPercent), systemImage: "battery.75")
                    .foregroundStyle(Color.white.opacity(t.cellVolts < 3.5 ? 1 : 0.7))
                Label(String(format: "%.0f km/h", t.speedKmh), systemImage: "speedometer")
                Label(String(format: "%.0f m", t.altitudeMeters), systemImage: "arrow.up.and.down")
                if let lap = t.lapTime {
                    Label(String(format: "%.2f s", lap), systemImage: "stopwatch")
                }
            }
            Spacer()
            Text("FROSTLINE").font(.caption2.weight(.heavy)).tracking(3).foregroundStyle(.white.opacity(0.35))
        }
        .font(.caption.monospacedDigit())
        .foregroundStyle(.white)
        .padding(.horizontal, 14)
        .padding(.vertical, 7)
        .background(Color.black.opacity(0.4), in: RoundedRectangle(cornerRadius: 11))
        .overlay(RoundedRectangle(cornerRadius: 11).stroke(Color.white.opacity(0.08), lineWidth: 1))
    }
}
