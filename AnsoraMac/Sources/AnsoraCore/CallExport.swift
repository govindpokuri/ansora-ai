import Foundation

public enum CallExport {
    public static func escape(_ text: String) -> String {
        text.replacingOccurrences(of: "&", with: "&amp;")
            .replacingOccurrences(of: "<", with: "&lt;")
            .replacingOccurrences(of: ">", with: "&gt;")
            .replacingOccurrences(of: "\"", with: "&quot;")
            .replacingOccurrences(of: "'", with: "&#39;")
    }
    public static func html(_ call: CallRecord) -> String {
        let turns = call.answers.map { item in
            "<article><h2>\(escape(item.question))</h2><pre>\(escape(item.answer))</pre><p>\(item.usage.total) tokens\(item.usagePending ? " · usage pending" : "")\(item.incomplete ? " · answer incomplete" : "")</p>\(item.error.map { "<p>\(escape($0))</p>" } ?? "")</article>"
        }.joined(separator: "\n")
        return """
        <!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
        <title>Ansora AI call notes</title><style>body{font:17px system-ui;max-width:960px;margin:40px auto;padding:24px;color:#17243a;background:#f7f9fc}article{background:white;padding:24px;margin:20px 0;border-radius:16px}pre{white-space:pre-wrap;overflow-wrap:anywhere;font:16px/1.6 ui-monospace,monospace}h2{font-size:20px}</style>
        <h1>Ansora AI · Call notes</h1><p>\(escape(call.startedAt.ISO8601Format())) · \(call.usage.total) reported AI tokens</p>
        <p>\(call.answers.contains { $0.usagePending } ? "Some provider usage is pending reconciliation; see your account for final billing." : "Input and output token usage included.")</p>
        \(turns)<h2>Caller transcript</h2><pre>\(escape(call.transcript))</pre></html>
        """
    }
    public static func save(_ call: CallRecord, directory: URL) throws -> URL {
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true, attributes: [.posixPermissions: 0o700])
        let stem = "call-" + call.id.uuidString
        let json = directory.appendingPathComponent(stem + ".json")
        let html = directory.appendingPathComponent(stem + ".html")
        let encoder = JSONEncoder(); encoder.outputFormatting = [.prettyPrinted, .sortedKeys]; encoder.dateEncodingStrategy = .iso8601
        try encoder.encode(call).write(to: json, options: .atomic)
        try Data(Self.html(call).utf8).write(to: html, options: .atomic)
        for file in [json, html] { try FileManager.default.setAttributes([.posixPermissions: 0o600], ofItemAtPath: file.path) }
        return html
    }
}
