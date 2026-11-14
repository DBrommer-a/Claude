import Foundation

/// Gespeicherte Einstellungen der App.
enum Settings {
    enum Keys {
        static let useManualHost = "useManualHost"
        static let manualHost = "manualHost"
        static let gyroMaxTilt = "gyroMaxTilt"
        static let haptics = "haptics"
        static let pilotName = "pilotName"
        static let deadband = "deadband"
        // Knüppel wie am Sender
        static let gimbalSize = "gimbalSize"
        static let fingerTravel = "fingerTravel"
        static let stickMode = "stickMode"
        static let absoluteGrip = "absoluteGrip"
        static let detentHaptics = "detentHaptics"
        // Übers Internet: auch im WLAN den Server nehmen
        static let preferRelay = "preferRelay"
    }

    static var pilotName: String {
        let stored = UserDefaults.standard.string(forKey: Keys.pilotName) ?? ""
        return stored.isEmpty ? "iPhone" : stored
    }
}
