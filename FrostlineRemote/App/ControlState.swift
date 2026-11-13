import Foundation
#if canImport(FrostlineRemoteKit)
import FrostlineRemoteKit
#endif

/// Aktuelle Stickstellungen und Schalter. Wird vom Bildschirm (Hauptthread) geschrieben und
/// vom Sende-Takt (250 Hz, eigener Thread) gelesen, daher mit Sperre.
final class ControlState {
    private let lock = NSLock()
    private var roll: Float = 0
    private var pitch: Float = 0
    private var yaw: Float = 0
    private var throttle: Float = 0
    private var armed = false
    private var turtle = false
    private var gyro = false
    private var mode: FlightMode = .acro
    private var resetUntilMs: UInt32 = 0

    func setSticks(roll: Float? = nil, pitch: Float? = nil, yaw: Float? = nil, throttle: Float? = nil) {
        lock.lock()
        if let roll { self.roll = roll }
        if let pitch { self.pitch = pitch }
        if let yaw { self.yaw = yaw }
        if let throttle { self.throttle = throttle }
        lock.unlock()
    }

    func setArmed(_ value: Bool) { lock.lock(); armed = value; lock.unlock() }
    func setTurtle(_ value: Bool) { lock.lock(); turtle = value; lock.unlock() }
    func setGyro(_ value: Bool) { lock.lock(); gyro = value; lock.unlock() }
    func setMode(_ value: FlightMode) { lock.lock(); mode = value; lock.unlock() }

    /// Reset-Taste: 150 ms lang gedrückt melden, damit es trotz Paketverlust ankommt.
    func pressReset() {
        lock.lock()
        resetUntilMs = RemoteClock.nowMs() &+ 150
        lock.unlock()
    }

    func packet(sequence: UInt32, nowMs: UInt32) -> ControlPacket {
        lock.lock()
        defer { lock.unlock() }
        var p = ControlPacket()
        p.sequence = sequence
        p.sendTimeMs = nowMs
        p.roll = RemoteProtocol.encodeAxis(roll)
        p.pitch = RemoteProtocol.encodeAxis(pitch)
        p.yaw = RemoteProtocol.encodeAxis(yaw)
        p.throttle = RemoteProtocol.encodeThrottle(throttle)
        var buttons: RemoteButtons = []
        if armed { buttons.insert(.arm) }
        if turtle { buttons.insert(.turtle) }
        if gyro { buttons.insert(.gyroActive) }
        if RemoteProtocol.isNewer(resetUntilMs, than: nowMs) { buttons.insert(.reset) }
        p.buttons = buttons
        p.mode = mode.rawValue
        return p
    }
}
