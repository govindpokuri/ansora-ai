import Foundation

// Recognition revises one growing utterance. Submit only its unconsumed words.
public struct QuestionTracker {
    private var words: [String] = []
    private var consumed = 0
    private var changedAt: TimeInterval = 0
    private var submittedAt: TimeInterval = 0
    public init() {}

    public mutating func update(_ text: String, now: TimeInterval) {
        let next = text.split(whereSeparator: { $0.isWhitespace }).map(String.init)
        if next != words { words = next; changedAt = now }
        consumed = min(consumed, words.count)
    }
    public mutating func reset() { words = []; consumed = 0; changedAt = 0; submittedAt = 0 }

    public mutating func take(now: TimeInterval, final: Bool = false, force: Bool = false) -> String? {
        let pending = Array(words.dropFirst(consumed))
        guard !pending.isEmpty else { return nil }
        let text = pending.joined(separator: " ")
        let lower = text.lowercased()
        let question = text.contains("?") || ["what", "why", "how", "when", "where", "which", "can", "could", "would", "do", "did", "is", "are", "explain", "describe", "tell", "write", "implement", "show"].contains {
            lower == $0 || lower.hasPrefix($0 + " ") || lower.contains(" " + $0 + " ")
        }
        let settled = now - changedAt >= 0.9
        let sustained = pending.count >= 18 && now - submittedAt >= 8
        guard force || (pending.count >= 4 && question && (settled || final || text.hasSuffix("?"))) || (pending.count >= 9 && (final || settled)) || sustained else { return nil }
        consumed = words.count; submittedAt = now
        return text
    }
}
