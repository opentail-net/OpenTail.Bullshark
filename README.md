# OpenTail.Bullshark

Bullshark - saves your bytes.

> **Status (2026-11-17): built, not yet proven end to end.** The window and the supervisor are built, and the spike (P1) passed. The review fixes of 2026-11-17 are on `main`, and so are the "Needs you" alerts and the Changes tab (additions Phase A). The bug fixes of 2026-11-17 are on `main` too: the supervisor no longer resumes sessions that finished or were stopped (F61). The end-to-end run with the real `claude` CLI (review of 2026-11-15, Phase 3) has not been done. The orchestrator (P8) is a later, optional phase. See [Plans](#plans).

## What it is

OpenTail.Bullshark is a session manager for Claude Code, with a window that shows each session as a tab. It keeps a **planner** session and a **coder** session running, gives each one its first direction, and restarts them if they stop. The supervisor starts when the PC logs on.

Sessions are long-lived. A session is not closed after a task, so its context carries on from one day to the next.

**The goal, in the owner's words:** "I do vibe coding, I need to allow vibe coding to carry on." The owner should be able to walk away, come back, and continue the conversation with the planner where it stopped, without rebuilding context or re-explaining the project.

**The owner's requirement:** "the full experience". Every session is the real interactive Claude Code terminal. Redirected output puts Claude Code into non-interactive mode and cripples the project, so OpenTail.Bullshark does not use redirected or print-mode output for any session the owner sees.

## Where this came from

On 2026-11-12/13 a planner session directed a second local Claude session (a Haiku builder, `git-27`) through briefs sent with SendMessage. It worked (see `project-host-a2a-and-builder-loop` in memory). The owner found the agent-to-agent messaging too clunky. OpenTail.Bullshark keeps the two-session pattern and drops the messaging.

## Design principles

1. **Full experience.** Every session runs in a real pseudo-console, so keys, colours, resizing, mouse, paste and permission prompts work as in a normal terminal.
2. **Sessions outlive the window.** Closing OpenTail.Bullshark, or a tab, does not stop a session.
3. **Owner first.** The owner can type into any session at any time. OpenTail.Bullshark does not type into a tab the owner has used recently (D5).
4. **Live messages are the handoff.** The planner and the coder message each other directly with `SendMessage`. No file passes messages between sessions (see Handoff).
5. **Code before models.** Timers, restarts, state checks and alerts are plain code. A model is used only where reading is needed, and only later (P8).

## Architecture

Two parts, plus a later optional third:

- **Supervisor** (headless, starts at log-on). It starts the planner and the coder as background sessions (`claude --bg` with `--name`; `--bg` ignores `--session-id`, so the id is read back from the listing, F18), keeps their IDs in a state file, and polls `claude agents --json`. The window runs the same pass every 15 seconds while it is open, off the UI thread. If a session has gone, it restarts it. If a session is `blocked`, it raises an alert. *Built so far:* the headless host (`src/OpenTail.Bullshark.Supervisor`) is report-only for now: it prints the usage and cache reports and starts nothing. The loop runs inside the window, and the log-on start (P4) is not built.
- **Window** (opened by the owner). One tab per session. Each tab is an embedded terminal running `claude attach <id>`. Closing a tab detaches; the session keeps running.
- **Orchestrator** (later, optional, P8). A local model that handles situations the supervisor cannot, such as recovery when a restart is not enough. See its own section.

The sessions run whether or not the window is open. The window is a view onto them, and the owner can type into any tab.

## Requirements

1. **Starts with the PC.** The supervisor starts at log-on and keeps watch over both sessions.
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
- **Permission mode:** both sessions must run in the same mode, or a message waits for approval (F22). Default: `auto` for both (D3).

- **The plan:** `docs/bullshark/PLAN.md`, a checkbox list (the owner's plan format). The planner writes each batch as a block with fixed decisions, STOP conditions and a capped report format.
- **The report:** the coder ticks the boxes and writes a short report under the batch.
- **The fallback:** if a live message cannot be delivered, OpenTail.Bullshark types one line into the tab, only when the owner has not used it recently (D5). It is typed input to the live session, not a file.
- **Parallel work:** both sessions share one repo. Two coders on the same files need separate worktrees (`--worktree`). Default: one planner, one coder.

## Startup

- **Supervisor:** a Windows Task Scheduler task, trigger "At log on", running as the owner's user account. *Expected, not verified:* a Windows Service would run under another account and would not see the owner's Claude login, so a service is the wrong choice.
- **Window:** opened by the owner. Auto-open at log-on is optional (owner's choice).
- **Log-on, not power-on.** A PC that boots without logging in does not run OpenTail.Bullshark. Auto-logon would change that (D6).
- **Blocked sessions:** the supervisor alerts once per entry into `blocked`. The window lists the session under "Needs you" at the top of the rail, shows the count in the status bar, and flashes the taskbar button with a sound when a session newly needs the owner (turn off with `"notify": false` in `ui.json`). Windows toast notifications are not built (they need a package; owner decision).

## Visibility

Decided by the owner (2026-11-14): the session terminals appear as tabs inside OpenTail.Bullshark. That is the primary view.

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
- Default permission mode for the coder: open (D3).
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
| D3 | Coder permission mode: `acceptEdits` with an allowlist, or `bypassPermissions`? | `acceptEdits` plus an allowlist. Bypass only in a sandbox with no internet. |
| D4 | Budget: own spend check (the `--print` cap is ruled out). | Own spend check, in P7. |
| D5 | How long must a tab be idle before OpenTail.Bullshark may type into it? | Ten minutes, set by the owner. Check the typing behaviour in P1. |
| D6 | Startup: log-on only, or also auto-logon? | Log-on only. Auto-logon is a security trade-off for the owner to decide. |
| D7 | Window stack | C# WPF with WPF-UI.Terminal, to be proven in P1. Fallback: Windows Terminal tabs. Intelligent Terminal not chosen. |
| D8 | Orchestrator (later): which model, and when to start? | Not now. Start P8 only after P1-P7 work. Stingray with a Qwen3-class model, chosen by testing. |
| D9 | Orchestrator authority: which actions may it take without asking? | A fixed list only (continue, run tests, wait). Everything else is an alert to the owner. |
| D10 | How do alerts reach the owner when AFK? | Partly done (2026-11-17): in the window, a "Needs you" rail group, taskbar flash and sound. Still open for AFK: a Windows toast (needs a package), or the Teams path in OpenTail.Orca (already used for approval cards; check its limits). |

## Repo

Folder: `C:\Git\split\OpenTail.Bullshark` (next to the other `OpenTail.*` projects). Git repo with remote `origin` = `github.com/opentail-net/OpenTail.Bullshark`, branch `main`.

Projects: `src/OpenTail.Bullshark.Core` (logic, no UI), `src/OpenTail.Bullshark.App` (WPF window), `src/OpenTail.Bullshark.Supervisor` (headless host), `tests/OpenTail.Bullshark.Core.Tests`. Build and test: `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` (Visual Studio's build nodes otherwise lock new folders).

## Plans

Plans live in `docs/plans/`, as checkbox lists; `DONE_` in a name means finished. Findings (facts checked against the real CLI, and bugs fixed) are in `docs/findings/FINDINGS.md`.

| Plan | State (2026-11-17) |
|---|---|
| `review-2026-11-17.md` | Done, on `main` (6 items: trust file read once, pass off the UI thread, one agent listing per pass, launch length guard, testable clock, file lock fixes). |
| `additions-2026-11-17.md` | Phase A items 1-2 done (Needs you, Changes tab). Item 3 (worktree per coder) skipped at its gate: `claude --bg` refuses an untrusted folder (F62). Phase B items 4 and 5 (limits per run, run timeline) and item 6's listing argument built; item 6's use of `--cwd` waits on the gate (D6.2). Phase C item 7 (export) built. On branch `gems-2026-11-17`, not pushed or merged. |
| `bugs-2026-11-17.md` | Done, on `main` (finished/stopped sessions no longer resumed, restart-loop alert once, one bad transcript skipped, one shared rule for opening transcripts). |
| `bugs-round3-2026-11-17.md` | All 16 items built on branch `bugs-round3-2026-11-17`, one local commit each; not pushed or merged. Open: the open points at the end of the plan's decisions (CLAUDE_CONFIG_DIR, `VAR=1 cmd` hooks, `--projects` flag, the other catch lists, `File.Exists` in RunStore and StateStore). |
| `round4-2026-11-17.md` | All 5 items built on branch `round4-2026-11-17`, one commit per item; not pushed or merged. A denied state or run file is never read as empty or replaced; the expected store failures share one filter; the setup check reads `VAR=1` and shell built-ins; `--projects` needs a folder. The new screens were seen on scratch fixtures (`scripts/Test-NewScreens.ps1`), and the unreadable Timeline and effort-header text was fixed. Open: the rail's Trust row is cut at its edge, the light tab labels look pale, and the effort panel's contents are not in a screenshot. |
| `round5-2026-11-17.md` | All 18 items built on branch `round5-2026-11-17`, one commit per item; not pushed or merged. A stopped session is not resumed after `claude rm`; a run's duration counts from its start time, and its limit notice comes once; the archive waits for the session list and skips transcripts written in the last 24 hours; overlap and conflict notices are only for live sessions and are announced once; the timeline draws the newest 2,000 rows with dates and run ids; launch length counts escaped quotes; a start time keeps its wall-clock time across daylight saving; a save keeps the file's line endings; an unreadable trust or instructions file is an error; a failed rotation does not stop the events. Screens checked on scratch fixtures (`scripts/Test-NewScreens.ps1`, 11 screens). 760 tests. |
| `round6-2026-11-17.md` | All 11 items built on branch `round6-2026-11-17`, one commit per item; not pushed or merged. The limit time of a run counts from its first session start; a revived stopped session that is later lost is resumed; a limit notice no longer replaces a restart-loop or stop alert, clears when the limit goes, and shows only for a live session; an overlap notice is announced once, even after a listing gap; a transcript rewritten in place is read whole; a last record with no newline counts; the Storage archive waits for the first listing; the effort panel shows the file name. P4 (log-on supervisor) is marked not wanted now. 782 tests. |
| `round7-cli-features-2026-11-17.md` | Items 1-5 built on branch `round7-2026-11-17`, one commit per item; not pushed or merged. A dead background daemon (not running, or control unreachable) makes a supervisor pass decide nothing, and the Needs you group says so; a daemon on another claude version gets a confirmed Respawn all; the listing's `waitingFor` shows in the Needs-you row and the tab tooltip; the Changes tab, merge check and overlap use the folder the session really edits; the Storage page says how long Claude keeps transcripts. Items 6 (`--session-id` with `--bg`) and 7 (ideas) are recorded, not built. 857 tests. |
| `gems-2026-11-17.md` | Items 1, 2, 4, 5 and 6 built on branch `gems-2026-11-17`: file-overlap and merge-conflict notices, the effort panel, runs with a start time, the storage page and archive. Item 3 (live OpenTelemetry data) waits for the owner's gate. Not pushed or merged. |
| `review-2026-11-15.md` | Phase 1 done; Phase 3 (end-to-end run) open. |
| Others (`setup-*`, `session-library`, `tabs-and-panes`, `transcript-activity`, `zoom`) | Status line at the top of each file. |
