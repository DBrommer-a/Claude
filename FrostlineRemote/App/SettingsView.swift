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
    @AppStorage(Settings.Keys.gimbalSize) private var gimbalSize = 205.0
    @AppStorage(Settings.Keys.fingerTravel) private var fingerTravel = 0.85
    @AppStorage(Settings.Keys.stickMode) private var stickMode = 2
    @AppStorage(Settings.Keys.absoluteGrip) private var absoluteGrip = false
    @AppStorage(Settings.Keys.detentHaptics) private var detentHaptics = true
    @AppStorage(Settings.Keys.preferRelay) private var preferRelay = false
    @State private var showScanner = false
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

                Section {
                    if let pairing = connection.pairing {
                        LabeledContent("Gekoppelt mit", value: pairing.name.isEmpty ? pairing.host : pairing.name)
                        LabeledContent("Server", value: "\(pairing.host):\(pairing.port)")
                        LabeledContent("Spiel am Server", value: connection.viaRelay ? (connection.relayPeerPresent ? "angemeldet" : "nicht gemeldet") : "–")
                        Button("Jetzt über Server verbinden") { connection.connectRelay() }
                        Toggle("Immer über Server (auch im WLAN)", isOn: $preferRelay)
                        Button("Kopplung löschen") { connection.forgetPairing() }
                    } else {
                        Text("Noch nicht gekoppelt. Im Spiel F1 › Spiel öffnen und den QR-Code scannen – mit dem Knopf unten oder der Kamera-App.")
                            .foregroundStyle(.secondary)
                    }
                    Button {
                        showScanner = true
                    } label: {
                        Label(connection.pairing == nil ? "QR-Code scannen" : "Neu koppeln (QR-Code scannen)", systemImage: "qrcode.viewfinder")
                    }
                } header: {
                    Text("Übers Internet (ohne gleiches WLAN)")
                } footer: {
                    Text("Einmal scannen genügt: Die Kopplung bleibt gespeichert. Im selben WLAN verbindet sich die App weiter direkt, sonst über den Server – auch über Mobilfunk.")
                }

                Section("Name im Spiel") {
                    TextField("iPhone", text: $pilotName)
                }

                Section {
                    Picker("Belegung", selection: $stickMode) {
                        Text("Mode 2 (Gas links)").tag(2)
                        Text("Mode 1 (Gas rechts)").tag(1)
                    }
                    LabeledSlider(title: "Knüppelgröße", value: $gimbalSize, range: 130...260, format: "%.0f pt")
                    LabeledSlider(title: "Fingerweg für Vollausschlag", value: $fingerTravel, range: 0.5...1.0, format: "%.0f %%", scale: 100)
                    LabeledSlider(title: "Totzone", value: $deadband, range: 0...0.1, format: "%.2f")
                    Toggle("Knüppel springt unter den Daumen", isOn: $absoluteGrip)
                    Toggle("Rasten spüren (Mitte, 0 % und 50 % Gas)", isOn: $detentHaptics)
                } header: {
                    Text("Knüppel")
                } footer: {
                    Text("Standard wie am Sender: Der Knüppel bewegt sich ab seiner Stellung mit dem Daumen und springt beim Anfassen nicht. Gas bleibt stehen, alle anderen Achsen federn zur Mitte. Weniger Fingerweg = empfindlicher.")
                }

                Section("Gyro-Modus") {
                    LabeledSlider(title: "Kippwinkel für Vollausschlag", value: $gyroMaxTilt, range: 15...60, format: "%.0f°")
                    Text("Die Haltung beim Einschalten des Gyro-Modus ist der Nullpunkt.").font(.footnote).foregroundStyle(.secondary)
                }

                Section {
                    Toggle("Vibration", isOn: $hapticsOn)
                }
            }
            .sheet(isPresented: $showScanner) {
                QRScannerSheet { url in
                    connection.pair(with: url)
                    showScanner = false
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
    var scale: Double = 1

    var body: some View {
        VStack(alignment: .leading) {
            HStack {
                Text(title)
                Spacer()
                Text(String(format: format, value * scale)).monospacedDigit().foregroundStyle(.secondary)
            }
            Slider(value: $value, in: range)
        }
    }
}
