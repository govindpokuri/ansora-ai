import AppKit
import Combine
import PDFKit
import UniformTypeIdentifiers
import AnsoraCore

struct ReviewedProfile: Codable {
    var facts = ""
    var languages = ""
}

@MainActor final class AppModel: ObservableObject {
    @Published var login: Session?
    @Published var email = ""
    @Published var code = ""
    @Published var challenge = ""
    @Published var acceptedTerms = false
    @Published var authBusy = false
    @Published var message = ""
    @Published var account: AccountSnapshot?
    @Published var applications: [CallApplication] = []
    @Published var selectedApplication: Int32 = 0
    @Published var loadingApplications = false
    @Published var listening = false
    @Published var starting = false
    @Published var stopping = false
    @Published var writing = false
    @Published var caller = ""
    @Published var answerQuestion = ""
    @Published var answer = ""
    @Published var pastedQuestion = ""
    @Published var profile = ReviewedProfile()
    @Published var profileConfirmed = false
    @Published var showProfile = false
    @Published var opacity = 0.97
    @Published var keepOnTop = true
    @Published var lastSaved: URL?
    @Published var saveError = ""
    @Published var call = CallRecord()

    private let backend = BackendClient()
    private let audio = CallerAudioCapture()
    private let archive = CallArchive()
    private var tracker = QuestionTracker()
    private var timer: Timer?
    private var finalizedTranscript = ""
    private var partialTranscript = ""
    private var answerTask: Task<Void, Never>?
    private var listenTask: Task<Void, Never>?
    private var pendingQuestion: String?
    private var revision = 0
    private var persistedRevision = -1

    var status: String { starting ? "Starting audio…" : stopping ? "Saving call…" : writing ? "Writing" : listening ? "Listening" : "Ready to read" }
    var minutesLeft: String {
        guard let account else { return "Sign in to view balance" }
        let duration = Date().timeIntervalSince(call.startedAt) / 60
        let usage = call.usage.total
        let observed = duration >= 2 && usage > 0
        let rate = observed ? Double(usage) / duration : 1_000_000.0 / 240
        return "~\(max(0, Int(Double(account.wallet.available) / rate))) min left · \(observed ? "this call’s rate" : "starting estimate")"
    }

    init() {
        do {
            if let session = try KeychainStore.load(Session.self, name: "session"), session.isValid { login = session; email = session.email }
            if let saved = try KeychainStore.load(ReviewedProfile.self, name: "profile") { profile = saved; profileConfirmed = true }
        } catch { message = error.localizedDescription }
        audio.onTranscript = { [weak self] text, final in
            Task { @MainActor in self?.receiveTranscript(text, final: final) }
        }
        audio.onFailure = { [weak self] text in
            Task { @MainActor in
                guard let self, self.listening || self.starting else { return }
                await self.stopListening(); self.message = text
            }
        }
        timer = Timer.scheduledTimer(withTimeInterval: 0.3, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.pollQuestion() }
        }
        Task { await refreshAccount() }
    }

    func sendCode() async {
        guard acceptedTerms else { message = "Read and accept the terms and privacy notice first."; return }
        authBusy = true; defer { authBusy = false }
        do {
            challenge = try await backend.sendCode(email: email.trimmingCharacters(in: .whitespacesAndNewlines))
            message = "Code sent. Check your inbox and spam folder; it expires in 10 minutes."
        } catch { message = error.localizedDescription }
    }
    func verifyCode() async {
        authBusy = true; defer { authBusy = false }
        do {
            let session = try await backend.verify(challenge: challenge, code: code.trimmingCharacters(in: .whitespacesAndNewlines))
            try KeychainStore.save(session, name: "session")
            login = session; email = session.email; code = ""; challenge = ""; message = "Signed in."
            await refreshAccount()
            if !profileConfirmed { showProfile = true }
        } catch { message = error.localizedDescription }
    }
    func refreshAccount() async {
        guard let login else { return }
        do { account = try await backend.account(login) }
        catch { message = error.localizedDescription }
    }
    func signOut() async {
        await stopListening()
        if let login { try? await backend.logout(login) }
        do { try KeychainStore.remove("session"); login = nil; account = nil }
        catch { message = error.localizedDescription }
    }
    func refreshApplications() async {
        loadingApplications = true; defer { loadingApplications = false }
        do {
            applications = try await audio.applications()
            if !applications.contains(where: { $0.id == selectedApplication }) { selectedApplication = applications.first?.id ?? 0 }
        } catch { message = "Allow Screen & System Audio Recording in System Settings → Privacy & Security, then refresh the app list. \(error.localizedDescription)" }
    }
    func startListening() {
        guard !starting, !listening, !stopping else { return }
        guard login != nil else { message = "Sign in before listening."; return }
        guard selectedApplication != 0 else { message = "Refresh the app list and select your call application."; return }
        starting = true; message = ""
        listenTask = Task {
            do {
                try await audio.start(applicationID: selectedApplication)
                try Task.checkCancellation()
                if call.endedAt != nil || !hasCallContent { newCall() }
                listening = true; starting = false
            } catch {
                await audio.stop(); starting = false
                if !Task.isCancelled { message = error.localizedDescription }
            }
        }
    }
    func stopListening() async {
        guard !stopping else { return }
        stopping = true; listening = false; pendingQuestion = nil
        listenTask?.cancel(); await listenTask?.value; listenTask = nil
        await audio.stop()
        answerTask?.cancel(); await answerTask?.value; answerTask = nil
        writing = false; tracker.reset()
        if hasCallContent { call.endedAt = Date(); revision += 1; await saveCall() }
        stopping = false
    }
    private var hasCallContent: Bool { !call.transcript.isEmpty || !call.answers.isEmpty }
    private func newCall() {
        call = CallRecord(); revision = 0; persistedRevision = -1; lastSaved = nil
        finalizedTranscript = ""; partialTranscript = ""; tracker.reset()
        caller = ""; answer = ""; answerQuestion = ""; saveError = ""
    }
    private func receiveTranscript(_ text: String, final: Bool) {
        guard listening, !stopping else { return }
        partialTranscript = text; caller = text
        call.transcript = finalizedTranscript + text
        revision += 1
        tracker.update(text, now: Date().timeIntervalSince1970)
        if let question = tracker.take(now: Date().timeIntervalSince1970, final: final) { enqueue(question) }
        if final {
            finalizedTranscript += text + "\n"; partialTranscript = ""; call.transcript = finalizedTranscript
            tracker.reset(); Task { await saveCall() }
        }
    }
    private func pollQuestion() {
        guard listening, !stopping else { return }
        if let question = tracker.take(now: Date().timeIntervalSince1970) { enqueue(question) }
    }
    func submitPastedQuestion() {
        let question = pastedQuestion.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !question.isEmpty, question.count <= 12000 else { message = "Paste a question of 1–12,000 characters."; return }
        if call.endedAt != nil { newCall() }
        pastedQuestion = ""; enqueue(question)
    }
    private func enqueue(_ question: String) {
        guard let login, login.isValid else { message = "Sign in again to get AI answers."; return }
        guard !stopping else { return }
        if let account, !account.ai_configured { message = "AI is unavailable on the server. Contact support."; return }
        if let account, account.wallet.available <= 0 { message = "Your token balance is empty. Top up on the website."; return }
        // One paid request at a time; keep the newest pending caller question.
        if writing { pendingQuestion = question; return }
        writing = true; answerQuestion = question; answer = ""; message = ""
        var record = AnswerRecord(question: question)
        let recordID = record.id
        call.answers.append(record); revision += 1
        let callID = call.id
        let facts = profileConfirmed ? profile.facts : ""
        let languages = profileConfirmed ? profile.languages : ""
        answerTask = Task {
            do {
                try await backend.answer(login: login, callID: callID, question: question, profile: facts, languages: languages) { event in
                    record.answer += event.text
                    if let usage = event.usage { record.usage = usage; record.usagePending = event.usagePending }
                    if event.usagePending { record.usagePending = true }
                    if let finish = event.finishReason { record.incomplete = finish == "length" }
                    answer = record.answer
                    if let index = call.answers.firstIndex(where: { $0.id == recordID }) { call.answers[index] = record }
                    revision += 1
                }
                if record.incomplete { message = "The response reached its continuation limit; the saved answer is marked incomplete." }
            } catch {
                record.incomplete = true; record.usagePending = true
                record.error = Task.isCancelled ? "Stopped before response completion. Check account usage for final billing." : error.localizedDescription
                if !Task.isCancelled { message = error.localizedDescription }
            }
            if let index = call.answers.firstIndex(where: { $0.id == recordID }) { call.answers[index] = record }
            revision += 1; writing = false
            await saveCall()
            if !Task.isCancelled { await refreshAccount() }
            if !Task.isCancelled, !stopping, let next = pendingQuestion {
                pendingQuestion = nil; enqueue(next)
            }
        }
    }
    func saveCall() async {
        guard hasCallContent else { return }
        let snapshot = call, version = revision
        do {
            let url = try await archive.save(snapshot, revision: version)
            if snapshot.id == call.id { lastSaved = url; persistedRevision = max(persistedRevision, version); saveError = "" }
        } catch { saveError = "Call notes could not be saved: \(error.localizedDescription)" }
    }
    func prepareToClose() async -> Bool {
        await stopListening(); await saveCall()
        return !hasCallContent || persistedRevision >= revision
    }
    func exportCopy() async {
        guard hasCallContent else { message = "There are no call notes to export yet."; return }
        await saveCall()
        let panel = NSSavePanel(); panel.allowedContentTypes = [.html]; panel.nameFieldStringValue = "Ansora-call-\(call.id.uuidString).html"
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do { try Data(CallExport.html(call).utf8).write(to: url, options: .atomic); message = "Call notes exported." }
        catch { message = "Export failed: \(error.localizedDescription)" }
    }
    func importResume() {
        let panel = NSOpenPanel(); panel.allowedContentTypes = [.pdf, .plainText]; panel.allowsMultipleSelection = false
        guard panel.runModal() == .OK, let url = panel.url else { return }
        do {
            let size = (try url.resourceValues(forKeys: [.fileSizeKey])).fileSize ?? 0
            guard size <= 10_000_000 else { throw CoreError.message("Choose a resume smaller than 10 MB.") }
            let text: String
            if url.pathExtension.lowercased() == "pdf" {
                guard let pdf = PDFDocument(url: url), let extracted = pdf.string, !extracted.isEmpty else { throw CoreError.message("This PDF has no readable text. Paste a text version instead.") }
                text = extracted
            } else { text = try String(contentsOf: url, encoding: .utf8) }
            profile.facts = String(text.prefix(8000)); profileConfirmed = false
            message = "Review the imported text, remove unnecessary personal details, and confirm your coding languages."
        } catch { message = error.localizedDescription }
    }
    func confirmProfile() {
        profile.facts = String(profile.facts.prefix(8000)); profile.languages = String(profile.languages.prefix(250))
        do { try KeychainStore.save(profile, name: "profile"); profileConfirmed = true; showProfile = false }
        catch { message = error.localizedDescription }
    }
    func clearProfile() {
        do { try KeychainStore.remove("profile"); profile = ReviewedProfile(); profileConfirmed = false }
        catch { message = error.localizedDescription }
    }
    func openWebsite(_ path: String = "dashboard") { NSWorkspace.shared.open(BackendClient.website.appendingPathComponent(path)) }
}
