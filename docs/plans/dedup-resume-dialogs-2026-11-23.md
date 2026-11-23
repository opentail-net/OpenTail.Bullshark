# Dedup: the Codex and Copilot resume dialogs (2026-11-23)

Status: plan, not started. Found by the project-wide duplication review of 2026-11-23. Scope is the two dialogs that are close copies. OpenCode, Claude and the window-side resume code are out of scope (see "Not in this plan").

Goal: one shared implementation of the resume dialog's logic, used by the Codex and Copilot dialogs, with the per-provider parts kept in each provider's own file. The screens look and act the same as now, except the one decided text change (D1).

## Facts read from the code (2026-11-23)

Measures are from diffs of the two files after the provider names are replaced by one placeholder (`Codex`, `Copilot`, `OpenCode` to `PROV`). They are rough, but they say where the work is.

- `CodexResumePrompt.xaml.cs` 544 lines; `CopilotResumePrompt.xaml.cs` 490 lines. After normalising names, 94 changed lines between them. The XAML files: 10 changed lines of 272.
- The window side (`SessionWindow.CodexResume.cs` 354 lines, `SessionWindow.CopilotResume.cs` 333 lines): 251 changed lines. Less alike, so not in this plan.
- OpenCode dialog (326 lines): 390 changed lines against the Codex dialog. Not a close copy.
- Mode enums: `CodexResumeMode`, `CopilotResumeMode` and `OpenCodeResumeMode` have the same three members (`Full`, `LastTurns`, `Summary`), each with its own doc comment. Used in 10, 10 and 5 files (79, 78 and 66 lines).
- Choice records: `CodexResumeChoice` has a `Model` parameter; `CopilotResumeChoice` does not.
- Preview records: `CodexPreview(Text, KeptSummary, Problem)` and `CopilotPreview(Text, KeptSummary, Problem)`: identical shape.
- Model list (Codex only): `LoadModelsAsync`, `ModelReady`, `SelectedModel`, `_modelsReady`, the model row in `IsCross`, and the constructor parameter `models`.
- Screenshot helpers: Codex has `MakeSummaryForScreenshot()`; Copilot has it and also `ChooseFullForScreenshot()`. Callers: `SessionWindow.CodexResume.cs:85`, `SessionWindow.CopilotResume.cs:70-71`.
- Tests read these members of the dialogs: `Choice` (25 uses), `KeptSummary` (13), `PreviewText` (6), `FindName` (18). They find the controls by name: `StartButton`, `FullChoice`, `SummaryChoice`, `TailChoice`, `RememberBox`, `TurnsBox`, `CopyButton`, `PreviewHeader`, `TargetMenu`, `ModelBox`, `ModelNote`. So the `x:Name` values must not change.
- The Claude dialog (`ResumePrompt.xaml.cs`, 511 lines) shares only 43 of 215 distinct long lines with the Codex dialog, so it is not part of this plan. It is older and differs in structure.

### The whole difference, mapped (every changed line in the 94, grouped)

| Area | Codex | Copilot | Plan |
|---|---|---|---|
| Comments and wording (about 25 lines) | names the provider in the text | names the provider in the text | shared text; the provider name comes from one constant per dialog |
| Choice record | six parameters, with `Model` | five parameters | shared interface; `Model` stays on the Codex record |
| Model list and its gate (`LoadModelsAsync`, `ModelReady`, `_modelsReady`, the model row) | present | absent | Codex-only hook, stays in `CodexResumePrompt` |
| Start gate in `Refresh` and `RunRequestAsync` | waits for the model list | no wait | a virtual check: the base returns true; Codex overrides it with the model check |
| Failed-summary text | `"The start text could not be made: {ex.Message}"` | `"The start text could not be made ({ex.GetType().Name})."` | **decision D1** |
| Screenshot helpers | `MakeSummaryForScreenshot` | plus `ChooseFullForScreenshot` | the shared part is in the base; each provider keeps its own entry points |
| Mode enum | `CodexResumeMode` | `CopilotResumeMode` | one `ProviderResumeMode` (identical members) |

## Contracts this must satisfy

| Member | Who relies on it | What must not change |
|---|---|---|
| Constructor of each dialog | `SessionWindow.CodexResume.cs:74`, the Copilot window | Parameters, order and defaults. The Codex constructor keeps `models`, `initialMode`, `folder` and `folderExists` |
| `Choice` (null on cancel) | the windows, the tests | Set only when the user starts; `Remember` is returned, and the screen writes nothing |
| `PreviewText`, `KeptSummary` | the windows, the tests | Same meaning as now: the kept summary is dropped when the level changes |
| Preview delegate `Func<TChoice, KeptSummary?, CancellationToken, Task<TPreview>>` | the windows (`BuildCodexStartTextAsync`) | Same signature; `TPreview` keeps `Text`, `KeptSummary`, `Problem` |
| `ProviderResumeRules.CanStart`, `KeptMatches`, `KeptAfterChange` | the dialogs already | Unchanged (already shared) |
| XAML `x:Name` values | the tests (`FindName`) | Unchanged |
| Start and Copy behaviour | the tests and the owner's sitting | Enabled and disabled in the same states |

## Design (decisions, with why)

- **Composition, not a base class.** A common base class for the dialogs was considered and not used. The generated partial class for a XAML window names `Window` as its base, and a different base in the code-behind may not compile (a base-class conflict between partial declarations). This is **to verify**, but the plan does not depend on it: composition avoids the question.
- A presenter class `ProviderResumeScreen<TChoice, TPreview>` (plain C#, not a window) holds the logic: `Selected`, the enable rules in `Refresh`, `RunRequestAsync`, `ShowPreview`, `ShowFailedSummary`, the kept summary. It gets the controls it needs through one record, `ResumeControls`, that each window builds from its own `x:Name` controls. Each window's code-behind keeps the event handlers and forwards them to the presenter.
- Provider hooks, passed to the presenter as an interface `IProviderResumeHooks`: the provider's name for the text, `MakeChoice(mode, level, turns)` (Codex adds the model), `StartReady(choice)` (Codex checks the model list), and `ShowsModelRow(choice)`.
- Interfaces on the records (no behaviour change): `IProviderResumeChoice` (`Mode`, `Level`, `Turns`, `Remember`, `Target`) and `IProviderPreview` (`Text`, `KeptSummary`, `Problem`). Both records already have these members.
- One enum `ProviderResumeMode` in `App/Providers`, next to `ProviderResumeRules`. The three enums are replaced. OpenCode's is included only if D3 says so.

## Steps (checkboxes, with why)

- [ ] D-S1. Add `ProviderResumeMode` (same three members and the common doc comment). Keep the three old enums for now. Why: a step that changes nothing yet.
- [ ] D-S2. Replace the Codex and Copilot enums with `ProviderResumeMode`. Mechanical rename in the 10 + 10 files; tests updated the same way. Why: one enum for both dialogs before the presenter needs it.
- [ ] D-S3. Add the two interfaces and make the records implement them. No logic change. Why: the presenter is generic over them.
- [ ] D-S4. Add `ResumeControls` (a record of the controls the logic uses) and `ProviderResumeScreen<TChoice, TPreview>`, with the logic copied from `CopilotResumePrompt` first. Why: Copilot is the smaller file and the one with no model list, so it is the first to move.
- [ ] D-S5. `CopilotResumePrompt` uses the presenter. Its tests (`CopilotResumePromptTests`) pass unchanged. Why: proves the presenter on the simple case before Codex.
- [ ] D-S6. `CodexResumePrompt` uses the presenter. Model list, `ModelReady`, the model row and `LoadModelsAsync` stay in the Codex file as hooks. Why: the model list is the only Codex-only logic.
- [ ] D-S7. Apply D1 (the failed-summary text). Add a test that the message names the exception type and not its text, for both dialogs. Why: a visible text change, so it gets its own test.
- [ ] D-S8. Screenshot entry points stay as they are (`MakeSummaryForScreenshot`, `ChooseFullForScreenshot`); each window's `Loaded` wiring is unchanged. Why: the App's `--resume` screenshot modes call them.
- [ ] D-S9. Build App with 0 errors. Run App tests (baseline 681) on net10.0 and net11.0. Record the counts. Why: this is the check that the dialogs still behave.
- [ ] D-S10. Live check, owner only (not run here): both dialogs open, the preview, the Start gate, the screenshot modes. Listed in the sitting; nothing here claims it was done.
- [ ] D-S11. Commit per step. Push when D-S9 passes.

## Decisions for the owner (defaults in place)

- D1. Failed-summary text: use the exception type name, as Copilot does, not the exception message. Default: the type name. Why: the rule in this project is that messages name no output, and an exception message can carry text from the command.
- D2. Scope: this plan covers Codex and Copilot only. Default: yes.
- D3. OpenCode's dialog (390 changed lines against Codex) joins only in a later plan, if the owner wants it. Default: not in this plan.

## Not in this plan

- The Claude dialog (`ResumePrompt.xaml.cs`): structurally older; needs its own reading.
- The window-side builders (`SessionWindow.CodexResume.cs` and `SessionWindow.CopilotResume.cs`, 251 changed lines of 354). A separate plan if wanted.
- The OpenCode dialog (see D3).

## Review (self, against the code, before handover)

- [ ] Every row of the difference table has a step that keeps or removes it. Rows: wording (D-S4), choice record (D-S3, D-S6), model list (D-S6), start gate (D-S6), failed-summary text (D-S7), screenshot helpers (D-S8), mode enum (D-S1, D-S2).
- [ ] The contracts table: each row has a test or a named reading. Constructor and `Choice` are covered by the existing dialog tests; the `FindName` names by the same tests.
- [ ] "To verify" (the base-class conflict) is not a step, so the plan does not depend on it. Composition is chosen for the reason in Design.
- [ ] The 94-line figure is a measure of the diff, not of behaviour. Each changed line listed above was read in the diff output; none is claimed to be identical in behaviour beyond what the diff shows.
