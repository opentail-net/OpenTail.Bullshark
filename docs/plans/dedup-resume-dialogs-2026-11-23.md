# Dedup: the Codex and Copilot resume dialogs (2026-11-23)

Status: D-S10, the owner's live check, is open. The other steps are done: D-S3 to D-S9 and D-S11 (commits 2811483, 3812b5c, and the push), and D-S1 and D-S2 were dropped. Those steps, with the plan's facts, contracts, design and results, are in [Done/dedup-resume-dialogs-2026-11-23-done.md](Done/dedup-resume-dialogs-2026-11-23-done.md). Found by the project-wide duplication review of 2026-11-23. Scope is the two dialogs that are close copies. OpenCode, Claude and the window-side resume code are out of scope (see "Not in this plan").

Goal: one shared implementation of the resume dialog's logic, used by the Codex and Copilot dialogs, with the per-provider parts kept in each provider's own file. The screens look and act the same as now, except the one decided text change (D1).

## Steps (open)

- [ ] D-S10. Live check, owner only (not run here): both dialogs open, the preview, the Start gate, the screenshot modes. Listed in the sitting; nothing here claims it was done.

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
