# Dedup: the OpenCode resume dialog onto the shared screen (2026-11-23)

Status: O-S1 and O-S2 done on 2026-11-23 (commits 1e2e4f1 and 9a89d76), in the revised scope below. O-S3 is the owner's decisions (D2 to D5), and O-S6 is the owner's check. Top-10 item 9. It follows the Codex and Copilot migration (`dedup-resume-dialogs-2026-11-23.md`, done). That plan left OpenCode out because the dialog is not a close copy of Copilot.

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

## Revision (2026-11-23): the first design does not hold

The design first written here (optional hooks on the shared presenter) was checked against the dialog in full, and it does not fit. Five places in the OpenCode dialog differ in behaviour, not only in names. A hook for each would change what the screen does, and the plan says the screen must not change:

1. **Failed summary.** Copilot and Codex fall back to In full when a summary fails (`ShowFailedSummary`). OpenCode shows the problem and stays on Summary.
2. **Busy.** Copilot and Codex turn the whole screen off (choices, turns, Copy, Start) while a summary is made. OpenCode turns off only Make summary, and only while any request runs (`_cts is null`). Its Start is not gated by busy.
3. **Summary can start.** OpenCode's `CanStart(Summary)` is `previewReady`, because it keeps no summary. The shared rule also needs a kept summary, so the presenter would never let an OpenCode summary start.
4. **Level buttons.** OpenCode's three level radios are written in its XAML (`MinimalLevel`, `NormalLevel`, `CompleteLevel`). The other screens build theirs in code.
5. **Cost line.** OpenCode adds the last-turns line once a summary is shown. The others do not.

What is identical in all four screens is the **preview pane**: `ShowPreview`, `ShowMessage`, `HidePreview`, `Copy`, and the `Enable` helper. The revised scope moves those into one class. OpenCode keeps its own gating, fallback, level and cost logic.

## Design (revised)

- `ResumePreviewPane` (App): holds the shown text (null when there is none) and the preview's four controls. Methods: `Show(text, sizeLine)`, `ShowMessage(message, problem)`, `Hide()`, `Copy()`. The code is moved, not changed.
- `ResumeScreenElements.Enable(element, enabled)`: the same helper, moved.
- The presenter (Copilot and Codex) uses the pane. Its behaviour does not change.
- OpenCode uses the pane. Its gating, fallback, level and cost logic stay as they are.

## Steps (checkboxes, with why)

- [x] O-S1. Move the preview pane and `Enable` into `ResumePreviewPane.cs`. The presenter uses them. App tests pass unchanged. Why: the same code in one place, with the Copilot and Codex tests as the check.
- [x] O-S2. OpenCode uses the pane for its preview, message, hide and copy, and the shared `Enable`. Its gating, fallback, level and cost logic stay. App tests pass unchanged. Why: removes the copy without changing the screen.
- [x] O-S3. The differences are recorded as owner decisions D2 to D5. The fifth difference, the level buttons written in OpenCode's XAML, changes no behaviour and needs no decision. Not changed. Why: each of D2 to D5 is a choice about what the screen should do, not a refactor.
- [x] O-S4. Line counts before and after, recorded below. Why: the saving has to be real.
- [x] O-S5. Build, run the App tests on net10.0 and net11.0, commit, and push when they pass. Why: the normal gate.
- [ ] O-S6. Live check (owner): the opencode resume screen at the PC: the three modes, the cost lines, the Start gate, Copy. Not run here.

## Not in this plan

- The window-side builders (`SessionWindow.OpenCodeResume.cs` and the Codex and Copilot equivalents, 251 changed lines of 354 for Codex against Copilot). A separate plan if wanted.
- Any change to what opencode does. The shared screen only changes how the code is organised.

## Decisions for the owner

- D1. Is the OpenCode summary still not kept (OC-O4)? This plan assumes yes. If the owner wants it kept, `KeptSummary` must be kept for opencode too, and that is a behaviour change, not a refactor.
- D2. A failed summary. OpenCode shows the problem and stays on Summary. Copilot and Codex fall back to In full. Keep OpenCode's behaviour (the default), or align it with the other two?
- D3. Busy gating. Copilot and Codex turn the whole screen off while a summary is made. OpenCode turns off only Make summary, and only while any request runs. Keep (default), or align?
- D4. A summary's start. On OpenCode a summary can start on its preview alone, because it keeps no summary. The shared rule needs a kept summary. This is the same question as D1: it stays as it is unless D1 changes.
- D5. The cost line. OpenCode adds the last-turns line once a summary is shown; the other screens do not. Keep (default), or align?

## Results (2026-11-23)

- **O-S1 (1e2e4f1).** `ResumePreviewPane` (Show, ShowMessage, Hide, Copy) and `ResumeScreenElements.Enable` move out of the shared presenter, unchanged. `ResumePreviewPaneTests` (5) check each one on the WPF thread, so the Copy path has a test of its own. App: 688 passed on net10.0 and net11.0.
- **O-S2 (9a89d76).** The OpenCode dialog uses the pane and the shared `Enable`. Its gating, fallback, level and cost logic are untouched. App: 688 passed on net10.0 and net11.0.
- **Line counts.** OpenCode dialog 326 → 284. Shared presenter 525 → 484. The new pane file is 92 lines, with its docs and the `Enable` helper. The total is about nine lines higher. The gain is one copy of the preview pane, not fewer lines.
- **Not done, by design:** the presenter hooks in the first design (see Revision). Doing them would change behaviour: D2 to D5 above.
- **O-S6 (owner):** the opencode resume screen at the PC: the three modes, the cost lines, the Start gate, Copy. Not run here.
