# Resume: the source session and the target platform

Date: 2026-11-22. Version 2 (after the plan review in `resume-source-target-2026-11-22-review-notes.md`). Status: **plan, no code yet.** Owner's rule, 2026-11-22 (the rule is the spec; defaults below are mine and are marked as such):

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

## 3. Owner items (defaults are safe to run on)

- **RS-O1** Last-turns into another platform. *Default: no* (RS-D2). The owner's words could also allow it; only "not in full" is banned.
- **RS-O2** Button wording "Start in <Target>". *Default: as RS-D4.*
- **RS-O3** Copilot as a target before G-O5 is settled. *Default: disabled* (RS-D8).
- **RS-O4** Claude's title changes from "Resume a session" to "Resume a Claude session". *Default: change* (a visible change to Claude's screen, asked for by the owner).

## 4. Parity table: what each screen does today

| Screen | Full | Last turns | Summary |
|---|---|---|---|
| Claude (`SessionWindow.Resume.cs`) | `ResumeInFullAsync` (:144), native id | `BuildStartTextAsync` (:270), no call | `BuildStartTextAsync` (:270), one call, then the start (`StartFromTextAsync`, :312) |
| Codex (`SessionWindow.CodexResume.cs`) | resume command, native id | `BuildCodexStartTextAsync` (:142), no call | same builder, one call; `StartCodexFromTextAsync` (:222) |
| Copilot (`SessionWindow.CopilotResume.cs`) | `--resume=<id>` terminal, native id | `BuildCopilotStartTextAsync` (:137), no call | same builder, one call; `StartCopilotFromText` (:241) |

The change adds a target to the Summary column only. Full and Last turns keep their own platform. Verified in review: Claude's Last turns and Summary both use `BuildStartTextAsync`.

## 5. Contracts this must satisfy (read in the code, 2026-11-22)

- `SessionWindow.Resume.cs:45` `ResumeAsync(digest, initialMode)`, `:75` `ResumeOnceAsync`, `:144` `ResumeInFullAsync`, `:180` `StartFullResumeAsync`. Callers rely on the mode and the `Remember` flag in the choice. (The Claude start at `:218` is the Full resume's start, not the summary's.)
- `SessionWindow.Resume.cs:201` `FolderCheck.Check(folder, ...)`; `:202` `Missing` or `NotTrusted` refuses. `:127`, `:208`, `:317` the Claude launch check.
- **`Core/Resume/ResumeText.cs:148` `Fit(summary, level, lines, turns, sourceSessionId, commandLineOf, limit)` calls `Tail` and `Compose` directly.** A `Compose` overload alone is not enough: S3 needs a `Fit` overload that takes compose and tail as delegates, or a parameter for the header. Its limit is `ClaudeCli.MaxLaunchCharacters`.
- `SessionWindow.CodexResume.cs:60` `ShowCodexResumeAsync`; `:142` `BuildCodexStartTextAsync`; `:222` `StartCodexFromTextAsync(session, folder, choice, text)`; `:258` writes the start file and builds the command line.
- `SessionWindow.CopilotResume.cs:40` `ShowCopilotResumeAsync`; `:137` `BuildCopilotStartTextAsync`; `:241` `StartCopilotFromText(choice, text, folder, title)`; `:160` the supervisor-guard refusal.
- The builders hand a **preview** to the start (`PreviewText`), and Codex and Copilot start from the finished text. Claude's `StartFromTextAsync` rebuilds from the digest and lines. So each target needs a *start-from-finished-text* method (S3).
- `Core/Resume/ProviderResumeText.cs:56` `Compose(cliPhrase, summary, level, tail, turns, sourceSessionId)`: the header takes the target phrase only, so a source-named header needs an overload. `CodexResumeText.cs:37` and `CopilotResumeText.cs:38` wrap it. Claude's is `ResumeText.cs:194`.
- Strings that name the platform, which the cross-platform start must take from the *target* (or name the source where the plan says so). Found in review; each to verify at S3: `SessionWindow.CodexResume.cs` tab title "Codex: resume of" (:269) and `SessionWindow.CopilotResume.cs` "Copilot: resume of" (:273); `StartedLine`; the status line at `SessionWindow.Resume.cs:368`; the wording rules; the cost lines in the dialogs; "Continues session: <id>" (a foreign id must not be shown as the new session's); the tail label (Claude "tool calls…", the providers' "messages only").
- `Core/Resume/SummaryFiles.cs:36` `Save(...)`: the summary folder beside the state file.
- `App/Providers/ProviderResumeRules.cs` (`ProviderResumeWording`): the words each CLI uses.

## 6. Stages

- **S0 Plan and review.** Done 2026-11-22: a Sonnet review of v1 (must 6, should 8, notes 3); every must item is dispositioned in section 9. Version 2 is this document.
- **S1 Pure rules. Built and tested 2026-11-22 (committed with this note).** App.Tests 409 on each full target (320 before: 89 new, none changed), exit 0. **Correction (2026-11-22, found in S2): the S1 warning count was not verified.** The check used minimal verbosity, which hides warnings. The S2 clean build shows three pre-existing warning locations (`MainWindow.xaml.cs:23`, `CodexModelPromptTests.cs:71`, `CodexResumePromptTests.cs:386`), none in a file S1 or S2 changed; their blame dates predate this work. Files: `App/Providers/ResumeTarget.cs` (namespace `OpenTail.Bullshark.App`, like its neighbours), `tests/OpenTail.Bullshark.App.Tests/ResumeTargetTests.cs`. Nothing on a screen changed. Its contents: `Allowed(source, mode, target)`, `DefaultTarget(source)`, `CopilotTargetEnabled` (RS-D8), the labels, and the refusal reason. Tests: every source × mode × target combination (3 × 3 × 3 = 27 cases). *Gate:* tests pass; Full and Last turns allow only the source; Summary allows all three, with Copilot disabled while the constant is false.
- **Order changed (2026-11-22): S3a, then S2, then S3b.** The target chooser must not be visible before the start can use the chosen target, or a screen could offer Codex and start Claude. So S2 ships the titles, the button wording, the Target field and the summary line, with the chooser hidden; S3b wires the target and shows the chooser.
- **S3a Core overloads. Built and tested 2026-11-22 (committed with this note).** `ResumeText.FitWith` (the tail and compose steps as parameters; `Fit` delegates to it, same signature and output). `ProviderResumeText.ComposeFrom(targetPhrase, sourcePhrase, summary, level, tail, turns)`: **no source id parameter** (a cross-platform start is not a continuation, RS-D5); a summary and its level are **required**, and it throws `ArgumentException` otherwise (RS-D2). Header, in order: "Summary of a <source> session", "This new <target> session starts from that summary. The <source> session itself is not continued.", "## Summary (<level>)", the summary, the tail, the closing line. Golden tests (`ResumeOverloadsGoldenTests`, 12) captured the existing `Fit` and `Compose` output before the change and pass unchanged; `ResumeOverloadTests` (new) updated once, in this commit, after review: the header order and the summary requirement changed, which the coder's first version had wrong. Core.Tests 3164 on each target (3137 before), App.Tests 409 on each full target, exit 0.
- **S2 Screens.** Titles (RS-D4, RS-O4), button wording (RS-O2), the target chooser (Summary only, RS-D14: the choice records get a `Target` field defaulting to the source), the "Summary of … → new …" line, the preview rebuilt on a target change, and the wrong-platform strings listed in section 5 (each either taken from the target or named as the source on purpose). *Gate:* existing tests change only where they assert an old title, button or string, and each change is named in the review log. Other Codex and Copilot strings are byte-identical.
- **S3b-A Cross-platform start text. Built and tested 2026-11-22 (committed with this note).** `App/Providers/CrossPlatformStartText.cs` (`Build`, `ClaudeDefaults`, `RefusalBeforeSummaryAsync`, `PhraseOf`). The three builders take a trailing `ProviderId? target` (null is the own platform; the same-platform branch is the original code, moved only where the Claude summary became `SummaryForAsync`). Each cross start runs the target's checks before the one summary call (RS-D13): folder, Claude's trust check, Claude's and Codex's launch checks, the Codex start tools; Copilot is refused with `ResumeTarget`'s reason. **Decision (tail labels, found in review):** the cross tail keeps each message under the label its source wrote, through the shared `ProviderResumeText.Tail` with the source's kind, so a Claude reply reads `claude:` even in a Codex start. A first version re-labelled them with the target's kind, which misattributed the words; it was corrected before commit, with tests that fail on a swap. **Decision (RS-D12):** a Claude target uses the Claude screen's default planner model and effort (`SavedSolo()`, falling back to `opus`/`high`). **Not built yet:** the start dispatch (S3b-B). A cross start from the Claude screen throws `InvalidOperationException` if it reaches the Claude start; that is unreachable today because the target is always the own platform. Counts: App.Tests 442 on each full target (421 before), Core.Tests 3164 on each. **Gap:** the builder-level byte-identity test is not written, because the builders start CLI version probes, which the test rules forbid; the same-platform code is reviewed by reading the diff instead, and the S4 review checks it again.
- **S2 Screens. Built and tested 2026-11-22 (committed with this note).** Shown: Claude's title "Resume a Claude session" (was "Resume a session") and button "Start in Claude" (was "Resume"); Codex's and Copilot's titles unchanged, buttons "Start in Codex" and "Start in Copilot" (were "Start"). Every string comes from `ResumeTarget`. `ResumeChoice`, `CodexResumeChoice` and `CopilotResumeChoice` gain a trailing `Target`, which each Start handler sets to its own platform (RS-D14). A record with a static-field default cannot take `= ProviderId.Claude` as a default value, so each record has a secondary constructor with the old parameter list; existing calls compile unchanged. The accessible name of each button now equals its visible text (it was "Resume" or "Start" in the XAML). **Not built, as planned:** the chooser, the "Summary of … → new …" line, the wrong-platform strings (S3b). Existing tests changed: none. New: `ResumeScreenTextTests` (12). **Test isolation to watch:** the new file's helper sets `ShutdownMode = OnExplicitShutdown`, because a second `Application` in the same process throws; the other screen test helpers use the default mode and have not failed yet. Counts: App.Tests 421 on each full target (409 before), exit 0; Core.Tests 3164 on each, unchanged.
- **S3b-B1 Start dispatch (with B1b). Built and tested 2026-11-22 (committed with this note); no chooser yet.** `SessionWindow.CrossStart.cs` (`StartCrossAsync`) starts the target's CLI with the cross text, and nothing else: a Codex target through a new overload `StartCodexFromTextAsync(folder, model, text, tabTitle)` (model null, the CLI default, RS-D12), with tab title `"<Target>: summary of <source title>"` (D-TAB) and status `"Started <Target> from a summary of the <Source> session."` (D-STATUS); a Claude target through the supervised run `StartRunAsync`, with `CrossClaudeRequest(folder, text)` (solo pattern, one coder, the Claude screen's default planner model and effort), after the same `SetupPlan.Validate` and trust check as the same-platform start. Copilot is refused (RS-D8). **Link checks (RS-D5, RS-D7):** no library write under the source key except the remembered mode after a start; no "resumed" mark; no "Continues session" line; the new tab's key is a new GUID, not the source's session key. **Decisions:** the Claude request is shared by the fit and the start, so they measure the same command line; the saved solo is read once per fit (it was read on every measurement). **Privacy fixes, found in review and made here (log text only):** `summary saved: {path}` (the file name carries the session's name) now logs the length; the run line `run … written … in {WorkingDirectory}` (every run, Claude's own included) now logs the count only. **Not changed, to decide at S5:** a refused supervised run reports on the setup page, not the status line (the same-platform start does the same). The three Start handlers still overwrite `Target` with the own platform: S3b-B2 must change that. Counts: App.Tests 507 on each full target (442 before), Core.Tests 3164 on each.
- **S3 Cross-platform start.** (a) A `Fit` overload with compose and tail delegates (section 5). (b) An additive `Compose` overload with the source-named header (RS-D6). (c) A start-from-finished-text method per target (section 5). (d) The target's default model and effort (RS-D12). (e) The target's checks before the summary call (RS-D13, RS-D5). *Gate:* a dump of the existing `Compose` and `Fit` output before and after is identical (the dump test is named when the stage starts); new tests for the cross-platform header and for each refusal.
- **S4 Code review and tests.** The four suites: Core short names `net10.0` and `net11.0`; App full names `net10.0-windows10.0.19041.0` and `net11.0-windows10.0.19041.0`. A Sonnet review of the S1 to S3 **code** (privacy, the Claude, Codex and Copilot defaults, the new-session rule, the supervised run in RS-D15). Dispositions go in the review log and in section 9.
- **S5 Screen check (owner, at the PC, devenv closed).** Each source platform, Full, Last turns and Summary, each enabled target. Includes the Copilot symptoms in section 7. Not run in AFK mode.

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

## 9. Review disposition (plan review, v1, 2026-11-22)

| Item | Disposition |
|---|---|
| Must 1: RS-D11 "never on a command line" is false | Fixed: RS-D11 rewritten; the file route claim removed from section 8. |
| Must 2: `Fit` hard-codes `Compose` and `Tail` | Fixed: S3(a), section 5. Verified at `ResumeText.cs:148-160`. |
| Must 3: start rebuilds from digest or preview | Fixed: start-from-finished-text per target, S3(c). |
| Must 4: target needs model and effort | Fixed: RS-D12. |
| Must 5: target launch checks before the summary | Fixed: RS-D5, RS-D13. |
| Must 6: RS-D8 default not implementable as worded | Fixed: RS-D8 is now a constant with a reason; the version gate is dropped. |
| Should: wrong-platform strings | Fixed: section 5 list, to verify at S3. |
| Should: preview rebuild and where the target lives | Fixed: RS-D14. |
| Should: Remember after a failed cross start | Fixed: RS-D7. |
| Should: Claude's supervised run vs plain tabs | Fixed: RS-D15. |
| Should: Copilot source guard | Fixed: RS-D16. |
| Should: RS-D2 reading | Kept as RS-O1; the Summary-mode caveat added to RS-D2. |
| Should: S0 and S4 wording (plan review vs code review) | Fixed: S0 is the plan review, S4 is the code review. |
| Notes: S3 dump test named; S5 owner-only | The dump test is named at S3's start. S5 is owner-only, as stated. |
| Citation: `Resume.cs:218` is the Full start | Fixed in section 5. |
| Citation: G-O5 date | Fixed: measured 2026-11-23. |

Not checked by the reviewer, and to check at S3: whether existing tests assert the old titles, and what the real Codex and Copilot CLIs do with a long prompt. Neither process was run.
