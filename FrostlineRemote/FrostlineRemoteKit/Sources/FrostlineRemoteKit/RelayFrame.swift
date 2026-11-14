import Foundation

/// Kopplung mit einem Mac übers Internet, aus dem QR-Code im Spiel (F1 › Spiel):
/// frostline://pair?v=1&h=Server&p=Port&k=Schlüssel&n=Name
public struct RelayPairing: Codable, Equatable {
    public var host: String
    public var port: UInt16
    public var secret: [UInt8]
    public var name: String

    public static let defaultPort: UInt16 = 47821
    public static let secretSize = 32

    public init(host: String, port: UInt16, secret: [UInt8], name: String) {
        self.host = host
        self.port = port
        self.secret = secret
        self.name = name
    }

    public init?(url: URL) {
        self.init(string: url.absoluteString)
    }

    public init?(string: String) {
        guard let components = URLComponents(string: string),
              components.scheme?.lowercased() == "frostline", components.host == "pair" else { return nil }
        var host: String?, port: UInt16?, secret: [UInt8]?, name = ""
        for item in components.queryItems ?? [] {
            switch item.name {
            case "h": host = item.value
            case "p": port = item.value.flatMap { UInt16($0) }
            case "k": secret = item.value.flatMap(RelayPairing.fromBase64URL)
            case "n": name = item.value ?? ""
            default: break
            }
        }
        guard let host, !host.isEmpty, let port, port > 0, let secret, secret.count == RelayPairing.secretSize else { return nil }
        self.init(host: host, port: port, secret: secret, name: name)
    }

    /// Raum-ID beim Relay: SHA-256("frostline-room-v1" + Schlüssel), erste 16 Bytes.
    public var roomID: [UInt8] {
        Array(RelayCrypto.sha256(Array("frostline-room-v1".utf8) + secret).prefix(16))
    }

    static func fromBase64URL(_ text: String) -> [UInt8]? {
        var s = text.replacingOccurrences(of: "-", with: "+").replacingOccurrences(of: "_", with: "/")
        while s.count % 4 != 0 { s += "=" }
        return Data(base64Encoded: s).map { Array($0) }
    }
}

/// Rahmen für den Frostline Relay (gleich in C# RemoteRelay.cs und Python frostline_relay.py):
/// „FLRY“, Version 1, Rolle, Raum-ID (16), HMAC-SHA-256[:8] über Kopf + Nutzdaten, Nutzdaten.
public enum RelayFrame {
    public static let headerSize = 30
    public static let roleServer: UInt8 = 0
    public static let roleGame: UInt8 = 1
    public static let rolePhone: UInt8 = 2

    public enum Opened: Equatable {
        case peer([UInt8])
        case serverAck(peerPresent: Bool)
        case invalid
    }

    /// Packt Nutzdaten (leer = Lebenszeichen) in einen signierten Rahmen.
    public static func seal(_ payload: [UInt8], role: UInt8, room: [UInt8], secret: [UInt8]) -> [UInt8] {
        var head: [UInt8] = Array("FLRY".utf8) + [1, role] + room
        let tag = RelayCrypto.hmacSHA256(key: secret, message: head + payload).prefix(8)
        head += tag
        return head + payload
    }

    public static func open(_ data: [UInt8], room: [UInt8], secret: [UInt8], expectedRole: UInt8) -> Opened {
        guard data.count >= headerSize, data[0] == 0x46, data[1] == 0x4C, data[2] == 0x52, data[3] == 0x59, data[4] == 1,
              Array(data[6..<22]) == room else { return .invalid }
        if data[5] == roleServer {
            return .serverAck(peerPresent: data.count > headerSize && data[headerSize] == 1)
        }
        guard data[5] == expectedRole else { return .invalid }
        let payload = Array(data[headerSize...])
        let tag = RelayCrypto.hmacSHA256(key: secret, message: Array(data[0..<22]) + payload).prefix(8)
        var diff: UInt8 = 0
        for (a, b) in zip(tag, data[22..<30]) { diff |= a ^ b }
        return diff == 0 ? .peer(payload) : .invalid
    }
}
