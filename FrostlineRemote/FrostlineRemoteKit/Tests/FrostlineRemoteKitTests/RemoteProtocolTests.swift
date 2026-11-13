import XCTest
@testable import FrostlineRemoteKit

/// Dieselben Testvektoren wie in Assets/Frostline/Tests/Editor/RemoteProtocolTests.cs.
final class RemoteProtocolTests: XCTestCase {
    let controlHex = "464c524d010100000403020140e20100004001800000ffff0902"
    let telemetryHex = "464c524d010400000403020140e20100070006090a0f4c1101d204e7ff32ef0000030102fa"
    let helloHex = "464c524d01020000bebafeca0b54657374206950686f6e65"
    let welcomeHex = "464c524d01030000bebafeca1e00084d6163206d696e69"

    func hex(_ data: Data) -> String { data.map { String(format: "%02x", $0) }.joined() }

    func data(_ hex: String) -> Data {
        var bytes = [UInt8]()
        var index = hex.startIndex
        while index < hex.endIndex {
            let next = hex.index(index, offsetBy: 2)
            bytes.append(UInt8(hex[index..<next], radix: 16)!)
            index = next
        }
        return Data(bytes)
    }

    func testControlMatchesVector() {
        var p = ControlPacket()
        p.sequence = 0x0102_0304
        p.sendTimeMs = 123_456
        p.roll = 16384
        p.pitch = -32767
        p.yaw = 0
        p.throttle = 65535
        p.buttons = [.arm, .gyroActive]
        p.mode = 2
        XCTAssertEqual(hex(p.encoded()), controlHex)
        XCTAssertEqual(ControlPacket.decode(data(controlHex)), p)
    }

    func testTelemetryMatchesVector() {
        var t = TelemetryPacket()
        t.echoSequence = 0x0102_0304
        t.echoSendTimeMs = 123_456
        t.holdMs = 7
        t.batteryCentivolts = 2310
        t.cellMillivolts = 3850
        t.batteryPercent = 76
        t.flags = [.armed, .raceRunning]
        t.mode = 1
        t.speedKmhTenths = 1234
        t.altitudeDecimeters = -25
        t.lapTimeMs = 61234
        t.armEvents = 3
        t.crashEvents = 1
        t.groundEvents = 2
        t.gateEvents = 250
        XCTAssertEqual(hex(t.encoded()), telemetryHex)
        let decoded = TelemetryPacket.decode(data(telemetryHex))
        XCTAssertEqual(decoded, t)
        XCTAssertEqual(decoded?.altitudeMeters ?? 0, -2.5, accuracy: 1e-4)
        XCTAssertEqual(decoded?.lapTime ?? 0, 61.234, accuracy: 1e-6)
    }

    func testHelloAndWelcomeMatchVectors() {
        XCTAssertEqual(hex(HelloPacket(session: 0xCAFE_BABE, name: "Test iPhone").encoded()), helloHex)
        XCTAssertEqual(hex(WelcomePacket(session: 0xCAFE_BABE, telemetryRateHz: 30, name: "Mac mini").encoded()), welcomeHex)
        XCTAssertEqual(HelloPacket.decode(data(helloHex))?.name, "Test iPhone")
        let welcome = WelcomePacket.decode(data(welcomeHex))
        XCTAssertEqual(welcome?.telemetryRateHz, 30)
        XCTAssertEqual(welcome?.name, "Mac mini")
    }

    func testLongNamesAreCutOnCharacterBoundary() {
        let long = String(repeating: "ä", count: 40)
        let decoded = HelloPacket.decode(HelloPacket(session: 1, name: long).encoded())
        XCTAssertEqual(decoded?.name, String(repeating: "ä", count: 16))
    }

    func testForeignPacketsAreRejected() {
        var bytes = [UInt8](data(controlHex))
        XCTAssertNil(ControlPacket.decode(Data(bytes.prefix(20))))
        bytes[0] = 0x58
        XCTAssertNil(ControlPacket.decode(Data(bytes)))
        bytes = [UInt8](data(controlHex))
        bytes[4] = 99
        XCTAssertNil(ControlPacket.decode(Data(bytes)))
        XCTAssertNil(ControlPacket.decode(data(telemetryHex)))
        XCTAssertEqual(RemoteProtocol.packetType(of: data(welcomeHex)), .welcome)
    }

    func testAxisEncoding() {
        XCTAssertEqual(RemoteProtocol.decodeAxis(RemoteProtocol.encodeAxis(0.5)), 0.5, accuracy: 1e-4)
        XCTAssertEqual(RemoteProtocol.encodeAxis(3), 32767)
        XCTAssertEqual(RemoteProtocol.encodeAxis(.nan), 0)
        XCTAssertEqual(RemoteProtocol.decodeAxis(Int16.min), -1)
        XCTAssertEqual(RemoteProtocol.encodeThrottle(-1), 0)
        XCTAssertEqual(RemoteProtocol.decodeThrottle(RemoteProtocol.encodeThrottle(0.25)), 0.25, accuracy: 1e-4)
    }

    func testSequenceWrapAround() {
        XCTAssertTrue(RemoteProtocol.isNewer(2, than: 0xFFFF_FFF0))
        XCTAssertFalse(RemoteProtocol.isNewer(7, than: 7))
        XCTAssertFalse(RemoteProtocol.isNewer(4, than: 5))
    }
}
