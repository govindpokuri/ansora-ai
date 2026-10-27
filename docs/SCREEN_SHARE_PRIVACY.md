# Screen-share privacy

Ansora uses the Windows `WDA_EXCLUDEFROMCAPTURE` setting to keep its own overlay
out of supported screen captures while leaving it visible on your computer.

**The maintainer reports successful testing with Zoom, Google Meet and Microsoft
Teams.** This is maintainer-reported compatibility, not an independent platform
certification. Exact app versions, Windows builds and individual share-mode
coverage were not supplied, so this document does not claim every combination.

## Before using a new setup

1. Use the supported Windows 11 x64 target and check the app's capture-exclusion
   status. A failed/unverified status is not a privacy guarantee.
2. Start a separate test meeting and inspect the remote/shared view, ideally
   from a second device or participant. Test the exact monitor, window or other
   share mode you intend to use, including after app/Windows updates.
3. Confirm that the overlay is absent and that the material you intend to share
   is visible. Do not rely only on how the local desktop looks.

Capture exclusion is an operating-system request, not protection against all
recording or detection methods. A camera, unsupported capture path, or directly
sharing the overlay itself can behave differently. It does not make the process
undetectable or bypass platform rules. Use assistance only where permitted.

Implementation: [WindowPrivacy.cs](../OverlayCopilot/Services/WindowPrivacy.cs).
API behavior and limitations: [Microsoft SetWindowDisplayAffinity documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity).
