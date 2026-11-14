import XCTest
@testable import FrostlineRemoteKit

final class GimbalMathTests: XCTestCase {
    func testThrottleStartsAtZeroAndHolds() {
        var stick = GimbalStick(springX: .center, springY: .hold)
        XCTAssertEqual(GimbalStick.throttle(fromAxis: stick.y), 0)
        stick.begin(touchX: 100, touchY: 200, centerX: 100, centerY: 100, travel: 50)
        stick.move(touchX: 100, touchY: 150, centerX: 100, centerY: 100, travel: 50) // 50 pt nach oben
        XCTAssertEqual(stick.y, 0, accuracy: 0.0001)
        stick.release()
        XCTAssertEqual(stick.y, 0, accuracy: 0.0001, "Gas bleibt nach dem Loslassen stehen")
        XCTAssertEqual(GimbalStick.throttle(fromAxis: stick.y), 0.5, accuracy: 0.0001)
    }

    func testRelativeGripDoesNotJumpOnTouch() {
        var stick = GimbalStick(springX: .center, springY: .hold)
        stick.set(y: 0.2)
        // Aufsetzen weit weg von der Knüppelstellung: nichts springt.
        stick.begin(touchX: 10, touchY: 10, centerX: 100, centerY: 100, travel: 50)
        XCTAssertEqual(stick.y, 0.2, accuracy: 0.0001)
        XCTAssertEqual(stick.x, 0, accuracy: 0.0001)
    }

    func testAbsoluteGripJumpsUnderThumb() {
        var stick = GimbalStick(grip: .absolute)
        stick.begin(touchX: 125, touchY: 75, centerX: 100, centerY: 100, travel: 50)
        XCTAssertEqual(stick.x, 0.5, accuracy: 0.0001)
        XCTAssertEqual(stick.y, 0.5, accuracy: 0.0001)
    }

    func testSquareLimitPerAxis() {
        var stick = GimbalStick()
        stick.begin(touchX: 0, touchY: 0, centerX: 0, centerY: 0, travel: 50)
        stick.move(touchX: 500, touchY: -500, centerX: 0, centerY: 0, travel: 50)
        // Quadratischer Weg wie am Sender: beide Achsen voll, nicht auf einen Kreis gekürzt.
        XCTAssertEqual(stick.x, 1)
        XCTAssertEqual(stick.y, 1)
    }

    func testReleaseCentersSpringAxesOnly() {
        var stick = GimbalStick(springX: .center, springY: .hold)
        stick.begin(touchX: 0, touchY: 0, centerX: 0, centerY: 0, travel: 50)
        stick.move(touchX: 25, touchY: -50, centerX: 0, centerY: 0, travel: 50)
        stick.release()
        XCTAssertEqual(stick.x, 0, "Gier federt zurück")
        XCTAssertEqual(stick.y, 0, accuracy: 0.0001, "Gas (von −1 um +1) bleibt")
    }

    func testDetentReportsCrossingOnce() {
        var detents = DetentTracker(detents: [0])
        XCTAssertFalse(detents.crossed(-0.3))
        XCTAssertTrue(detents.crossed(0.2), "Mitte überquert")
        XCTAssertFalse(detents.crossed(0.4))
        XCTAssertTrue(detents.crossed(0), "auf der Mitte angekommen")
        XCTAssertFalse(detents.crossed(0.1), "von der Mitte weg zählt nicht")
    }
}
