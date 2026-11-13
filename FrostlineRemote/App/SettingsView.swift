import SwiftUI
#if canImport(FrostlineRemoteKit)
import FrostlineRemoteKit
#endif

/// Verbindung wählen und Sticks einstellen.
struct SettingsView: View {
    @EnvironmentObject private var connection: RemoteConnection
    @Environment(\.dismiss) private var dismiss

    @AppStorage(Settings.Keys.manualHost) private var manualHost = ""
    @AppStorage(Settings.Keys.pilotName) private var pilotName = ""
    @AppStorage(Settings.Keys.stickSize) private var stickSize = 70.0
    @AppStorage(Settings.Keys.throttleTravel) private var throttleTravel = 220.0
    @AppStorage(Settings.Keys.gyroMaxTilt) private var gyroMaxTilt = 30.0
    @AppStorage(Settings.Keys.haptics) private var hapticsOn = true
    @AppStorage(Settings.Keys.deadband) private var deadband = 0.02

    var body: some View {
        NavigationStack {
            Form {
                Section("Mac im WLAN") {
                    if connection.hosts.isEmpty {
                        Text("Kein Mac gefunden. Läuft Frostline auf dem Mac, und sind beide im selben WLAN?")
                            .foregroundStyle(.secondary)
                    }
                    ForEach(connection.hosts) { host in
                        Button {
                            connection.connect(to: host)
                        } label: {
                            HStack {
                                Text(host.name)
                                Spacer()
                                if connection.status == .connected(host.name) { Image(systemName: "checkmark") }
                            }
                        }
                    }
                    HStack {
                        TextField("IP-Adresse, z. B. 192.168.1.20", text: $manualHost)
                            .keyboardType(.numbersAndPunctuation)
                            .autocorrectionDisabled()
                            .textInputAutocapitalization(.never)
                        Button("Verbinden") { connection.connectManual(address: manualHost) }
                            .disabled(manualHost.trimmingCharacters(in: .whitespaces).isEmpty)
                    }
                    Text(connection.status.label).font(.footnote).foregroundStyle(.secondary)
                }

                Section("Name im Spiel") {
                    TextField("iPhone", text: $pilotName)
                }

                Section("Sticks") {
                    LabeledSlider(title: "Stickgröße", value: $stickSize, range: 45...110, format: "%.0f pt")
                    LabeledSlider(title: "Gasweg", value: $throttleTravel, range: 120...360, format: "%.0f pt")
                    LabeledSlider(title: "Totzone", value: $deadband, range: 0...0.1, format: "%.2f")
                }

                Section("Gyro-Modus") {
                    LabeledSlider(title: "Kippwinkel für Vollausschlag", value: $gyroMaxTilt, range: 15...60, format: "%.0f°")
                    Text("Die Haltung beim Einschalten des Gyro-Modus ist der Nullpunkt.").font(.footnote).foregroundStyle(.secondary)
                }

                Section {
                    Toggle("Vibration", isOn: $hapticsOn)
                }
            }
            .navigationTitle("Frostline Remote")
            .toolbar {
                ToolbarItem(placement: .confirmationAction) {
                    Button("Fertig") { dismiss() }
                }
            }
        }
    }
}

private struct LabeledSlider: View {
    let title: String
    @Binding var value: Double
    let range: ClosedRange<Double>
    let format: String

    var body: some View {
        VStack(alignment: .leading) {
            HStack {
                Text(title)
                Spacer()
                Text(String(format: format, value)).monospacedDigit().foregroundStyle(.secondary)
            }
            Slider(value: $value, in: range)
        }
    }
}
