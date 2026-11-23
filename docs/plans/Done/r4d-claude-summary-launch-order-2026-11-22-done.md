# R4d: the Claude summary preview checks the launch before the summary (done items)

> Split from [r4d-claude-summary-launch-order-2026-11-22.md](../r4d-claude-summary-launch-order-2026-11-22.md) on 2026-11-23. These items were finished when they moved; the open items stay in the plan. The headings above each item are its place in that plan.

## Change

- [x] **Read the model and the paths first.** Why: R4c is the rule to copy, and the order of the reads, the summary call and the check must be read, not assumed.
- [x] **Launch check first in `PreviewAsync`, before `BuildStartTextAsync`.** Why: the summary call sits inside `BuildStartTextAsync`, so the check must run before the builder. It goes in `PreviewAsync`, not in the builder, because `StartFromTextAsync` also calls the builder: a check there would run twice on one Start press.
- [x] **Refusal: one count-only log line and the refusal as the preview.** Why: the owner sees why nothing ran; the log names no path, prompt or session.
- [x] **App record `ResumePromptPreview(ResumePreview? Preview, string? Problem = null)` and the dialog's delegate returns it.** Why: `ResumePreview` is a Core record with no refusal field. Changing Core for a display field is out of scope; the Codex side made the same choice (`CodexPreview` in the App).
- [x] **Dialog shows a refusal as Codex does.** The refusal text in the danger colour (`UiDanger`), no prompt text, no Copy. Why: the same display as `CodexResumePrompt` for a refused preview.
- [x] **Resume is off while a refusal shows; Preview stays on.** Why: Codex keeps Start off after a refused preview, so no summary is paid for. Preview stays on so the owner can check again after updating Claude.
- [x] **A choice change clears the refusal and turns Resume back on.** Why: `UpdateCost` already clears a stale preview; the refusal is part of that preview.
- [x] **App test: a refused preview shows the refusal, keeps Resume and Copy off, and a level change turns Resume on again.** Why: the dialog rules are testable headless with the existing fake-delegate seam (`NewPrompt`).
- [x] **Existing App tests moved to the new delegate type.** Why: they must compile; each builds a `ResumePreview` that now sits inside a `ResumePromptPreview`. Their assertions are unchanged.
- [x] **Start path unchanged.** Why: the spec keeps the start-time check as the real gate, and one check per Start press is kept.
