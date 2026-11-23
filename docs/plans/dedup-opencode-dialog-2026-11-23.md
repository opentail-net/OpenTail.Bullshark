# Dedup: the OpenCode resume dialog onto the shared screen (2026-11-23)

Status: plan, not started. Top-10 item 9. It follows the Codex and Copilot migration (`dedup-resume-dialogs-2026-11-23.md`, done). That plan left OpenCode out because the dialog is not a close copy of Copilot.

Goal: the OpenCode screen uses `ProviderResumeScreen` for the logic it shares with the other screens, and keeps what is opencode's own. No change to what the screen shows or does.

## Facts read from the code (2026-11-23)

- `OpenCodeResumePrompt.xaml.cs`: 326 lines. Against the Copilot dialog, after provider names are normalised, 349 lines differ. It is not a copy.
- No platform menu: the XAML has no `TargetMenu`, `TargetMenuButton` or target items (Copilot's has four references). Opencode starts only in opencode.
- Its choice is `OpenCodeResumeChoice(Mode, Turns, Remember, Level = null)` (`OpenCodeResumeRules.cs:27`). It has no `Target`.
- Its preview is `OpenCodePreview(Text, Problem)` (`OpenCodeResumeRules.cs:33`). It has no `KeptSummary`: the summary is not kept by the app (OC-O4, decided 2026-11-23). The presenter assumes a kept summary, so this is a difference the presenter must allow for.
- Its rules are its own (`OpenCodeResumeRules`): `CanStart(mode, previewReady)` takes no kept summary; `RunsAtOnce(mode)` is true only for last turns; the cost lines are opencode's (OC-D9): `FullCostLine`, `LastTurnsCostLine` ("Start makes one model turn on your opencode login"), `SummaryCostLine`, and `SummaryRunningLine`.
- Its constructor: `(heading, facts, palette, previewAsync, initialMode, folder, folderExists)`. Its callers: `SessionWindow.OpenCodeResume.cs` only.
- Its public surface: `Choice`, `PreviewText`, and the constants `OpenCodeTitle` and `OpenCodeStartText`.

## Contracts this must satisfy

| Member | Who relies on it | Must not change |
|---|---|---|
| Constructor | `SessionWindow.OpenCodeResume.cs` | Parameters, order and defaults |
| `Choice` (null on cancel) | the window, `OpenCodeResumePromptTests` | Set only on Start; `Remember` returned, nothing written |
| `PreviewText` | the window | The text Start sends is the text shown |
| Preview delegate `Func<OpenCodeResumeChoice, CancellationToken, Task<OpenCodePreview>>` | the window | Same signature. Note: no kept-summary argument |
| `OpenCodeTitle`, `OpenCodeStartText` | the window's title and button | Same text |
| Start gate | `OpenCodeResumePromptTests` | Start is off until the preview is ready, except In full (`CanStart`) |
| Last turns runs at once; summary needs "Make summary" | the tests | `RunsAtOnce` |
| Cost lines | the tests | The three opencode lines, word for word |
| Level row | the tests | Shown only for a summary |

## Map of the differences

| Area | Copilot / Codex (shared screen) | OpenCode | What the presenter needs |
|---|---|---|---|
| Platform menu and target | yes | no | a flag or a hook that turns the target logic off |
| Choice record | `Mode, Level, Turns, Remember, Target` | `Mode, Turns, Remember, Level` | a `TChoice` with no target (`WithTarget` as a no-op, or the interface split) |
| Preview | `Text, KeptSummary, Problem` | `Text, Problem` | `KeptSummary` optional on `IProviderPreview`, or an adapter |
| Kept summary | yes | no | when the preview has none, `_kept` stays null |
| Cost line | `ProviderResumeRules.CostLine` | opencode's lines | a hook `CostLine(mode)` |
| Runs at once | `ProviderResumeRules.RunsAtOnce` | `OpenCodeResumeRules.RunsAtOnce` | a hook `RunsAtOnce(mode, kept)` |
| Can start | `ProviderResumeRules.CanStart(mode, previewReady, kept)` | `CanStart(mode, previewReady)` | a hook `CanStart` |
| Summary running line | "Making the summary: one X call" | `SummaryRunningLine` | the working line from a hook |
| Mode enum | `OpenCodeResumeMode` | same members | map to `ProviderResumeMode` (as the other two do) |

## Design

- Extend `IProviderResumeScreenHooks<TChoice>` with optional members, each with a default that gives today's shared behaviour: `bool HasTarget` (default true); `string? CostLine(ProviderResumeMode)`; `bool? RunsAtOnce(...)`; `bool? CanStart(...)`; `string? WorkingLine(...)`. Copilot and Codex keep the defaults, so they do not change.
- `IProviderPreview.KeptSummary` becomes nullable-by-convention: an OpenCode preview returns null for it. The presenter already handles null.
- OpenCode's `TChoice` has no target: its `WithTarget` returns `this`.
- Keep `OpenCodeResumeRules` as the source of the words. The hooks call it; nothing moves out of it.

## Steps (checkboxes, with why)

- [ ] O-S1. Add the optional hooks with defaults that reproduce the shared behaviour. Build, and run the App tests: they must still pass unchanged. Why: the presenter changes before any OpenCode code does.
- [ ] O-S2. Make `OpenCodeResumeChoice` and `OpenCodePreview` implement the interfaces (`SharedMode` maps `OpenCodeResumeMode` as the other rules classes do). No behaviour change. Why: the same shape as the other two.
- [ ] O-S3. `OpenCodeResumePrompt` builds the presenter, with `HasTarget = false`, its cost, run and can-start hooks, and no kept summary. Keep the window's handlers (the Start, Cancel and Copy clicks, and the mode radio buttons if they differ). Why: the screen moves onto the shared logic.
- [ ] O-S4. Keep the public surface: `Choice`, `PreviewText`, `OpenCodeTitle`, `OpenCodeStartText`, the constructor. Why: the contracts table.
- [ ] O-S5. Tests: `OpenCodeResumePromptTests`, `OpenCodeResumeRulesTests`, `OpenCodeWindowHelpersTests`, and the rest of the App suite pass unchanged. Why: the tests are the check that the screen still behaves the same.
- [ ] O-S6. Line count and diff: record them in this file, as the dialog plan did. Why: the saving has to be real.
- [ ] O-S7. Live check (owner): the opencode resume screen at the PC: the three modes, the cost lines, the Start gate, Copy. Not run here.

## Not in this plan

- The window-side builders (`SessionWindow.OpenCodeResume.cs` and the Codex and Copilot equivalents, 251 changed lines of 354 for Codex against Copilot). A separate plan if wanted.
- Any change to what opencode does. The shared screen only changes how the code is organised.

## Decisions for the owner

- D1. Is the OpenCode summary still not kept (OC-O4)? This plan assumes yes. If the owner wants it kept, `KeptSummary` must be kept for opencode too, and that is a behaviour change, not a refactor.
