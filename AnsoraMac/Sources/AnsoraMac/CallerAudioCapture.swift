import Foundation
import ScreenCaptureKit
import Speech
import CoreMedia
import AnsoraCore

struct CallApplication: Identifiable, Hashable {
    let id: Int32
    let name: String
    let bundleID: String
}

// Audio stays in memory and feeds the on-device recognizer. No microphone input.
final class CallerAudioCapture: NSObject, SCStreamOutput, SCStreamDelegate {
    var onTranscript: (@MainActor (String, Bool) -> Void)?
    var onFailure: ((String) -> Void)?
    private var stream: SCStream?
    private let queue = DispatchQueue(label: "com.ansora-ai.caller-audio", qos: .userInitiated)
    private let recognitionQueue: OperationQueue = {
        let queue = OperationQueue()
        queue.name = "com.ansora-ai.speech-results"
        queue.qualityOfService = .userInitiated
        queue.maxConcurrentOperationCount = 1
        return queue
    }()
    private var recognizer: SFSpeechRecognizer?
    private var request: SFSpeechAudioBufferRecognitionRequest?
    private var recognition: SFSpeechRecognitionTask?
    private var rotation: DispatchWorkItem?
    private var active = false
    private var epoch = UUID()
    private var lastText = ""

    @MainActor func applications() async throws -> [CallApplication] {
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
        let ownPID = ProcessInfo.processInfo.processIdentifier
        return content.applications.filter { $0.processID != ownPID && !$0.applicationName.isEmpty }
            .map { CallApplication(id: $0.processID, name: $0.applicationName, bundleID: $0.bundleIdentifier) }
            .sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
    }

    @MainActor func start(applicationID: Int32) async throws {
        let authorization = await withCheckedContinuation { continuation in
            SFSpeechRecognizer.requestAuthorization { continuation.resume(returning: $0) }
        }
        guard authorization == .authorized else {
            throw CoreError.message("Enable Speech Recognition for Ansora AI in System Settings → Privacy & Security.")
        }
        guard let recognizer = SFSpeechRecognizer(locale: Locale(identifier: "en-US")),
              recognizer.supportsOnDeviceRecognition, recognizer.isAvailable else {
            throw CoreError.message("On-device English speech recognition is unavailable on this Mac. No audio was sent to a cloud recognizer. Check your English speech language resources and try again.")
        }
        // Speech defaults to the main queue; keep recognition callbacks independent of rendering.
        recognizer.queue = recognitionQueue
        recognizer.defaultTaskHint = .dictation
        let content = try await SCShareableContent.excludingDesktopWindows(false, onScreenWindowsOnly: false)
        guard let application = content.applications.first(where: { $0.processID == applicationID }),
              let display = content.displays.first else {
            throw CoreError.message("The selected call app is no longer available. Refresh the app list.")
        }
        let filter = SCContentFilter(display: display, including: [application], exceptingWindows: [])
        let configuration = SCStreamConfiguration()
        configuration.capturesAudio = true
        configuration.excludesCurrentProcessAudio = true
        configuration.sampleRate = 16000
        configuration.channelCount = 1
        // ScreenCaptureKit uses a display filter; no video output is attached or stored.
        configuration.width = 2; configuration.height = 2
        configuration.minimumFrameInterval = CMTime(value: 1, timescale: 1)
        configuration.showsCursor = false
        let next = SCStream(filter: filter, configuration: configuration, delegate: self)
        try next.addStreamOutput(self, type: .audio, sampleHandlerQueue: queue)
        queue.sync {
            self.recognizer = recognizer; active = true; beginRecognition()
        }
        stream = next
        do { try await next.startCapture() }
        catch { await stop(); throw error }
    }

    // All recognition state is owned by queue; callbacks from old requests are ignored.
    private func beginRecognition() {
        rotation?.cancel(); recognition?.cancel(); request?.endAudio()
        epoch = UUID(); let current = epoch; lastText = ""
        let next = SFSpeechAudioBufferRecognitionRequest()
        next.shouldReportPartialResults = true
        next.requiresOnDeviceRecognition = true
        next.addsPunctuation = true
        request = next
        recognition = recognizer?.recognitionTask(with: next) { [weak self] result, error in
            guard let self else { return }
            self.queue.async { [weak self] in
                guard let self, self.active, self.epoch == current else { return }
                if let result {
                    self.lastText = result.bestTranscription.formattedString
                    let text = self.lastText
                    self.deliver(text, final: result.isFinal)
                    if result.isFinal { self.beginRecognition(); return }
                }
                if error != nil {
                    self.active = false; self.rotation?.cancel(); self.request?.endAudio()
                    DispatchQueue.main.async { [weak self] in self?.onFailure?("Speech recognition stopped. Stop listening, check speech permissions and restart.") }
                }
            }
        }
        // Rotate before the recognizer's long-running request limit.
        let rotate = DispatchWorkItem { [weak self] in
            guard let self, self.active, self.epoch == current else { return }
            if !self.lastText.isEmpty { self.deliver(self.lastText, final: true) }
            self.beginRecognition()
        }
        rotation = rotate; queue.asyncAfter(deadline: .now() + 50, execute: rotate)
    }
    private func deliver(_ text: String, final: Bool) {
        DispatchQueue.main.async { [weak self] in self?.onTranscript?(text, final) }
    }
    func stream(_ stream: SCStream, didOutputSampleBuffer sampleBuffer: CMSampleBuffer, of outputType: SCStreamOutputType) {
        guard outputType == .audio, sampleBuffer.isValid, CMSampleBufferDataIsReady(sampleBuffer), active else { return }
        request?.appendAudioSampleBuffer(sampleBuffer)
    }
    func stream(_ stream: SCStream, didStopWithError error: Error) {
        DispatchQueue.main.async { [weak self] in self?.onFailure?("Call audio capture stopped. Check Screen & System Audio Recording permission and restart listening.") }
    }
    @MainActor func stop() async {
        let previous = stream; stream = nil
        queue.sync {
            active = false; epoch = UUID(); rotation?.cancel(); rotation = nil
            request?.endAudio(); recognition?.cancel(); recognition = nil; request = nil; recognizer = nil
        }
        try? await previous?.stopCapture()
    }
}
