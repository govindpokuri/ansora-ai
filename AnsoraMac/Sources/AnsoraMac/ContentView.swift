import SwiftUI
import AppKit

private let accent = Color(red: 0.39, green: 0.86, blue: 0.97)
private let surface = Color(red: 0.06, green: 0.10, blue: 0.16)

@MainActor struct ContentView: View {
    @ObservedObject var model: AppModel
    var body: some View {
        VStack(spacing: 16) {
            header
            if model.login == nil { signIn }
            else {
                captureControls
                HStack(alignment: .top, spacing: 16) {
                    pane(title: "CALLER QUESTION", width: 290) {
                        Text(model.caller.isEmpty ? "The caller’s words will appear here. Select the app playing the call; your microphone is not captured." : model.caller)
                            .font(.system(size: 18)).foregroundStyle(model.caller.isEmpty ? .secondary : .primary)
                    }
                    pane(title: model.writing ? "WRITING ANSWER" : "SUGGESTED ANSWER") {
                        if !model.answerQuestion.isEmpty {
                            Text(model.answerQuestion).font(.subheadline).foregroundStyle(accent)
                            Divider().padding(.vertical, 8)
                        }
                        AnswerText(text: model.answer.isEmpty ? "Your next answer will appear here automatically. You can also paste a question below." : model.answer)
                    }
                }.frame(maxHeight: .infinity)
                HStack(alignment: .bottom) {
                    TextEditor(text: $model.pastedQuestion)
                        .font(.system(size: 14, design: .monospaced))
                        .frame(height: 70).scrollContentBackground(.hidden)
                        .padding(8).background(.black.opacity(0.2), in: RoundedRectangle(cornerRadius: 12))
                        .accessibilityLabel("Paste a question or coding problem")
                    Button("Ask AI") { model.submitPastedQuestion() }
                        .buttonStyle(.borderedProminent).tint(accent).foregroundStyle(.black)
                        .disabled(model.stopping || model.pastedQuestion.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                }
            }
            if !model.message.isEmpty || !model.saveError.isEmpty {
                Text(model.saveError.isEmpty ? model.message : model.saveError)
                    .font(.callout).foregroundStyle(model.saveError.isEmpty ? Color.secondary : .orange)
                    .textSelection(.enabled).frame(maxWidth: .infinity, alignment: .leading)
            }
            HStack {
                Text("Mac prototype · English · Use where AI assistance is permitted")
                Spacer()
                if model.lastSaved != nil { Label("Call saved locally", systemImage: "checkmark.circle") }
            }.font(.caption).foregroundStyle(.secondary)
        }
        .padding(22)
        .background(surface.opacity(model.opacity))
        .sheet(isPresented: $model.showProfile) { profileReview }
    }
    private var header: some View {
        HStack(spacing: 14) {
            Image(systemName: "sparkle").font(.title).foregroundStyle(accent)
            VStack(alignment: .leading, spacing: 4) {
                Text("Ansora AI").font(.title2.bold())
                Text(model.login?.email ?? "Every question. Your best answer.").font(.caption).foregroundStyle(.secondary)
            }
            Spacer()
            if model.login != nil {
                VStack(alignment: .trailing, spacing: 3) {
                    Label(model.tokenBalance, systemImage: "circle.hexagongrid.fill")
                        .font(.callout.weight(.semibold)).foregroundStyle(accent)
                    if let note = model.balanceNote { Text(note).font(.caption2).foregroundStyle(.secondary) }
                }
                .padding(.horizontal, 12).padding(.vertical, 8)
                .background(accent.opacity(0.08), in: RoundedRectangle(cornerRadius: 10))
                .help("Available AI tokens from your account. Refreshes after sign-in and completed answers; refresh manually in the menu.")
            }
            Text(model.status).font(.callout.weight(.semibold)).foregroundStyle(accent)
            Menu {
                Toggle("Keep window on top", isOn: $model.keepOnTop)
                Button("Resume and coding languages…") { model.showProfile = true }.disabled(model.login == nil)
                Button("Refresh balance") { Task { await model.refreshAccount() } }.disabled(model.login == nil)
                Button("Export a copy…") { Task { await model.exportCopy() } }
                Button("Open saved call folder") { NSWorkspace.shared.open(CallArchive.directory) }
                Button("Account and top-up") { model.openWebsite() }
                Divider()
                Button("Sign out") { Task { await model.signOut() } }
                    .disabled(model.login == nil || model.starting || model.stopping)
            } label: { Image(systemName: "ellipsis.circle").font(.title2) }
            .menuStyle(.borderlessButton).fixedSize()
        }
    }
    private var signIn: some View {
        VStack(alignment: .leading, spacing: 16) {
            Text("Sign in to your Ansora account").font(.title2.bold())
            Text("Use the same email as the website. Your existing token balance works on Mac.").foregroundStyle(.secondary)
            TextField("Email address", text: $model.email).textFieldStyle(.roundedBorder).disabled(!model.challenge.isEmpty)
            if model.challenge.isEmpty {
                Toggle("I have read and accept the terms and privacy notice", isOn: $model.acceptedTerms)
                HStack {
                    Link("Terms", destination: BackendClient.website.appendingPathComponent("terms"))
                    Link("Privacy", destination: BackendClient.website.appendingPathComponent("privacy"))
                }
                Button(model.authBusy ? "Sending…" : "Send sign-in code") { Task { await model.sendCode() } }
                    .disabled(!model.acceptedTerms || model.email.isEmpty || model.authBusy)
            } else {
                TextField("Six-digit code", text: $model.code).textFieldStyle(.roundedBorder)
                Button(model.authBusy ? "Signing in…" : "Sign in") { Task { await model.verifyCode() } }.disabled(model.code.count != 6 || model.authBusy)
                Button("Use another email") { model.challenge = ""; model.code = "" }.disabled(model.authBusy)
            }
        }.padding(28).frame(maxWidth: 500).frame(maxWidth: .infinity, maxHeight: .infinity)
    }
    private var captureControls: some View {
        HStack(spacing: 14) {
            Picker("Call app", selection: $model.selectedApplication) {
                Text("Select an application").tag(Int32(0))
                ForEach(model.applications) { app in Text("\(app.name) · \(app.id)").tag(app.id) }
            }.disabled(model.listening || model.starting || model.stopping)
            Button(model.loadingApplications ? "Loading…" : "Refresh apps") { Task { await model.refreshApplications() } }
                .disabled(model.loadingApplications || model.listening || model.starting || model.stopping)
            Button(model.listening ? "Stop listening" : "Start listening") {
                if model.listening { Task { await model.stopListening() } } else { model.startListening() }
            }.buttonStyle(.borderedProminent).tint(model.listening ? Color.orange : accent).foregroundStyle(.black)
                .disabled(model.starting || model.stopping)
        }
    }
    private func pane<Content: View>(title: String, width: CGFloat? = nil, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 14) {
            Text(title).font(.caption.weight(.bold)).tracking(1.4).foregroundStyle(accent)
            ScrollView { VStack(alignment: .leading, spacing: 0, content: content).frame(maxWidth: .infinity, alignment: .leading).textSelection(.enabled) }
        }.padding(18).frame(width: width).frame(maxWidth: width == nil ? .infinity : nil, maxHeight: .infinity)
            .background(.white.opacity(0.045), in: RoundedRectangle(cornerRadius: 18))
            .overlay(RoundedRectangle(cornerRadius: 18).stroke(.white.opacity(0.09)))
    }
    private var profileReview: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Your experience, in your words").font(.title2.bold())
            Text("Import PDF or text, review the facts and confirm your languages. Only confirmed context is sent with AI questions. No experience is invented.").foregroundStyle(.secondary)
            Button("Import resume…") { model.importResume() }
            TextEditor(text: $model.profile.facts).font(.body).frame(minHeight: 240).border(.gray.opacity(0.3))
            TextField("Confirmed coding languages, e.g. C#, SQL", text: $model.profile.languages).textFieldStyle(.roundedBorder)
            Text("Background opacity").font(.caption)
            Slider(value: $model.opacity, in: 0.3...1)
            HStack {
                Button("Remove saved profile") { model.clearProfile() }
                Spacer()
                Button("Skip for this call") { model.profileConfirmed = false; model.showProfile = false }
                Button("Use reviewed profile") { model.confirmProfile() }.buttonStyle(.borderedProminent)
            }
            if !model.message.isEmpty { Text(model.message).font(.caption).foregroundStyle(.secondary) }
        }.padding(24).frame(width: 680)
    }
}

// Keep streaming code readable even before the closing fence arrives.
struct AnswerText: View {
    let text: String
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            ForEach(Array(text.components(separatedBy: "```").enumerated()), id: \.offset) { index, segment in
                if index % 2 == 1 {
                    let code = segment.components(separatedBy: "\n").dropFirst().joined(separator: "\n")
                    ScrollView(.horizontal) { Text(code).font(.system(size: 15, design: .monospaced)).fixedSize(horizontal: true, vertical: false).padding(14) }
                        .background(.black.opacity(0.3), in: RoundedRectangle(cornerRadius: 10))
                } else {
                    Text(segment.trimmingCharacters(in: .whitespacesAndNewlines))
                        .font(.system(size: 20)).lineSpacing(7).frame(maxWidth: .infinity, alignment: .leading)
                }
            }
        }
    }
}
