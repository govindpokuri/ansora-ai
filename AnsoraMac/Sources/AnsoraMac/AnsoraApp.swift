import SwiftUI
import AppKit
import AnsoraCore

@main @MainActor struct AnsoraApp: App {
    @StateObject private var model = AppModel()
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var delegate
    var body: some Scene {
        WindowGroup("Ansora AI · Mac Preview") {
            ContentView(model: model)
                .frame(minWidth: 800, minHeight: 600)
                .preferredColorScheme(.dark)
                .background(WindowSetup(model: model, delegate: delegate))
                .onAppear { delegate.model = model }
        }
        .defaultSize(width: 1080, height: 760)
        .commands {
            CommandGroup(replacing: .newItem) { }
            CommandMenu("Call") {
                Button(model.listening ? "Stop listening" : "Start listening") {
                    if model.listening { Task { await model.stopListening() } } else { model.startListening() }
                }.keyboardShortcut("l", modifiers: [.command, .option])
                Button("Export a copy…") { Task { await model.exportCopy() } }.keyboardShortcut("e", modifiers: [.command, .shift])
            }
        }
    }
}

@MainActor final class AppDelegate: NSObject, NSApplicationDelegate, NSWindowDelegate {
    weak var model: AppModel?
    private var closing = false
    func applicationShouldTerminate(_ sender: NSApplication) -> NSApplication.TerminateReply {
        guard !closing else { return .terminateLater }
        guard let model else { return .terminateNow }
        closing = true
        Task {
            var quit = await model.prepareToClose()
            if !quit {
                let alert = NSAlert()
                alert.messageText = "This call could not be saved."
                alert.informativeText = model.saveError + " Stay open to export a copy, or quit and lose unsaved changes."
                alert.addButton(withTitle: "Stay open"); alert.addButton(withTitle: "Quit without saving")
                quit = alert.runModal() == .alertSecondButtonReturn
            }
            closing = false; sender.reply(toApplicationShouldTerminate: quit)
        }
        return .terminateLater
    }
    func windowShouldClose(_ sender: NSWindow) -> Bool { NSApplication.shared.terminate(nil); return false }
    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}

struct WindowSetup: NSViewRepresentable {
    @ObservedObject var model: AppModel
    let delegate: AppDelegate
    func makeNSView(context: Context) -> NSView { NSView() }
    func updateNSView(_ view: NSView, context: Context) {
        DispatchQueue.main.async {
            guard let window = view.window else { return }
            window.delegate = delegate
            window.level = model.keepOnTop ? .floating : .normal
            window.isMovableByWindowBackground = true
            window.isOpaque = false
            window.backgroundColor = .clear
            window.collectionBehavior = [.fullScreenAuxiliary]
            // No claim of exclusion from screen capture: macOS needs separate validation.
        }
    }
}
