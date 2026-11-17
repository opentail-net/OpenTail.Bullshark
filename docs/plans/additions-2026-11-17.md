# Additions, 2026-11-17

Source: research of 2026-11-17 into similar tools (Agent View, Claude Code desktop, Claude Squad, ccmanager, ChatML, AgentsRoom) and
current `claude` CLI features. Agent View and the desktop app now cover plain "see all sessions". Bullshark's edge is unattended
supervision, planner/coder runs, history/search/resume and cache facts on Windows. These additions build on that edge.

Build after `review-2026-11-17.md` is done; items 2 and 3 of that plan change the same pass code (`PassAndRefreshAsync`,
`SupervisorRunner`). Same branch rules apply: worktree `C:\Git\split\OpenTail.Bullshark-review`, tests with every item,
`dotnet test OpenTail.Bullshark.slnx -nodeReuse:false`, one commit per item, no push, no new NuGet packages unless an item says so.

## Facts checked (installed CLI 2.1.326, `claude --help`, 2026-11-17)

| Fact | Status |
|---|---|
| `claude agents --json` lists active sessions; `--all` adds completed ones; `--cwd <path>` limits to sessions started under a path | Verified in `claude agents --help` |
| A waiting session shows as state `blocked` in the listing (Bullshark findings F19, F21) | Verified in code (`SupervisorPolicy`, `AlertKind.Blocked`) |
| `-w, --worktree [name]` creates a git worktree for a session | Verified in `claude --help` |
| `claude rm <id>` deletes the session's worktree only if safe; refuses with unpushed commits, then accepts `--discard-unpushed <commit>@<worktree-id>` or `--force-remove-worktree <worktree-id>` from its own report | Verified in `claude rm --help` |
| Whether `--bg` already puts a session in a worktree by default (one blog says Agent View does), whether `-w` combines with `--bg`, and whether `agents --json` reports the worktree path | **Not verified: gate of item 3** |
| `claude --bg --exec <cmd>` (shell jobs) | **Not in 2.1.326 help**: dropped, see end |

## Contracts this must satisfy

| Member | What it promises / what callers rely on |
|---|---|
| `AgentInfo` / `AgentInfo.ParseList` | Fields id, sessionId, name, kind, state, status, cwd, pid, startedAt. `EffectiveState` = state ?? status ?? "unknown". Unknown JSON fields are ignored, so new fields are additive. |
| `SupervisorPolicy.Decide` | Alerts `Blocked` once per entry into `blocked` (uses `SessionRecord.LastState`), never answers on the owner's behalf. `RestartLoop` after `SupervisorRules.MaxRestartsPerHour` (3). |
| `SupervisorRunner.CheckOnceAsync` | Records keyed by session name; saves only changed records under the file lock; failed start/resume becomes an alert, not an exception. |
| `SessionSpec` | Role, Name, WorkingDirectory, Model, Effort, Brief, PermissionMode, ResumePrompt. Serialized inside `RunRecord` in runs.json. |
| `RunRecord` (runs.json) | RunId, PatternKey, WorkingDirectory, PermissionMode, CreatedAt, Specs. Old files must still load: any new field is optional with a default. |
| `SessionRecord` (state.json) | Role, SessionId, Name, StartedAt, Restarts, RestartTimes, LastState, LastAlert. Same rule for new fields. |
| `ClaudeCli.StopAsync` / `RemoveAsync` | Take the short id, not the name (F14). `requireSuccess: false`: callers read the result. |
| `TranscriptIndex` digests | Carry `FilesWritten`; `TokenUsage(Input, Output, CacheCreation, CacheRead)` per message, totals in `IndexTotals`. |
| Window process | The supervisor pass runs inside the window process (15 s timer). With the window closed, nothing is supervised or notified. |

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

### 3. One worktree per coder (gated)
- [ ] **Gate, do first:** start one throwaway background session in a scratch git repo with `--bg` alone, then with `--bg -w <name>`; record in this file: where it runs, what `agents --json` shows (cwd, any worktree field), what `claude rm` does. Then `claude rm` both and delete the scratch repo. If `-w` does not work with `--bg`, stop this item and record why. **Result 2026-11-17: not run; `--bg` refused the scratch folder as an untrusted workspace. Item 3 skipped, see D3.1.**
- [ ] `SessionSpec` gets optional `bool Worktree = false`; the setup page offers "own worktree per coder" for runs with more than one coder (planner stays in the main folder).
- [ ] `BackgroundLaunch` gets `string? Worktree`; `StartBackgroundAsync` adds `-w <name>` (name = session name). Resume never passes `-w` (the session already has its worktree).
- [ ] Ending a run: offer "Remove session" which calls `claude rm`; if it reports unpushed work, show its report and offer the `--discard-unpushed` value it printed. Never pass `--force-remove-worktree` without the owner's click.
- [ ] Item 2 must show the worktree's changes (uses `cwd` from the listing, so this follows if the gate shows `cwd` is the worktree).
- [ ] Tests: argument lists with and without worktree; resume never gets `-w`; `rm` report parsing from captured samples.

## Phase B: control and hindsight

### 4. Limits per run
- [ ] `RunRecord` gets optional `RunLimits? Limits` (`long? MaxOutputTokens`, `TimeSpan? MaxDuration`); setup page fields, empty = no limit.
- [ ] Core: `RunSpend.Of(run, digests)` sums `TokenUsage` of the run's sessions (by session id) and time since `CreatedAt`. Unit tests.
- [ ] Policy: at 80 % → alert `LimitNear` once; at 100 % → alert `LimitReached` and no more resumes or starts for that run (a running session is not stopped; the owner stops it). New `AlertKind` values; existing alerts unchanged.
- [ ] Tests: old runs.json without `Limits` loads; thresholds fire once per entry.

### 5. Run timeline
- [ ] Core: `EventLog` appends one JSON line per supervisor action and state change to `events.jsonl` beside state.json (under `FileLock`, append only; rotate at 5 MB to `events.1.jsonl`). `SupervisorRunner` writes through an optional `IEventSink`, so existing constructors and tests do not change.
- [ ] Window: a "Timeline" tab per run: time, session, event, detail; filter by session.
- [ ] Tests: append under lock, rotation, reading a partly written last line.

### 6. Narrow listings with `--cwd`
- [ ] `ClaudeCli.ListAgentsAsync(string? cwd = null)` adds `--cwd <path>` when given. Keep the no-argument call for the full list (the window needs all sessions for the rail and name checks).
- [ ] Use it only where one folder is wanted (for example `ResolveSessionIdAsync` after a start, with the launch folder) — check first that `--cwd` matches a worktree session started from that folder (gate of item 3), else keep the full list there.
- [ ] Tests: argument lists.

## Phase C

### 7. Export a past session
- [ ] Transcript tab: "Export…" writes Markdown (title, date, folder, model; then turns as `## You` / `## Claude`, tool calls as one-line summaries, no thinking blocks) and, if a resume summary exists, the summary first. Reuse the existing transcript line reading; no new parser.
- [ ] Tests on a small sample transcript.

## Not in this plan (and why)

- Shell jobs via `claude --bg --exec`: not in the installed CLI's help (2.1.326); blogs describe it but it cannot be relied on.
- Copying Agent View's dashboard: research preview, keys and layout may change.
- Notifications through Claude Code hooks: would change the owner's Claude settings; the polled `blocked` state gives the same signal.

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

- D3.1 Skipped. The gate was run as far as it could go: `claude --bg --model haiku --effort low --name gate-plain "reply with the word ok and stop"`, in a new scratch repo (one commit, under the session scratchpad, not one of the owner's repos). It exited 1 with `Workspace not trusted. Run claude in <scratch folder> once and accept the trust prompt, then retry.` `claude agents --json --all` listed the same 20 sessions before and after, so nothing was started. The `-w` form was not run, because the first step refuses. The gate is therefore unclear, and the brief says to skip the rest of the item. Nothing of item 3 is built: no `Worktree` on `SessionSpec`, no `-w` in `BackgroundLaunch`, no "Remove session", no tests for them.
- D3.2 Trust was not granted for the scratch folder. The trust record is kept per path in `~/.claude.json` (`projects[path].hasTrustDialogAccepted`; the scratchpad folder of another session there is `false`). Granting it needs an interactive `claude` run in the folder, which writes that file, and that changes the owner's permission settings. I only read `~/.claude.json`.
- D3.3 No session was stopped, removed or started by this run, so there was nothing to clean up with `claude stop` or `claude rm`. The scratch repo was deleted.
- To finish item 3: the owner accepts trust once for a throwaway folder (or names one that is already trusted), then the gate is run again. The same trust gate stops any run in an untrusted repository (finding F24), so the setup page's trust step matters to item 3 as well.
- Item 2 does not depend on item 3. Its folder is the listed `cwd`, else the spec's folder (D2.8), so it works for a session in the main folder and will show a worktree's changes once one exists and is listed with its `cwd`.
