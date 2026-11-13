import SwiftUI
#if canImport(FrostlineRemoteKit)
import FrostlineRemoteKit
#endif

/// Hauptbildschirm im Querformat: Sticks, Telemetrie oben, Schalter unten.
struct ContentView: View {
    @EnvironmentObject private var connection: RemoteConnection
    @Environment(\.scenePhase) private var scenePhase

    @AppStorage(Settings.Keys.stickSize) private var stickSize = 70.0
    @AppStorage(Settings.Keys.throttleTravel) private var throttleTravel = 220.0
    @AppStorage(Settings.Keys.gyroMaxTilt) private var gyroMaxTilt = 30.0
    @AppStorage(Settings.Keys.haptics) private var hapticsOn = true
    @AppStorage(Settings.Keys.deadband) private var deadband = 0.02

    @State private var armed = false
    @State private var mode: FlightMode = .acro
    @State private var gyroOn = false
    @State private var showSettings = false
    @State private var turtleHeld = false

    @StateObject private var devices = DeviceServices()

    var body: some View {
        ZStack {
            LinearGradient(colors: [Color(white: 0.07), Color(white: 0.12)], startPoint: .top, endPoint: .bottom)
                .ignoresSafeArea()

            StickPad(stickRadius: stickSize, throttleTravel: throttleTravel, deadband: Float(deadband), rightStickEnabled: !gyroOn) { output in
                if gyroOn {
                    connection.control.setSticks(yaw: output.yaw, throttle: output.throttle)
                } else {
                    connection.control.setSticks(roll: output.roll, pitch: output.pitch, yaw: output.yaw, throttle: output.throttle)
                }
            }
            .ignoresSafeArea()

            VStack {
                TelemetryBar()
                Spacer()
                controls
            }
            .padding(.horizontal, 24)
            .padding(.vertical, 10)
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
            devices.gyro.onAxes = { roll, pitch in
                connection.control.setSticks(roll: roll, pitch: pitch)
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
    }

    private var controls: some View {
        HStack(spacing: 14) {
            Button {
                armed.toggle()
                connection.control.setArmed(armed)
                devices.haptics.tap()
            } label: {
                Text(armed ? "ARMED" : "ARM")
                    .font(.headline.weight(.heavy))
                    .frame(width: 110, height: 44)
                    .background(armed ? Color.red : Color.white.opacity(0.12), in: RoundedRectangle(cornerRadius: 10))
                    .foregroundStyle(.white)
            }

            Picker("Modus", selection: $mode) {
                ForEach(FlightMode.allCases) { m in Text(m.label).tag(m) }
            }
            .pickerStyle(.segmented)
            .frame(width: 240)
            .onChange(of: mode) { _, value in connection.control.setMode(value) }

            Text("Turtle")
                .font(.subheadline.weight(.bold))
                .frame(width: 80, height: 44)
                .background(turtleHeld ? Color.orange : Color.white.opacity(0.12), in: RoundedRectangle(cornerRadius: 10))
                .gesture(DragGesture(minimumDistance: 0)
                    .onChanged { _ in
                        if !turtleHeld {
                            turtleHeld = true
                            connection.control.setTurtle(true)
                        }
                    }
                    .onEnded { _ in
                        turtleHeld = false
                        connection.control.setTurtle(false)
                    })

            Button {
                connection.control.pressReset()
                devices.haptics.tap()
            } label: {
                Image(systemName: "arrow.counterclockwise")
                    .frame(width: 44, height: 44)
                    .background(Color.white.opacity(0.12), in: RoundedRectangle(cornerRadius: 10))
            }

            Button {
                setGyro(!gyroOn)
            } label: {
                Image(systemName: "gyroscope")
                    .frame(width: 44, height: 44)
                    .background(gyroOn ? Color.teal : Color.white.opacity(0.12), in: RoundedRectangle(cornerRadius: 10))
            }

            Button {
                showSettings = true
            } label: {
                Image(systemName: "gearshape")
                    .frame(width: 44, height: 44)
                    .background(Color.white.opacity(0.12), in: RoundedRectangle(cornerRadius: 10))
            }
        }
        .foregroundStyle(.white)
    }

    private func setGyro(_ on: Bool) {
        gyroOn = on && devices.gyro.isAvailable
        connection.control.setGyro(gyroOn)
        if gyroOn {
            devices.gyro.start(maxTilt: gyroMaxTilt)
        } else {
            devices.gyro.stop()
            connection.control.setSticks(roll: 0, pitch: 0)
        }
    }
}

/// Haptik und Gyro leben so lange wie der Bildschirm, nicht nur so lange wie ein View-Wert.
final class DeviceServices: ObservableObject {
    let haptics = Haptics()
    let gyro = GyroController()
}

/// Telemetrie vom Mac: Verbindung, Laufzeit, Akku, Tempo, Höhe, Rundenzeit.
struct TelemetryBar: View {
    @EnvironmentObject private var connection: RemoteConnection

    var body: some View {
        let t = connection.telemetry
        HStack(spacing: 18) {
            HStack(spacing: 6) {
                Circle()
                    .fill(connection.hasTelemetry ? Color.green : (connection.status.isConnected ? Color.yellow : Color.red))
                    .frame(width: 10, height: 10)
                Text(connection.status.label).lineLimit(1)
            }
            if connection.hasTelemetry {
                if let rtt = connection.roundTripMs {
                    Label("\(Int(rtt.rounded())) ms", systemImage: "wifi")
                }
                Label(String(format: "%.1f V  %d %%", t.batteryVolts, t.batteryPercent), systemImage: "battery.75")
                    .foregroundStyle(t.cellVolts < 3.5 ? Color.red : Color.white)
                Label(String(format: "%.0f km/h", t.speedKmh), systemImage: "speedometer")
                Label(String(format: "%.0f m", t.altitudeMeters), systemImage: "arrow.up.and.down")
                if let lap = t.lapTime {
                    Label(String(format: "%.2f s", lap), systemImage: "stopwatch")
                }
                if t.flags.contains(.failsafe) {
                    Text("FAILSAFE").foregroundStyle(.red).bold()
                } else if t.flags.contains(.armingBlocked) {
                    Text("GAS RUNTER").foregroundStyle(.orange).bold()
                }
            }
            Spacer()
        }
        .font(.subheadline.monospacedDigit())
        .foregroundStyle(.white)
        .padding(.horizontal, 14)
        .padding(.vertical, 8)
        .background(Color.black.opacity(0.35), in: RoundedRectangle(cornerRadius: 12))
    }
}
