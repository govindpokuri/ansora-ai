# Changelog

## UPI payment confirmation emails — 2026-11-21

- New bank-verified administrator approvals queue a branded customer receipt with amount, tokens, account details and Windows setup steps.
- Sends after credit commit using the existing email-provider configuration; provider errors do not reverse paid tokens.
- Durable audit-based queue/claim/outcome records prevent automatic duplicate sends on approval replay or uncertain provider responses, without a schema migration.
- Admin approvals show email status separately from payment status. Existing historical approvals are not emailed retroactively.

## Mobile manual UPI approval desk — 2026-11-21

- Added an OTP-protected phone-friendly administrator page to record bank-verified UPI payments and credit published token packs.
- Purchases, wallet credits, ledger and audit entries commit atomically; duplicate references and concurrent attempts cannot double-credit.
- Added a Home Screen web-app manifest, recent approvals, customer lookup and read-only dashboard balance refresh.
- Uses existing tables and the current Starter/Standard/Plus catalogue; no bank verification or public UPI collection integration is implied.


## Mac preview 0.1.3

- Moved speech result callbacks and AI stream decoding away from the main UI actor, prioritized audio processing and removed a redundant transcript UI hop.
- Added guidance distinguishing preview signing warnings from runtime permissions and transcription/answer delay.
- Real-device latency measurement and Developer ID notarization remain outstanding.


## 0.1.17-community.1 / Mac preview 0.1.2

- Verbal programming-logic exercises now request a short approach, working code and a small example, respecting confirmed coding languages.
- The hosted backend recognizes implicit coding exercises and gives them the 1,600-token code allowance; concept-only and explicit no-code questions retain 320 tokens.
- Existing hosted desktop builds receive the server-side instruction after the backend patch is deployed.


## 0.1.16-community.1 — source update

- Hosted Windows mode now displays available AI tokens instead of an estimated remaining time.
- Retains the last balance on refresh failures and identifies its update time in the tooltip.


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
