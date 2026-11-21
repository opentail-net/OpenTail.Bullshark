# Reliability batch (2026-11-19, written AFK)

Source: brainstorm round 1, reliability audit (researcher-reported static read, 13 findings). The orchestrator re-read the code for the ones marked "checked" below; the coder must re-check the rest **before changing anything** and report a finding that turns out wrong.
**Queued** after `night-batch-2026-11-19.md` (running) is reviewed and committed; before `ui-polish-batch-2026-11-19.md` (must/should items outrank polish). Not queued: finding 12 and 13 (notes).

## Facts checked by the orchestrator, 2026-11-19
- No single-instance guard for the supervisor: the only named mutexes are the per-file locks in `Core/Supervisor/FileLock.cs` (`Local\OpenTail.Bullshark.file.<key>`). `SupervisorRunner.CheckOnceAsync` does read, then `claude` calls, then save; two windows on one state can both act on a stale read.
- `TrustCheck.DefaultClaudeJsonPath`, `SetupCheck` (line ~29), `TranscriptReader` default projects root, `TranscriptArchive`, `InstructionFiles` all build `~/.claude...` from the user profile and ignore `CLAUDE_CONFIG_DIR`.
  **Tested 2026-11-19 with claude 2.1.328: with `CLAUDE_CONFIG_DIR=<dir>` set, `claude mcp list` writes `.claude.json` inside `<dir>`** (answers the open gate "where does `.claude.json` live", `live-run-checklist.md`). So with the variable set the app reads a wrong or missing trust file.
- `SupervisorLoop` (lines ~53-78) runs `claude logs` for **every live session on every pass** and stops only when one shows a login expiry; a session that has just gone logs a line each time.
- `MergeCheck` uses `git merge-tree --write-tree` (writes tree objects) and `ShouldRecheck` returns true for every Problem result, so a persistent problem re-runs it each 15 s pass while the coder stays `done`.
- `_trustTimer` (3 s) calls `PassAndRefreshAsync` (`SessionWindow.xaml.cs` 101, 165, 481, 507) while a Trust step is open.

## Contracts
- State files (`runs.json`, `state.json` records, `library.json`, `ui.json`) are read and written through `FileLock` + atomic writes; `UiFile.Read/Set` read, change one key and write under the lock. A read-modify-write must happen **inside** the lock.
- The supervisor never types into a session and never accepts/merges (principle 3); starts/restarts happen only in `SupervisorRunner`.
- `PassAndRefreshAsync` is the one pass; `_passRunning` guards re-entry; the status text is the only place a pass failure shows today.
- `AttentionList`, `Needs you`, `ToastCategories` (Problems, Notices, Blocked, Ready, LimitReset), `_daemonNotice` (the daemon note row) are the existing notice mechanisms; new notices use them, not a new channel.
- `PathsForClaude`-style defaults are used in tests with explicit paths; keep every existing constructor/option that takes a path.

## Steps (verify each claim in code first; then Core with tests, then App)
- [x] R1. **Single supervisor per state.** Hold a named mutex `Local\OpenTail.Bullshark.supervisor.<hash of the full state path, lower case>` for the life of a window (acquire in the window's start, release on close; an `AbandonedMutexException` counts as acquired). A second window on the same state does not run the supervisor pass (no start, no resume, no restart, no alert writes) and shows a calm status line and a note under Needs you: "Another OpenTail.Bullshark window supervises this state. This one only watches." It still reads and draws. Core helper `SingleInstance` (named-mutex wrapper, testable with a random name) + tests (second acquire fails, release then acquire works, abandoned handled).
- [x] R2. **One place for Claude's paths.** Core `Health/ClaudePaths.cs`: `ConfigDir(Func<string,string?> env)` = `CLAUDE_CONFIG_DIR` if set and non-empty, else `~/.claude`; `ClaudeJson(env)` = `<configDir>\.claude.json` when the variable is set, else `~\.claude.json` (the tested behaviour); `ProjectsRoot`, `ArchiveRoot`, and the instruction-file root derive from it. Use it in `TrustCheck`, `SetupCheck`, `TranscriptReader`, `TranscriptArchive`, `InstructionFiles` (keep their explicit-path overloads). Tests with an injected env lookup (set/empty/unset, trailing slash, relative path left as is). Update the gate item and `round3` notes in `live-run-checklist.md` with the tested fact.
- [x] R3. **Trust timer rate.** While a Trust step is open, the 3 s timer must only re-read the trust file (cheap, no `claude` spawn); the full pass runs at most every 15 s. Measure before/after: count `claude` spawns per minute with a Trust step open (the fake `claude` in the screenshot fixture logs calls: `tools\calls.log`) and report the numbers.
- [x] R4. **Login check rate.** Read `claude logs` for one live session at most once an hour (account-wide), or on the next pass after a read that showed the login expiring soon; not for every session on every pass. Keep `AlertLogin` once-per-state. Core tests with a fake CLI: N live sessions give at most one `logs` call per hour; an expiring result keeps checking each pass until it clears; a failure does not retry every pass.
- [x] R5. **Lost marks between windows.** The Ready-for-review "reviewed" marks (`SessionWindow.ReadyForReview.cs` ~30-32, 119; `SessionWindow.Changes.cs` ~115, 149) and the viewed-file ticks are read once and written whole: add `UiFile.Update(statePath, key, Func<JsonNode?, JsonNode?>)` that reads, transforms and writes inside one lock hold, and use it for both. Tests (Core `UiSettingsFile` or a new test with two interleaved updates keeps both changes).
- [x] R6. **Failed close restores a stale snapshot** (`SessionWindow.Tabs.cs` ~251, 307): verify; if real, restore only the failed session's record from the snapshot, not the whole dictionary; test the logic if it can be pulled into Core, otherwise describe the fix.
- [x] R7. **UI thread and the file lock.** The UI thread takes the file lock about 3 times per pass (`RefreshRail`: `LoadLibrary`, `ReadRecords`; `Attention`; `Library`) with a 10 s wait. Give UI-thread callers a short wait (2 s) and, on timeout, keep the last good data and say "state is busy, showing the last read" once on the status line; background callers keep their longer wait. Do not change lock semantics for writers. Report which call sites you changed.
- [x] R8. **Merge check backoff.** A Problem result is rechecked at most every 5 minutes per coder (not every pass), and an unchanged HEAD with the same Problem is not rerun more often than every 30 minutes; update the `GitRunner` "reading only" claim in its comment to say `merge-tree --write-tree` writes objects (or switch to a read-only form if one exists in git 2.4x: check `git --version` and the docs before claiming). Tests for the schedule (pure function with injected clock).
- [x] R9. **A failed pass is invisible at 3 AM.** When a pass fails (unreadable `runs.json`/`.claude.json`, `claude` missing, an unexpected exception), show a "Supervisor problem: <short reason>" row under Needs you (like the daemon note), raise one toast in the Problems category when it starts (not every pass), clear it when a pass succeeds. The reason text contains no paths beyond a file name. Pure Core helper for the state machine (failed -> notice once -> recovered) with tests.
- [x] R10. **Unobserved task exceptions.** Fire-and-forget calls (`SessionWindow.LimitStatus.cs` ~37, 47; `SessionWindow.xaml.cs` ~253-256) catch only the expected store errors: add a small helper that logs the exception type and message (no stack with paths beyond what AppLog already writes) for anything else, and use it at those sites; the status tick must not run while a pass is starting if that is what the finding says (verify).
- [x] R11. **Cache read outside the lock** (`Core/Activity/IndexCache.cs` ~158; `Core/Setup/LimitHistoryCache.cs` ~120): if a read can see a half-replaced file, retry once after 100 ms before treating it as no cache. Tests with a file that is locked/replaced during the read if feasible; else a unit test of the retry helper.
- [x] R12. Docs: README "Known limits" bullet for single supervisor and CLAUDE_CONFIG_DIR; `live-run-checklist.md`: two windows on one state (the second says it only watches), a Trust step open for a minute (spawn rate), a broken `runs.json` gives the Supervisor problem row; tick this plan, list false findings if any.

## Checks
Core tests with counts; App build; whole suite `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false`. For R1/R3/R9 a manual-style check with the screenshot fixture: launch two windows on one scratch state (both `--no-pass`? no: R1 needs the supervisor pass; use the fake claude on PATH as `Test-NewScreens.ps1` does, never the real `claude`) and read `calls.log`/`app.log` to show the second made no start/resume calls. Do not start real claude sessions.

## Orchestrator review
Contracts: a read-modify-write is inside the lock; the second window never starts or restarts anything; no new `claude` spawns per pass; paths come from one helper and old explicit-path constructors still work; a failed pass shows once; no `respawn --all`, no real sessions.

## Open / not decided
- Whether to make the second window refuse to open instead of watching (watching chosen: less surprising).
- Findings 12, 13 (uncached per-pass tail work, uncapped CLI output, sound at any hour): notes, quiet hours now exist in the night batch.

### Coder's record, 2026-11-19 (steps R1-R12 done; not committed, not pushed)

**Claims checked in code (verified / false)**
- R1 true: no single-instance guard; only the per-file locks existed.
- R2 partly false: `InstructionFiles` (ClaudeConfig.Dir) and `SetupCheck.DefaultClaudeDir` already read `CLAUDE_CONFIG_DIR`. True for `TrustCheck`/`SetupCheck` `.claude.json`, `TranscriptReader`, `TranscriptArchive`.
- R3 true: 23 passes a minute with a Trust step open (`_trustTimer` 3 s, `PassAndRefreshAsync`).
- R4 true: `claude logs` for every live session on every pass until one showed the expiry.
- R5 true: `MarkReviewed` and `SaveViewed` wrote whole maps from memory.
- R6 partly false: the record restore already put back only this session's record. The run restore was real: it put back whole runs from before the close (`Concat(originals)`), which resurrected a sibling closed meanwhile. Fixed by `RunStore.WithSessionRestored`.
- R7 true: `LoadLibrary`, `ReadRecords`, `ReadRuns` and `UiFile.Read` took the 10 s lock on the UI thread. UI call sites now use a 2 s wait with last-good data; pool-thread reads (`ViewedStateOf`, the Night quiet-hours read) keep 10 s via `UiFile.ReadInBackground`.
- R8 true: a Problem was rechecked each pass. `GitRunner` comment corrected. Git 2.53 has no read-only full merge: `merge-tree --trivial-merge` only merges trivially, so `--write-tree` stays and the comment now says it writes objects.
- R9: new.
- R10 true for `LimitStatus` (its catch covered expected store errors only). The `xaml.cs` site is `RefreshLimitStatus()`, which only draws and starts that scan, so it needed no change. `LoadHistoryAsync` already catches everything, and `CheckMergeAsync` catches and logs everything.
- R11 true: both cache loaders read without the lock. Only the retry helper is unit-tested, since the cache types are internal and Core has no InternalsVisibleTo (the plan allows this).
- R12: the README had no "Known limits" section; one was added.

**Decisions taken beyond the plan** (owner may reverse)
- A watching window takes over the supervisor name on a later pass when the supervising window has closed or been killed (`TakeOverIfFree`). The plan said acquire at start only. Verified on screen: the takeover line appears after `Stop-Process` of the first window (abandoned mutex).
- A watching window raises no toast or sound, opens no Trust step, offers no Respawn button, runs no automatic naming. Owner-started actions still work in either window (resume a past session, close, setup run, retry failed starts). A run started from setup is picked up by the supervising window.
- The supervisor-problem toast follows quiet hours (no toast; the row stays) and is not shown while the window is in front, as the Needs-you toast is.
- An archive made before `CLAUDE_CONFIG_DIR` was set stays in `~\.claude\bullshark-archive` and is not listed once the variable is set. The owner should decide whether to move it.
- The busy notice (R7) is put on the status line at the end of a pass, so the next pass replaces it.

**Measured**
- R3, fixture with a Trust step open, 60 s, fake claude call log: before (HEAD build) 118 calls: `agents --json` 46, `daemon status` 46, `--version` 23, `attach` 2, trust terminal 1. After: 24 calls: 8, 8, 4, 3, 1. Supervisor passes fell from about 23 to about 4 a minute (5 spawns a pass).
- R1, two windows on one trusted fixture: supervisor 15 calls in 20 s (2 `--bg` resumes); watcher 6 calls, 0 `--bg`; takeover after the supervisor was killed, with the next pass logged as "this window supervises the state now".

**Tests**: Core suite 1635 passed before, 1702 passed after (0 failed). Full `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false`: 1702 passed, 0 failed, exit 0. App build: 0 errors (one warning, CS8601, in `MainWindow.xaml.cs`, not touched).

**Not done or unsure**
- The trust-accept path (a Trust step closes and its pass runs within 15 s) is coded but not run on screen.
- The R9 toast is not seen on the desktop: the fixture's ui.json had notify off, so no Windows toast was raised; the decision is logged.
- R9 run, 2026-11-19 (broken runs.json, fixture): run X logged "supervisor problem" once over three passes and its row is in the screenshot; run Y restored the file and logged "supervisor problem cleared" in the same window; the cleared screenshot has no row.
- Open, outside R9's rule: the status bar still prints the full path of runs.json (the existing `PassSnapshot.StatusParts` text, from the raw exception message). The Needs-you row and the log use the short reason. Also the `AppLog` lines from `StoreUnreadable` still carry the full path (existing). Not changed: say if the status line should use `PassHealth.Short` too.
- The fake claude's own log write raced between concurrent fake processes once (an `IOException` shown in a terminal tab). A stand-in artefact, not an App fault.
- Watcher terminals still run `claude attach` for live rail rows (existing behaviour, read-only attach).
