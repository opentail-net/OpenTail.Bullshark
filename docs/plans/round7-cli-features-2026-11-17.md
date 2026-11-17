# Round 7, 2026-11-17: use what the CLI now offers

Status: not started.

Source: research of 2026-11-17 into recent Claude Code CLI features (notes: `research-cli-features-2026-11-17.md`, items C1..C12),
checked by the planner against the installed CLI (2.1.326) on the owner's machine, read-only.

## Facts checked (planner, 2026-11-17, read-only commands)

| Fact | Evidence |
|---|---|
| `claude daemon status` printed `not running`, `control.sock: unreachable (connect ENOENT ...)`, `bg workers: 0 in roster.json (control unreachable)`, `roster.json: updated 5467s ago`, and the daemon log path — while `claude agents --json --all` still listed background sessions as `blocked` and `done`. So the listing can show sessions whose daemon is down. | Run locally |
| `claude daemon --help`: `run`, `status`, `logs`, `uninstall`, `stop` ("Shut down the supervisor and terminate background sessions"). `claude respawn <id>\|--all`: "Restart a background session (or all of them) so it picks up the current Claude binary." | Local help |
| `claude --help` lists `--session-id <uuid>`. Bullshark finding F18 says `--bg` ignored it (2026-11-14); the research says it now works with `--bg`. **Not verified**: needs a real start (gated, owner). | Local help; F18 |
| `agents --json` entries here carry `cwd, id, kind, name, sessionId, startedAt, state`; no `status` or `waitingFor` field was present on 2.1.326 (the docs mention `waitingFor`). `cwd` is the launch folder. | Local listing |
| No `.claude/worktrees` folder exists under the folders of the listed background sessions, so on this machine background sessions edit in place. The docs say `--bg` isolates into `.claude/worktrees/` unless `worktree.bgIsolation` is `none` (not verified locally). | Local folders; research C6 |
| Docs: `claude purge` and `cleanupPeriodDays` delete old transcripts (default not verified). | Research notes |

## Contracts this must satisfy

| Member | Promise |
|---|---|
| `AgentInfo.ParseList` | Unknown fields ignored; missing optional fields are null. |
| `ClaudeCli` | Read-only commands only, except owner-clicked actions (stop, rm, start). Never runs `daemon stop`, `daemon uninstall` or `respawn` on its own. |
| `SupervisorPolicy` | Unchanged by this round, except where an item says so. |
| Changes tab / `FileOverlap` / `MergeCheck` | Work on the folder the session really edits. |

## Items

- [x] **1. Daemon health.** `ClaudeCli.DaemonStatusAsync()` runs `claude daemon status` (read-only) and parses: running or not, pid, version, uptime, control reachable, worker count, roster age, log path. Unknown output → "unknown", never an error. The window reads it each pass in the background step. When the daemon is not running or control is unreachable: a "Needs you" row "Background daemon is not running — sessions listed as working/blocked are not alive", the status bar says so, and **the supervisor makes no start or resume decisions that pass** (a listing from a dead daemon is not trusted, like an unreadable listing). Tests with captured outputs (running, not running, unreachable, garbage).
- [x] **2. Daemon version drift.** When `daemon status` shows a version different from `claude --version`, show a notice with a "Respawn all…" button that asks for confirmation and runs `claude respawn --all`. Never automatic. Tests on the version compare.
- [x] **3. Forward-compatible listing fields.** Parse optional `status` and `waitingFor` (string or object; keep the raw text). When present, the Needs-you row and the tab tooltip say what it waits for (permission, input, dialog…). Tests with samples that have and lack the fields.
- [x] **4. Work on the folder the session really edits.** Read the latest `cwd` from the session's transcript records (if the transcript lines carry one; check a real transcript read-only, and a test fixture); prefer it over the listing's `cwd` for the Changes tab, file overlap and the merge check. Paths compared normalised. Tests: a transcript whose `cwd` is under `.claude/worktrees/x` makes those features use it.
- [x] **5. Transcript clean-up warning.** Read `cleanupPeriodDays` from `~/.claude/settings.json` (read-only; absent → say "Claude's default", without a number unless the docs page states one — check code.claude.com/docs settings page and record the URL). The Storage page shows "Claude deletes transcripts older than N days; archive to keep them" and marks sessions within 3 days of that age. Tests on the age rule.
- [ ] **6. `--session-id` with `--bg` (gated).** Do not build. Record the gate for the owner: one throwaway start with `--bg --session-id <uuid>` in a trusted folder; if the session id matches, Bullshark can name the transcript before launch and drop the read-back poll.
- [ ] **7. Ideas recorded, not built** (write them under "Later" in this file with the research references): HTTP hooks per run for live overlap and limits (C3), `/goal` as a run's stop condition (C9), OpenTelemetry cross-check (C11).

## Decisions made while building

Item 1 (daemon health):

- **D1.1 Only a dead daemon stops a pass.** "Dead" means `DaemonStatus.BlocksDecisions`: not running, or the control socket unreachable. An unknown status does not stop the pass. The running text is not captured, so an unrecognised line must not stop every run.
- **D1.2 The guard sits in the runner, not the policy.** `SupervisorRunner.Daemon` is set by the window before each pass, as `Budgets` is. With a dead daemon the pass returns before it reads the state file and writes nothing; `SupervisorLoop` reads no session output either. `SupervisorPolicy` is unchanged.
- **D1.3 The exit code is not used.** `claude daemon status` exits 1 when the daemon is not running (checked 2026-11-17). The text decides.
- **D1.4 Where it shows.** The Needs-you group shows the daemon row first, even with no session waiting. The status line says "background daemon not running: no session started or resumed". No sound or flash: it is not a session's wait.
- **D1.5 The running output is hand-built.** No daemon was running on the machine, and the brief forbids starting one. The running fixture follows `claude daemon --help`. Check the first real running status against it.
- **D1.6 Open risk for the owner (not decided here).** The help says the daemon "runs on demand and exits once no client is connected and no background session is running". So a dead daemon is also the normal idle state. With this rule Bullshark starts no run from Setup until something brings the daemon up. The help does not say whether `claude --bg` brings it up. That needs one owner-run start (the item 6 gate). If it does, the owner may want the rule to block only "control unreachable".
- **D1.7 Fixtures.** The not-running text is the real output of 2.1.326 on 2026-11-17, with the user's folder name replaced. The listing samples in the tests are synthetic.
- **D1.8 Screens.** `scripts/Test-NewScreens.ps1`: the stand-in answers `daemon status` (down or running, from `FAKECLAUDE_DAEMON`) and `--version`, and logs every call. The run passed 14 of 14 screens into the r7-ui scratch folder.

Item 4 (the folder the session edits):

- **D4.1 The order.** The transcript's latest `cwd`, normalised (`FolderKey.Normalise`), then the listing's `cwd`, then the spec's folder. The transcript's folder is not checked for existence: a worktree that is gone shows the Changes tab's "folder does not exist" message, not the listing's folder.
- **D4.2 Overlap.** A relative path in a session's writes is taken from that session's transcript folder when it names one, else from the run's folder. Absolute paths are unchanged. This is the one behaviour change for sessions whose transcripts name another folder.
- **D4.3 Reads.** `TranscriptWrites.Facts` reads the transcript once and gives the files, the note and the folder. The Changes tab and the merge check use it. The writes cache keeps the last folder across incremental reads.
- **D4.4 Evidence.** The key `cwd` is on most records of a real 2.1.326 transcript (787 of 1,876 lines in one sampled file, checked read-only, key names only). No sample there has a worktree cwd, so the worktree case is a hand-built fixture.
- **D4.5 Not unit-tested.** The Changes tab and the merge check are WPF code. Their folder choice goes through `GitChanges.FolderOf`, which is tested. No screenshot of a worktree session was taken this round.

Build order: items 1 and 4 were committed in that order before items 2 and 3, because the work tree held item 4's edits when item 1 was finished. Items 2 and 3 do not depend on item 4, so nothing is affected; the history is not rewritten.

Item 2 (daemon version drift):

- **D2.1 When the version is read.** `claude --version` runs only when `daemon status` reports a version. Otherwise there is nothing to compare, and no extra process runs on each pass.
- **D2.2 What counts as drift.** Both versions known, and different after taking the leading number ("2.1.326 (Claude Code)" is "2.1.326"). An unknown side gives no notice. The version line of a running daemon is not captured (D1.5), so this path is the least verified part of item 2.
- **D2.3 Where and how.** A "Respawn all…" button in the status bar, shown only with drift, with the drift sentence as its tooltip. A click asks a Yes/No question first, then runs `claude respawn --all` on a pool thread, disables the button while it runs, and runs a pass after. The supervisor never calls respawn; a test checks a pass against a drifted daemon.
- **D2.4 Not known.** The help text does not say whether `respawn --all` interrupts a session that is working. The confirmation text claims only what the help says.

Item 3 (listing fields):

- **D3.1 Reading.** `status` and `waitingFor` are read as text. A string is its value. An object or an array is kept as its raw JSON. A number, a boolean or null gives null, and a blank string means no reason. `Status` now keeps an object's raw text too, where it was null before. `EffectiveState` uses `Status` only when `state` is absent, so no decision changes.
- **D3.2 Where it shows.** Two places only: the Needs-you row's state text ("blocked · waiting for permission prompt"), and the tab tooltip (the title, then "waiting for …"). The blocked item's detail says "waiting for …" too. The policy's alert text and the other rail rows are unchanged.
- **D3.3 Not on 2.1.326.** The owner's listing has neither field, so the App shows them only once a later CLI prints them. The tests use hand-built samples with and without the fields. No screenshot was taken for it.

Item 5 (transcript clean-up warning):

- **D5.1 The default and where it comes from.** 30 days. The data-usage docs say transcripts are kept "for 30 days by default ... Adjust the period with `cleanupPeriodDays`" (https://code.claude.com/docs/en/data-usage#data-retention, read 2026-11-17). The all-settings reference lists the key (https://code.claude.com/docs/en/settings-reference), but its entry was cut off in the fetch, so the number comes from the data-usage page only.
- **D5.2 Age measure (assumption).** The docs do not say whether the age counts from creation or from the last write. This uses the last write, which the Storage page already shows as "Written". To verify on a real clean-up (an owner gate, with item 6).
- **D5.3 Reading.** `settings.json` beside the projects folder, read only. An absent key gives "Claude's default". A key that is not a positive whole number, or a file that is not JSON, gives the default with the reason stated on the page. The owner's user settings file has no `cleanupPeriodDays` (checked read-only, key name only), so the owner's page says "Claude's default".
- **D5.4 Marks.** A transcript is "Near" from 3 days before its deletion age up to it ("deleted in N days"), and "Due" at or past it ("due for deletion at the next clean-up"). The marks are on the transcript rows, which show when a project is opened. The project rows are unchanged. No screenshot shows a mark: the test screens cannot open a project without a new option, which was left out.
