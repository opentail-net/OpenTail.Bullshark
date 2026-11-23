# Resume: the source session and the target platform

Date: 2026-11-22. Version 2 (after the plan review in `resume-source-target-2026-11-22-review-notes.md`). Status: **S0 to S4 built and committed; the owner's screen check (S5) is open.** Owner's rule, 2026-11-22 (the rule is the spec; defaults below are mine and are marked as such):

> The default should be to resume into the session's own platform. Resuming into something else can only happen from a summary, not in full.

Example from the owner: Claude's summary resumed into Codex, or a Copilot summary resumed into Claude.

## 1. Goal

Each resume screen shows two things: the **source** (the session being resumed) and the **target** (the platform the new session starts in). The target is the source's platform unless the owner picks another, and another is allowed only when resuming from a **summary**.

**Parity target:** not applicable. This is an app-level rule, the same for all providers. Left out, with a reason: a *full* transcript into another platform (a native session id belongs to one CLI, so it cannot be resumed elsewhere).

## 2. Decisions

- **RS-D1 Full resume targets the source platform only.** A native session id belongs to one CLI. Owner rule, 2026-11-22.
- **RS-D2 Cross-platform start only in Summary mode.** Last-turns is not a summary, so it resumes into its own platform. *Default, mine; owner item RS-O1.* A Summary-mode start still carries the verbatim last turns, as now.
- **RS-D3 The target defaults to the source platform.** A target choice is shown only in Summary mode. Owner rule, 2026-11-22.
- **RS-D4 Screen wording.** The title names the source: "Resume a <Source> session" on all three screens (Claude's today is "Resume a session"). When the target differs, a line reads "Summary of <Source> session → new <Target> session". The button reads "Start in <Target>" everywhere (today "Resume" on Claude, "Start" on Codex and Copilot). *Default, mine; owner item RS-O2.*
- **RS-D5 A cross-platform start is a new session.** It opens in the target CLI, in the source session's folder. It does not reuse or claim the source session's id and is not recorded as a continuation of it. Refusals, each with a status line and before any model call: a missing folder; for Claude as target, the folder's trust status (`FolderCheck`, `FolderStatus.NotTrusted` refuses); for Codex and Copilot as targets, the launch check (`pwsh` and the shim present).
- **RS-D6 The new session's first text names the source.** It is the summary, headed with the source platform's phrase and the summary level, then the target's usual start. Same-platform text is unchanged byte for byte. *Default, mine.*
- **RS-D7 The remembered mode is for the source session.** Remember (C6) saves the mode under the source's key and never saves a target. Remember saves when Start is pressed, as now (C6 decision 1), so it is kept even if a cross-platform start is refused. *Default, mine.*
- **RS-D8 Copilot as a target is disabled by a constant.** `ResumeTarget.CopilotTargetEnabled = false`, with the reason "G-O5: the copy that runs is not confirmed to have the flags the app passes" (see `provider-copilot-2026-11-22.md`, G-O5, measured 2026-11-23). The owner flips it when the install question is settled. The setup check does not read flags today, so a version-based gate cannot be built from what the code knows. *Default, mine; owner item RS-O3.*
- **RS-D9 No extra model call.** A cross-platform summary is one call, made by the source platform's CLI, as now, with its cost line shown before the call. The target's start is a normal start with that text.
- **RS-D10 Summaries are saved as now**, beside the state file. A cross-platform start saves nothing new.
- **RS-D11 Privacy, corrected.** Logs carry provider names and counts only; no summary text goes to a log. The summary text reaches the target CLI as its start argument, as the existing starts already do: Claude's on its command line, fitted to `ClaudeCli.MaxLaunchCharacters`; Codex's and Copilot's through the start file, then as the CLI's prompt argument. So the text appears on the target process's command line. This is existing practice, accepted, and it is **not** avoided by the file route (the file route only keeps long text out of the `cmd.exe` line).
- **RS-D12 Model and effort.** A cross-platform start uses the target's own default model and effort, as its screen starts them by default. The source's choice is not carried over (the choice records differ: `ResumeChoice` has Model and Effort, `CodexResumeChoice` an optional Model, `CopilotResumeChoice` neither). *Default, mine.*
- **RS-D13 Checks before the summary call.** The target's launch check and folder check run before the source's one summary call, so a refusal costs no model call.
- **RS-D14 The target lives in the choice record.** A `Target` field is added to each choice record, defaulting to the source. The preview rebuilds when the target changes, because the text depends on it.
- **RS-D15 The kind of run differs by target.** Claude's Summary start is a supervised, kept-alive run (`SessionWindow.xaml.cs:995`, reviewer's citation, to verify at S3); Codex and Copilot starts open a plain tab. A cross-platform start into Claude is therefore supervised, and its tab rules follow Claude's, not the target's usual tab. Stated, not changed.
- **RS-D17 When both refusals apply, the mode reason comes first.** A cross-platform full or last-turns start into a disabled Copilot target gets "Only a summary can start in another platform…", not the Copilot text. The spec did not say; pinned by `WhenBothRulesFailTheModeReasonComesFirst`. *Default, the coder's choice, confirmed by the orchestrator.*
- **RS-D16 The Copilot source guard stays.** Copilot as a source keeps its supervisor-guard refusal (`CopilotResume.cs:160`).

- **RS-D18 A target change hides the preview; it does not rebuild it.** Changing the target hides the preview, as changing the mode does, and calls nothing. The user presses "Make summary" again to see the new text. The kept summary reaches the Start build, so a summary start makes at most one model call (verified at `SessionWindow.Resume.cs:102`, `:321`, `:412`). *Decision, mine, made to keep the model cost to one call.*

## 3. Owner items (defaults are safe to run on)

- [ ] **RS-O1** Last-turns into another platform. *Default: no* (RS-D2). The owner's words could also allow it; only "not in full" is banned.
- [ ] **RS-O2** Button wording "Start in <Target>". *Default: as RS-D4.*
- [ ] **RS-O3** Copilot as a target before G-O5 is settled. *Default: disabled* (RS-D8).
- [ ] **RS-O4** Claude's title changes from "Resume a session" to "Resume a Claude session". *Default: change* (a visible change to Claude's screen, asked for by the owner).

## 4. Stages and checks

Stages S0 to S4 are built and committed, and reviewed. Their text (the parity table, the contracts, the stages and the review dispositions) is in [Done/resume-source-target-2026-11-22-done.md](Done/resume-source-target-2026-11-22-done.md). The open stage is S5.

- [ ] **S5 Screen check (owner, at the PC, devenv closed).** Each source platform, Full, Last turns and Summary, each enabled target. Includes the Copilot symptoms in section 7. Not run in AFK mode.

## 7. Copilot symptoms: diagnosis deferred

The owner reports that much of Copilot does not work, with no errors shown. What is known, from files and version strings only (see G-O5, measured 2026-11-23):

- The `cmd.exe` route runs the WinGet copy, 1.0.34. Its help lists neither `-C` nor `--sync-session`.
- The start route runs the npm shim, which reports 1.0.51.
- The flags the app passes exist only in the two auto-updated builds (1.0.103, 1.0.104-3).

A click-through was **not run** in AFK mode, for three reasons: it would start the owner's Copilot CLI against the owner's own `~/.copilot` folder (the standing rule forbids probes writing there); Visual Studio is open (the screen check is for devenv closed); and the owner is away. The next step is the owner's screen check (S5) with the symptom written down: which action, what was on screen.

## 8. Risks

- A summary's quality differs between CLIs. The new session may start from a weaker summary than the source's own history. Mitigation: the header says it is a summary.
- The target CLI may refuse a long prompt. Mitigation: the fitting limit (Claude) and the start file (Codex and Copilot). Not a full fix: RS-D11.
- The summary text is visible on the target's process command line (RS-D11). Accepted as existing practice; owner may want it changed.
- A new session in another CLI can look like the same session in the tab list. Mitigation: its title is its own, and the header names the source.
- The Copilot target depends on G-O5 (RS-D8).

## 11. Open items (checklist)

- [ ] Copilot symptoms (section 7): the owner's screen check (S5) with the symptom written down: which action, and what was on screen.
- [ ] Keyboard selection of a disabled ComboBox item (section 10): not verified without a window; check at S5.
- [ ] 63 log lines in 38 App files write an exception message (section 10). Not fixed here; a separate decision.
- [ ] What the real Codex and Copilot CLIs do with a long prompt (section 9): neither process was run.