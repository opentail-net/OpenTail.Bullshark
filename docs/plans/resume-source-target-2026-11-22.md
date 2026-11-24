# Resume: the source session and the target platform

Date: 2026-11-22. Version 3 (2026-11-23: the design is revised, see RS-D2, RS-D6, RS-D9, RS-D17 and RS-D19 to RS-D22; the text it replaces is in `resume-source-target-2026-11-22-history.md`). Version 2 followed the plan review in `resume-source-target-2026-11-22-review-notes.md`. Status: **S0 to S4 built and committed; the owner's screen check (S5) is open. The version 3 design is not built (S6).** Owner's rule, 2026-11-22 (the rule is the spec; defaults below are mine and are marked as such):

> The default should be to resume into the session's own platform. Resuming into something else can only happen from a summary, not in full.

Example from the owner: Claude's summary resumed into Codex, or a Copilot summary resumed into Claude.

## 1. Goal

Each resume screen shows two things: the **source** (the session being resumed) and the **target** (the platform the new session starts in). The target is the source's platform unless the owner picks another, and another is allowed only when resuming from a **summary**.

**Parity target:** not applicable. This is an app-level rule, the same for all providers. Left out, with a reason: a *full* transcript into another platform (a native session id belongs to one CLI, so it cannot be resumed elsewhere).

## 2. Decisions

- **RS-D1 Full resume targets the source platform only.** A native session id belongs to one CLI. Owner rule, 2026-11-22.
- **RS-D2 Cross-platform start only in Summary mode.** Full is never cross-platform (RS-D1). Owner decision, 2026-11-23: the separate last-turns mode is removed. Its text is a Summary at the Zero level with the tail (RS-D19), so it is a Summary start and may go to another platform. Answers RS-O1.
- **RS-D3 The target defaults to the source platform.** A target choice is shown only in Summary mode. Owner rule, 2026-11-22.
- **RS-D4 Screen wording.** The title names the source: "Resume a <Source> session" on all three screens (Claude's today is "Resume a session"). When the target differs, a line reads "Summary of <Source> session → new <Target> session". The button reads "Start in <Target>" everywhere (today "Resume" on Claude, "Start" on Codex and Copilot). *Default, mine; owner item RS-O2.*
- **RS-D5 A cross-platform start is a new session.** It opens in the target CLI, in the source session's folder. It does not reuse or claim the source session's id and is not recorded as a continuation of it. Refusals, each with a status line and before any model call: a missing folder; for Claude as target, the folder's trust status (`FolderCheck`, `FolderStatus.NotTrusted` refuses); for Codex and Copilot as targets, the launch check (`pwsh` and the shim present).
- **RS-D6 The new session's first text names the source.** It is a header (the source platform's phrase and the session title), then the summary at its level if there is one, then the tail if it is ticked (RS-D19). There is no session line. With the tail unticked, a Summary's text is unchanged byte for byte. *Default, mine; revised 2026-11-23.*
- **RS-D7 The remembered mode is for the source session.** Remember (C6) saves the mode under the source's key and never saves a target. Remember saves when Start is pressed, as now (C6 decision 1), so it is kept even if a cross-platform start is refused. *Default, mine.*
- **RS-D8 Copilot as a target is behind a constant. Superseded: enabled.** The code has `ResumeTarget.CopilotTargetEnabled = true` (PP-D2, 2026-11-23, the owner's direction). The start route still refuses a Copilot target, because the Copilot start is not built (RS-O6). The text below is the earlier default, kept for its reasons: `ResumeTarget.CopilotTargetEnabled = false`, with the reason "G-O5: the copy that runs is not confirmed to have the flags the app passes" (see `provider-copilot-2026-11-22.md`, G-O5, measured 2026-11-23). The owner flips it when the install question is settled. The setup check does not read flags today, so a version-based gate cannot be built from what the code knows. *Default, mine; owner item RS-O3.*
- **RS-D9 No extra model call.** A Summary start makes at most one summary call (the Zero level makes none), with its cost line shown before the call. The call is made by the source platform's CLI, as now. When the source is out of quota, the target's CLI writes the summary instead (owner, 2026-11-23). How the app knows the source is out of quota is RS-O5. The target's start is a normal start with that text.
- **RS-D10 Summaries are saved as now**, beside the state file. A cross-platform start saves nothing new.
- **RS-D11 Privacy, corrected.** Logs carry provider names and counts only; no summary text goes to a log. The summary text reaches the target CLI as its start argument, as the existing starts already do: Claude's on its command line, fitted to `ClaudeCli.MaxLaunchCharacters`; Codex's and Copilot's through the start file, then as the CLI's prompt argument. So the text appears on the target process's command line. This is existing practice, accepted, and it is **not** avoided by the file route (the file route only keeps long text out of the `cmd.exe` line).
- **RS-D12 Model and effort.** A cross-platform start uses the target's own default model and effort, as its screen starts them by default. The source's choice is not carried over (the choice records differ: `ResumeChoice` has Model and Effort, `CodexResumeChoice` an optional Model, `CopilotResumeChoice` neither). *Default, mine.*
- **RS-D13 Checks before the summary call.** The target's launch check and folder check run before the source's one summary call, so a refusal costs no model call.
- **RS-D14 The target lives in the choice record.** A `Target` field is added to each choice record, defaulting to the source. The preview rebuilds when the target changes, because the text depends on it.
- **RS-D15 The kind of run differs by target.** Claude's Summary start is a supervised, kept-alive run (`SessionWindow.xaml.cs:995`, reviewer's citation, to verify at S3); Codex and Copilot starts open a plain tab. A cross-platform start into Claude is therefore supervised, and its tab rules follow Claude's, not the target's usual tab. Stated, not changed.
- **RS-D17 When both refusals apply, the mode reason comes first.** Written for the old last-turns mode. Moot after the revision (RS-D2, RS-D3): the target chooser shows only in Summary, so a Full start cannot pick a target. The test `WhenBothRulesFailTheModeReasonComesFirst` is to be reviewed at S6. *Default, the coder's choice, confirmed by the orchestrator.*
- **RS-D16 The Copilot source guard stays.** Copilot as a source keeps its supervisor-guard refusal (`CopilotResume.cs:160`).

- **RS-D18 A target change hides the preview; it does not rebuild it.** Changing the target hides the preview, as changing the mode does, and calls nothing. The user presses "Make summary" again to see the new text. The kept summary reaches the Start build, so a summary start makes at most one model call (verified at `SessionWindow.Resume.cs:102`, `:321`, `:412`). *Decision, mine, made to keep the model cost to one call.*

- **RS-D19 The tail is a checkbox under Summary.** It reads "[v] Include last [N] turns" and is shown only in Summary mode. The tail is the last N turns, word for word, oldest first, under the existing cap of about 4,000 tokens (a turn starts at each owner message, as the code splits them today). Unticked, the summary covers the whole session. The value of N starts as the dialog has it today (10). *Owner, 2026-11-23.*
- **RS-D20 No message appears twice.** The summary covers only the messages before the tail, and the tail is never summarised. *Owner, 2026-11-23.*
- **RS-D21 A summary is tied to its tail.** A summary is made for one setting of the tail (N, or unticked). Changing N, or ticking or unticking the box, after Make summary marks the summary out of date, and Start waits until it is made again. Reason: a lower N would leave messages in neither part, and a higher N would put them in both. *Decision, mine.*
- **RS-D22 The Zero level.** Zero makes no model call. Its text is the header, with the session title, and the tail if ticked. It exists so the whole path can be tested without spending tokens. *Owner, 2026-11-23.*
- **RS-D23 One split serves both parts.** The tail is worked out first: the last N turns, then the token cap, which drops the oldest turns first. The summary covers every message before the first turn the tail keeps, so turns the cap drops are in the summary. Nothing is in both parts, and nothing is in neither. *Decision, mine: without this, the cap loses turns.*
- **RS-D24 The command-line fit can drop tail turns.** A Claude target's text is fitted to 30,000 characters (`ClaudeCli.MaxLaunchCharacters`), and the fit can still drop the oldest tail turns. Those turns are in neither part, so when the fit drops any, the start text says how many turns it left out. The fit is otherwise unchanged. *Decision, mine.*
- **RS-D25 Zero.** Zero has no summary and makes no model call. With the tail ticked, its text is the header and the tail. For a cross start, the header names the source, and says that the new session starts from the last N turns of it and that the source is not continued. With the tail unticked, Zero is refused with a status line ("Nothing to start from: tick the tail or choose a summary level"), so no empty start is sent. *Decision, mine.*
- **RS-D26 Nothing to summarise.** If the tail keeps every turn, the summary has no input. Make summary is then disabled, with the words "The tail covers the whole session, so there is nothing to summarise." *Decision, mine.*
- **RS-D27 Stored last-turns.** A stored `lastTurns` (in the settings and the library) is read as Summary at Zero with the tail on. `ContinueMode.LastTurns` and its text stay so old data still reads; no screen offers it. *Decision, mine.*
- **RS-D28 The target writes the summary when the source is out of quota** (RS-D9; owner, 2026-11-23). The coder first checks whether the app has a limit state per platform. If it has one, the summary goes to the target by itself when the source is at its limit, and the dialog says so before the call. If it has none, a failed source summary is followed by a button, "Make summary with <Target>". The target's launch and folder checks run first (RS-D13). RS-O5 stays open for the automatic case. *Decision, mine.*

## 3. Owner items (defaults are safe to run on)

- [x] **RS-O1** Last-turns into another platform. *Answered by the owner, 2026-11-23: yes, as a Summary at the Zero level with the tail* (RS-D2, RS-D19 to RS-D22).
- [ ] **RS-O2** Button wording "Start in <Target>". *Default: as RS-D4.*
- [ ] **RS-O3** Copilot as a target before G-O5 is settled. *Default: disabled* (RS-D8).
- [ ] **RS-O4** Claude's title changes from "Resume a session" to "Resume a Claude session". *Default: change* (a visible change to Claude's screen, asked for by the owner).
- [ ] **RS-O5** How the app knows the source is out of quota (RS-D9). *Default, mine: use the app's limit state for that platform if it has one, and check that at S6; otherwise the source's summary call failing with a limit.* Level row: the owner prefers a dropdown (2026-11-24, because the four-segment row is too wide with the Zero label). Built in S6g.
- [ ] **RS-O6** Copilot as a target. The chooser offers it (PP-D2), but the start route refuses it, because the Copilot start is not built (`CrossStartRoute`, RS-D8). Options: (a) hide Copilot from the chooser until its start is built; (b) build the Copilot start as a stage of its own. *Default, mine: (a), so no option leads to a refusal; (b) when the owner wants it.*

## 4. Stages and checks

Stages S0 to S4 are built and committed, and reviewed. Their text (the parity table, the contracts, the stages and the review dispositions) is in [Done/resume-source-target-2026-11-22-done.md](Done/resume-source-target-2026-11-22-done.md). The open stages are S5 and S6.

- [ ] **S5 Screen check (owner, at the PC, devenv closed).** Each source platform, Full, and Summary at each level (Zero, Minimal, Normal, Complete), with the tail ticked and unticked, and each enabled target. Includes the Copilot symptoms in section 7. Not run in AFK mode. Checks the built version 2 screens; re-run after S6.
- [ ] **S6 Build version 3.** Not started. Parts, in order (each one builds and passes its tests before the next):
  - [x] **S6a Core split and Zero.** Built in `0f50995`; the wording fix is `6056baf`. The one golden case that drops a turn now carries the RS-D24 note, which is the intended change (confirmed by the orchestrator). One function gives the kept turns, used by `Tail` and by the split (RS-D23). `ComposeFrom` accepts no summary, for Zero (RS-D25). Tests: no message in both parts, none lost, cap-dropped turns in the summary part, the Zero text for a same-platform and a cross start, Zero refused with an empty tail, the note when the command-line fit drops turns (RS-D24).
  - [x] **S6b Modes and targets.** Built in `0f50995`. The stored-mode mapping is in `ProviderResumeRules`; the screens do not use it yet (S6c). `ContinueMode.LastTurns` reads as Summary at Zero with the tail on (RS-D27). `ResumeTarget.Allowed` and `RefusalReason` allow a cross start from Summary only; `ResumeTargetMenu` and the refusal wording follow. Tests.
  - [x] **S6c Dialogs.** Built in `d92f8b1`. Open review items are in S6d below. The four resume screens (Claude, Codex, Copilot, OpenCode): radios for Full and Summary; a level row with Zero, Minimal, Normal and Complete; "[v] Include last [N] turns" under Summary only. Zero rebuilds the text at once, with no call. A change of N, the tail or the level after a summary marks it out of date (RS-D21), and Start waits for a new one. Make summary uses the split (RS-D23) and is disabled when the tail covers everything (RS-D26). Tests.
  - [x] **S6d The target writes the summary** (RS-D28). Built in `a575cc0`, with the S6c review fixes in `976a0ba`: the summary is tied to its transcript's line count, dead members are gone, and the Zero label is "Zero · no summary call". Claude at its limit uses its limit state (`_activeLimits`); the other platforms use the button after a failed summary. Writers use the source's instructions and input (`SummaryWriterRules`). Tests: `ResumeWriterTests`.
  - [ ] **S6e Checks.** `scripts/Check-BeforePush.ps1` passes (build, then tests not in Category=Live). Nothing is pushed until it does. Layout on screen is S5, not S6.
  - [ ] **S6f Zero text by the button (AFK, 2026-11-24).** Automatic builds are off (commit `4128d72`), so Zero had no way to build its text. Make summary builds the Zero text too, with no model call; the button stays the only gate. Built in `4ad40ca` (the button reads "Build text" at Zero); pushed.
  - [ ] **S6g Level row as a dropdown (owner, 2026-11-24).** Replace the four-segment level row (Zero, Minimal, Normal, Complete) with a dropdown on all four screens. Same choices, same word counts, same rules (RS-D19 to RS-D26). Starts after S6f, because both touch the same screens.
  - [ ] **S6h OpenCode as a cross-platform target (owner, 2026-11-24: "fill the gap").** Supersedes OC-D18. Starts after S6g is committed (the same screens); not run in parallel with it. Adds OpenCode to the target chooser on the Claude, Codex and Copilot screens, as a start from a summary only.
    - *Contracts this must satisfy.* `ResumeTarget.Allowed`: another platform only from Summary (RS-D2); `RefusalReason` keeps its wording for Full. `CrossStartRoute.For`: a Copilot target is still refused (RS-O6); the source's own platform is refused. `CrossTargetCheckAsync` (SessionWindow.Resume.cs, around line 474): the target's checks run before the one summary call (RS-D13). A cross start sends the text the summary built and fitted, and never calls the summariser again (RS-D9, RS-D13). Privacy (RS-D11): the text goes in the start file, and the logs carry counts only.
    - *Decisions, mine, recorded.* OC-O5 is accepted for this start: it submits its text at once, so it makes one model turn on the owner's login, and the cost line says so before Start (as Last turns does today). The tab is plain, as OpenCode's own text start is (RS-D15 applies to Claude's supervised run only). The model and effort are the target's defaults (RS-D12). OpenCode can write a summary for the target (RS-D28), through `OpenCodeSummarizer`, which needs the OpenCode state path.
    - [ ] **S6h-1 Names.** OpenCode's name in `ContinueDecision` and the tab and status wording follow the others. *Why:* the started line and the tab title name the target.
    - [ ] **S6h-2 Target rule.** `ResumeTarget.Allowed` allows OpenCode from Summary, like Claude and Codex. *Why:* one rule for every target; Copilot's own switch stays.
    - [ ] **S6h-3 Chooser.** Add the OpenCode item to the target chooser on the Claude, Codex and Copilot screens, with `ResumeTargetMenu.Apply` and the same off-outside-Summary rule. *Why:* the option the owner asked for.
    - [ ] **S6h-4 Route.** `CrossStartKind.OpenCode`; `CrossStartRoute.For` returns it for an OpenCode target; `TabTitleOf` and `StartedLine` name OpenCode. *Why:* the dispatch needs a kind to branch on.
    - [ ] **S6h-5 Checks.** `CrossTargetCheckAsync` gets an OpenCode branch: pwsh on PATH, then the shim (`OpenCodeResumeRules.NoPwshOnPath` and `NoShimOnPath`), before any model call. *Why:* a refused target costs no model call (RS-D13).
    - [ ] **S6h-6 Start.** `StartCrossAsync` gets an OpenCode branch after the folder check. It writes the start file with `OpenCodeStartLauncher.WriteStart`, builds the command with `CommandLine(folder, path)`, and opens a plain tab through the same tab code as `StartOpenCodeFromText`. Extract the shared part instead of copying it. *Why:* one way to start from text, not two.
    - [ ] **S6h-7 Cost line.** `CrossModelRow.CostLineForCross` names OpenCode and says the start makes one model turn on the owner's login (OC-O5). *Why:* the owner sees the cost before Start.
    - [ ] **S6h-8 Writer.** The S6d writer path can write with OpenCode (`OpenCodeSummarizer`, with the OpenCode state path), when OpenCode is the target. *Why:* the summary can come from the target when the source is out of quota (RS-D28).
    - [ ] **S6h-9 Tests.** The rule and the route (OpenCode allowed from Summary; refused for Full; Copilot still refused); the refusals before any call (missing folder, no pwsh, no shim); a start writes the file and the command with the cross text, through a fake launcher; the cost line wording; the chooser lists OpenCode from Summary; the OpenCode writer with a fake summariser. Use the existing fakes.
    - [ ] **S6h-10 Checks.** `scripts/Check-BeforePush.ps1` passes (build, then tests not in Category=Live). Push only when it does.
    - [ ] **S6h-11 Owner, at the PC (not AFK).** A real start into OpenCode from a Claude, Codex or Copilot summary, with its cost line read first. Nothing here has been run live.

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
- [ ] 31 App tests are marked Skip (commit `4128d72`, reason "Automatic builds are off for now"). Check each one's cause before re-enabling it: some may fail for another reason. Their bodies are unchanged.
- [ ] RS-O6 (Copilot as a target) is still open. The owner's PP-D2 enabled it, so hiding it is the owner's call, not AFK work.
- [x] OpenCode resume read only the first 5,000 lines. Fixed in `b8d610a`: the resume reads the whole session (the reader takes a line limit; the transcript view keeps the cap). The resume's long-session refusal is removed with it, since the refusal's reason no longer applies (AFK decision, reversible). Cost grows with session length: the whole session is read on open and on each preview.