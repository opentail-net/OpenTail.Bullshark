# R4d: the Claude summary preview checks the launch before the summary

Backlog row R4d in `docs/plans/00-current-work.md`. Model: R4c (commit b5cdd66, the Codex preview).

## Goal

The Claude "from a summary" resume makes its summary call (one model call) before the launch check (the minimum CLI version). When the launch
would be refused, the summary call is wasted. Change the Claude preview so the launch check runs first: a refused launch shows the refusal as the
preview, with no message read and no summary call. The Start-time check is unchanged.

## Contracts this must satisfy

- The start text (`BuildStartTextAsync`, `ResumeText.Fit`, the `RequestFor` request) is unchanged: same text, same fitting, same request.
- The kept summary is unchanged: a preview's summary is kept in the dialog and reused by Resume when its level matches; a level change drops it.
- The refusal text is unchanged: the one `LaunchCheckAsync` writes (`Not started: {tool} {installed} is older than {minimum} ...`).
- The warning text is unchanged: `Warning: ... it starts anyway.` is still shown with the start (`StartRunAsync` via `WithWarning`).
- The Start-time check (`StartRunAsync`, SessionWindow.xaml.cs) still refuses a launch below the minimum, before any run is written.
- The Codex side is not changed.
- The Full resume (`ResumeInFullAsync`, `StartFullResumeAsync`) is not changed.
- Core is not changed: the refusal is carried by an App record, as the Codex side carries `CodexPreview.Problem`.

## Old and new order

Old, the Claude preview (`SessionWindow.Resume.cs`):
1. `PreviewAsync` (line 104) calls `BuildStartTextAsync` (line 108).
2. `BuildStartTextAsync` (line 245) makes the summary call (`_cli.SummariseAsync`, line 259) when no kept summary of the level exists.
3. `ResumeText.Fit`, then `PreviewAsync` returns the text (line 111).
Launch checks per preview press: 0.

Old, the Start (`StartFromTextAsync`, line 287):
1. `BuildStartTextAsync` (line 296): summary call.
2. `SetupPlan.Validate`, then `StartRunAsync` (line 331).
3. `StartRunAsync` (`SessionWindow.xaml.cs` line 981) runs `ClaudeLaunchCheckAsync` (line 983), refuses or writes the run.
Launch checks per Start press: 1.

New, the Claude preview:
1. `PreviewAsync` runs `ClaudeLaunchCheckAsync` first (new).
2. A refusal logs `claude resume preview: launch refused, no summary` and returns `ResumePromptPreview(null, refusal)`: no message read, no summary call.
3. A pass or a warning goes on to `BuildStartTextAsync` as before.
Launch checks per preview press: 1 (none in `BuildStartTextAsync`, so the shared builder does not run it twice).

Start: unchanged. 1 check per Start press (`StartRunAsync`, line 983).

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
- [ ] **App test that proves no summary call on refusal.** Not done: there is no seam. `PreviewAsync` is private to `SessionWindow`, which needs a live window, the CLI, the daemon and the version probe. The rule is not to invent one. The check order is proved by reading only.
- [x] **Start path unchanged.** Why: the spec keeps the start-time check as the real gate, and one check per Start press is kept.

## Open (not in this task)

- [ ] **Resume without a preview still makes the summary before the refusal.** `StartFromTextAsync` builds the summary (line 296) before `StartRunAsync` checks (line 331). With no preview first, the owner still pays for the summary call on a refused launch. A check at the top of `StartFromTextAsync` would fix it, and make two checks per Start press (cheap: cached when Supported). Left open because the spec keeps the Start path unchanged. Owner or orchestrator decision.
- [ ] **Backlog row R4d** in `docs/plans/00-current-work.md` is not updated here; the orchestrator updates it with the commit.

## Tests

- Core: no change expected; Core.Tests run anyway (net11.0, net10.0, `--filter "Category!=Live"`).
- App: App.Tests on net11.0-windows10.0.19041.0 and net10.0-windows10.0.19041.0. The count goes from 145 to 146 (one test added).
- The App cannot be run here: the change is checked by reading, and by the headless dialog test.
