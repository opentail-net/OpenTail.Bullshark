# Dedup: the Codex and Copilot resume dialogs (2026-11-23) (done items)

> Split from [dedup-resume-dialogs-2026-11-23.md](../dedup-resume-dialogs-2026-11-23.md) on 2026-11-23. These items were finished when they moved; the open item (D-S10, the owner's live check) stays in the plan. The headings above each item are its place in that plan.

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

- [-] D-S1 (dropped, see Results). Add `ProviderResumeMode` (same three members and the common doc comment). Keep the three old enums for now. Why: a step that changes nothing yet.
- [-] D-S2 (dropped, see Results). Replace the Codex and Copilot enums with `ProviderResumeMode`. Mechanical rename in the 10 + 10 files; tests updated the same way. Why: one enum for both dialogs before the presenter needs it.
- [x] D-S3. Add the two interfaces and make the records implement them. No logic change. Why: the presenter is generic over them.
- [x] D-S4. Add `ResumeControls` (a record of the controls the logic uses) and `ProviderResumeScreen<TChoice, TPreview>`, with the logic copied from `CopilotResumePrompt` first. Why: Copilot is the smaller file and the one with no model list, so it is the first to move.
- [x] D-S5. `CopilotResumePrompt` uses the presenter. Its tests (`CopilotResumePromptTests`) pass unchanged. Why: proves the presenter on the simple case before Codex.
- [x] D-S6. `CodexResumePrompt` uses the presenter. Model list, `ModelReady`, the model row and `LoadModelsAsync` stay in the Codex file as hooks. Why: the model list is the only Codex-only logic.
- [x] D-S7. Apply D1 (the failed-summary text). Add a test that the message names the exception type and not its text, for both dialogs. Why: a visible text change, so it gets its own test.
- [x] D-S8. Screenshot entry points stay as they are (`MakeSummaryForScreenshot`, `ChooseFullForScreenshot`); each window's `Loaded` wiring is unchanged. Why: the App's `--resume` screenshot modes call them.
- [x] D-S9. Build App with 0 errors. Run App tests (baseline 681) on net10.0 and net11.0. Record the counts. Why: this is the check that the dialogs still behave.
- [x] D-S11. Commit per step. Push when D-S9 passes.

## Results (2026-11-23)

- **D-S1 and D-S2 dropped.** The plan assumed no shared mode enum existed. It did: `ProviderResumeMode` is in `Providers/ProviderResumeRules.cs`, and its doc comment says each provider keeps its own public enum on purpose (the R-series decision). Removing `CodexResumeMode` and `CopilotResumeMode` would change public types that the tests use (about 150 lines). Instead each choice record maps its own enum to the shared one through `SharedMode` (`ToShared`, now internal), and the presenter works on the shared one. `FromShared` maps back.
- **The presenter** is `ProviderResumeScreen<TChoice, TPreview>` in `App/ProviderResumeScreen.cs`, with `ResumeScreenControls` (the controls it uses, built by each window from its own `x:Name`s), `IProviderResumeScreenHooks<TChoice>` (provider, wording, `MakeChoice`, `StartReady`, `RefreshOwnControls`), and the interfaces `IProviderResumeChoice<TSelf>` and `IProviderPreview` (D-S3).
- **Codex's hooks:** the model in `MakeChoice`, the model-list gate in `StartReady`, and the model row in `RefreshOwnControls`. The model state stays in the Codex window, which the hooks read.
- **D1 applied** to both screens. The Codex test that asserted the message text was updated to the Copilot pattern (type name shown, message absent).
- **Test seam kept:** `TargetChooserTests` reaches `ShowPreview` by reflection. Each window keeps a one-line `ShowPreview` forwarder to `ShowBuiltPreview`.
- **Sizes:** Codex dialog 544 to 232 lines; Copilot dialog 490 to 163; presenter 525 (new). Net about 114 fewer lines. The duplicated logic now exists once; most of the remaining size is documentation and the hook definitions.
- **Checks:** App 681 passed on net10.0 and net11.0 after the Copilot step, and again after the Codex step (with the one test update above). Core not run: no Core code changed in this plan.
- **Not done (out of this plan):** the OpenCode dialog (390 changed lines against Codex, not a close copy), the Claude dialog (structurally older), and the window-side builders (`SessionWindow.CodexResume.cs` and `SessionWindow.CopilotResume.cs`, 251 changed lines of 354). Each would need its own plan.
- **D-S10 (owner, open):** both screens at the PC: the modes, the preview, the Start gate (Codex waits for its model list), Copy, the Start menu in a summary, and the `--codex-resume` and `--copilot-resume` screenshot modes. Nothing here was shown on screen.
