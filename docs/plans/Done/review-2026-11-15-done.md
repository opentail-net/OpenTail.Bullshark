# OpenTail.Bullshark: review and plan (2026-11-15) (done items)

> Split from [review-2026-11-15.md](../review-2026-11-15.md) on 2026-11-21. These items are checked in the original; the open items stay there. The headings above each item are its place in that plan.

## 6. Plan
### Phase 1: no lost writes, no surprise sessions
- [x] One `Update(change)` per store, under a named mutex, with a unique temp name. The pass applies its own records, not its snapshot. Replace every Load-then-Save pair (finding 2). Code: `FileLock`, `StateStore.Update`, `RunStore.Update`, `SupervisorRunner.Merge`.
- [x] Runner: any exception from Start or Resume becomes `Failed`. `Program.cs` catches all (1). Code: `SupervisorRunner.cs:69-72`, the pass catch in `Program.cs`.
- [x] Policy: adopt by name before Start (4). Code: `SupervisorPolicy.cs:58-65`.
- [x] No `[` is a failure, not an empty list (5). Code: `AgentInfo.cs:29-35`. Bad JSON in a store stops the pass with `StoreUnreadableException` and a log line (6). Bad JSON in `~/.claude.json` counts as untrusted: nothing starts, but the log says "waiting for trust", not that the file is unreadable. The plan said "skips the pass with a message". Closed 2026-11-16: `TrustCheck.UnreadableProblem` reports the unreadable file, and the window's pass starts and restarts nothing while it is unreadable (tests in `UnreadableClaudeJsonTests.cs`).
- [x] Close: list first, stop a listed session unless it is idle, catch all, put back what a failed stop took out. A closed tab stays closed (7, 8). On 2026-11-16 the status line was fixed: it said "Stopped" for an idle or unlisted session that had not been stopped. The plan's rule was "stop unless unlisted"; question 6 decides whether idle sessions are stopped too.

- [x] Arm the timeout before the stdin write; kill the process tree on timeout or cancel (14). Code: `CliRunner.cs:66-92`.
- [x] Logs follow `--state`; `--state` with no value is an error (16). Code: `App.xaml.cs:17-18`, the window's option parse (`SessionWindow.xaml.cs`), the supervisor's option parse.
- [x] Disable Start until the run is written (19). Code: `SetupView.xaml.cs` `Start_Click` and `RunFinished`.

### Phase 2: cuts and deferrals
- [x] Dead code: `NudgePolicy`, `PlanFile`, `TrustCheck.OpenClaudeForTrust` and `HistoryLabel` deleted with their tests (2026-11-16).
- [x] Data copy: `AppDataMigration` deleted with its tests and its two calls (App and supervisor). Its precondition holds: `%LOCALAPPDATA%\OpenTail.Bullshark` holds `state.json` and `runs.json` (checked 2026-11-16).
- [x] Assistant: parked by the owner, 2026-11-16. The idea is valid; it can be deferred. Not cut, and the code stays. Its precondition held: no record in either live file had the Assistant role. It was built on purpose before this review (D-S41; commits e7b91b0 and bbfc668).
- [x] End the legacy run (owner, 2026-11-16): planner stopped, both records and the run removed from the live files. `MigrateLegacyIfNeeded` only runs when `runs.json` is missing, so it will not recreate the run.
- [x] Legacy code cut (2026-11-16): `RunPlan.Legacy`, `RunStore.MigrateLegacyIfNeeded` and `LegacyRunId`, and `SessionSpec.PlannerName`, `CoderName` and the default-spec helpers are deleted. The supervisor no longer takes `--workdir` or `--task`; they now fail as unknown arguments. The window's `--workdir` and `--task` stay: they pre-fill the setup page, which is not legacy. `scripts/Demo-Handoff.ps1` is deleted: it used the removed options and the legacy run. Test fixtures use `TestSpecs` (names `test-planner` and `test-coder`). The old `ovc-` names remain only as data in tests that read old state files and in the title and run-name tests.
- [x] Summary levels stay: Minimal, Normal, Complete and the tail. Decided by the owner, 2026-11-16. The cut is withdrawn. For a long session Complete may be the right default; decide after a measurement (below).

- [x] Two supervisor hosts to one (section 4). Decided 2026-11-16 (the owner left the choice to the planner; question 4): the
      window is the one supervisor, as section 4 recommends. The exe keeps its two reports and no longer supervises. A headless
      start at log-on (P4) is not wanted now; if it is wanted later, it replaces the window's loop and does not run beside it.
      Built 2026-11-16 (uncommitted): the exe prints the two reports and exits with 2 when given nothing else; the window's pass is
      the only one. `scripts/Test-Supervisor.ps1` checks it (6 checks pass). The README still describes a headless supervisor at log-on;
      that is the owner's to reword.
- [x] Rewrite the README status and the plan's status line (21). README: "built, not yet proven end to end". `setup-and-sessions.md` line 3 now points at its checkboxes.
