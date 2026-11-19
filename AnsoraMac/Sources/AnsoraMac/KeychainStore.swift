import Foundation
import Security
import AnsoraCore

enum KeychainStore {
    private static let service = "com.ansora-ai.mac.prototype"
    private static func query(_ name: String) -> [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: name]
    }
    static func save<T: Encodable>(_ value: T, name: String) throws {
        let data = try JSONEncoder().encode(value)
        let existing = SecItemUpdate(query(name) as CFDictionary, [kSecValueData as String: data] as CFDictionary)
        if existing == errSecSuccess { return }
        guard existing == errSecItemNotFound else { throw CoreError.message("Keychain could not update the saved account (\(existing)).") }
        var item = query(name)
        item[kSecValueData as String] = data
        item[kSecAttrAccessible as String] = kSecAttrAccessibleWhenUnlockedThisDeviceOnly
        let status = SecItemAdd(item as CFDictionary, nil)
        guard status == errSecSuccess else { throw CoreError.message("Keychain could not save the account (\(status)).") }
    }
    static func load<T: Decodable>(_ type: T.Type, name: String) throws -> T? {
        var item = query(name)
        item[kSecReturnData as String] = true; item[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?
        let status = SecItemCopyMatching(item as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess, let data = result as? Data else { throw CoreError.message("Keychain could not read saved data (\(status)).") }
        return try JSONDecoder().decode(type, from: data)
    }
    static func remove(_ name: String) throws {
        let status = SecItemDelete(query(name) as CFDictionary)
        guard status == errSecSuccess || status == errSecItemNotFound else { throw CoreError.message("Keychain could not remove saved data (\(status)).") }
    }
}
