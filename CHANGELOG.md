# Changelog

## 0.1.15-community.1 — source update

- Removed **Keep this on screen**. Reviewing a previous answer no longer pauses automatic answers.
- The first text from each new response immediately switches the display back to the live answer. Subsequent text remains batched to keep the interface responsive.
- Adjusted question stability timing so eligible questions can dispatch between two-second transcript previews.
- Added a response-body deadline so a stalled stream releases the active request and later questions can proceed. Partial text is retained; the failed question is not automatically resubmitted.
- Enabled export immediately after caller text arrives, even without an AI response. Export formatting and writes run off the UI thread, file-picker visibility is improved, and results appear outside Options.
- Added regression coverage and CI checks for history transitions, first-text display, streaming recovery and transcript-only exports. UI tests load the application theme without launching the production single-instance workflow.

This source update does not replace the existing downloadable Community preview or change the managed service's update feed. Build from source to use these changes until a new Community binary release is published.
