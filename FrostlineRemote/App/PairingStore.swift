import Foundation
import Security
#if canImport(FrostlineRemoteKit)
import FrostlineRemoteKit
#endif

/// Bewahrt die Kopplung aus dem QR-Code im Schlüsselbund auf (der Schlüssel gibt Zugriff auf die Drohne).
enum PairingStore {
    private static let service = "app.frostline.remote"
    private static let account = "relay-pairing"

    static func load() -> RelayPairing? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
        ]
        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess, let data = item as? Data else { return nil }
        return try? JSONDecoder().decode(RelayPairing.self, from: data)
    }

    static func save(_ pairing: RelayPairing) {
        guard let data = try? JSONEncoder().encode(pairing) else { return }
        delete()
        let item: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly,
            kSecValueData as String: data,
        ]
        SecItemAdd(item as CFDictionary, nil)
    }

    static func delete() {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
        ]
        SecItemDelete(query as CFDictionary)
    }
}
