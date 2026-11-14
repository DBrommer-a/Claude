import Foundation

/// Ein Steuerknüppel wie an einem echten Sender: quadratischer Weg, jede Achse einzeln begrenzt,
/// pro Achse federnd (zur Mitte) oder stehen bleibend (Gas). Ohne UIKit, damit es testbar ist.
public struct GimbalStick: Equatable {
    public enum Spring: Equatable {
        /// Federt beim Loslassen zur Mitte (Roll, Nick, Gier).
        case center
        /// Bleibt stehen (Gas).
        case hold
    }

    /// Wie der Daumen den Knüppel führt.
    public enum Grip: Equatable {
        /// Der Knüppel bewegt sich ab seiner Stellung mit dem Daumen; Anfassen lässt ihn nicht springen.
        case relative
        /// Der Knüppel springt unter den Daumen.
        case absolute
    }

    /// Stellung −1…1; y positiv = nach oben.
    public private(set) var x: Float = 0
    public private(set) var y: Float = 0
    public var springX: Spring
    public var springY: Spring
    public var grip: Grip

    private var startTouchX = 0.0
    private var startTouchY = 0.0
    private var startX: Float = 0
    private var startY: Float = 0

    public init(springX: Spring = .center, springY: Spring = .center, grip: Grip = .relative) {
        self.springX = springX
        self.springY = springY
        self.grip = grip
        // Gas steht beim Start unten.
        if springY == .hold { y = -1 }
        if springX == .hold { x = -1 }
    }

    /// Daumen setzt auf. travel: Weg in Punkten von der Mitte bis zum Anschlag.
    public mutating func begin(touchX: Double, touchY: Double, centerX: Double, centerY: Double, travel: Double) {
        startTouchX = touchX
        startTouchY = touchY
        startX = x
        startY = y
        if grip == .absolute { move(touchX: touchX, touchY: touchY, centerX: centerX, centerY: centerY, travel: travel) }
    }

    public mutating func move(touchX: Double, touchY: Double, centerX: Double, centerY: Double, travel: Double) {
        guard travel > 0 else { return }
        switch grip {
        case .relative:
            x = Self.clamp(startX + Float((touchX - startTouchX) / travel))
            y = Self.clamp(startY - Float((touchY - startTouchY) / travel))
        case .absolute:
            x = Self.clamp(Float((touchX - centerX) / travel))
            y = Self.clamp(Float((centerY - touchY) / travel))
        }
    }

    /// Daumen hebt ab: federnde Achsen zurück zur Mitte.
    public mutating func release() {
        if springX == .center { x = 0 }
        if springY == .center { y = 0 }
    }

    /// Stellung von außen setzen (z. B. Gas auf null, Tests).
    public mutating func set(x: Float? = nil, y: Float? = nil) {
        if let x { self.x = Self.clamp(x) }
        if let y { self.y = Self.clamp(y) }
    }

    /// Gas aus der senkrechten Achse: unten 0, oben 1.
    public static func throttle(fromAxis value: Float) -> Float {
        max(0, min(1, (value + 1) * 0.5))
    }

    static func clamp(_ v: Float) -> Float { max(-1, min(1, v)) }
}

/// Rasten für spürbares Feedback: meldet, wenn ein Wert eine Raste überquert oder erreicht.
public struct DetentTracker: Equatable {
    public let detents: [Float]
    private var last: Float?

    public init(detents: [Float]) {
        self.detents = detents
    }

    /// true, wenn der Wert seit dem letzten Aufruf eine Raste überquert oder genau erreicht hat.
    /// Von einer Raste wegzugehen zählt nicht, sonst gäbe es doppelte Rückmeldungen.
    public mutating func crossed(_ value: Float) -> Bool {
        defer { last = value }
        guard let previous = last, previous != value else { return false }
        for d in detents where previous != d {
            if (previous < d && value >= d) || (previous > d && value <= d) { return true }
        }
        return false
    }

    public mutating func reset() { last = nil }
}
