# Changelog

## macOS preview 0.1.1

- Replaced remaining-time estimates with the available AI token balance in the header.
- Refreshes balance after sign-in and answers; failed refreshes retain the last known balance and show its update time.


## macOS prototype — 2026-11-19

- Added the native SwiftUI/ScreenCaptureKit/Apple Speech prototype and Mac build instructions.
- Reused hosted email-code login and AI quota APIs; no provider secrets are bundled.
- Added XCTest coverage, macOS CI and ad-hoc preview artifact packaging.
- This is a separate preview; physical-Mac audio and permission validation and Developer ID notarization remain release gates.


## 0.1.15-community.1 — source update

- Removed **Keep this on screen**. Reviewing a previous answer no longer pauses automatic answers.
- The first text from each new response immediately switches the display back to the live answer. Subsequent text remains batched to keep the interface responsive.
- Adjusted question stability timing so eligible questions can dispatch between two-second transcript previews.
- Added a response-body deadline so a stalled stream releases the active request and later questions can proceed. Partial text is retained; the failed question is not automatically resubmitted.
- Enabled export immediately after caller text arrives, even without an AI response. Export formatting and writes run off the UI thread, file-picker visibility is improved, and results appear outside Options.
- Added regression coverage and CI checks for history transitions, first-text display, streaming recovery and transcript-only exports. UI tests load the application theme without launching the production single-instance workflow.

This source update does not replace the existing downloadable Community preview or change the managed service's update feed. Build from source to use these changes until a new Community binary release is published.
