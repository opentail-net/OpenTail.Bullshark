# Additions, 2026-11-17 (done items)

> Split from [additions-2026-11-17.md](../additions-2026-11-17.md) on 2026-11-21. These items are checked in the original; the open items stay there. The headings above each item are its place in that plan.

## Phase A: leave it running unattended
### 1. Tell the owner when a session waits
Now: a `blocked` session raises `AlertKind.Blocked`, which goes to the log and the record's `LastAlert`; nothing draws attention.

- [x] Core: `AttentionList.From(records, agents)` returns the sessions that need the owner (state `blocked`, or `RestartLoop`/`StartFailed` alert), oldest first. Unit tests.
- [x] Window: when the list gains a session since the last pass, flash the taskbar button (`FlashWindowEx` P/Invoke, until the window is focused) and play the system notification sound once.
- [x] Rail: a "Needs you" group at the top with those sessions; click selects the session as today.
- [x] Status bar shows the count. `ui.json` setting `notify: true|false` (default true).
- [x] Owner decision (record, do not build): Windows toast notifications need a package (CommunityToolkit notifications or Windows App SDK). Recorded as D1.9.

### 2. A "Changes" tab per session
- [x] Core: `GitChanges.Read(folder)` runs `git -C <folder> status --porcelain=v1` and `git -C <folder> diff --stat` and `git diff` (read-only, 10 s timeout, through a small `IGitRunner` like `ICliRunner` so tests use a fake). Not a repository → a plain message, not an error. Diff capped at 2 MB with a "truncated" line. Built as `--numstat -z` and `status -z`, see D2.1 and D2.2.
- [x] Folder = the session's `cwd` from the listing, else its spec's `WorkingDirectory`.
- [x] Window: a new tab kind `Changes` opened from the session's rail context menu: file list with +/- counts, the diff below in the terminal font, colours from `TerminalColours`. A "Files written" list from the transcript digest's `FilesWritten` beside it.
- [x] Refresh button; no automatic refresh.
- [x] Tests: porcelain and `--stat` parsing (renames, spaces in paths, binary files), not-a-repo, truncation.

## Phase B: control and hindsight
### 4. Limits per run
- [x] `RunRecord` gets optional `RunLimits? Limits` (`long? MaxOutputTokens`, `TimeSpan? MaxDuration`); setup page fields, empty = no limit.
- [x] Core: `RunSpend.Of(run, digests)` sums `TokenUsage` of the run's sessions (by session id) and time since `CreatedAt`. Unit tests. (Built as `Of(run, records, writes, now)`, see D4.1.)
- [x] Policy: at 80 % → alert `LimitNear` once; at 100 % → alert `LimitReached` and no more resumes or starts for that run (a running session is not stopped; the owner stops it). New `AlertKind` values; existing alerts unchanged. (The notices reach the log and the record, see D4.4.)
- [x] Tests: old runs.json without `Limits` loads; thresholds fire once per entry.

### 5. Run timeline
- [x] Core: `EventLog` appends one JSON line per supervisor action and state change to `events.jsonl` beside state.json (under `FileLock`, append only; rotate at 5 MB to `events.1.jsonl`). `SupervisorRunner` writes through an optional `IEventSink`, so existing constructors and tests do not change.
- [x] Window: a "Timeline" tab per run: time, session, event, detail; filter by session. (Built as one tab with a run chooser, see D5.1.)
- [x] Tests: append under lock, rotation, reading a partly written last line.

### 6. Narrow listings with `--cwd`
- [x] `ClaudeCli.ListAgentsAsync(string? cwd = null)` adds `--cwd <path>` when given. (The folder is required, with no default, see D6.1.) Keep the no-argument call for the full list (the window needs all sessions for the rail and name checks).

- [x] Tests: argument lists.

## Phase C
### 7. Export a past session
- [x] Transcript tab: "Export…" writes Markdown (title, date, folder, model; then turns as `## You` / `## Claude`, tool calls as one-line summaries, no thinking blocks) and, if a resume summary exists, the summary first. Reuse the existing transcript line reading; no new parser.
- [x] Tests on a small sample transcript.

## Decisions made while building
### Item 1 (Needs you)

- D1.1 "Oldest first" is the session's own start time (`SessionRecord.StartedAt`), then the name. The state file does not record when a session entered `blocked`, so it cannot be sorted by that.
- D1.2 The loss reasons (restart loop, failed resume) are read from `LastAlert`, which the supervisor writes as `RestartLoop: ...` or `resume failed: ...`. They count only while the session is not listed: a listed session is not waiting because of an old alert.
- D1.3 `LastAlert` is cleared when a resume succeeds (`SupervisorRunner.Carry`, the resume case). Without that, the alert of a loop that had ended stays in the record, and the next time the session went missing it would be announced again, although the supervisor resumes it at once. The rail reads `LastAlert` only for records with no session id, so nothing it shows changes.
- D1.4 A start that failed has no terminal. Its row under Needs you is muted and not selectable, with its reason, as under its folder. It leaves the list when the owner presses Retry failed starts.
- D1.5 The Needs you group shows with the All and Running filters only, as the live sessions do. A waiting session also stays under its folder, so it appears twice. Selection goes to the first match in the tree, which is the Needs you row, while the session waits.
- D1.6 The count is its own element at the left of the status bar, shown while at least one session waits. It is not part of the pass summary, so the summary's text changes do not hide it.
- D1.7 The announcement fires once per session, when it joins the list. Sound: `SystemSounds.Asterisk`. Flash: `FlashWindowEx` with caption and taskbar, until the window is in front. Both are off with `"notify": false` in ui.json, read at the time of the announcement. No settings control was added; the owner edits ui.json.
- D1.8 The first pass after the window opens sets the baseline and announces nothing: the sessions already waiting are in view then.
- D1.9 Announcements also fire while the window has focus (the flash then stops at once). Owner decision, not built: Windows toast notifications need a package (CommunityToolkit notifications or Windows App SDK). Until the owner decides, the flash and the sound are the signal.
- Verified: 16 unit tests in `tests/OpenTail.Bullshark.Core.Tests/Supervisor/AttentionTests.cs`; the whole solution builds with the existing warning in `MainWindow.xaml.cs` only. The WPF side (the group, the count, the flash, the sound) compiles but was not run in a window in this session.

### Item 2 (Changes tab)

- D2.1 The counts come from `git diff --numstat -z`, not `--stat`. The `--stat` text is laid out for people: it draws bars, prints renames as `{a => b}`, and quotes names. The `-z` numstat gives exact fields for names with spaces, tabs and renames. So the "`--stat` parsing" tests cover numstat.
- D2.2 `git status --porcelain=v1 -z` for the list. With `-z` a name with spaces, quotes or non-ASCII parses exactly, and a rename carries the old path in the next field. Checked on git 2.53 in a scratch repository, 2026-11-17.
- D2.3 The git process is read-only in practice: `GIT_OPTIONAL_LOCKS=0` (status does not rewrite the index the session may be using at the same moment), `GIT_TERMINAL_PROMPT=0` (no credential prompt can hang it), and `--no-color --no-ext-diff` (the owner's git config cannot add colour codes or run an external diff tool).
- D2.4 The list and the diff are the unstaged changes (`git diff`, as the plan says). A new untracked file is listed as "new, not in the diff" with no counts and no text. A file staged only is listed with no counts. Nothing was changed for staged work; it is a limit of this view.
- D2.5 Each command is read up to 2 MB and the process is stopped at the limit (it is not read to its end and then cut). A list cut at the limit drops its last partial entry and says so. A diff cut at the limit ends with a line `[truncated: the diff is longer than 2 MB, so the rest is not shown]`.
- D2.6 Each command has 10 seconds. Past that the folder shows a message that names the command and the limit, not an error dialog.
- D2.7 The "Files written" list reads the whole transcript (`TranscriptReader.ReadFile`) each time the tab opens and on Refresh, on a pool thread. It is not cached, so a session that has written since then shows its new files at the next Refresh.
- D2.8 A session with no known folder (not in the listing and no spec) gets a message and no read. A folder that does not exist is a problem message.
- D2.9 The Changes tab opens from the right-click menu of a live session row only. A past session has no folder to read, so it has no entry.
- D2.10 Not verified in a window: a 2 MB diff is one rich-text paragraph with one run per line. Its drawing speed was not measured in the running app.
- Verified: 27 Core tests. `GitChangesTests` (24) use a fake git and cover the parsing, the merge, not-a-repository, timeouts, the cap, the folder choice. `ProcessGitRunnerTests` (3) run the real `git` on scratch repositories, including a diff longer than the limit. They return at once on a machine without `git` on the PATH, so there they check nothing.


### Item 3 (one worktree per coder): skipped at the gate
### Item 4 (limits per run)

- D4.1 `RunSpend.Of(run, records, writes, now)`, not `(run, digests)`. A run record holds no session ids, so the state records map each spec name to its session, as the gems overlap check does. The writes come from the same transcript read as the overlap check, and the usage rides on `SessionWrites.Usage`, so no transcript is read twice.
- D4.2 The token limit is output tokens (`MaxOutputTokens`, as the plan names it). A run's budget is the larger of its token fraction and its time fraction. A field left blank is no limit.
- D4.3 The budgets are worked out after each pass and given to the runner (`SupervisorRunner.Budgets`), so a limit takes effect on the next pass, up to 15 seconds later. A session resumed in the pass that crosses a limit runs until the next pass.
- D4.4 The notices (`LimitNear`, `LimitReached`) are written to the log and to the session record's last alert, once per level while the last alert still says so. They are not shown under "Needs you", because the attention list reads only the restart, resume, stopped and failed alerts. Owner decision: whether limit notices should join "Needs you".
- D4.5 At the limit, a session that is not listed is not resumed, and a run with no record yet is not started. A listed session that is running is not stopped.
- D4.6 The setup page has two text boxes, output tokens and minutes. Blank is no limit. A value that is not a positive whole number stops Start, with the reason in the status line, before anything is written.
- D4.7 Verified: 9 new Core tests (`RunLimitTests`), 555 passing in all, and the solution builds with App. Not seen in a running window: the fields, and the notices in the log.

### Item 5 (run timeline)

- D5.1 One Timeline tab with a run chooser (newest run first) and a session chooser, instead of one tab per run. Tabs for every run would crowd the tab bar, and the chooser gives the same view for any run.
- D5.2 The events are "start", "adopt", "resume", "state", and "alert:" followed by the alert kind. An action is written when it is performed; a failed start is written as its `StartFailed` alert. A state change is written on the pass after it, because the state is compared with the last pass (the change is seen up to 15 seconds late).
- D5.3 A write that fails (the disk, or the lock) is logged, and the pass goes on. The log must never stop supervision.
- D5.4 Rotation keeps one older file: at 5 MB, `events.jsonl` moves to `events.1.jsonl`, replacing the one there. The plan names one rotated file but not how many, so the log holds at most about 10 MB, and the events of older rotations are dropped. Owner decision if more history is wanted.
- D5.5 A line cut off at the end of a file that is still being written is skipped when reading, and the complete lines before it are kept.
- D5.6 Only supervisor actions and state changes are events. The window's own actions (Start now, archive, restore) are not written here.
- D5.7 Not verified in a window: the tab and its chooser. The sink's behaviour is tested on the file, and the runner's write to the sink with a fake CLI (5 new Core tests, `EventLogTests`; 560 passing in all; the solution builds with App).

### Item 6 (narrow listings with `--cwd`)

- D6.1 The folder is a required parameter: `ListAgentsAsync(string? cwd, bool includeCompleted, ct)`. A default value would make the existing call `ListAgentsAsync()` ambiguous with the overload that takes a cancellation token, so the call sites stay as they were. A null or blank folder gives the full listing. The existing `bool` overload now calls the new one with null, and its command is unchanged (a test checks the argument lists).
- D6.2 Used nowhere, as the brief for this build says. The second box stays open: the plan requires the gate of item 3 first, and that gate was skipped (D3.1). So it is not known whether `--cwd` returns a worktree session that was started from a folder. The option itself is confirmed in `claude agents --help` (a read-only command).
- D6.3 Verified: 4 new Core tests (`ListAgentsCwdTests`), 564 passing in all.

### Item 7 (export a past session)

- D7.1 "If a resume summary exists" is read as Claude's own recap in the transcript (`LastRecap`, from its away-summary records), not the saved resume summary files. Those files are named by date, level and session name, and the window has no lookup from a session to its file. Owner decision, if the saved summary should be exported too.
- D7.2 The export is built from `TranscriptLine`, the reader the transcript tab shows, so it lists the same lines as on screen. Tool results and turn-finished lines are left out, since they are long and add nothing a reader needs. Thinking blocks never reach that reader, so they are not exported.
- D7.3 "Folder" is the project folder name as Claude encodes it (the name of the folder under `~/.claude/projects`). The transcript does not give the working folder in a form the window reads back.
- D7.4 The Export button sits in the transcript header. It opens the standard save dialog, which suggests the first eight characters of the session id as the file name. A file that cannot be written gets a message box.
- D7.5 Not verified in a window: the button and the saved file. Verified: 5 new Core tests (`TranscriptExportTests`), 569 passing in all, and the solution builds with App.
