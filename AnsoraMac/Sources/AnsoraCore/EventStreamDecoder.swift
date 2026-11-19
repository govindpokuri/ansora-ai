import Foundation

// Frame SSE from bytes rather than relying on platform line adapters preserving blank lines.
public struct EventStreamDecoder {
    private var line = Data()
    private var rows: [String] = []
    private var frameBytes = 0
    public init() {}
    public mutating func append(_ byte: UInt8) throws -> String? {
        if byte != 10 {
            guard line.count < 1_000_000 else { throw CoreError.message("The answer stream exceeded its frame limit.") }
            line.append(byte); return nil
        }
        return try consumeLine()
    }
    private mutating func consumeLine() throws -> String? {
        if line.last == 13 { line.removeLast() }
        guard let text = String(data: line, encoding: .utf8) else { throw CoreError.message("The answer stream contained invalid text.") }
        line.removeAll(keepingCapacity: true)
        if text.isEmpty {
            guard !rows.isEmpty else { return nil }
            let payload = rows.joined(separator: "\n"); rows = []; frameBytes = 0
            return payload
        }
        if text.hasPrefix("data:") {
            var value = String(text.dropFirst(5))
            if value.hasPrefix(" ") { value.removeFirst() }
            frameBytes += value.utf8.count
            guard frameBytes <= 1_000_000 else { throw CoreError.message("The answer stream exceeded its frame limit.") }
            rows.append(value)
        }
        return nil
    }
    public mutating func finish() throws -> String? {
        if !line.isEmpty, let payload = try consumeLine() { return payload }
        guard !rows.isEmpty else { return nil }
        let payload = rows.joined(separator: "\n"); rows = []; frameBytes = 0
        return payload
    }
}
