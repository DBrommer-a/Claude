import XCTest
@testable import FrostlineRemoteKit

final class StickMathTests: XCTestCase {
    func testSpringAxesClampToCircle() {
        let full = StickMath.springAxes(dx: 200, dy: 0, radius: 70)
        XCTAssertEqual(full.x, 1, accuracy: 1e-5)
        XCTAssertEqual(full.y, 0, accuracy: 1e-5)
        // Finger nach oben (dy negativ) = Stick nach vorn = positiv.
        let up = StickMath.springAxes(dx: 0, dy: -35, radius: 70)
        XCTAssertEqual(up.y, 0.5, accuracy: 1e-5)
        let diagonal = StickMath.springAxes(dx: 100, dy: -100, radius: 70)
        XCTAssertEqual(Double(diagonal.x * diagonal.x + diagonal.y * diagonal.y), 1, accuracy: 1e-4)
    }

    func testDeadband() {
        XCTAssertEqual(StickMath.deadband(0.02, 0.03), 0)
        XCTAssertEqual(StickMath.deadband(1, 0.03), 1, accuracy: 1e-6)
        XCTAssertEqual(StickMath.deadband(-0.5, 0.1), -0.4444, accuracy: 1e-3)
    }

    func testThrottleDoesNotJumpWhenTouchedAgain() {
        var throttle = RelativeThrottle()
        throttle.begin(touchY: 500)
        throttle.move(touchY: 400, travel: 200)
        XCTAssertEqual(throttle.value, 0.5, accuracy: 1e-5)
        // Loslassen und weit oben wieder anfassen: Gas bleibt bei 0,5.
        throttle.begin(touchY: 100)
        throttle.move(touchY: 100, travel: 200)
        XCTAssertEqual(throttle.value, 0.5, accuracy: 1e-5)
        throttle.move(touchY: 400, travel: 200)
        XCTAssertEqual(throttle.value, 0, accuracy: 1e-5, "nicht unter null")
    }

    func testGyroMapping() {
        var gyro = GyroMapping(maxTiltDegrees: 31.5)
        gyro.calibrate(pitch: 0.2, roll: -0.1)
        let rest = gyro.axes(pitch: 0.2, roll: -0.1)
        XCTAssertEqual(rest.roll, 0)
        XCTAssertEqual(rest.pitch, 0)
        let deg = Double.pi / 180
        let tilted = gyro.axes(pitch: 0.2 - 16.5 * deg, roll: -0.1 + 31.5 * deg)
        XCTAssertEqual(tilted.pitch, 0.5, accuracy: 1e-3, "nach vorn kippen = Nick nach vorn")
        XCTAssertEqual(tilted.roll, 1, accuracy: 1e-3)
        let far = gyro.axes(pitch: 0.2, roll: 2)
        XCTAssertEqual(far.roll, 1)
    }

    func testLatencyMeterSubtractsHoldTime() {
        var meter = LatencyMeter(smoothing: 1)
        meter.add(nowMs: 1030, echoSendTimeMs: 1000, holdMs: 12)
        XCTAssertEqual(meter.roundTripMs ?? 0, 18, accuracy: 1e-6)
        meter.add(nowMs: 5, echoSendTimeMs: 0xFFFF_FFF0, holdMs: 1)
        XCTAssertEqual(meter.roundTripMs ?? 0, 20, accuracy: 1e-6, "Überlauf der Uhr")
    }

    func testEventTrackerReportsChangedCounters() {
        var tracker = EventTracker()
        var t = TelemetryPacket()
        XCTAssertEqual(tracker.update(t), [])
        t.crashEvents = 1
        t.gateEvents = 255
        XCTAssertEqual(tracker.update(t), [.crash, .gate])
        t.gateEvents = 0
        XCTAssertEqual(tracker.update(t), [.gate], "Überlauf zählt auch")
        XCTAssertEqual(tracker.update(t), [])
    }
}
