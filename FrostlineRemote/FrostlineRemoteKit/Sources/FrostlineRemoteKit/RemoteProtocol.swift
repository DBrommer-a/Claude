import Foundation

/// Funkprotokoll zwischen „Frostline Remote“ und dem Spiel auf dem Mac.
/// UDP, Little Endian, jedes Paket beginnt mit „FLRM“, Version und Typ.
/// Gegenstück in C#: Assets/Frostline/Shared/Remote/RemoteProtocol.cs.
/// Beide Seiten prüfen gegen dieselben Testvektoren.
public enum RemoteProtocol {
    public static let port: UInt16 = 47820
    public static let bonjourType = "_frostline._udp"
    public static let version: UInt8 = 1
    public static let headerSize = 8
    public static let maxNameBytes = 32
    /// Bleiben so lange Steuerpakete aus, schaltet das Spiel die Motoren ab.
    public static let failsafeMilliseconds = 200
    public static let noLapTime: UInt32 = 0xFFFF_FFFF
    public static let controlSize = headerSize + 18
    public static let telemetrySize = headerSize + 29

    static let magic: [UInt8] = [0x46, 0x4C, 0x52, 0x4D] // "FLRM"

    public enum PacketType: UInt8 {
        case control = 1
        case hello = 2
        case welcome = 3
        case telemetry = 4
        case goodbye = 5
    }

    /// Pakettyp eines empfangenen Datagramms, oder nil bei fremden/kaputten Paketen.
    public static func packetType(of data: Data) -> PacketType? {
        let bytes = [UInt8](data)
        guard bytes.count >= headerSize,
              Array(bytes[0..<4]) == magic,
              bytes[4] == version else { return nil }
        return PacketType(rawValue: bytes[5])
    }

    static func header(_ type: PacketType) -> ByteWriter {
        var writer = ByteWriter()
        writer.bytes(magic)
        writer.u8(version)
        writer.u8(type.rawValue)
        writer.u16(0)
        return writer
    }

    // MARK: Umrechnung

    public static func encodeAxis(_ value: Float) -> Int16 {
        guard value.isFinite else { return 0 }
        let clamped = max(-1, min(1, value))
        return Int16((clamped * 32767).rounded())
    }

    public static func decodeAxis(_ value: Int16) -> Float {
        max(-1, Float(value) / 32767)
    }

    public static func encodeThrottle(_ value: Float) -> UInt16 {
        guard value.isFinite else { return 0 }
        let clamped = max(0, min(1, value))
        return UInt16((clamped * 65535).rounded())
    }

    public static func decodeThrottle(_ value: UInt16) -> Float {
        Float(value) / 65535
    }

    /// true, wenn a neuer ist als b (Überlauf berücksichtigt).
    public static func isNewer(_ a: UInt32, than b: UInt32) -> Bool {
        Int32(bitPattern: a &- b) > 0
    }

    public static func goodbye() -> Data {
        header(.goodbye).data
    }
}

public struct RemoteButtons: OptionSet, Equatable {
    public let rawValue: UInt8
    public init(rawValue: UInt8) { self.rawValue = rawValue }
    public static let arm = RemoteButtons(rawValue: 1)
    public static let turtle = RemoteButtons(rawValue: 2)
    public static let reset = RemoteButtons(rawValue: 4)
    public static let gyroActive = RemoteButtons(rawValue: 8)
}

public struct TelemetryFlags: OptionSet, Equatable {
    public let rawValue: UInt8
    public init(rawValue: UInt8) { self.rawValue = rawValue }
    public static let armed = TelemetryFlags(rawValue: 1)
    public static let failsafe = TelemetryFlags(rawValue: 2)
    public static let turtle = TelemetryFlags(rawValue: 4)
    public static let armingBlocked = TelemetryFlags(rawValue: 8)
    public static let raceRunning = TelemetryFlags(rawValue: 16)
}

public enum FlightMode: UInt8, CaseIterable, Identifiable {
    case acro = 0
    case angle = 1
    case horizon = 2

    public var id: UInt8 { rawValue }
    public var label: String {
        switch self {
        case .acro: return "Acro"
        case .angle: return "Angle"
        case .horizon: return "Horizon"
        }
    }
}

/// App → Spiel, 250-mal pro Sekunde.
public struct ControlPacket: Equatable {
    public var sequence: UInt32 = 0
    /// Uhr der App in ms; kommt in der Telemetrie zurück (Laufzeitmessung).
    public var sendTimeMs: UInt32 = 0
    public var roll: Int16 = 0
    public var pitch: Int16 = 0
    public var yaw: Int16 = 0
    public var throttle: UInt16 = 0
    public var buttons: RemoteButtons = []
    public var mode: UInt8 = 0

    public init() {}

    public func encoded() -> Data {
        var w = RemoteProtocol.header(.control)
        w.u32(sequence)
        w.u32(sendTimeMs)
        w.i16(roll)
        w.i16(pitch)
        w.i16(yaw)
        w.u16(throttle)
        w.u8(buttons.rawValue)
        w.u8(mode)
        return w.data
    }

    public static func decode(_ data: Data) -> ControlPacket? {
        guard RemoteProtocol.packetType(of: data) == .control, data.count >= RemoteProtocol.controlSize else { return nil }
        var r = ByteReader(data, offset: RemoteProtocol.headerSize)
        var p = ControlPacket()
        p.sequence = r.u32()
        p.sendTimeMs = r.u32()
        p.roll = r.i16()
        p.pitch = r.i16()
        p.yaw = r.i16()
        p.throttle = r.u16()
        p.buttons = RemoteButtons(rawValue: r.u8())
        p.mode = r.u8()
        return p
    }
}

/// Spiel → App, etwa 30-mal pro Sekunde.
public struct TelemetryPacket: Equatable {
    public var echoSequence: UInt32 = 0
    public var echoSendTimeMs: UInt32 = 0
    /// Wie lange das letzte Steuerpaket im Spiel lag, bevor diese Telemetrie rausging.
    public var holdMs: UInt16 = 0
    public var batteryCentivolts: UInt16 = 0
    public var cellMillivolts: UInt16 = 0
    public var batteryPercent: UInt8 = 0
    public var flags: TelemetryFlags = []
    public var mode: UInt8 = 0
    public var speedKmhTenths: UInt16 = 0
    public var altitudeDecimeters: Int16 = 0
    public var lapTimeMs: UInt32 = RemoteProtocol.noLapTime
    // Zähler statt einmaliger Ereignisse: übersteht verlorene Pakete.
    public var armEvents: UInt8 = 0
    public var crashEvents: UInt8 = 0
    public var groundEvents: UInt8 = 0
    public var gateEvents: UInt8 = 0

    public init() {}

    public var batteryVolts: Float { Float(batteryCentivolts) / 100 }
    public var cellVolts: Float { Float(cellMillivolts) / 1000 }
    public var speedKmh: Float { Float(speedKmhTenths) / 10 }
    public var altitudeMeters: Float { Float(altitudeDecimeters) / 10 }
    public var lapTime: Double? { lapTimeMs == RemoteProtocol.noLapTime ? nil : Double(lapTimeMs) / 1000 }

    public func encoded() -> Data {
        var w = RemoteProtocol.header(.telemetry)
        w.u32(echoSequence)
        w.u32(echoSendTimeMs)
        w.u16(holdMs)
        w.u16(batteryCentivolts)
        w.u16(cellMillivolts)
        w.u8(batteryPercent)
        w.u8(flags.rawValue)
        w.u8(mode)
        w.u16(speedKmhTenths)
        w.i16(altitudeDecimeters)
        w.u32(lapTimeMs)
        w.u8(armEvents)
        w.u8(crashEvents)
        w.u8(groundEvents)
        w.u8(gateEvents)
        return w.data
    }

    public static func decode(_ data: Data) -> TelemetryPacket? {
        guard RemoteProtocol.packetType(of: data) == .telemetry, data.count >= RemoteProtocol.telemetrySize else { return nil }
        var r = ByteReader(data, offset: RemoteProtocol.headerSize)
        var p = TelemetryPacket()
        p.echoSequence = r.u32()
        p.echoSendTimeMs = r.u32()
        p.holdMs = r.u16()
        p.batteryCentivolts = r.u16()
        p.cellMillivolts = r.u16()
        p.batteryPercent = r.u8()
        p.flags = TelemetryFlags(rawValue: r.u8())
        p.mode = r.u8()
        p.speedKmhTenths = r.u16()
        p.altitudeDecimeters = r.i16()
        p.lapTimeMs = r.u32()
        p.armEvents = r.u8()
        p.crashEvents = r.u8()
        p.groundEvents = r.u8()
        p.gateEvents = r.u8()
        return p
    }
}

/// App meldet sich an.
public struct HelloPacket: Equatable {
    public var session: UInt32
    public var name: String

    public init(session: UInt32, name: String) {
        self.session = session
        self.name = name
    }

    public func encoded() -> Data {
        var w = RemoteProtocol.header(.hello)
        w.u32(session)
        w.name(name)
        return w.data
    }

    public static func decode(_ data: Data) -> HelloPacket? {
        guard RemoteProtocol.packetType(of: data) == .hello, data.count >= RemoteProtocol.headerSize + 5 else { return nil }
        var r = ByteReader(data, offset: RemoteProtocol.headerSize)
        let session = r.u32()
        guard let name = r.name() else { return nil }
        return HelloPacket(session: session, name: name)
    }
}

/// Spiel antwortet.
public struct WelcomePacket: Equatable {
    public var session: UInt32
    public var telemetryRateHz: UInt16
    public var name: String

    public init(session: UInt32, telemetryRateHz: UInt16, name: String) {
        self.session = session
        self.telemetryRateHz = telemetryRateHz
        self.name = name
    }

    public func encoded() -> Data {
        var w = RemoteProtocol.header(.welcome)
        w.u32(session)
        w.u16(telemetryRateHz)
        w.name(name)
        return w.data
    }

    public static func decode(_ data: Data) -> WelcomePacket? {
        guard RemoteProtocol.packetType(of: data) == .welcome, data.count >= RemoteProtocol.headerSize + 7 else { return nil }
        var r = ByteReader(data, offset: RemoteProtocol.headerSize)
        let session = r.u32()
        let rate = r.u16()
        guard let name = r.name() else { return nil }
        return WelcomePacket(session: session, telemetryRateHz: rate, name: name)
    }
}

// MARK: - Bytes lesen und schreiben (Little Endian)

struct ByteWriter {
    private(set) var data = Data()

    mutating func bytes(_ values: [UInt8]) { data.append(contentsOf: values) }
    mutating func u8(_ v: UInt8) { data.append(v) }
    mutating func u16(_ v: UInt16) { data.append(UInt8(v & 0xFF)); data.append(UInt8(v >> 8)) }
    mutating func i16(_ v: Int16) { u16(UInt16(bitPattern: v)) }
    mutating func u32(_ v: UInt32) {
        for shift in stride(from: 0, to: 32, by: 8) { data.append(UInt8((v >> UInt32(shift)) & 0xFF)) }
    }

    /// Längen-Byte und UTF-8, höchstens 32 Bytes, nie mitten in einem Zeichen abgeschnitten.
    mutating func name(_ text: String) {
        var utf8 = [UInt8](text.utf8)
        if utf8.count > RemoteProtocol.maxNameBytes {
            var count = RemoteProtocol.maxNameBytes
            while count > 0 && (utf8[count] & 0xC0) == 0x80 { count -= 1 }
            utf8 = Array(utf8[0..<count])
        }
        u8(UInt8(utf8.count))
        bytes(utf8)
    }
}

struct ByteReader {
    private let bytes: [UInt8]
    private var index: Int

    init(_ data: Data, offset: Int) {
        bytes = [UInt8](data)
        index = offset
    }

    mutating func u8() -> UInt8 {
        guard index < bytes.count else { return 0 }
        defer { index += 1 }
        return bytes[index]
    }

    mutating func u16() -> UInt16 {
        let lo = UInt16(u8())
        let hi = UInt16(u8())
        return lo | (hi << 8)
    }

    mutating func i16() -> Int16 { Int16(bitPattern: u16()) }

    mutating func u32() -> UInt32 {
        var value: UInt32 = 0
        for shift in stride(from: 0, to: 32, by: 8) { value |= UInt32(u8()) << UInt32(shift) }
        return value
    }

    mutating func name() -> String? {
        let count = Int(u8())
        guard count <= RemoteProtocol.maxNameBytes, index + count <= bytes.count else { return nil }
        defer { index += count }
        return String(decoding: bytes[index..<(index + count)], as: UTF8.self)
    }
}
