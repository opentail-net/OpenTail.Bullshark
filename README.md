# OpenTail.Bullshark

Bullshark - saves your bytes.

> **Status (2026-11-17, end of an autonomous session): built, not yet proven end to end with live sessions.** Everything planned on 2026-11-17, up to round 12, is on `main`, and on 2026-11-18 three additions that read the usage-limit entries of the transcripts: start a run when the limit resets, the status-bar limit text and a Limits history page (1038 tests; see [docs/plans/backlog-2026-11-18.md](docs/plans/backlog-2026-11-18.md)). What was built, the owner's open decisions and the gated checks are in [docs/plans/session-summary-2026-11-17.md](docs/plans/session-summary-2026-11-17.md). The end-to-end run with the real `claude` CLI (review of 2026-11-15, Phase 3) has not been done. See [Plans](#plans).

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

1. **Starts with the PC.** *Not built, and not wanted now (P4, owner decision of 2026-11-15):* the window is the one supervisor, so sessions are watched while the window is open. The sessions themselves keep running without it.
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

- **Supervisor:** a Windows Task Scheduler task, trigger "At log on", running as the owner's user account. *Expected, not verified:* a Windows Service would run under another account and would not see the owner's Claude login, so a service is the wrong choice.
- **Window:** opened by the owner. Auto-open at log-on is optional (owner's choice).
- **Log-on, not power-on.** A PC that boots without logging in does not run OpenTail.Bullshark. Auto-logon would change that (D6).
- **Blocked sessions:** the supervisor alerts once per entry into `blocked`. The window lists the session under "Needs you" at the top of the rail, shows the count in the status bar, and flashes the taskbar button with a sound when a session newly needs the owner (turn off with `"notify": false` in `ui.json`). When the window is not in front (minimised or behind other windows), a silent Windows toast also shows the session's name and reason, one per batch; turn it off with `"toast": false` in `ui.json` or "Toggle toasts" in the command palette. A toast is not seen on screen until a run is checked by the owner.
- **Row marks (2026-11-18):** a session row shows its context figure (`ctx 142k`, with ⚠ at 80 % of the window; the window is the owner's setting in Settings, 200k assumed, not read from Claude) and, when a plan file is attached in its Edit dialog, the checked items of that plan (`3/7`). Each kind of toast (blocked, problems, notices, ready for review, usage limit reset) can be switched off under Settings, General; the last two are silent toasts.
- **Viewed files (2026-11-18):** the Changes tab has a "viewed" box per changed file, plus "Mark all viewed" and "Clear". A tick is kept in `ui.json` (key `viewedFiles`: session, path and two numbers, never file text) with the file's size and write time, so a file that changes again is unviewed; a deleted file has its own stamp. Nothing is written to the folder or to git.
- **Big-deletion check (2026-11-18):** a changed file that lost most of its lines (at least 50 removed and at least three times as many removed as added) or a tracked file that is deleted gets a "check" label on the Changes tab, and a count line under its header. It is a label, not an alarm, with no sound or toast; the thresholds are the orchestrator's assumption and sit in `DeletionCheck.cs`.
- **Same-call loop (2026-11-18):** the effort panel lists a call that failed three times or more in a row, with the same tool and the same input and no success of that call in between, with its first and last line. A fail, edit, pass sequence is not listed. The input is hashed, never kept.
- **Group by tag (2026-11-18):** the rail's show menu groups the History section by folder or by tag (in memory: every start shows folders). A session with several tags is under each of its tags, the sessions with no tag are last, and Needs you and the search are unchanged.
- **Night page (2026-11-19):** Insights has a Night sub-tab, the bedtime preflight: ten checks (the window's last pass, the background daemon, the login, the setup check, a usage limit in force, the runs due in the next 12 hours, folders waiting for trust, the AC sleep timeout, what waits for you, and the quiet hours) and one verdict, "Ready for the night" or how many things to check. The checks are read when the page is shown, on Refresh, and every 30 seconds while it is open. Nothing is started or sent to a session. The window does not keep the computer awake: the Sleep row only reports the AC sleep timeout, read with `powercfg /query` (read-only, five-second timeout).
- **Heartbeat (2026-11-19):** after each supervisor pass the window writes `heartbeat.json` beside the state file (at most every 10 seconds). The Night page shows "Last pass: 12 s ago", so the owner can see in the morning whether the window ran in the night. The window is the only supervisor: a closed window or a sleeping PC means no pass.
- **Night report (2026-11-19):** when the owner returns to the window after at least 30 minutes away, and something happened, the "While you were away" digest is also saved as Markdown in `reports\` beside the state file, as `away-yyyy-MM-dd-HHmm.md` (local time), with the sessions' plans and the token totals of the period. A report is never overwritten, and the newest 60 are kept. The Away page names the saved file and has an "Open reports folder" button. Nothing is sent anywhere.
- **Quiet hours (2026-11-19, off by default):** Settings, General, Notifications: a window in local time, such as 22:00 to 07:00, that may cross midnight. In those hours a new wait or a re-alert gives no sound, flash or toast (the silent ready and usage-limit toasts too). The rail, the Needs-you list, the log, the digest and the re-alert count still run. Stored as `quietHours` in `ui.json`.

- **Look (2026-11-19):** the check boxes, text boxes, lists, combo boxes, sliders, scroll bars and tooltips are drawn from the palette in both themes (`Styles/Controls.xaml`), so every window looks the same. Resume is green like Start. The History folder names are the leaf of the folder the transcripts name (`C--Git` shows as `Git`; the full path is in the tooltip); the group is still Claude's folder name. The contrast figures are in `docs/plans/ui-polish-batch-2026-11-19.md`.

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
| D3 | Coder permission mode: `acceptEdits` with an allowlist, or `bypassPermissions`? | `acceptEdits` plus an allowlist. Bypass only in a sandbox with no internet. **Decided by the owner, 2026-11-19: coders (and planners) run in `auto`.** The code already does this (`SetupPlan.PermissionMode`, `RunPlan` default; D-S14 in `docs/plans/setup-and-sessions.md`). The first recommendation above is kept as the record. Not yet checked live: whether `--bg` sessions accept `--permission-mode auto` as documented (the desktop research could not confirm it, `ideas-desktop-codex-2026-11-18.md` A3). |
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

## Repo

Folder: `C:\Git\split\OpenTail.Bullshark` (next to the other `OpenTail.*` projects). Git repo with remote `origin` = `github.com/opentail-net/OpenTail.Bullshark`, branch `main`.

Projects: `src/OpenTail.Bullshark.Core` (logic, no UI), `src/OpenTail.Bullshark.App` (WPF window), `src/OpenTail.Bullshark.Supervisor` (headless host), `tests/OpenTail.Bullshark.Core.Tests`. Build and test: `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` (Visual Studio's build nodes otherwise lock new folders).

## Plans

Plans live in `docs/plans/`, as checkbox lists; `DONE_` in a name means finished. Findings (facts checked against the real CLI, and bugs fixed) are in `docs/findings/FINDINGS.md`.

| Plan | State (2026-11-17) |
|---|---|
| `review-2026-11-17.md` | Done, on `main` (6 items: trust file read once, pass off the UI thread, one agent listing per pass, launch length guard, testable clock, file lock fixes). |
| `additions-2026-11-17.md` | Phase A items 1-2 done (Needs you, Changes tab). Item 3 (worktree per coder) skipped at its gate: `claude --bg` refuses an untrusted folder (F62). Phase B items 4 and 5 (limits per run, run timeline) and item 6's listing argument built; item 6's use of `--cwd` waits on the gate (D6.2). Phase C item 7 (export) built. On branch `gems-2026-11-17`, merged to `main` and pushed. |
| `bugs-2026-11-17.md` | Done, on `main` (finished/stopped sessions no longer resumed, restart-loop alert once, one bad transcript skipped, one shared rule for opening transcripts). |
| `bugs-round3-2026-11-17.md` | All 16 items built on branch `bugs-round3-2026-11-17`, one local commit each; merged to `main` and pushed. Open: the open points at the end of the plan's decisions (CLAUDE_CONFIG_DIR, `VAR=1 cmd` hooks, `--projects` flag, the other catch lists, `File.Exists` in RunStore and StateStore). |
| `round4-2026-11-17.md` | All 5 items built on branch `round4-2026-11-17`, one commit per item; merged to `main` and pushed. A denied state or run file is never read as empty or replaced; the expected store failures share one filter; the setup check reads `VAR=1` and shell built-ins; `--projects` needs a folder. The new screens were seen on scratch fixtures (`scripts/Test-NewScreens.ps1`), and the unreadable Timeline and effort-header text was fixed. Open: the rail's Trust row is cut at its edge, the light tab labels look pale, and the effort panel's contents are not in a screenshot. |
| `round5-2026-11-17.md` | All 18 items built on branch `round5-2026-11-17`, one commit per item; merged to `main` and pushed. A stopped session is not resumed after `claude rm`; a run's duration counts from its start time, and its limit notice comes once; the archive waits for the session list and skips transcripts written in the last 24 hours; overlap and conflict notices are only for live sessions and are announced once; the timeline draws the newest 2,000 rows with dates and run ids; launch length counts escaped quotes; a start time keeps its wall-clock time across daylight saving; a save keeps the file's line endings; an unreadable trust or instructions file is an error; a failed rotation does not stop the events. Screens checked on scratch fixtures (`scripts/Test-NewScreens.ps1`, 11 screens). 760 tests. |
| `round6-2026-11-17.md` | All 11 items built on branch `round6-2026-11-17`, one commit per item; merged to `main` and pushed. The limit time of a run counts from its first session start; a revived stopped session that is later lost is resumed; a limit notice no longer replaces a restart-loop or stop alert, clears when the limit goes, and shows only for a live session; an overlap notice is announced once, even after a listing gap; a transcript rewritten in place is read whole; a last record with no newline counts; the Storage archive waits for the first listing; the effort panel shows the file name. P4 (log-on supervisor) is marked not wanted now. 782 tests. |
| `round7-cli-features-2026-11-17.md` | Items 1-5 built on branch `round7-2026-11-17`, one commit per item; merged to `main` and pushed. While the background daemon is not running (its normal idle state), a listed working or blocked session is shown as stale and blocks nothing, and starts and resumes go ahead; the Needs you group says how many listed sessions are not alive; a daemon on another claude version gets a confirmed Respawn all; the listing's `waitingFor` shows in the Needs-you row and the tab tooltip; the Changes tab, merge check and overlap use the folder the session really edits; the Storage page says how long Claude keeps transcripts. Items 6 (`--session-id` with `--bg`) and 7 (ideas) are recorded, not built. 860 tests. |
| `round8-2026-11-17.md` | Items 1-11 built on branch `round8-2026-11-17`, one commit per item; merged to `main` and pushed. While the daemon is not running, a stale working session is resumed as lost, and a stale blocked one is alerted once, listed under Needs you and not resumed; a blocked, stale, blocked sequence sounds once. Starts and resumes wait 20 seconds after Respawn all. An adopted session counts its run's time from its own start. The Needs you header counts its rows. A folder must exist to be read, and a merge problem is checked again. A daemon version counts only while the daemon runs. A relative write is resolved against its own folder. An object-valued `waitingFor` or `status` shows its text. The effort panel fits at most 200 paths, after the width settles. The Storage note names `~/.claude/settings.json` and its marks refresh. Open for the owner: closing a session drops it from its run's budget (B4.3); a stale blocked session makes no sound when the daemon stops (B2.2). 908 tests. |
| `round9-2026-11-17.md` | Items 1-8 built on branch `round9-2026-11-17`, one commit per item; merged to `main` and pushed. A closed session's start and output tokens stay on its run, so the run's clock and spend do not drop (B4.3). A respawn holds the rest of a pass that began before it, and a stale-blocked alert; the stale-blocked hold lasts an hour from its alert. A listed row is stale only when the daemon reads not running before and after the listings, and only a running daemon clears a stale-blocked alert. The respawn hold uses the monotonic clock. Closing a stale session runs no `claude stop`. An object-valued listing field shows text only under a known key. 937 tests (908 at the start). The screen script wrote 15 of 15 screens. Open for the owner: the Respawn click does not wait for a pass already running; a listed stopped or failed session is still stopped on close. |
| `round10-performance-2026-11-17.md` | Items 1, 2 (four fixes) and 4 built on branch `round10-perf`, one commit per fix; merged to `main` and pushed. On the owner's folder (163 transcripts, 862 MB): the history index takes 3.9 s to 1.6 s with no cache and 0.18 s with the cache; the history path 1.8 s to 0.26 s warm; the Windows-path search 2.0 s to 0.4 s; the common-word search 0.95 s to 0.5 s warm. The index and the searches give the same results (checked by tests and by a hash on the folder). Open: the first read with no cache is still about 1.6 s; item 3 is partly done, because the window was not run (its startup pass calls the `claude` CLI, which the round does not allow). 960 tests (937 at the start). |
| `round11-2026-11-17.md` | Items 1-6 built on branch `round11-2026-11-17`, one commit per item; merged to `main` and pushed. Item 1: the cache key is a fresh stat, and a file written in the last 2 minutes is read again. Item 2: cache load and save never fail the history load, and failures are logged. Item 3: save runs under `FileLock` with an atomic write, and the cache's stale temporary files are swept on load. Item 4: search and index use the processor count minus one (at least one), search is cancellable, and batches are sized to the matches still needed. Item 5: the cache carries `Records`, and a test fails when a summary or digest field is not carried. 980 tests. |
| `round12-2026-11-17.md` | Items A1-A6 (Core) and B1-B8 (App) built on branches `round12-core` and `round12-app`, one commit per item; merged to `main` and pushed. A transcript line or file that cannot be read is skipped and the rest loads, in every reader and in the window's pass (the owner's decision). One shared line helper replaces nine copies. The merge check and the Changes tab read the writes cache; the pass hands the rail the runs it read. CLI output reads are bounded; the supervisor reads logs for live sessions only, one login alert an hour. The log rotates at 5 MB and never throws. The index parses UTF-8 bytes: cold read 1.69 s to 1.35 s, peak 206 MB to 122 MB, same output. A6(a) (drop ledgers) tried, not kept. 1007 tests. |
| `gems-2026-11-17.md` | Items 1, 2, 4, 5 and 6 built on branch `gems-2026-11-17`: file-overlap and merge-conflict notices, the effort panel, runs with a start time, the storage page and archive. Item 3 (live OpenTelemetry data) waits for the owner's gate. Merged to `main` and pushed. |
| `review-2026-11-15.md` | Phase 1 done; Phase 3 (end-to-end run) open. |
| Others (`setup-*`, `session-library`, `tabs-and-panes`, `transcript-activity`, `zoom`) | Status line at the top of each file. |
