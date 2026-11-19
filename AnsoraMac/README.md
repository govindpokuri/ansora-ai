# Ansora AI for macOS — native prototype

This is the first native Mac implementation, separate from the Windows WPF app. It is **not a production release**. Source and packaging scripts were prepared on Windows; a Mac build and permission/audio tests are required before distributing it. Do not publish a Mac download button yet.

## Implemented

- SwiftUI interface with a movable, resizable floating window, readable answers and a background-opacity slider.
- Existing Ansora email-code login, terms checkbox above Send code, Keychain-backed session storage and account balance.
- Select a running call application; capture its output audio through ScreenCaptureKit. No microphone capture or video recording is implemented.
- Apple Speech partial English transcription with on-device recognition required. If local recognition is unavailable, capture does not start; there is no silent cloud fallback. Recognition requests rotate every 50 seconds.
- Automatic question submission, one paid AI request at a time, and the newest pending question retained while writing. New answer text replaces the previous answer without a hold/pin switch.
- Existing `/api/chat/completions` streaming API, server-side model selection, quota enforcement and fallback. No Azure/payment credentials in the app.
- Bounded automatic continuation of clipped answers (up to two extra requests); incomplete answers and uncertain token usage remain marked in exports.
- Paste a question/coding problem; show actual code blocks in a selectable monospaced area.
- Import PDF/text resume, review facts and confirm coding languages; only confirmed context is used. Reviewed profile is stored in Keychain. This prototype does not include DOCX import or a separate AI resume-analysis flow.
- Automatic local JSON/HTML call notes on recognition boundaries, answer completion, Stop, and Quit. Export a copy while listening. Warn before quitting if saving fails.

## Requirements

- Apple Silicon Mac, macOS 13 or later. Test on the exact macOS version intended for release.
- Xcode and its Command Line Tools, Swift 5.9 or newer.
- On-device English (`en-US`) speech recognition available on the machine.
- Existing Ansora account, internet access and sufficient token balance for paid AI requests.
- For public distribution: Apple Developer Program, Developer ID Application certificate and notarization credentials in the Mac's Keychain.

## Build and run on a Mac

Copy the `AnsoraMac` folder onto your Mac, then:

```bash
cd /path/to/AnsoraMac
bash scripts/build-app.sh
open "dist/Ansora AI.app"
```

The script runs XCTest, compiles the arm64 executable, builds the `.app`, generates the Ansora icon and applies ad-hoc signing for local development. Run the **bundled app**, not `.build/.../AnsoraAI`: privacy prompts require the bundle metadata and usage descriptions. A locally ad-hoc signed preview may still require proper Developer ID signing for permissions to behave consistently; it is not suitable for customer distribution. Do not disable Gatekeeper or SIP.

You can open `Package.swift` in Xcode to inspect/build the code. For interactive permission testing use the app produced by the bundle script, or create a signed app target with the same Info.plist and entitlements.

## First call test

1. Launch the signed app. Accept the service terms yourself, request the code and sign in with an existing Ansora email.
2. Review a resume and languages, or choose **Skip for this call**. Imported facts are sent to the existing AI backend with questions; remove unnecessary personal data first.
3. Open a test call or play spoken audio in the target app. Choose **Refresh apps**. Grant macOS Screen Recording / Screen & System Audio Recording permission and Speech Recognition permission when prompted. Depending on macOS, quit and relaunch after changing permission.
4. Select Teams, Zoom, or your browser. Browser capture includes other audio from that browser, so mute unrelated tabs. This is application-audio filtering, not speaker diarization. Own-voice echo or call audio mixed by the source app cannot be separated automatically.
5. Start listening. Ask three distinct questions, including a follow-up before the first answer finishes. Verify the transcript changes and later answers replace the first.
6. Continue for at least 10 minutes to test recognition rotation. There may be a small gap at rotation; seamless speech-model parity with Windows is not yet validated.
7. Paste a coding question in a confirmed language and verify readable code. Model routing, output limits, backup and billing remain managed by the existing server.
8. Export while listening, Stop, and Quit. Verify saved JSON/HTML notes and server usage. Check an interrupted response remains marked incomplete; pending usage can still be billed by the server even if you stop locally.
9. Deny each permission, test an expired login, empty balance, disconnected internet and capture-app closure. Verify recoverable error messages.

Call files are private local files under `~/Library/Application Support/AnsoraAI/Calls/` (HTML/JSON, not encrypted). The app uses restrictive file permissions. They contain conversation content; manage/delete them according to your retention needs. A crash can lose the current unsaved recognition segment or response. There is no cloud transcript upload beyond question/context requests to the AI backend.

## Release signing and notarization

Configure your Developer ID certificate in Keychain and store a notarytool credential profile through Apple's documented setup. Do not add signing credentials to this repository.

```bash
export SIGNING_IDENTITY="Developer ID Application: Your Business (TEAMID)"
export NOTARY_PROFILE="ansora-notary"
bash scripts/notarize.sh
```

This builds, signs, submits to Apple, staples the ticket, assesses the app and creates `dist/AnsoraAI-Mac.zip`. Apple approval and all real-device tests must pass before uploading. Script signing/notarization has not been executed on this Windows workspace.

## Automated checks

```bash
swift test
swift build -c release
```

XCTest covers draft revisions, follow-up questions, speech-session resets, greeting suppression, streamed text, usage-only/error/truncation frames and HTML export escaping. `.github/workflows/macos-prototype.yml` at the workspace root is a macOS CI build/test workflow; it has not been pushed or run as part of this task. If this folder moves into another repository, copy that workflow too.

## Not included / release gates

- No guaranteed exclusion from screen sharing. The Mac app makes no hidden/undetectable claim. Verify actual behaviour independently on every supported platform and share mode.
- No Intel distribution, automatic updater, App Store package, installer DMG, or Windows feature-parity claim yet.
- No Whisper runtime integration yet; native Apple Speech is the first prototype implementation. Validate recognition quality for the target accents and vocabulary before launch.
- No live-payment or hosting changes are needed for development. Existing backend identity, purchases and balances are reused. Do not put provider secrets on the Mac.

## Apple references

- [ScreenCaptureKit](https://developer.apple.com/documentation/screencapturekit)
- [On-device speech recognition](https://developer.apple.com/documentation/speech/sfspeechrecognitionrequest/requiresondevicerecognition)
- [Speech audio-buffer request](https://developer.apple.com/documentation/speech/sfspeechaudiobufferrecognitionrequest)
