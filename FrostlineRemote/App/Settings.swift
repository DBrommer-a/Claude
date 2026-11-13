import Foundation

/// Gespeicherte Einstellungen der App.
enum Settings {
    enum Keys {
        static let useManualHost = "useManualHost"
        static let manualHost = "manualHost"
        static let stickSize = "stickSize"
        static let throttleTravel = "throttleTravel"
        static let gyroMaxTilt = "gyroMaxTilt"
        static let haptics = "haptics"
        static let pilotName = "pilotName"
        static let deadband = "deadband"
    }

    static var pilotName: String {
        let stored = UserDefaults.standard.string(forKey: Keys.pilotName) ?? ""
        return stored.isEmpty ? "iPhone" : stored
    }
}
