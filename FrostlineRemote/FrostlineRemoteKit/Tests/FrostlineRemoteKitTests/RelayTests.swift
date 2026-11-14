import XCTest
@testable import FrostlineRemoteKit

final class RelayTests: XCTestCase {
    private func hex(_ bytes: [UInt8]) -> String { bytes.map { String(format: "%02x", $0) }.joined() }
    private let secret: [UInt8] = Array(0..<32)

    func testSHA256KnownVector() {
        XCTAssertEqual(hex(RelayCrypto.sha256(Array("abc".utf8))),
                       "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
        XCTAssertEqual(hex(RelayCrypto.sha256([])),
                       "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855")
    }

    func testHMACRFC4231Case2() {
        XCTAssertEqual(hex(RelayCrypto.hmacSHA256(key: Array("Jefe".utf8), message: Array("what do ya want for nothing?".utf8))),
                       "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843")
    }

    func testFrameMatchesSharedTestVector() {
        // Derselbe Vektor steht im Python-Relay und in den C#-Tests des Spiels.
        let pairing = RelayPairing(host: "x", port: 1, secret: secret, name: "")
        let frame = RelayFrame.seal([1, 2, 3], role: RelayFrame.rolePhone, room: pairing.roomID, secret: secret)
        XCTAssertEqual(hex(frame), "464c525901025ff9a001b4697e8eb69dea7d3cda081a869d8f12c1aacd6a010203")
    }

    func testOpenChecksRoleRoomAndSignature() {
        let room = RelayPairing(host: "x", port: 1, secret: secret, name: "").roomID
        var frame = RelayFrame.seal([7, 7], role: RelayFrame.roleGame, room: room, secret: secret)
        XCTAssertEqual(RelayFrame.open(frame, room: room, secret: secret, expectedRole: RelayFrame.roleGame), .peer([7, 7]))
        XCTAssertEqual(RelayFrame.open(frame, room: room, secret: secret, expectedRole: RelayFrame.rolePhone), .invalid)
        frame[frame.count - 1] ^= 1
        XCTAssertEqual(RelayFrame.open(frame, room: room, secret: secret, expectedRole: RelayFrame.roleGame), .invalid)

        let ack: [UInt8] = Array("FLRY".utf8) + [1, 0] + room + [UInt8](repeating: 0, count: 8) + [1]
        XCTAssertEqual(RelayFrame.open(ack, room: room, secret: secret, expectedRole: RelayFrame.roleGame), .serverAck(peerPresent: true))
    }

    func testPairingURLFromGame() {
        // So schreibt ihn das Spiel (RemoteRelay.PairingUrl).
        let url = "frostline://pair?v=1&h=31.70.130.250&p=47821&k=AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8&n=MacBook%20Air%20von%20David"
        let pairing = RelayPairing(string: url)
        XCTAssertEqual(pairing?.host, "31.70.130.250")
        XCTAssertEqual(pairing?.port, 47821)
        XCTAssertEqual(pairing?.secret, secret)
        XCTAssertEqual(pairing?.name, "MacBook Air von David")
        XCTAssertNil(RelayPairing(string: "https://example.org/pair?h=x"))
        XCTAssertNil(RelayPairing(string: "frostline://pair?h=x&p=1&k=kurz"))
    }
}
