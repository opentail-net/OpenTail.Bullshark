# Performance and App-layer testability batch (2026-11-19, written AFK)

Source: brainstorm round 3, performance/testability report (researcher-reported static read; **every item is verified or measured by the coder before it is changed**). The researcher corrected two ideas that are not worth doing: live rows already appear at start (the synchronous `RefreshRail` in `LoadHistoryAsync`), and search is already bounded (100 sessions, 3 hits each); pager open is 52 ms on a 72.9 MB file (round 10; not re-measured here).
Queued behind `firstrun-diagnostics-batch-2026-11-19.md` (done; shared files: `SessionWindow.Settings.cs`, `SessionWindow.Rail.cs`, `SessionWindow.Attention.cs`).

## Status (coder, 2026-11-19, evening)

- Q1 done: soak log. A 9 h 25 min run, numbers below.
- Q2 done: one ui.json read per pass, on a pool thread. Numbers below.
- Q3 done in code: pages on a pool thread, cap 1,000 with a note. The working set after ten Load-earlier clicks was NOT measured (no UI automation).
- Q4 closed with no code change (measured: 2.5 ms per read).
- Q5 done: the rail is rebuilt only when its signature changes. Numbers below.
- Q6 done in Core and wired in the App, except (e) (not done: nothing to extract).
- Q7 BLOCKED. The test project is written, but it is NOT in the solution and has not run stably. See Incident.
- Q8 done: README, live-run checklist, this plan.
- Nothing committed or pushed.
- Core tests: 2045 passed, 0 failed, in 10 of 10 runs (baseline 1981; +64). App build: 0 errors.

## Incident (2026-11-19, during Q7)

While I was debugging the App test project, one test run built a full `SessionWindow` inside the test process, with supervision on (no `--no-pass`), against the owner's default state. The log shows it read the owner's 212 transcripts and created one terminal. Its supervisor pass then started the owner's real session `abd348a2` with `claude --resume` (pid 13780), plus a daemon (36852), a pty host (29480) and an `opentail-mcp` child (19184). Those four processes are still running.

I tried to stop exactly those four by PID; the auto-mode permission check denied that, and I did not work around the denial. The owner's own `claude.exe` (pid 23220, started 17:07) and the MCP and orca servers were not touched.

Owner files the run changed: `ui.json` (17:41:24), `heartbeat.json` (17:42:23), and the transcript `abd348a2` (appended at 17:41:27 by the resumed session). `state.json` and `runs.json` are unchanged since 07:11.

Cause, as far as I know: `SessionWindow` is built only in `App.OnStartup`, and the generated `App.g.cs` `Main` calls `Run`. The test thread calls neither, and I did not establish the exact trigger. I removed the App test project from `OpenTail.Bullshark.slnx`, so `dotnet test OpenTail.Bullshark.slnx` cannot reach it.

Evidence kept: `%TEMP%\bullshark-app-tests\logs\app.log` and its crash file.

Needed from the owner: stop pids 13780, 29480, 36852 and 19184 (or allow it); decide on the resumed transcript and on `ui.json`'s `lastSeen`. Then Q7 can resume, with a guard that the test thread can never reach `OnStartup` or the default state.

## Contracts (verified in the code; verdicts added)

- `RefreshRail` clears and rebuilds the whole tree on the UI thread on every pass: TRUE. Measured before Q5: 2,247 rebuilds in 2,247 passes, mean 67.9 ms each.
- `RestoreSelection`, `_expanded`, the tag filter, search matches and group-by-tag feed it: TRUE. `ShownHistoryFolders()` is the list the rail draws; I did not read the magic rename's use of it.
- ui.json read about four times per pass on the UI thread (Rail ~378, 386; Attention ~234, 250), each with a 2 s wait: TRUE for a pass that does not supervise (measured: 4 per pass). FOUND MORE: a supervising pass also reads it in `WriteAttentionFile` (AttentionFile.cs). `LoadLibrary` reads `library.json` on the UI thread on every rail build (not in the plan; not changed).
- `TranscriptView` ~203 and ~404: `ReadPage` on the UI thread: TRUE (both sites). Cost measured under Q3.
- "Load earlier" adds blocks with no cap; the 60 MB transcript has 16,519 lines: TRUE (counted: 16,519 records, 60.2 MB).
- `_terminals` keeps one terminal per recorded session until closed: TRUE in the code. Not measured with live sessions (every scratch run had none). The incident run showed one terminal per record.
- `ClaudeTrust` parses `.claude.json` each pass and every 3 s in a Trust step: TRUE (both on a pool thread). The plan's line numbers were off (`Read` is at lines ~36–40 of 104); the cause is the same.
- Behaviours that must not change: Needs-you not filtered (TRUE); selection kept (TRUE, now `SelectionPolicy`); transcript pane kept without a live session (TRUE); tab close selects a neighbour (TRUE, `TabPolicy`); quiet hours and notify gates (TRUE, `AnnouncePolicy`).
- "A rebuild resets scroll and selection": FALSE for the scroll. With `--rail-scroll 400`, 557 rebuilds were made while the rail was scrolled, and the offset stayed at 400 px at every soak line (same-height content). Selection is restored by `RestoreSelection`; not measured in a UI test.

## Steps and results

- [x] Q1. Soak and phase log (measure only).
  - Built: `SessionWindow.Soak.cs`, `--soak-seconds <n>` (default 1800), counters in `UiFile`, `TranscriptView.BlockCount`.
  - Run A: 9 h 25 min (close line at uptime 09:25:37), `--no-pass`, scratch state, the owner's 212 real transcripts read-only. Its 30-minute lines: working set 194–210 MB, private 97–113 MB, GC heap 26–49 MB, handles 652–681, threads 15–22, terminals 0, rail items 182, transcript blocks 0. Passes +120 per 30 min; mean pass 1,167 ms (CLI-dominated: `agents`, `daemon status`, `--version`). No growth over the run.
  - Startup (run A): rail built 1,186 ms, window shown 1,378 ms, first pass done 3,674 ms, history read done 3,881 ms, after the process started.
  - Assumptions: the pass's read-only claude queries ran (they start no session); no live sessions in the scratch state. The owner's 12-hour run is still the live check.

- [x] Q2. One ui.json read per pass, off the UI thread.
  - Before (run A, Q1 build, 9 h 25 min): UI-thread reads 4 per pass (+480 per 30 min; 9,040 in total); background 0.
  - After (run C, Q2 build, 9 h 21 min): UI-thread 0 per pass (8 in total, at startup); background 1 per pass (2,248).
  - Pass mean: 1,167 ms before, 1,236 ms after. CLI-dominated; the change is in the UI-thread reads, not the pass total.
  - Changed: `PassSettings` (Core `Ui/PassSettings.cs`); `UiSettingsFile.TryReadAll`; `UiFile.TryReadAll` and `TryReadAllInBackground`. The pass reads one set on the pool thread, and the announcements and `WriteAttentionFile` use it. `SettingsNow()` serves a refresh from a user action (it reads at once, so a Settings change shows on that refresh). A busy file keeps the last pass's values.
  - Tests: `PassSettingsTests` (6 methods). `TryReadAll` has no direct test (the busy path).
  - Not done: `library.json` read on the UI thread per rail build (found; left).

- [x] Q3. Transcript pages off the UI thread, capped.
  - Before (run D, 60 MB transcript, last page): read 5.2 ms on the UI thread, drawn 11.1 ms, 55 lines.
  - After (run E): read 4.7 ms on the pool thread, drawn 13.3 ms.
  - Changed: `ShowAsync` reads on the pool thread with a cancel (a newer show cancels it). `LoadEarlier` reads on the pool thread, ignores a second click while one reads, and releases the far lines past `TranscriptWindow.DefaultCap` (1,000), with one muted note. `TranscriptPager.ReadPage` takes a token. A jump or find to a released line shows the page around it again (the existing path).
  - Tests: `TranscriptWindowTests` (10), `TranscriptPagerCancellationTests` (2). The App test for the cap and the jump (`TranscriptViewTests`) is written but NOT runnable now (Q7).
  - Not measured: the working set after ten Load-earlier clicks (no UI automation). Not screenshotted: the note needs more than five clicks.

- [x] Q4. Trust file parse cache: closed, no change.
  - `.claude.json` is 231.6 KB. `ClaudeTrust.Read` takes a median 2.47 ms (p90 2.73 ms, 50 runs; scratch timing tool on the Core build). It runs on a pool thread, once per pass and every 3 s in a Trust step. Not worth a cache. The cache tests were not written.

- [x] Q5. Rail rebuild: measure, then skip when unchanged.
  - Before (run C, 9 h 21 min, rebuild every pass): 2,247 rebuilds in 2,247 passes, mean 67.9 ms each (about 153 s of UI-thread work in the run).
  - After (run F, 9 h 16 min, Q5 build, `--rail-scroll 400`): 558 rebuilds in 2,224 passes (one a minute from the minute bucket, plus changes); 1,666 kept; mean rebuild 61.8 ms (about 35 s). The signature's own time is not measured separately.
  - Scroll: 400 px kept through 557 rebuilds (see Contracts).
  - Changed: `RailSignature` (Core: length-prefixed parts; the UTC minute). `SessionWindow.RailSignature.cs` lists the inputs: theme, filter, tag, group, library error, watching, daemon notice, problem, pinned group, history version, settings, records, runs, agents, attention, review, marks, running facts, clashes, merges, trust steps, plan counts, search matches. The skip is on the pass path only. A user action always draws, and stores its signature. Expanded folders are NOT in the signature: a toggle is already on the rows, and the stored state is read on each build. A failed signature makes the next pass draw.
  - Tests: `RailSignatureTests` (7 methods, with theory rows).
  - Not done: lazy rows for collapsed folders. The measurement did not show it matters (182 rows, about 60 ms).

- [x] Q6. Decisions in Core, tested.
  - (a) `SelectionPolicy` (Core `Ui/SelectionPolicy.cs`): the App's `RestoreSelection` uses it, and every old branch is kept (Insights and Settings keep the pane only with no row, as before). Tests: `SelectionPolicyTests` (7 methods).
  - (b) `TabPolicy.ActiveAfterClose` (Core `Tabs/TabPolicy.cs`): `CloseTab` uses it, computed before the removal, with the same results as the old code. Tests: `TabPolicyTests` (7 methods).
  - (c) Existing: `RestoreAfterFailedCloseTests` (6 methods, Core) already covers that only the closed session's entries come back. No code change.
  - (d) `AnnouncePolicy` (Core `Ui/AnnouncePolicy.cs`): `ForWaits`, `SilentToastShown`, `ProblemToastShown`, `WaitToastAllowed`, used by `AnnounceWaits`, `AnnounceQuiet`, `AnnounceProblem` and the toast filter. Tests: `AnnouncePolicyTests` (7 methods).
  - (e) Not done: the Night page's Settings link always opens General (`SessionWindow.Night.cs` line 62). There is no decision to extract, so no test.

- [ ] Q7. Headless WPF test project: BLOCKED (see Incident).
  - Written: `tests/OpenTail.Bullshark.App.Tests` (csproj, `UiThread`, `LogicalTree`, `TranscriptFixture`; `TranscriptViewTests` (4 methods), `SearchViewTests` (3), `KeyboardRowTests` (3, four cases), `PillTabsTests` (3)). Internals: `InternalsVisibleTo` in `AssemblyInfo.cs`; `TranscriptView` hooks (`BlockCount`, `WindowNoteText`, `FindCountText`, `FocusLineNumber`, `EarlierEnabled`, `LoadEarlierForTest`).
  - Last run before the incident: 13 of 14 cases passed. The failing one (the find count stayed "Searching…") is an unobserved `InvalidOperationException` in `PickHitIndex` → `CurrentLine` → `TransformToAncestor`: the view was not in a window. Open question whether the real app can reach it.
  - Not in `OpenTail.Bullshark.slnx` (removed after the incident). Not run 10 times. No picture taken.

- [x] Q8. Docs: README "Soak log" section; `live-run-checklist.md` "Perf and testability batch" (Q1, Q3, Q5, Q6 checks); this plan.

## Checks
- Core tests: 2045 passed, 0 failed, 10 of 10 runs (`dotnet test tests/OpenTail.Bullshark.Core.Tests`). `OpenTail.Bullshark.slnx` now runs the Core tests only.
- App build (`dotnet build src/OpenTail.Bullshark.App -nodeReuse:false`): 0 errors.
- Pictures: none taken. The only visible change is the released-lines note (Q3), which needs more than five Load-earlier clicks.
- Scratch: `%TEMP%\bullshark-perf-f347fe943eef4b75832215afbb9a53fe` (soak states, logs, binary copies). Kept as evidence for the numbers above.

## Orchestrator review
Contracts: behaviours unchanged (selection, panes, tab close, announce gates), checked by reading old against new, not by a UI test. Q2 changes take effect on the next pass; a Settings change on a refresh reads the file at once. Q3 keeps jump and find correctness outside the window (existing path). Q5 skips only on the pass path and only when the signature is equal; the minute bucket refreshes the clock text. Numbers are present for each performance step. The new test project is NOT stable (Q7).

## Open / not decided
- The 12-hour soak itself is a live check for the owner (`live-run-checklist.md`).
- Whether to virtualise the transcript (VirtualizingStackPanel) if the 1,000-line cap is not enough: not decided. Q3 shows the page read is cheap (about 5 ms); the drawing is the cost.
- The incident: the trigger of the window in the test process; the four running processes; the owner's transcript and `ui.json`.

## Incident, 2026-11-19 (a test run resumed one of the owner's real sessions)
While building the headless App test project (Q7), a temporary diagnostic test (the coder's `FindDiagnosticTests.cs`, since deleted) built a full `SessionWindow` inside the test process with the owner's default state and supervision on. Its first pass resumed the owner's run planner
`bullshark-planner-3062` (session `abd348a2`, `claude --resume ... --permission-mode auto --model opus`) and started a transient claude daemon, a pty host and an opentail-mcp child (pids 36852, 29480, 13780, 19184, started 17:41:25-28; the owner's own claude 23220 and its mcp child 35552 are separate and untouched).
Checked by the orchestrator at 17:48: the resumed session is `idle` / `blocked` (waiting for input), its transcript has not been written since 17:41:27, nothing is working or spending usage. Touched: owner's `ui.json` (`lastSeen` 17:41:24, so the next Away digest period starts there), `heartbeat.json` (17:42:23), transcript `abd348a2` (resume entry appended 17:41:27). `state.json` and `runs.json` unchanged.
The processes were **not** killed: stopping the resumed planner would mark one of the owner's own runs as stopped; the app would have resumed that planner the same way in normal use. **Owner: decide whether to stop session `abd348a2` / `bullshark-planner-3062` (its run is yours) and whether the daemon should stay.**
Fix: `SessionWindow` now forces `NoPass` when the process is a test host (`RunningUnderTest`, App/SessionWindow.xaml.cs); the App test project stays out of the solution until Q7 is stable and proven to start no `claude` process (count `claude.exe` before and after a run).
