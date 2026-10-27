# Try the Community preview on Windows

## Portable download

1. Open [v0.1.14-community.1](https://github.com/govindpokuri/ansora-ai/releases/tag/v0.1.14-community.1).
   Download the Windows x64 ZIP and `SHA256SUMS.txt` from that release.
2. Check the archive with PowerShell:

   ```powershell
   Get-FileHash -Algorithm SHA256 ./AnsoraAI-Community-0.1.14-community.1-win-x64.zip
   ```

   Compare the value with SHA256SUMS.txt. A checksum detects a damaged or different
   file; it is not a substitute for trusting the release's source.
3. Extract the entire archive to a writable folder. Do not start inside the ZIP.
4. The target is Windows 11 x64 with a compatible CPU. .NET is included, so no
   .NET SDK is needed. Install Microsoft's Visual C++ x64 runtime if it is missing.
   The included `Install-Prerequisites.ps1` downloads Microsoft's official installer,
   verifies its signature, and opens interactive setup for you to review.
5. Read the scripts, then double-click `Start.cmd`, or run `./run-community.ps1`
   from PowerShell in the extracted folder. This is an unsigned preview. If Windows
   has marked the ZIP as blocked, review its origin/checksum and use the ZIP's
   Properties > Unblock before extracting. Follow your administrator's rules on
   managed machines; do not disable endpoint protection or organization policies.
6. Enter the complete chat-completions URL, deployment/model and authentication
   type (`Bearer` or `api-key`). The key prompt is hidden. No hosted purchase or
   free provider credits are included. Use your own provider account/key.
7. First setup downloads the English Whisper model separately (about 148 MB),
   checks SHA-256, and puts it in `models/` beside the app. A verified local copy
   is reused. The download can take time; keep the network connection available.
8. In **Options > Settings**, choose the speakers/headphones used by the caller.
   Play some permitted caller audio, then choose **Start listening**. Your own
   microphone is not transcribed in caller-only mode.

After configuration you can start `AnsoraAI.exe` directly; use Start.cmd again
to change the connection. Keep all DLLs and models with the app. Only one copy
should run at once to avoid shortcut conflicts.

## Troubleshooting

| Symptom | First checks |
|---|---|
| No caller transcript | Confirm caller audio is playing; choose the matching output device; stop before changing devices. Speaking into your mic alone is not this test. |
| Whisper model missing | Run Start.cmd and allow the model download to finish. Do not rename/remove the models folder. |
| Missing runtime/native DLL | Keep the full extracted folder and install the Microsoft Visual C++ x64 runtime; confirm Windows/CPU compatibility. |
| AI 401 | Check your own key, authentication type and full deployment URL. Do not post the key in an issue. |
| AI not configured | Replace the placeholder URL/model via Start.cmd; verify your provider deployment exists. |
| Window hidden | Ctrl+Alt+H restores it. Ctrl+Alt+Q exits. Close another copy if shortcuts are occupied. |
| Controls ignore clicks | Ctrl+Alt+T restores overlay interaction after click-through mode. |
| Script blocked by policy | Review FIRST-RUN.txt and your organization's requirements. Avoid changing machine-wide security settings. |

Calls can autosave to `Documents/Ansora AI Community/Calls`, including request
context. Review/delete sensitive records. Only share fictional or appropriately
consented data when reporting bugs. Use assistance where it is permitted.
