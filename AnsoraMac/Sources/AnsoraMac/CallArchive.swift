import Foundation
import AnsoraCore

actor CallArchive {
    private var versions: [UUID: Int] = [:]
    static var directory: URL {
        FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
            .appendingPathComponent("AnsoraAI/Calls", isDirectory: true)
    }
    func save(_ call: CallRecord, revision: Int) throws -> URL {
        if let saved = versions[call.id], saved > revision {
            return Self.directory.appendingPathComponent("call-" + call.id.uuidString + ".html")
        }
        let url = try CallExport.save(call, directory: Self.directory)
        versions[call.id] = revision
        return url
    }
}
