# Supervisor: open work

The supervisor is the pass that starts, resumes and watches the `claude` sessions (`SupervisorPolicy`, `SupervisorRunner`). This file holds what is still open for it, in the order to do it. The probes it waits on are in [cli-gates.md](cli-gates.md). The owner's checks are in [live-run-checklist.md](live-run-checklist.md).

## 1. Phase 1 leftovers (review of 2026-11-15)

- [ ] Resume: solo restart prompt (9), refuse a listed session (10), drop "ask one short question" and cap the summary at 6000 characters (11): all met. Not as written: "one write for run and record" cannot be one write, because the record and the run are two files. The record is written first, and a failed run write is logged with the status line. Partly met; the rest is a design limit.

- [ ] Still open from this item: accept the design limit it states, or reopen it. A planner's call, not a build item.

- [ ] Check, 2026-11-16: `grep -rnE "ovc-|OverClaude|Legacy|NudgePolicy|PlanFile" src` finds nothing. `Assistant` still finds the parked feature (SetupView, SetupPlan, RunPlan, PatternStore, and the transcript's own "assistant" messages), so the full check cannot pass until that feature is cut or the check is narrowed.

## 2. The spike and `--smoke` (review of 2026-11-15, phase 2)

- [ ] Spike (`MainWindow`, `--smoke`, `Run-Spike.ps1`, `Test-Spike.ps1`): waiting for the owner (question 8).

## 3. Per-coder worktrees (waits for G2)

- [ ] Skip sessions in their own worktree (when the worktree item lands): the same relative path in two worktrees is not an overlap until merge — item 2 covers that. Left open: no session has its own worktree, because additions item 3 is skipped (D1.6).

- [ ] `SessionSpec` gets optional `bool Worktree = false`; the setup page offers "own worktree per coder" for runs with more than one coder (planner stays in the main folder).

- [ ] `BackgroundLaunch` gets `string? Worktree`; `StartBackgroundAsync` adds `-w <name>` (name = session name). Resume never passes `-w` (the session already has its worktree).

- [ ] Ending a run: offer "Remove session" which calls `claude rm`; if it reports unpushed work, show its report and offer the `--discard-unpushed` value it printed. Never pass `--force-remove-worktree` without the owner's click.

- [ ] Item 2 must show the worktree's changes (uses `cwd` from the listing, so this follows if the gate shows `cwd` is the worktree).

- [ ] Tests: argument lists with and without worktree; resume never gets `-w`; `rm` report parsing from captured samples.

- [ ] Use it only where one folder is wanted (for example `ResolveSessionIdAsync` after a start, with the launch folder) — check first that `--cwd` matches a worktree session started from that folder (gate of item 3), else keep the full list there.

- Item 2 does not depend on item 3. Its folder is the listed `cwd`, else the spec's folder (D2.8), so it works for a session in the main folder and will show a worktree's changes once one exists and is listed with its `cwd`.

- Background sessions edit in `.claude/worktrees/` by default (`worktree.bgIsolation`). The Changes tab must read that folder.

## 4. Performance on the owner's transcripts (round 10)

### Rules

- Read the owner's transcripts **read-only**. Never write, move, copy or archive anything under `~/.claude`. Never commit any
  transcript content, file name, project name or session id from the owner's data: commit only code, synthetic tests, and timing
  numbers with sizes (MB, line counts).
- No Claude CLI calls except `--help`/`--version`; the app runs only with `--no-pass` and a scratch `--state`; it may use the real
  `--claude-home` read-only for timing (no clicks that move files; Storage archive must not be pressed).
- Own worktree and branch, `dotnet build` + `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` green before each commit, one
  commit per item, no push/merge, no new NuGet packages, public signatures kept.

### Contracts this must satisfy

| Member | Promise |
|---|---|
| `TranscriptIndex.Build` / `UsageLedger` | Same totals as before (each message counted once). |
| `HistorySearch` | Same results as before for the same query. |
| `FileOverlap` / `SessionWritesCache` | Same overlaps as a whole-file read. |
| `StorageReport` / `EffortReport` / `TranscriptPager` | Same output. |
| UI thread | No file or process work on it. |

- [ ] **2. Fix what is slow.** Anything over 1 s on this data, or over 300 MB peak, gets a fix (streaming instead of whole-file reads, caching by path+length+write time, avoiding repeated JSON parsing, parallel per-file work where it is safe for the ledger). Each fix: its own commit, a test that proves the output is unchanged (synthetic fixture), and the before/after numbers in the perf file.
  - Status: done, with a fix each: index in parallel (`334c8f7`), search pre-filter for escaped words (`34dfa37`), search in batches (`ef88740`), index cache (`c6dc664`). Open: the cold index read with no cache, about 1.6 s on this data.

- [ ] **3. Window start.** Time the window from start to the history rail being filled, with `--no-pass`, a scratch `--state` and the real `--claude-home` (read-only), using the existing screenshot option to end the run. If over 3 s, find why (log timestamps) and fix it in the same way as item 2. Record before/after.
  - Status: not done as written. The window's startup pass calls the `claude` CLI, so it was not run. The history path it waits for (index and tails) was timed with the bench: 1.8 s with no cache, 0.26 s with the cache. That is under 3 s, so no fix was made for it.

- [ ] C2. Cold index read 1.35 s (over the 1 s line): look at lazy/background first load only if A-items finish early. **Measured again 2026-11-18 (fixes batch F6, `docs/findings/perf-2026-11-17.md`):** cold 1.44 s median on 194 transcripts, warm scratch cache 0.27 s. Not changed; still open.

## 5. Open for the owner

- [ ] `ProcessCliRunner` kills the whole process tree on a 60 s timeout. For a `--bg` launch this may also kill the background session it started.
  And if the background process inherits the redirected pipes, the output read waits until it exits. The live tests pass today, so neither seems
  to happen, but nobody has checked on purpose.

## 6. Known open issues from finished plans (engineering, not visual)

Each line is copied from the record it came from, so its context is there. Check each against the code before acting: some were written before later fixes.

From the magic-rename batch:

- [x] **Fixed 2026-11-23:** `ClaudeCli.SummariseAsync` now passes `--no-session-persistence`, as `MagicNamer` already does. `SummariseTests` asserts the flag. Not checked live: whether the unflagged call really wrote a transcript into History.

From the perf and testability batch:

  - [ ] Tests: `TranscriptWindowTests` (10), `TranscriptPagerCancellationTests` (2). The App test for the cap and the jump (`TranscriptViewTests`) is written but NOT runnable now (Q7).

From the features batch:

- [x] Closed 2026-11-23 (stale item): the viewed ticks are one key in ui.json, and a save is a read-change-write under one lock hold. `SessionWindow.Changes.cs` `SaveViewed` calls `UiFile.Update`, which calls `UiSettingsFile.Update` (reliability batch R5, with the test `UiSettingsUpdateTests`). The read-then-write pair is now under one lock hold; no two-window run was done to show it.

- [ ] Open: the Ready-for-review tooltip "3 of 7 viewed" is not done (optional in G1; it needs a git read per row).

- [ ] Open: `--rail-group tag` is a test flag only (for the screenshot), like `--rail-tag`.

From the escalation and backlog batches:

- [ ] Review (orchestrator) of D2 escalation: read the App diff; checked that the tab-acknowledge hook in `SetActive` is reached by rail selection only when not `_rebuilding` (a pass refresh does not acknowledge), and that no other pass-driven path calls `SetActive` on a session tab. Suite rerun by me. Open: a session whose tab is already the active one when it starts waiting is not acknowledged (it re-alerts; judged right, the owner may not be at the PC); sound, flash and menu not seen in a window.

From the ready-for-review batch:

- [ ] **Open idea (not built):** a mark is written with the pass's time, so a transcript write in the seconds between the pass and the click shows the coder again. Not seen happening.

From the batch B (review prompt, digest, capture):

- [ ] **Open:** `Process.Start` with `UseShellExecute`. A file with no program associated (`Win32Exception`) is said in the status line.

From the fixes batch:

- [ ] F4: `ArgumentException: Process with an Id of ... is not running` (app.log, 2026-11-17) is a process-gone case the new filter does not
  catch (it lists only ObjectDisposed, IO and InvalidOperation). Its site is not known; not widened without a run that shows it.

From round 5 (status line):

Status: built on branch `round5-2026-11-17` (2026-11-17). Items 1-18 are done, one commit per item, each with its plan ticks and decision. 760 tests, all green; the build keeps its one warning, the one already on `main`. Not pushed or merged. Left open, and recorded in the decisions: the reviewer's additions D4.3 (a limit applies one pass late) is not changed; a coder's merge result is still dropped when it leaves `done` (D7); the first read of each transcript is still whole (D9); the window wiring of items 4, 11, 16 and 18 has no unit test, because the App project has no test project; the Trust row was screenshot on a scratch repo, and the light theme of the rail was not re-shot.

From round 6 (status line):

Status: built on branch `round6-2026-11-17` (2026-11-17). Items 1-11 are done, one commit per item, each with its plan ticks and decision. 782 tests, all green; the build keeps its one warning (`MainWindow.xaml.cs` CS8601, already on `main`). Not pushed or merged. Left open, and recorded in the decisions: P4 (log-on supervisor) is not built (item 11); the Storage Archive button is disabled before the first listing by code only (the screens read the listing, so their picture shows it enabled); the App wiring of items 1, 4, 6 and 9 has no unit test, because the App project has no test project. No finding was "not reproduced" and none was skipped.

From round 9 (status line):

Status: items 1-8 built on branch `round9-2026-11-17`, one commit per item (b319c01 to bcc32f8), all boxes ticked; not pushed or merged. 937 tests (908 at the start). The screen script (`scripts/Test-NewScreens.ps1`) wrote 15 of 15 screens on the scratch fixtures. Open for the owner: the Respawn click does not wait for a pass already running (item 2); a listed `stopped` or `failed` session is still stopped on close (item 7). The round 8 README row still lists B4.3 as open; this round builds it.

From review round 6 notes:

- [ ] Item 14: TextToSave returns the file text when only line breaks differ; lone CR no longer converted (WPF TextBox handling of a lone CR was not checked).

From the desktop and Codex ideas (findings to check, not ideas):

- `claude agents --json` states, per docs: `working`, `blocked`, `done`, `failed`, `stopped`; `idle` is UI-only (https://code.claude.com/docs/en/agent-view). Answers
  the README's open state-values item from docs, not from a run.

- [ ] Unattached sessions that finished or wait for input are stopped after about an hour. `Ctrl+T` pins one; no CLI pin is documented (UNCONFIRMED). The supervisor's
  restart path must handle `stopped`.

From the bugs of round 3 (status line; its open points were not re-checked in this restructure):

Status: built on branch `bugs-round3-2026-11-17` (2026-11-17). All 16 items are done, one local commit each. Not pushed, not merged. The open points are at the end of the decisions.
