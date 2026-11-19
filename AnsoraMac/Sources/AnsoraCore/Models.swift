import Foundation

public struct Session: Codable, Equatable {
    public let email: String
    public let token: String
    public let expiresAt: Int64
    public init(email: String, token: String, expiresAt: Int64) {
        self.email = email; self.token = token; self.expiresAt = expiresAt
    }
    public var isValid: Bool { expiresAt > Int64(Date().timeIntervalSince1970) }
}

public struct Usage: Codable, Equatable {
    public var input: Int
    public var output: Int
    public var total: Int
    public init(input: Int = 0, output: Int = 0, total: Int = 0) {
        self.input = input; self.output = output; self.total = total
    }
    public static func + (lhs: Usage, rhs: Usage) -> Usage {
        Usage(input: lhs.input + rhs.input, output: lhs.output + rhs.output, total: lhs.total + rhs.total)
    }
}

public struct AnswerRecord: Codable, Identifiable {
    public let id: UUID
    public let date: Date
    public let question: String
    public var answer: String
    public var usage: Usage
    public var usagePending: Bool
    public var incomplete: Bool
    public var error: String?
    public init(question: String) {
        id = UUID(); date = Date(); self.question = question; answer = ""
        usage = Usage(); usagePending = true; incomplete = false
    }
}

public struct CallRecord: Codable {
    public let id: UUID
    public let startedAt: Date
    public var endedAt: Date?
    public var transcript: String
    public var answers: [AnswerRecord]
    public init() {
        id = UUID(); startedAt = Date(); transcript = ""; answers = []
    }
    public var usage: Usage { answers.reduce(Usage()) { $0 + $1.usage } }
}

public struct CompletionEvent: Equatable {
    public init() {}
    public var text = ""
    public var model: String?
    public var finishReason: String?
    public var usage: Usage?
    public var usagePending = false
    public var error: String?
    public var done = false
}

// Parse complete SSE data payloads, including usage-only and provider-error frames.
public enum CompletionParser {
    public static func parse(_ data: String) throws -> CompletionEvent {
        if data.trimmingCharacters(in: .whitespacesAndNewlines) == "[DONE]" {
            var event = CompletionEvent(); event.done = true; return event
        }
        guard let bytes = data.data(using: .utf8),
              let root = try JSONSerialization.jsonObject(with: bytes) as? [String: Any] else {
            throw CoreError.message("The answer service returned an invalid event.")
        }
        var event = CompletionEvent()
        event.model = root["model"] as? String
        event.usagePending = root["usage_incomplete"] as? Bool ?? false
        if root["error"] != nil { event.error = "The answer service could not finish this response. Check your balance before retrying." }
        if let usage = root["usage"] as? [String: Any], let total = usage["total_tokens"] as? Int {
            event.usage = Usage(input: usage["prompt_tokens"] as? Int ?? 0,
                                output: usage["completion_tokens"] as? Int ?? 0, total: total)
        }
        if let choice = (root["choices"] as? [[String: Any]])?.first {
            event.finishReason = choice["finish_reason"] as? String
            event.text = ((choice["delta"] ?? choice["message"]) as? [String: Any])?["content"] as? String ?? ""
        }
        return event
    }
}

public enum CoreError: LocalizedError {
    case message(String)
    public var errorDescription: String? { if case .message(let text) = self { return text }; return nil }
}
