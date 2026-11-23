# OpenTail.Bullshark

Bullshark - saves your bytes.

Closing the window shouldn't end the conversation. OpenTail.Bullshark shows each Claude Code, OpenAI Codex CLI or GitHub Copilot CLI session as a tab, and the conversation is kept, so you can come back tomorrow and pick it up, with the project already in its head.

A long-running session? Bullshark can summarise it and carry on from where you left off. Your session data stays on your machine, so you don't start from scratch again.

Ran out of tokens? Start a new session from that summary in another platform.

Need multiple sessions? Sure, you can add each in a new tab.

## What it is

OpenTail.Bullshark is a session manager for AI coding CLIs: Claude Code, OpenAI Codex CLI and GitHub Copilot CLI. A window shows each session as a tab. For Claude Code, it keeps a **planner** session and a **coder** session running, gives each one its first direction, and restarts them if they stop. The window is the supervisor: it starts when the owner opens it, and watches the Claude sessions while it is open. There is no log-on startup (P4, owner decision of 2026-11-15).

**Supervision is for Claude sessions only.** The supervisor runs Claude sessions; Codex and Copilot sessions are listed, read, resumed and summarised, not supervised (see [Providers](#providers)).

Sessions are long-lived. A session is not closed after a task, so its context carries on from one day to the next.

**The goal, in the owner's words:** "I do vibe coding, I need to allow vibe coding to carry on." The owner should be able to walk away, come back, and continue the conversation with the planner where it stopped, without rebuilding context or re-explaining the project.

**The owner's requirement:** "the full experience". Every session is the real interactive Claude Code terminal. Redirected output puts Claude Code into non-interactive mode and cripples the project, so OpenTail.Bullshark does not use redirected or print-mode output for any session the owner sees.

## Providers

A dropdown at the top of the rail chooses the provider, and the rail lists that provider's sessions.

| Provider | Status | List | Transcript | Resume in a terminal | Start from text | Summary | Supervision | Plan |
|---|---|---|---|---|---|---|---|---|
| Claude Code | The original. Built. | Yes | Yes | Yes | Yes | Yes | Yes | This README, and [docs/plans/Done/](docs/plans/Done/) |
| OpenAI Codex CLI | Built. The owner's checks at the PC are open. | Yes | Yes, read-only, messages only | Yes (`codex resume <id>`; whether it restores the chat is open) | Yes | Yes, on demand, one call per press | No | [provider-codex-2026-11-21.md](docs/plans/provider-codex-2026-11-21.md) |
| GitHub Copilot CLI | Built (stages G1 to G4; G5 optional, not started). Screen check not run. Install question open (G-O5). | Yes | Yes, read-only, messages only, first 5,000 | Yes (`copilot --resume=<id>`) | Yes | Yes, on demand, one call per press | No | [provider-copilot-2026-11-22.md](docs/plans/provider-copilot-2026-11-22.md) |
| opencode | In progress. The provider identity, the terminal commands and the version check are built; the database reader is being built. Not yet in the window. | No | No | No | No | No | No | [provider-opencode-2026-11-22.md](docs/plans/provider-opencode-2026-11-22.md) |

- **Codex and Copilot are not supervised.** Only Claude implements the supervisor's interfaces (`src/OpenTail.Bullshark.Core/Providers/Capabilities.cs`, lines 35 to 41). The Codex and Copilot provider classes implement none of them (`src/OpenTail.Bullshark.Core/Codex/CodexProvider.cs`, line 10; `src/OpenTail.Bullshark.Core/Copilot/CopilotProvider.cs`, line 11). Their rows show no live status and no restarts.
- **Copilot's install question is open.** The copy that `cmd.exe` runs (WinGet, bundled version 1.0.34) lacks two of the flags the app passes, `-C` and `--sync-session`; the auto-updated builds have them ([docs/plans/provider-copilot-2026-11-22.md](docs/plans/provider-copilot-2026-11-22.md), G-O5). Copilot is switched off as the target of a cross-platform start (`src/OpenTail.Bullshark.App/Providers/ResumeTarget.cs`, `CopilotTargetEnabled = false`).
- **Continue** (the button and the palette entry) picks the most recently active session of the current provider and opens its resume screen; it starts nothing when it opens. **Resume into another platform** is allowed only from a summary: a full or last-turns resume stays in the session's own platform ([docs/plans/resume-source-target-2026-11-22.md](docs/plans/resume-source-target-2026-11-22.md)).

## Where it stands

As of 2026-11-22:

- **CI:** the latest CI run on main is green (run 45082603240, for commit a7d5967). CI runs the tests without the live tests, which start the real `claude` and spend tokens ([.github/workflows/ci.yml](.github/workflows/ci.yml), lines 34 to 37). That run had 3178 Core tests passed on net10.0 and net11.0, and 574 App tests passed on net10.0-windows10.0.19041.0 and net11.0-windows10.0.19041.0; 0 failed. A run for the newest push, commit 17f1972 (docs only), was still in progress when this was written.
- **Providers:** Claude Code is the original. OpenAI Codex CLI and GitHub Copilot CLI are built, and each still has checks at the PC to do. Copilot's install question (G-O5) is open. opencode is planned, not built.
- **Continue** (the button beside the provider dropdown, and the palette entry "Continue last session") is built in stages C1 to C6, with the review (C8) done. Its check at the PC (C7) is still to do ([docs/plans/continue-last-session-2026-11-23.md](docs/plans/continue-last-session-2026-11-23.md)).
- **Resume into another platform** (a summary can start a new session in another platform) is built. Its check at the PC (S5) is still to do ([docs/plans/resume-source-target-2026-11-22.md](docs/plans/resume-source-target-2026-11-22.md)).
- **The live run** with the real `claude` CLI is still open: section B of [docs/plans/live-run-checklist.md](docs/plans/live-run-checklist.md) is unchecked.

The current work is in [docs/plans/00-current-work.md](docs/plans/00-current-work.md), and the owner's decisions are in [docs/plans/owner-decisions-2026-11-22.md](docs/plans/owner-decisions-2026-11-22.md). See [Plans](#plans).

## Where this came from

On 2026-11-12/13 a planner session directed a second local Claude session (a Haiku builder, `git-27`) through briefs sent with SendMessage. It worked. The owner found the agent-to-agent messaging too clunky. OpenTail.Bullshark keeps the two-session pattern and drops the messaging.

## Design principles

1. **Full experience.** Every session runs in a real pseudo-console, so keys, colours, resizing, mouse, paste and permission prompts work as in a normal terminal.
2. **Sessions outlive the window.** Closing OpenTail.Bullshark, or a tab, does not stop a session.
3. **Owner first.** The owner can type into any session at any time. OpenTail.Bullshark does not type into a tab the owner has used recently (D5).
4. **Live messages are the handoff.** The planner and the coder message each other directly with `SendMessage`. No file passes messages between sessions (see Handoff).
5. **Code before models.** Timers, restarts, state checks and alerts are plain code. A model is used only where reading is needed, and only later (P8).

## Architecture

Two parts, plus a later optional third:

- **Supervisor** (the window runs it; a headless host exists but is not started at log-on, P4). It starts the planner and the coder as background sessions (`claude --bg` with `--name`; `--bg` ignores `--session-id`, so the id is read back from the listing, F18), keeps their IDs in a state file, and polls `claude agents --json`. The window runs the same pass every 15 seconds while it is open, off the UI thread. If a session has gone, it restarts it. If a session is `blocked`, it raises an alert. *So far:* the headless host (`src/OpenTail.Bullshark.Supervisor`) is report-only: its `Program.cs` prints two read-only reports, `--usage-report` and `--cache-report`, and starts nothing. Without one of those flags it prints that it runs no supervisor, and exits with code 2. The loop runs inside the window, every 15 seconds and off the UI thread (`src/OpenTail.Bullshark.App/SessionWindow.xaml.cs`, line 170), and the log-on start (P4) is not built.
- **Window** (opened by the owner). One tab per session. For a Claude session, each tab is an embedded terminal running `claude attach <id>`, and closing the tab detaches; the session keeps running. Codex and Copilot tabs run their own CLI (see [Providers](#providers)).
- **Orchestrator** (later, optional, P8). A local model that handles situations the supervisor cannot, such as recovery when a restart is not enough. See its own section.

The sessions run whether or not the window is open. The window is a view onto them, and the owner can type into any tab.

## Requirements

1. **Starts with the PC.** *Not wanted now (P4, owner decision of 2026-11-15), so not built:* the window is the one supervisor, so sessions are watched while the window is open. The sessions themselves keep running without it.
2. **Sessions persist.** A session runs until the owner ends it. Its transcript stays on disk and can be resumed by ID.
3. **Launch with direction.** OpenTail.Bullshark starts each session and gives it its first instruction: a role brief and a pointer to the plan file.
4. **Visible.** Each session is a tab in OpenTail.Bullshark, with its full terminal. The owner sees the activity without attaching elsewhere.
5. **Vibe coding continues.** The owner can take over any tab.
6. **Full experience.** No session the owner sees uses redirected or print-mode output.
7. **Unattended work is bounded.** Autonomy follows the owner's standing rules (see Safety).

## Terminal window

**Chosen to test:** a C# WPF window with [WPF-UI.Terminal](https://nuget.org/packages/WPF-UI.Terminal) 1.0.39 (MIT). It is built on the Windows Terminal console host, so it gives the full experience. P1 is the test that decides whether it works.

**Fallback if P1 fails:** tabs in Windows Terminal, one profile per session running `claude attach <id>`. This keeps the supervisor and the full experience, but the tabs live in Windows Terminal, not in OpenTail.Bullshark.

**Options considered (2026-11-14):**

| Option | Result |
|---|---|
| WPF-UI.Terminal 1.0.39 | Chosen to test. Depends on a CI build of Microsoft's control, so expect pre-release behaviour. |
| `Microsoft.Terminal.Wpf` (Microsoft's own) | Not on nuget.org (404). The CI build is what WPF-UI.Terminal uses. |
| EasyWindowsTerminalControl | Its own page says the WPF control is not officially packaged and relies on beta APIs. Not chosen. |
| VirtualTerminal | A smaller terminal engine. Not checked for full-screen TUI support. Not chosen. |
| Microsoft Intelligent Terminal 0.1 | An experimental fork of Windows Terminal, MIT. A standalone app with an agent pane. No embedding control, SDK or tab API is documented. Forking it means building a large C++ solution on an experimental base. Not chosen as the host. |

**Airspace:** WPF overlays (menus, dialogs) may not draw over the terminal area. Keep dialogs outside the terminal.

**Stack facts (checked 2026-11-14):** .NET SDK 10.0.404 is installed. WPF-UI.Terminal 1.0.39 has a `net10.0-windows7.0` build. It depends on `CI.Microsoft.Terminal.Wpf` 1.25.260303002 and `Microsoft.Windows.Console.ConPTY` 1.24.260710001.

## What the app can read

The app owns each embedded terminal's pseudo-console, so it sees the output stream. The rendered screen is not exposed.

| What | Readable? | Source |
|---|---|---|
| Raw output of an embedded session | Yes | `TermPTY.TerminalOutput` event, `GetConsoleText()`, `ConsoleOutputLog`. Includes colour codes and full-screen redraws. |
| The rendered screen as text | No public API | `TerminalControl` exposes `GetSelectedText` only. |
| What a session is doing | Yes, the best source | The transcript: one JSON object per line in `~\.claude\projects\<project>\<session-id>.jsonl`. User and assistant records with content blocks. Format checked on one file. |
| Busy, blocked or gone | Yes | `claude agents --json`. |
| Recent output of a background session | **Not yet** | `claude logs 0b6cfaa9` failed: `connect ENOENT \\.\pipe\cc-daemon-*-control`. The background daemon's pipe was unreachable. |

The app can also type into a tab (`WriteToTerm`) and read the process (`Process`). That is how the nudge works.

In a tab running `claude attach`, the raw stream is the attach screen, not the session's own output. So state comes from `agents --json` and the transcripts, and the tab is the display.

## Contracts this must satisfy

Members checked by reading the package DLLs (reflection) and XML docs. Behaviour is taken from the package README, not run.

- **`ITerminalConnection`** (Microsoft.Terminal.Wpf): `Start()`, `WriteInput(string)`, `Resize(uint, uint)`, `Close()`, event `TerminalOutput`. `TermPTY` implements it.
- **`TermPTY`** (WPF-UI.Terminal): `Start`, `WriteToTerm`, `WriteToTermBinary`, `Resize`, `CloseStdinToApp`, `GetConsoleText`, `ConsoleOutputLog`, `ConsoleOutStream`, `Process`, `TermProcIsStarted`, events `TerminalOutput` and `TermReady`, `Dispose`.
- **`ExtendedTerminalControl`** (WPF-UI.Terminal): `Terminal`, `ConPTYTerm`, `StartupCommandLine`, `WorkingDirectory`, `LogConPTYOutput`, `RestartTerm`, `DisconnectConPTYTerm`, `Dispose`.
- **`claude` CLI** (from `claude --help`): `--bg`, `--session-id`, `--name`, `--resume`, `--worktree`, `--permission-mode`, `--allowedTools`, `--model`, `--effort`, `attach`, `agents --json`, `logs`, `respawn`, `stop`/`kill`, `rm`.
- **`agents --json`** returns an array. Background entries carry `id`, `cwd`, `kind`, `startedAt`, `sessionId`, `name`, `state`. Interactive entries carry `pid`, `status` (`busy`), `cwd`, `startedAt`, `sessionId`, `name`.
- **Transcript** (one file checked): JSON Lines under `~\.claude\projects\<project>\<session-id>.jsonl`, with `user` and `assistant` records holding content blocks.

**Not verified yet (P1 and P2 test these):**

- whether `claude attach` renders and takes input correctly inside an embedded terminal;
- whether `--bg`, `--session-id` and `--name` can be combined in one launch;
- why `claude logs` could not reach the background daemon, and whether the daemon starts reliably;
- whether `claude respawn` restarts a session that has stopped, or only re-runs it on a new binary;
- whether `agents --json` lists sessions that have ended, and the full set of `state` values;
- whether typing into an embedded tab (`WriteToTerm`) reaches `claude attach` as real input;
- how spend can be read per session (transcript usage fields, to verify).

## Gaps the CLI does not fill

- **Budget cap.** `--max-budget-usd` works only with `--print`, which the owner has ruled out. OpenTail.Bullshark needs its own spend check (D4).
- **Context is finite.** "Persist" means the transcript is kept and resumable. The context window still fills, and auto-compaction summarises older turns, so the planner loses detail over time. **The plan file and this README are the memory that survives compaction.** The planner writes its decisions there, not only in chat.

## Handoff between planner and coder

The sessions see each other directly and live, with `SendMessage` by session name (finding F22). A file-only handoff is a step back, so it is not the channel. The plan file is the record.

- **Live messages only:** the planner sends the coder each batch with `SendMessage` (the batch text is in the message), and the coder sends its report back the same way. Session names come from `ListAgents`. No file is used to pass messages (owner decision, 2026-11-14).
- **Recovery:** a restarted session resumes with its own transcript, so it keeps its context. Its continuation prompt tells it to ask the other session to resend the current batch (F23). OpenTail.Bullshark writes no plan file for messages.
- **Permission mode:** both sessions must run in the same mode, or a message waits for approval (F22). Default: `auto` for both (D3, revised; the code's default, see `SetupPlan` and `RunPlan`).

- **The plan:** `docs/bullshark/PLAN.md`, a checkbox list (the owner's plan format). The planner writes each batch as a block with fixed decisions, STOP conditions and a capped report format.
- **The report:** the coder ticks the boxes and writes a short report under the batch.
- **The fallback:** if a live message cannot be delivered, OpenTail.Bullshark types one line into the tab, only when the owner has not used it recently (D5). It is typed input to the live session, not a file.
- **Parallel work:** both sessions share one repo. Two coders on the same files need separate worktrees (`--worktree`). Default: one planner, one coder.

## Startup

- **Supervisor (P4, not wanted now, so not built):** the design was a Windows Task Scheduler task, trigger "At log on", running as the owner's user account. *Expected, not verified:* a Windows Service would run under another account and would not see the owner's Claude login, so a service is the wrong choice.
- **Window:** opened by the owner. Auto-open at log-on is optional (owner's choice).
- **Log-on, not power-on.** A PC that boots without logging in does not run OpenTail.Bullshark. Auto-logon would change that (D6).
- **Blocked sessions:** the supervisor alerts once per entry into `blocked`. The window lists the session under "Needs you" at the top of the rail, shows the count in the status bar, and flashes the taskbar button with a sound when a session newly needs the owner (turn off with `"notify": false` in `ui.json`). When the window is not in front (minimised or behind other windows), a silent Windows toast also shows the session's name and reason, one per batch; turn it off with `"toast": false` in `ui.json` or "Toggle toasts" in the command palette. A toast is not seen on screen until a run is checked by the owner.
- **Row marks (2026-11-18):** a session row shows its context figure (`ctx 142k`, with ⚠ at 80 % of the window; the window is the owner's setting in Settings, 200k assumed, not read from Claude) and, when a plan file is attached in its Edit dialog, the checked items of that plan (`3/7`). Each kind of toast (blocked, problems, notices, ready for review, usage limit reset) can be switched off under Settings, General; the last two are silent toasts.
- **Viewed files (2026-11-18):** the Changes tab has a "viewed" box per changed file, plus "Mark all viewed" and "Clear". A tick is kept in `ui.json` (key `viewedFiles`: session, path and two numbers, never file text) with the file's size and write time, so a file that changes again is unviewed; a deleted file has its own stamp. Nothing is written to the folder or to git.
- **Big-deletion check (2026-11-18):** a changed file that lost most of its lines (at least 50 removed and at least three times as many removed as added) or a tracked file that is deleted gets a "check" label on the Changes tab, and a count line under its header. It is a label, not an alarm, with no sound or toast; the thresholds are the orchestrator's assumption and sit in `DeletionCheck.cs`.
- **Same-call loop (2026-11-18):** the effort panel lists a call that failed three times or more in a row, with the same tool and the same input and no success of that call in between, with its first and last line. A fail, edit, pass sequence is not listed. The input is hashed, never kept.
- **Group by tag (2026-11-18):** the rail's show menu groups the History section by folder or by tag. A session with several tags is under each of its tags, the sessions with no tag are last, and Needs you and the search are unchanged.
- **Night page (2026-11-19):** Insights has a Night sub-tab, the bedtime preflight: ten checks (the window's last pass, the background daemon, the login, the setup check, a usage limit in force, the runs due in the next 12 hours, folders waiting for trust, the AC sleep timeout, what waits for you, and the quiet hours) and one verdict, "Ready for the night" or how many things to check. The checks are read when the page is shown, on Refresh, and every 30 seconds while it is open. Nothing is started or sent to a session. The window does not keep the computer awake: the Sleep row only reports the AC sleep timeout, read with `powercfg /query` (read-only, five-second timeout).
- **Heartbeat (2026-11-19):** after each supervisor pass the window writes `heartbeat.json` beside the state file (at most every 10 seconds). The Night page shows "Last pass: 12 s ago", so the owner can see in the morning whether the window ran in the night. The window is the only supervisor: a closed window or a sleeping PC means no pass.
- **Night report (2026-11-19):** when the owner returns to the window after at least 30 minutes away, and something happened, the "While you were away" digest is also saved as Markdown in `reports\` beside the state file, as `away-yyyy-MM-dd-HHmm.md` (local time), with the sessions' plans and the token totals of the period. A report is never overwritten, and the newest 60 are kept. The Away page names the saved file and has an "Open reports folder" button. Nothing is sent anywhere.
- **Quiet hours (2026-11-19, off by default):** Settings, General, Notifications: a window in local time, such as 22:00 to 07:00, that may cross midnight. In those hours a new wait or a re-alert gives no sound, flash or toast (the silent ready and usage-limit toasts too). The rail, the Needs-you list, the log, the digest and the re-alert count still run. Stored as `quietHours` in `ui.json`.
  A wait that begins in quiet hours is silent when it begins and is not sounded again when the hours end (review F12, by design); after that only the re-alert interval and the morning digest remind you of it.
- **Privacy note (review F13):** the transcript index cache, `cache/transcript-index.json` in the state folder, keeps each session's last and first user text, so transcript text is kept on disk beyond Claude's own retention. The app does not delete it; a switch to turn it off is not built, and you may want one.
- **Damaged `ui.json` (review F7):** a damaged file is moved to `ui.json.bad-yyyyMMdd-HHmmss`; the newest five such copies are kept.

- **Look (2026-11-19):** the check boxes, text boxes, lists, combo boxes, sliders, scroll bars and tooltips are drawn from the palette in both themes (`Styles/Controls.xaml`), so every window looks the same. Resume is green like Start. The History folder names are the leaf of the folder the transcripts name (`C--Git` shows as `Git`; the full path is in the tooltip); the group is still Claude's folder name. The contrast figures are in `docs/plans/Done/ui-polish-batch-2026-11-19.md`.
- **Name clash (lifecycle batch L1, 2026-11-19):** a lost session is not resumed while another listed session of the same name is working, blocked or busy: a second paid session with that name would start. The supervisor raises one "name taken by another session" alert, which is shown under Needs you and re-alerts like a restart loop. The resume runs once that other session is done, stopped, failed or gone. A row the dead daemon left (stale) does not hold the name.
- **Restart backoff (lifecycle batch L2, 2026-11-19):** the first restart of a lost session in an hour is immediate. After it, the supervisor waits one minute before the next resume, and two minutes after that one; a refused resume counts as a restart too. The cap of three per hour and its restart-loop alert are unchanged. The gaps are in `SupervisorRules.RestartGaps`, an assumption until the owner has seen them work.
- **Silent working session (lifecycle batch L3, 2026-11-19):** a session listed as working that has made no API request for the threshold shows a notice under Needs you ("no output for 45 min; may be a long command"). It sounds once per silent spell, with a Notices toast, and goes away when the session writes again. It is a notice only: nothing is sent to the session and its state does not change. A long command can trip it. Settings, Naming, "A working session with no output for": Off, 30, 45 (the default), 60 or 120 minutes, or a value set by hand as `silentMinutes` in `ui.json` (clamped to 5 to 1440). It uses the request times the window already reads for the cache; no transcript is read for it.
- **Find and jump in a transcript (transcript and search batch T4 and T5, 2026-11-19):** Ctrl+F opens a find bar over the shown session. The query is one substring, without case; a hit is a line, and a tool result is searched whole, though the line shows only its start. Enter goes to the next hit, Shift+Enter to the previous, Escape closes. The hits are highlighted, and the search runs on a pool thread and stops when the text changes (at most 2,000 lines). The keys act only while the transcript has the keyboard: `[` and `]` go to the previous and next prompt, `{` and `}` to a failed tool result, `Alt+[` and `Alt+]` to a file write, `Ctrl+[` and `Ctrl+]` to a compaction. A hint line above the transcript lists them. Nothing is sent to a session.
- **Copy and full results (transcript and search batch T3, 2026-11-19):** right-click a line for "Copy this message", "Copy command" (a Bash call) or "Show full result" (a tool result: the whole text is read again from the file, shown in a read-only box, and cut at 200,000 characters with a note). Ctrl+C copies the focused line. The clipboard is the only destination.
- **Keyboard and folder names (transcript and search batch T1 and T2, 2026-11-19):** search results and the effort panel's items take the keyboard (Tab, Enter or Space; an accent edge shows the focus). Search results and the Usage page's top projects show the folder's name, as the rail does, with the whole path on the tooltip; two folders with one name get their parent folder in brackets. The group key is unchanged: only the names changed.

## First run, errors and diagnostics (firstrun batch, 2026-11-19)

- **Launcher and version:** `scripts\Start-OpenTail.Bullshark.bat` starts the newest built Debug or Release app with no demo folder or task; `scripts\Start-Demo.bat` starts the demo folder and task. Settings, Claude shows the version: `Version 0.1.0 (abc1234)`, with the git short hash when the build had git. The version `0.1.0` is an assumption until the owner names one. The Claude Code version is shown only when a pass has already read it.
- **Setup check, first row:** "Claude Code · claude" says whether `claude` is on PATH (no process is started to check it). A missing `claude` is a problem with the next step written beside it, and the Night preflight counts it.
- **Errors on the status line:** a failed pass says what to do ("Claude Code could not be started: check that `claude` is installed and on PATH …") instead of the exception. The log keeps the exception.
- **Crash files:** an error the app kept going past writes `logs\crash-yyyyMMdd-HHmmss.txt` beside `app.log`: the version, the OS, the exception, and the last 200 log lines, all redacted (the profile folder as `~`, each other path cut to its file name, including paths with spaces in their folders and Git Bash or WSL paths; a URL keeps its scheme, host and path, and loses its user name, password, query and fragment; session ids cut to 8 characters, tokens and prompt or transcript lines removed, lines cut at 400 characters). The newest ten are kept. The status line names the file, and the app keeps running. `--crash-test` throws one test exception; it is for checking this path only.
- **Diagnostics zip:** Settings, Claude, "Save diagnostics…" writes `diagnostics\diagnostics-yyyyMMdd-HHmmss.zip` beside the state file and opens the folder. It holds `versions.txt`, `setup-check.txt`, `preflight.txt`, `app.log` (the last 500 lines), `events.tail.txt` (the last 200 events) and `settings.txt` (the keys of `ui.json`, with booleans, numbers and choice names, never a note's text). The same redaction applies, so URLs and paths are redacted by rule (a remote MCP server's URL keeps its host and path, and loses its query, fragment and user name and password). It never holds transcripts, `library.json`, `runs.json` or `cache\`. Nothing is sent, and the newest five zips are kept. Read the zip before you share it: the redaction is by rule, and a secret without a known form is not caught.
- **Theme on first run:** with no `railTheme` saved in `ui.json`, the rail follows Windows' app theme (light or dark); a saved choice always wins, and the app never writes the Windows value into `ui.json`.
- **Keyboard:** the command palette's "Keyboard shortcuts" shows the chords and where each one works. The palette also has zoom in, zoom out, zoom 100 %, "Switch theme", and "Filter rail: …" for each rail view. The transcript's hint line is built from the same table.
- **Empty states:** Storage with no transcripts names the folder it looked in and has "Open Claude's folder"; Timeline with no runs says to start one from the + tab and has "Open the setup tab"; a search with no hit says how many sessions it read. The setup check's rows show Ok, Warning or Problem as words as well as colours.
- **Attention file (off by default):** Settings, Claude, "Write attention.json for other tools" writes `attention.json` beside the state file on each pass: the time in UTC, the count, and each waiting item's reason code and session name (no detail text). It is deleted when the window closes. Nothing is sent, and nothing reads it back.
- **Not built, by the owner's decision:** publishing a release zip, an update check, and a Teams mirror. The licence (PolyForm Strict, `LICENSE.md`) gives no right to distribute the app, and the app makes no network call of its own.

## Soak log (perf and testability batch, 2026-11-19)

- **Where:** `logs\app.log` beside the state file (`%LOCALAPPDATA%\OpenTail.Bullshark\logs\app.log` by default). Lines start with `soak (start)`, `soak (periodic)` every 30 minutes, and `soak (close)`; `--soak-seconds <n>` changes the interval (0 turns the periodic line off).
- **What a line holds:** the uptime, the GC heap, working set, private bytes, handles, threads, the terminals, rail items and transcript blocks held, the passes run (and the mean, max and last time), the rail builds (and their mean, max and last time, the rails kept, the rebuilds made while scrolled and the scroll offset), and the ui.json reads since the line before, split into UI thread and background. No session, folder, file or message text is in it.
- **Startup:** `startup: window shown`, `history read done`, `rail built` and `first pass done` are each logged once, as the milliseconds after the process started.
- **Reading it:** a steady working set, handle and thread count over hours means no growth; a UI-thread ui.json read per pass should now be 0 (the pass reads the file on a pool thread); a rebuild line with `rebuilds while scrolled` above zero, followed by a scroll offset of 0, is a scroll reset.

## Visibility

Decided by the owner (2026-11-14): the session terminals appear as tabs inside OpenTail.Bullshark. That is the primary view. The status bar keeps the zoom at its bottom-right corner, and has three permanent text buttons: Go to… (the command palette), Insights (Away, usage and limits, the run timeline and storage, as sub-tabs) and Settings (theme, notifications, automatic naming, the re-alert interval, the standing instructions and the setup check). Ctrl+Shift+N goes to the next waiting session (it only shows the tab, and sends nothing); Ctrl+Shift+P opens the command palette, which also finds sessions by their tags, project folder and state. The rail's show menu filters by one tag at a time (Needs you is never filtered). A magic rename also adds up to two tags to a session, and automatic naming (off by default: `magicAuto` in `ui.json`, or "Toggle automatic magic naming" in the palette) names a never-named session when its cache goes from warm to cold while the window is open; it uses your usage when on.

An activity panel, built from the session transcripts (busy or blocked, last action, what changed), can sit alongside the tabs later (P6). It adds to the tabs; it does not replace them.

## Later (optional): orchestrator

Not needed for the core. It is useful for recovery when a restart is not enough, for example a session stuck in a state that a restart does not fix, or a planner that needs an answer while the owner is away.

- **Engine:** OpenTail.Stingray, the owner's own engine. OpenTail.Bullshark is .NET, so it can reference the package and run the model in-process, or call Stingray's server. Stingray's request audit lists `response_format` with `json_schema` as implemented, so the model's reply can be limited to a fixed list of actions. Its Anthropic-style endpoint refuses forced tool use, so use the OpenAI-style call. Its grammar-constrained tool decoding (`ToolGrammar`) is opt-in and off by default.
- **Model:** a Qwen3-class model. Stingray has a tool-call adapter for Qwen 2/3/3.5/3.6/3.8, and its docs use Qwen3-8B-Q4_K_M as the example. Speed on this machine (Ryzen 7 5700G, integrated Radeon through Vulkan, 63 GB RAM) is not measured. A 27B model is probably too slow here.
- **Authority:** the model picks an action from a fixed list (for example `continue`, `run_tests`, `wait`, `escalate`). Anything else goes to the owner as an alert. The list and the blocked categories live in code. A cap limits automatic replies per session per hour, and a kill switch stops everything.
- **Shadow mode first:** it suggests actions and logs them, and the owner compares them with what the owner would do. Real actions switch on only after the labelled test set passes.

## Safety

- OpenTail.Bullshark adds no permission that the owner has not set. Sessions follow the owner's `CLAUDE.md` and memory rules, which already say: no deleting repos, no changes that destabilise the OS, no pushing unless asked, no commits outside the project's scope.
- The nudge is typed input. OpenTail.Bullshark types only the nudge line, and only into a tab the owner is not using (D5).
- Default permission mode for the coder: `auto`, the mode the code sets for both roles (D3, revised; see the decision table below).
- An unattended session that hits a permission prompt stops in `blocked`. That is the main failure to watch for. A blocked session does nothing until someone looks.

## Phases

- [ ] P0 Owner decides D1, D3, D4, D5, D6, D7 and D10.
- [x] **P1 Spike (the gate).** PASSED 2026-11-14: acceptance tests T1-T5 in `scripts/Test-Spike.ps1`, on two full runs. One WPF window with one embedded WPF-UI.Terminal tab running `claude attach` on a background session. Test: keys, colours, mouse, paste, resizing, a permission prompt, Ctrl+Z, whether the session survives the tab closing, typing into the tab (the nudge), and `Process` exit detection. Also find out why `claude logs` could not reach the daemon. If the tab fails, switch to the fallback and revisit before building more.
- [x] P2 Supervisor (headless, built 2026-11-14; host `src/OpenTail.Bullshark.Supervisor`, 64 offline tests, 2 live tests): starts both roles with `--bg` and reads the session id back (`--session-id` is ignored, F18); resumes a lost session with a continuation prompt (F19, F21); alerts on `blocked` and before the login expires (F8).
- (P2 original scope:) start planner and coder as `--bg` with `--session-id`; keep IDs in a state file; poll `claude agents --json`; restart lost sessions; alert on `blocked`.
- [ ] P3 Tabs: one tab per session; open, close and reattach; each tab shows its state.
- [ ] P4 Startup: Task Scheduler task at log-on runs the supervisor without a window. **Not wanted now: the window is the one supervisor (owner decision, review of 2026-11-15 §4; see `src/OpenTail.Bullshark.Supervisor/Program.cs`). Not built.**
- [ ] P5 Handoff: batch and report messages via `SendMessage`, a typed fallback line when delivery fails, and resume recovery by message.
- [ ] P6 Activity panel built from transcripts (busy, blocked, last action, what changed).
- [ ] P7 Budget (own spend check) and `blocked` alerts.
- [ ] P8 *Later, optional:* orchestrator for recovery. Sub-steps: pick the model with a labelled set of real excerpts; shadow mode; then enable the fixed action list for real. Not started. Not needed for P1-P7.

## Open decisions (owner)

| ID | Question | Recommendation |
|---|---|---|
| D1 | Planner model: Fable, Opus or Sonnet? | Opus by default; switch per project. Fable for the hardest planning. |
| D2 | Visibility | **Decided (2026-11-14):** tabs inside OpenTail.Bullshark. Activity panel later (P6). |
| D3 | Coder permission mode: `acceptEdits` with an allowlist, or `bypassPermissions`? | `acceptEdits` plus an allowlist. Bypass only in a sandbox with no internet. **Decided by the owner, 2026-11-19: coders (and planners) run in `auto`.** The code already does this (`SetupPlan.PermissionMode`, `RunPlan` default; D-S14 in `docs/plans/Done/setup-and-sessions.md`). The first recommendation above is kept as the record. Not yet checked live: whether `--bg` sessions accept `--permission-mode auto` as documented (the desktop research could not confirm it, `ideas-desktop-codex-2026-11-18.md` A3). |
| D4 | Budget: own spend check (the `--print` cap is ruled out). | Own spend check, in P7. |
| D5 | How long must a tab be idle before OpenTail.Bullshark may type into it? | Ten minutes, set by the owner. Check the typing behaviour in P1. |
| D6 | Startup: log-on only, or also auto-logon? | Log-on only. Auto-logon is a security trade-off for the owner to decide. |
| D7 | Window stack | C# WPF with WPF-UI.Terminal, to be proven in P1. Fallback: Windows Terminal tabs. Intelligent Terminal not chosen. |
| D8 | Orchestrator (later): which model, and when to start? | Not now. Start P8 only after P1-P7 work. Stingray with a Qwen3-class model, chosen by testing. |
| D9 | Orchestrator authority: which actions may it take without asking? | A fixed list only (continue, run tests, wait). Everything else is an alert to the owner. |
| D10 | How do alerts reach the owner when AFK? | Partly done (2026-11-17): in the window, a "Needs you" rail group, taskbar flash and sound. A Windows toast was added on 2026-11-18 (not seen on screen yet). Still open for AFK: the Teams path in OpenTail.Orca (already used for approval cards; check its limits). |

## Known limits

- **One supervisor per state.** The window that holds the state's supervisor name runs the pass: starts, resumes, restarts and alerts. A second window on the same state only watches. It reads and draws, shows "Another OpenTail.Bullshark window supervises this state. This one only watches." under Needs you, and raises no toast and no sound. When the first window closes or is killed, the second takes over on a later pass. The sessions keep running in every case.
- **Claude's folder.** Every path to a Claude file comes from one helper. With `CLAUDE_CONFIG_DIR` set, the app reads `.claude.json`, the transcripts and its archive from that folder, as Claude does (`.claude.json` tested with claude 2.1.328 on 2026-11-19). Without it, the app reads `~\.claude` and `~\.claude.json`. An archive made before the variable was set stays where it was, and is not listed.

## Privacy

The owner's standing rule for logs, as recorded in [docs/plans/owner-decisions-2026-11-22.md](docs/plans/owner-decisions-2026-11-22.md) ("Logs: exception messages and paths"): "Logs carry counts, provider names and exception types only, never a message, title, id or path. Screens may show the message (the owner's screen is not a log)."

The App and Core log lines were changed to follow the rule (commits `8fc42db` and `f6fdaf9`). The file log, `src/OpenTail.Bullshark.App/AppLog.cs`, writes each line it is given and filters none, so the rule holds only where each call follows it. One item is open: a run's failure text is kept in its stored alert, which is shown on screen and is not a log, and the owner decides whether it stays.

## Licence

The project is licensed under [PolyForm Strict 1.0.0](LICENSE.md). Personal and other noncommercial use is permitted; commercial use needs a separate licence from the licensor. The licence does not allow distributing the software or making changes or new works based on it. The code is public to be read and reviewed.

[LICENSE.md](LICENSE.md) states that OpenTail.Bullshark "is an independent project" and is "not affiliated with, endorsed by, sponsored by or approved by Microsoft, GitHub, Anthropic (Claude) or OpenAI (Codex)". It also states that "No code, schema, bundled file, icon or test fixture is taken from those tools or their packages." Its own text says that this section "does not change the terms below".

Third-party components and their licences are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Repo

Folder: the repository root. Git repo with remote `origin` = `github.com/opentail-net/OpenTail.Bullshark`, branch `main`.

Projects: `src/OpenTail.Bullshark.Core` (logic, no UI), `src/OpenTail.Bullshark.App` (WPF window), `src/OpenTail.Bullshark.Supervisor` (headless host), `tests/OpenTail.Bullshark.Core.Tests` and `tests/OpenTail.Bullshark.App.Tests`.

Build and test, as CI runs them: `dotnet build OpenTail.Bullshark.slnx -nodeReuse:false`, then `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false --no-build --filter "Category!=Live"`. Without the filter, the live tests are included, and they start the real `claude` CLI when their own environment gate is set (`.github/workflows/ci.yml`, lines 34 to 35). Visual Studio's build nodes otherwise lock new folders, so keep `-nodeReuse:false`.

## Plans

Open work is in [docs/plans/00-current-work.md](docs/plans/00-current-work.md): the path through the plans, and the owner decisions still open. The owner decisions of 2026-11-22, with their status, are in [docs/plans/owner-decisions-2026-11-22.md](docs/plans/owner-decisions-2026-11-22.md). The owner's checks are in [docs/plans/live-run-checklist.md](docs/plans/live-run-checklist.md), and the CLI probes that decide features are in [docs/plans/cli-gates.md](docs/plans/cli-gates.md). Finished plans, and the records of plans that were split, are in [docs/plans/Done/](docs/plans/Done/). Parked ideas are in [docs/later/ideas.md](docs/later/ideas.md). Findings (facts checked against the real CLI, and bugs fixed) are in [docs/findings/FINDINGS.md](docs/findings/FINDINGS.md).

