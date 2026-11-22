# Resume: the source session and the target platform

Date: 2026-11-22. Status: **plan, not started.** Owner's rule, 2026-11-22 (the rule is the spec; defaults below are mine and are marked as such):

> The default should be to resume into the session's own platform. Resuming into something else can only happen from a summary, not in full.

Example from the owner: Claude's summary resumed into Codex, or a Copilot summary resumed into Claude.

## 1. Goal

Each resume screen shows two things: the **source** (the session being resumed) and the **target** (the platform the new session starts in). The target is the source's platform unless the owner picks another, and another is allowed only when resuming from a **summary**.

**Parity target:** not applicable. This is an app-level rule, the same for all providers. What is left out, with a reason: a *full* transcript into another platform (a native session id belongs to one CLI, so it cannot be resumed elsewhere).

## 2. Decisions

- **RS-D1 Full resume targets the source platform only.** A native session id belongs to one CLI. Owner rule, 2026-11-22.
- **RS-D2 Cross-platform start only in Summary mode.** Last-turns is not a summary, so it resumes into its own platform. *Default, mine; owner item RS-O1.*
- **RS-D3 The target defaults to the source platform.** A target choice is shown only in Summary mode. Owner rule, 2026-11-22.
- **RS-D4 Screen wording.** The title names the source: "Resume a <Source> session" on all three screens (Claude's today is "Resume a session"). When the target differs, a line reads "Summary of <Source> session → new <Target> session". The button reads "Start in <Target>" everywhere (today "Resume" on Claude, "Start" on Codex and Copilot). *Default, mine; owner item RS-O2.*
- **RS-D5 A cross-platform start is a new session.** It opens in the target CLI, in the source session's folder. It does not reuse or claim the source session's id, and it is not recorded as a continuation of it. If the folder is missing, it is refused with a status line (the existing rule).
- **RS-D6 The new session's first text names the source.** It is the summary, headed with the source platform's phrase and the summary level, then the target's usual start. Same-platform text is unchanged byte for byte. *Default, mine.*
- **RS-D7 The remembered mode is for the source session.** Remember (C6) saves the mode under the source's key. It never saves a target. *Default, mine.*
- **RS-D8 Copilot as a target waits for G-O5.** Copilot is listed but disabled, with the reason, until the copy that runs is confirmed to have the flags the app passes (see `provider-copilot-2026-11-22.md`, G-O5, measured 2026-11-22). *Default, mine; owner item RS-O3.*
- **RS-D9 No extra model call.** A cross-platform summary is one call, made by the source platform's CLI, as now, with its cost line shown before the call. The target's start is a normal start with that text.
- **RS-D10 Summaries are saved as now**, beside the state file. A cross-platform start saves nothing new.
- **RS-D11 Privacy.** Logs carry provider names and counts only. The summary text reaches the target as a prompt through the file route the start already uses, never on a command line.

## 3. Owner items (defaults are safe to run on)

- **RS-O1** Last-turns into another platform. *Default: no* (RS-D2).
- **RS-O2** Button wording "Start in <Target>". *Default: as RS-D4.*
- **RS-O3** Copilot as a target before G-O5 is settled. *Default: disabled with a reason* (RS-D8).
- **RS-O4** Claude's title changes from "Resume a session" to "Resume a Claude session". *Default: change* (a visible change to Claude's screen, asked for by the owner).

## 4. Parity table: what each screen does today

| Screen | Full | Last turns | Summary |
|---|---|---|---|
| Claude (`SessionWindow.Resume.cs`) | `ResumeInFullAsync` (:144), native id | `BuildStartTextAsync` (:270), no call | `BuildStartTextAsync` (:270), one call, then `StartFromTextAsync` (:312) |
| Codex (`SessionWindow.CodexResume.cs`) | resume command, native id | `BuildCodexStartTextAsync` (:142), no call | same builder, one call; `StartCodexFromTextAsync` (:222) |
| Copilot (`SessionWindow.CopilotResume.cs`) | `--resume=<id>` terminal, native id | `BuildCopilotStartTextAsync` (:137), no call | same builder, one call; `StartCopilotFromText` (:241) |

The change adds a target to the Summary column only. Full and Last turns keep their own platform.

## 5. Contracts this must satisfy (read in the code, 2026-11-22)

- `SessionWindow.Resume.cs:45` `ResumeAsync(digest, initialMode)`, `:75` `ResumeOnceAsync`. Callers rely on the mode and the `Remember` flag in the choice.
- `SessionWindow.Resume.cs:218` Claude's background start (`_cli.StartBackgroundAsync`). The cross-platform start into Claude uses the same route.
- `SessionWindow.CodexResume.cs:60` `ShowCodexResumeAsync(session, forScreenshot, screenshotSummary, initialMode)`; `:222` `StartCodexFromTextAsync(session, folder, choice, text)`; `:258` writes the start file and builds the command line with the folder and the model.
- `SessionWindow.CopilotResume.cs:40` `ShowCopilotResumeAsync`; `:241` `StartCopilotFromText(choice, text, folder, title)`.
- `Core/Resume/ProviderResumeText.cs:56` `Compose(cliPhrase, summary, level, tail, turns, sourceSessionId)`. The header comes from the CLI phrase passed in. Codex and Copilot wrap it (`CodexResumeText.cs:37`, `CopilotResumeText.cs:38`). Claude uses `Core/Resume/ResumeText.cs:194`.
- `Core/Resume/SummaryFiles.cs:36` `Save(...)`: the summary folder beside the state file.
- `App/Providers/ProviderResumeRules.cs` (`ProviderResumeWording`): the words each CLI uses.

Gap found: `Compose` takes the target's phrase only. A cross-platform start needs a header that names the source platform. That is an additive overload in Core. Existing output must stay byte-identical.

## 6. Stages

- **S0 Plan and review.** This document. Review by a Sonnet reviewer at S4 (medium-high risk: a start that carries text across platforms, privacy, and the Copilot gate).
- **S1 Pure rules.** New `App/Providers/ResumeTarget.cs`: `Allowed(source, mode, target, copilotGate)`, `DefaultTarget(source)`, the labels, and the refusal reason. Tests: every source × mode × target combination (3 × 3 × 3 = 27 cases). *Gate:* tests pass; Full and Last turns allow only the source; Summary allows all three, with Copilot gated.
- **S2 Screens.** Titles (RS-D4, RS-O4), button wording (RS-O2), the target chooser (Summary only), the "Summary of … → new …" line. *Gate:* existing tests change only where they assert the old title or button text, and each such change is named in the review log. Codex and Copilot strings other than these are byte-identical.
- **S3 Cross-platform start.** An additive Core overload of `Compose` with the source header (RS-D6). The target's start is called with the source's folder (RS-D5). Missing folder and Copilot gate refusals each end in a status line. *Gate:* a dump of the existing `Compose` output before and after is identical; new tests for the cross-platform header.
- **S4 Tests and review.** The four suites: Core short names `net10.0` and `net11.0`; App full names `net10.0-windows10.0.19041.0` and `net11.0-windows10.0.19041.0`. Sonnet reviews S1 to S3 for privacy, the Claude and Codex and Copilot defaults, and the new-session rule. Dispositions go in `resume-source-target-2026-11-22-review-notes.md`.
- **S5 Screen check (owner, at the PC, devenv closed).** Each source platform, Full, Last turns and Summary, each target. Not run by me in AFK mode.

## 7. Copilot symptoms: diagnosis deferred

The owner reports that much of Copilot does not work, with no errors shown. What is known, from files and version strings only (see G-O5):

- The `cmd.exe` route runs the WinGet copy, 1.0.34. Its help lists neither `-C` nor `--sync-session`.
- The start route runs the npm shim, which reports 1.0.51.
- The flags the app passes exist only in the two auto-updated builds (1.0.103, 1.0.104-3).

A click-through of the Copilot path was **not run** in AFK mode, for three reasons: it would start the owner's Copilot CLI against the owner's own `~/.copilot` folder (the standing rule forbids probes writing there); Visual Studio is open (the screen check is for devenv closed); and the owner is away. The next step is the owner's screen check (S5 covers Copilot too) with the symptom written down: which action, what was on screen.

## 8. Risks

- A summary's quality differs between CLIs. The new session may start from a weaker summary than the source's own history. Mitigation: the header says it is a summary.
- The target CLI may refuse a long prompt. Mitigation: the existing file route (the Codex and Copilot start files).
- A new session in another CLI can look like the same session in the tab list. Mitigation: its title is its own, and the header names the source.
- The Copilot target depends on G-O5.

## 9. Review disposition

*(Filled in at S4.)*
