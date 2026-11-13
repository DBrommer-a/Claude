import CoreMotion
import UIKit
#if canImport(FrostlineRemoteKit)
import FrostlineRemoteKit
#endif

/// Gyro-Modus: Handy nach vorn kippen = Nick, zur Seite drehen = Roll. Nur der Gas-Stick
/// bleibt auf dem Display. Die Haltung beim Einschalten ist der Nullpunkt.
final class GyroController {
    private let motion = CMMotionManager()
    private let queue = OperationQueue()
    private var mapping = GyroMapping()
    private var needsCalibration = true
    /// Bildschirmlage; vom Hauptthread gesetzt, im Bewegungs-Thread gelesen.
    private var orientation: UIInterfaceOrientation = .landscapeRight
    var onAxes: ((Float, Float) -> Void)?

    var isAvailable: Bool { motion.isDeviceMotionAvailable }

    func start(maxTilt: Double) {
        guard motion.isDeviceMotionAvailable, !motion.isDeviceMotionActive else { return }
        mapping.maxTiltDegrees = maxTilt
        needsCalibration = true
        updateOrientation()
        queue.maxConcurrentOperationCount = 1
        motion.deviceMotionUpdateInterval = 1.0 / 100.0
        motion.startDeviceMotionUpdates(to: queue) { [weak self] data, _ in
            guard let self, let gravity = data?.gravity else { return }
            let (forward, side) = Self.tilt(gravity: gravity, orientation: self.orientation)
            // GyroMapping erwartet: kleinerer Winkel = nach vorn.
            if self.needsCalibration {
                self.mapping.calibrate(pitch: -forward, roll: side)
                self.needsCalibration = false
            }
            let axes = self.mapping.axes(pitch: -forward, roll: side)
            DispatchQueue.main.async { self.onAxes?(axes.roll, axes.pitch) }
        }
    }

    func stop() {
        if motion.isDeviceMotionActive { motion.stopDeviceMotionUpdates() }
    }

    func recalibrate() {
        updateOrientation()
        needsCalibration = true
    }

    /// Auf dem Hauptthread aufrufen, wenn sich die Bildschirmlage ändert.
    func updateOrientation() {
        orientation = (UIApplication.shared.connectedScenes.first as? UIWindowScene)?.interfaceOrientation ?? .landscapeRight
    }

    /// Neigung nach vorn und zur Seite aus dem Schwerkraftvektor, passend zur Bildschirmlage.
    private static func tilt(gravity g: CMAcceleration, orientation: UIInterfaceOrientation) -> (forward: Double, side: Double) {
        // Bildschirm-Achsen in Gerätekoordinaten für die beiden Querformat-Lagen.
        var upX = 0.0, upY = 1.0, rightX = 1.0, rightY = 0.0
        switch orientation {
        case .landscapeRight:
            upX = 1; upY = 0; rightX = 0; rightY = -1
        case .landscapeLeft:
            upX = -1; upY = 0; rightX = 0; rightY = 1
        default:
            break
        }
        let gUp = g.x * upX + g.y * upY
        let gRight = g.x * rightX + g.y * rightY
        let forward = atan2(-g.z, -gUp)
        let side = atan2(gRight, (gUp * gUp + g.z * g.z).squareRoot())
        return (forward, side)
    }
}
