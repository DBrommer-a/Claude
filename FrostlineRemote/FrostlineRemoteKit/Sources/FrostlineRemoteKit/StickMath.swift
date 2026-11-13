import Foundation

/// Stick-Logik der virtuellen Sticks, ohne UIKit, damit sie testbar ist.
public enum StickMath {
    /// Totzone um die Mitte, danach wieder auf den vollen Bereich gestreckt.
    public static func deadband(_ value: Float, _ band: Float) -> Float {
        let a = abs(value)
        if a <= band || band >= 0.999 { return 0 }
        return (value < 0 ? -1 : 1) * (a - band) / (1 - band)
    }

    /// Federnder Stick: Fingerversatz vom Aufsetzpunkt (in Punkten) → Achsen -1…1,
    /// auf einen Kreis begrenzt. y wächst nach unten (UIKit), Stick nach oben = positiv.
    public static func springAxes(dx: Double, dy: Double, radius: Double) -> (x: Float, y: Float) {
        guard radius > 0 else { return (0, 0) }
        var x = dx / radius
        var y = -dy / radius
        let length = (x * x + y * y).squareRoot()
        if length > 1 {
            x /= length
            y /= length
        }
        return (Float(x), Float(y))
    }
}

/// Gas-Stick, der nicht zurückfedert. Beim Aufsetzen springt das Gas nicht auf die
/// Fingerposition; es zählt nur die Bewegung ab dort. So bleibt es nach dem Loslassen stehen
/// und springt beim erneuten Anfassen nicht.
public struct RelativeThrottle {
    public private(set) var value: Float = 0
    private var startY: Double = 0
    private var startValue: Float = 0

    public init() {}

    public mutating func begin(touchY: Double) {
        startY = touchY
        startValue = value
    }

    /// travel: Fingerweg in Punkten für den vollen Gasbereich.
    public mutating func move(touchY: Double, travel: Double) {
        guard travel > 0 else { return }
        let delta = Float((startY - touchY) / travel)
        value = max(0, min(1, startValue + delta))
    }

    public mutating func set(_ newValue: Float) {
        value = max(0, min(1, newValue))
    }
}

/// Gyro-Modus: Handy kippen = Nick und Roll. Lage relativ zur beim Einschalten gemerkten Haltung.
public struct GyroMapping {
    /// Kippwinkel für vollen Ausschlag, Grad.
    public var maxTiltDegrees: Double = 30
    public var deadbandDegrees: Double = 1.5
    private var referencePitch: Double = 0
    private var referenceRoll: Double = 0

    public init(maxTiltDegrees: Double = 30) {
        self.maxTiltDegrees = maxTiltDegrees
    }

    /// Aktuelle Haltung als Nullpunkt übernehmen (Winkel im Bogenmaß).
    public mutating func calibrate(pitch: Double, roll: Double) {
        referencePitch = pitch
        referenceRoll = roll
    }

    /// Haltung im Querformat → (roll, pitch) als Stickwerte -1…1. Nach vorn kippen = Nick nach vorn.
    public func axes(pitch: Double, roll: Double) -> (roll: Float, pitch: Float) {
        let toDegrees = 180.0 / Double.pi
        var dPitch = (pitch - referencePitch) * toDegrees
        var dRoll = (roll - referenceRoll) * toDegrees
        dPitch = Self.applyDeadband(dPitch, deadbandDegrees)
        dRoll = Self.applyDeadband(dRoll, deadbandDegrees)
        let range = max(maxTiltDegrees - deadbandDegrees, 1)
        let r = max(-1, min(1, dRoll / range))
        let p = max(-1, min(1, -dPitch / range))
        return (Float(r), Float(p))
    }

    private static func applyDeadband(_ value: Double, _ band: Double) -> Double {
        if abs(value) <= band { return 0 }
        return value > 0 ? value - band : value + band
    }
}

/// Laufzeit App → Mac → App aus der Telemetrie. Geglättet, damit die Anzeige ruhig bleibt.
public struct LatencyMeter {
    public private(set) var roundTripMs: Double?
    private let smoothing: Double

    public init(smoothing: Double = 0.15) {
        self.smoothing = smoothing
    }

    public mutating func add(nowMs: UInt32, echoSendTimeMs: UInt32, holdMs: UInt16) {
        guard echoSendTimeMs != 0 else { return }
        let elapsed = Double(nowMs &- echoSendTimeMs)
        let sample = max(0, elapsed - Double(holdMs))
        guard sample < 5000 else { return }
        if let current = roundTripMs {
            roundTripMs = current + (sample - current) * smoothing
        } else {
            roundTripMs = sample
        }
    }

    public mutating func reset() {
        roundTripMs = nil
    }
}

/// Erkennt neue Ereignisse an den Zählern der Telemetrie (für die Haptik).
public struct EventTracker {
    public enum Event: Equatable { case armed, crash, ground, gate }

    private var last: TelemetryPacket?

    public init() {}

    public mutating func update(_ packet: TelemetryPacket) -> [Event] {
        defer { last = packet }
        guard let previous = last else { return [] }
        var events: [Event] = []
        if packet.armEvents != previous.armEvents { events.append(.armed) }
        if packet.crashEvents != previous.crashEvents { events.append(.crash) }
        if packet.groundEvents != previous.groundEvents { events.append(.ground) }
        if packet.gateEvents != previous.gateEvents { events.append(.gate) }
        return events
    }

    public mutating func reset() {
        last = nil
    }
}

/// Uhr der App in Millisekunden (läuft über, das Protokoll rechnet damit).
public enum RemoteClock {
    public static func nowMs() -> UInt32 {
        UInt32(truncatingIfNeeded: DispatchTime.now().uptimeNanoseconds / 1_000_000)
    }
}
