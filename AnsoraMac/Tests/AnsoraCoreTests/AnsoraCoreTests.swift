import XCTest
@testable import AnsoraCore

final class AnsoraCoreTests: XCTestCase {
    func testRevisedDraftDoesNotSubmitTwice() {
        var tracker = QuestionTracker()
        tracker.update("How would you improve a slow database query", now: 100)
        XCTAssertNil(tracker.take(now: 100.3))
        XCTAssertEqual(tracker.take(now: 101), "How would you improve a slow database query")
        XCTAssertNil(tracker.take(now: 102))
        tracker.update("How would you improve a slow database query?", now: 103)
        XCTAssertNil(tracker.take(now: 104))
    }
    func testNextQuestionAfterFirstAnswerIsNotDropped() {
        var tracker = QuestionTracker()
        tracker.update("What is dependency injection?", now: 100)
        XCTAssertNotNil(tracker.take(now: 101))
        tracker.update("What is dependency injection? How do you test it?", now: 102)
        XCTAssertEqual(tracker.take(now: 103), "How do you test it?")
    }
    func testRecognitionRotationAllowsNewQuestion() {
        var tracker = QuestionTracker()
        tracker.update("How do you use indexes?", now: 100)
        XCTAssertNotNil(tracker.take(now: 101))
        tracker.reset()
        tracker.update("Write a palindrome in C sharp", now: 102)
        XCTAssertEqual(tracker.take(now: 103), "Write a palindrome in C sharp")
    }
    func testBriefGreetingDoesNotSpendTokens() {
        var tracker = QuestionTracker()
        tracker.update("Hello thank you", now: 100)
        XCTAssertNil(tracker.take(now: 105, final: true))
    }
    func testStreamingTextAndUsageOnlyEvents() throws {
        let text = try CompletionParser.parse(#"{"choices":[{"delta":{"content":"Use an index."},"finish_reason":null}]}"#)
        XCTAssertEqual(text.text, "Use an index.")
        let usage = try CompletionParser.parse(#"{"choices":[],"usage":{"prompt_tokens":12,"completion_tokens":4,"total_tokens":16},"usage_incomplete":true}"#)
        XCTAssertEqual(usage.usage?.total, 16)
        XCTAssertTrue(usage.usagePending)
        XCTAssertTrue(try CompletionParser.parse("[DONE]").done)
    }
    func testEventFramingPreservesUnicodeAndBlankBoundaries() throws {
        var decoder = EventStreamDecoder(), frames: [String] = []
        let stream = ": heartbeat\r\ndata: {\"choices\":[{\"delta\":{\"content\":\"café\"}}]}\r\n\r\ndata: [DONE]\n\n"
        for byte in stream.utf8 { if let frame = try decoder.append(byte) { frames.append(frame) } }
        XCTAssertEqual(frames.count, 2)
        XCTAssertEqual(try CompletionParser.parse(frames[0]).text, "café")
        XCTAssertTrue(try CompletionParser.parse(frames[1]).done)
        XCTAssertNil(try decoder.finish())
    }
    func testMultilineAndUnterminatedFinalEvent() throws {
        var decoder = EventStreamDecoder()
        for byte in "data: first\ndata: second".utf8 { _ = try decoder.append(byte) }
        XCTAssertEqual(try decoder.finish(), "first\nsecond")
    }
    func testTruncationAndProviderErrorAreVisible() throws {
        XCTAssertEqual(try CompletionParser.parse(#"{"choices":[{"delta":{},"finish_reason":"length"}]}"#).finishReason, "length")
        XCTAssertNotNil(try CompletionParser.parse(#"{"error":{"message":"upstream failed"}}"#).error)
        XCTAssertThrowsError(try CompletionParser.parse("not json"))
    }
    func testExportEscapesTranscriptAndCode() throws {
        var call = CallRecord(); call.transcript = "<script>alert('x')</script>"
        var answer = AnswerRecord(question: "What is <T>?")
        answer.answer = "if (x < 2 && y > 1) {}"; answer.usage = Usage(input: 10, output: 5, total: 15)
        call.answers = [answer]
        let html = CallExport.html(call)
        XCTAssertFalse(html.contains("<script>"))
        XCTAssertTrue(html.contains("&lt;T&gt;"))
        XCTAssertTrue(html.contains("15 reported AI tokens"))
        let directory = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: directory) }
        let saved = try CallExport.save(call, directory: directory)
        XCTAssertEqual(try String(contentsOf: saved), html)
        XCTAssertTrue(FileManager.default.fileExists(atPath: directory.appendingPathComponent("call-\(call.id.uuidString).json").path))
    }
}
