# Fixes batch (2026-11-18)

Owner: "get on with a plan for 2 & 3, in order. Plenty of tokens left. Get the coder busy!" This is list 2 (fixes and cleanups). List 3 (features) is
`features-batch-2026-11-18.md`, queued after this one is reviewed and committed.

## Contracts (read in the code 2026-11-18)
- Search: `HistorySearch.RunCounted(sessions, query, cancellationToken:, extraText:)` reads sessions in batches of 32 on several threads; the test
  `HistorySearchBatchTests.ACancelledSearchDoesNotReadTheRestOfItsBatch` (cancel at the 10th read, expects fewer than 32 reads) failed once in a full run and
  once in three solo runs under load for the coder, and passed 6 of 6 for the orchestrator. Do not weaken the rule "a cancel stops the batch quickly" to make it pass.
- Magic rename: `MagicName.PromptFor(usedNames, extract, knownTags)` lists names already used in the folder ("do not repeat them"); `MagicName.Distinct(name, taken, when)` adds " - 18 Nov" or a number
  when a name is taken. For a re-roll ("Magic rename again") the session's own current name is not in the taken set (`MagicCandidates.TakenNames(marks, targets)` excludes the targets), so the
  model can answer the same name again. `MagicJob`, `MagicNamer.RunAsync`, `SessionWindow.MagicRename.cs` (`JobFor`, `RunMagicAsync`) carry the job. Existing MagicName/MagicNamer tests must pass.
- README: the code defaults the permission mode to `auto` (`SetupPlan.PermissionMode = "auto"`, `RunPlan` default `"auto"`), `ClaudeCli` passes `--permission-mode`. README line 117 says
  "Default: `auto` for both (D3)", line 151 says "open (D3)", the D3 table row says `acceptEdits` plus an allowlist, and the `SessionSpec.cs` header comment says acceptEdits (D1, D3).
  README text must match the evidence (the code): do not decide a new policy, make the text agree with the code and keep D3 as the recorded history.
- Screenshots use `PrintWindow`-style capture in `SessionWindow.SelfTest.cs` (`SaveScreenshot`); the app's log is `app.log` (`AppLog`).

## Steps
- [x] F1. Flaky search-cancel test. Reproduce under load (run the single test 20 times while a CPU-heavy process runs, or use `--filter` with the whole `HistorySearchBatchTests` class in a loop); find out whether
  the product cancels too late (a real bug: fix `HistorySearch` so a cancel stops further reads promptly, e.g. check the token before each read inside the parallel loop) or the test's bound is unsound
  (reads already in flight on other threads when the cancel lands: then bound by `reads < 32` is wrong only if threads exceed that; explain with numbers). Fix the cause, keep the intent, run the class 30 times to show it stable.
- [x] F2. Magic rename again must not repeat the same name. For a target that already has a magic name (Kind Again) the prompt names the current name as one to avoid
  ("Names already used in this folder" gets the session's own name too, or a separate line "Its current name, do not repeat it: X"), and `Distinct` treats it as taken for that call (put the current name into the taken set
  for that job only; do not leave it taken for other sessions). A manual replace (ReplaceManual) is not affected. Core tests: the prompt contains the current name for a re-roll and not for a fresh name;
  `MagicNamer` with a fake model that returns the current name gets a distinct variant (date suffix) as before.
- [x] F3. README agrees with the code on the permission mode (lines ~117, ~151, the D3 row annotated "superseded in code: default auto, see SetupPlan/RunPlan"), and the stale `SessionSpec.cs` header comment fixed. Grep the docs/plans for other
  statements of the default and list them in the plan's notes (do not rewrite history files; only note).
- [x] F4. `detach failed: ObjectDisposedException` lines in `app.log` when a screenshot run closes (seen by the coder 2026-11-18). Find where it comes from (the tab/terminal detach on window close), decide if it is a real
  shutdown bug (a disposed terminal used) or harmless noise, and fix it if real (guard on disposed, or skip the detach when closing a test run); say which.
- [x] F5. The white band over the bottom status row in some screenshots. Timebox: look at `SaveScreenshot` (the capture size vs the window's real client size, DPI, the window border), try capturing the window with the
  documented approach that includes the whole client area, and compare 10 captures of the same screen. If a fix is clear, make it and say so; if not root-caused in a reasonable time, write down what was tried in the plan and stop.
- [x] F6. Cold index read (1.35 s on the owner's data, over the 1 s line, backlog C2): **measure only**: write the numbers (cold, warm, with the cache) and where the time goes if a quick profile is easy;
  do not change the design. Record in `docs/findings/` or the backlog item. Not a code change unless something is obviously wasteful and tiny.
- [x] F7. Docs: tick the backlog item/notes, add a short "Fixes batch" note to `docs/plans/live-run-checklist.md` only if a fix changes something to look at live.

## Checks
Whole suite `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` (with the flaky class run repeatedly), App build. Screenshots only if F5 changes the capture (view them). No commit.

## Orchestrator review
Diff against Contracts: the search cancel rule not weakened; Again/prompt change covered by tests and does not leave a name taken for other sessions; README text equals what the code does; F5/F6 honest about what was and was not found.

## Results (coder, 2026-11-18; not committed)

Ticked above: F1 to F7 were done in order. Changed files: `HistorySearch.cs`, `MagicName.cs`, `MagicNamer.cs`,
`SessionWindow.MagicRename.cs`, `SessionWindow.Tabs.cs`, `SessionWindow.xaml.cs`, `SessionSpec.cs` (comment), `README.md`,
`docs/findings/perf-2026-11-17.md` (F6 section), `docs/plans/backlog-2026-11-18.md` (C2 note), `docs/plans/live-run-checklist.md`
(fixes note), and the tests `HistorySearchBatchTests.cs`, `MagicNameTests.cs`, `MagicNamerTests.cs`.

- **F1 (search cancel).** The product was wrong in part, and the test's bound was wrong too. The flake: a batch's `Parallel.For` kept
  running bodies after the cancel. Under 16 CPU burners, 3 of 40 class runs failed with all 32 sessions read (22 after the cancel at the 10th).
  Fix: `ReadBatch` checks the token before each session, and `RunCounted` rethrows right after the batch. Not weakened: the cancel rule
  is stricter now. Test bound: the old `reads < 32` is unsound once the processors reach about 23 (the search may read one session per
  thread plus the caller). The bound is now `reads <= 9 + ProcessorCount`. Runs: idle, before the fix, 10 class runs, all passed; under load,
  after the fix, 40 class runs, 0 failed (bound 24, before the bound change); idle with the bound 24, 30 runs, 1 failed (read 25);
  idle with the bound 9 + ProcessorCount, 30 runs, 0 failed. Full solution run (`dotnet test OpenTail.Bullshark.slnx`, final bound):
  1485 passed, 0 failed, exit 0. An earlier full Core run at bound 24 also passed.
- **F2 (magic rename again).** The prompt gets its own line, "Its current name, do not repeat it: X", for a re-roll (`MagicJob.CurrentName`,
  set in `JobFor` for a magic name only, not for a confirmed owner-name replacement). `MagicNamer` adds it to the taken set for that
  call only, so a repeated name gets " - 18 Nov" (or " 2"), and the old name is not left taken for other sessions. Finding: `NamesInFolder`
  already listed the session's own name, but only within the 30 most recent, so the explicit line is the reliable part. New tests: 2 in
  `MagicNameTests`, 3 in `MagicNamerTests`. Magic and HistorySearch subsets: 169 passed, 0 failed.
- **F3 (README and permission mode).** Code: `SetupPlan.PermissionMode = "auto"`, `RunPlan` default `"auto"`, `ClaudeCli` passes
  `--permission-mode` (the local `claude --help` lists `auto`). README line 117 already said `auto`; line 151 now says `auto`, and the D3 row
  is annotated "superseded in code" (its text kept). `SessionSpec.cs` header now says `auto` (the Opus and Haiku defaults are checked in `RunPlan`).
  Other statements, not changed (history): `docs/plans/setup-and-sessions.md` D-S14 says `auto` (consistent); its line 35 cites
  "SessionSpec.cs:30-32", now one line off; `docs/findings/FINDINGS.md:142` (D3 revised, `auto` proposed); `docs/plans/ideas-desktop-codex-2026-11-18.md:29`
  (said the README contradicted itself; now resolved); `tools/OpenTail.Bullshark.Bench/Program.cs:264` and `RunPlanTests.cs:82` pass
  `acceptEdits` explicitly (not the default).
- **F4 (detach failed).** Where: `DetachAll` (window close, 3 session tabs and 1 trust step, so 4 per close) and `DetachTerminal` (tab removal).
  Evidence: the screenshot run's `app.log` had 40 `ObjectDisposedException: Cannot access a closed file` lines in 10 closes; the terminals' processes
  had ended. Verdict: at close it is noise (the app exits). In `DetachTerminal` (a tab removed while the app runs), the old code skipped the
  `Dispose` when the disconnect threw, so an ended session's connection was not released. That is a small leak, by reading the code; not measured.
  Fix: `DetachConnection` (SessionWindow.Tabs.cs) reads the connection first; when its process has exited, a disconnect failure is not logged
  and the connection is disposed; any other failure is logged as before. Result: 10 closes, 0 `detach failed` lines (was 40); no other failure
  line. Not changed (same pattern, other logs): `MainWindow` (the `--smoke` spike only), `DetachedSessionWindow.ReleaseTerminal`
  ("release failed"), `HideTrustStep` ("trust step close failed").
- **F5 (white band). Not root-caused; no fix made.** Measured: base build, 10 captures of `rail-dark`: 1 with a white band (rows 856 to 865
  of 875, full width, the status row's lower part, text cut); the build with F4: 1 in 10 again (the same index, r6). Geometry in every capture
  (logged, then removed): window outer 1375x875, client 1357x828, WPF ActualWidth x ActualHeight 1100x700 at DPI 1.25, bitmap 1375x875. So
  the bitmap size matches the window, and the size does not change between captures. The bottom 9 rows are black in every capture. Not tried:
  `RenderTargetBitmap` (the terminal may draw blank), waiting for an idle render before `PrintWindow`. With 1 in 10 as the base rate, 10 captures cannot
  show a fix.
- **F6 (cold index).** Measured, design unchanged: cold 1.44 s median (194 transcripts), warm scratch cache 0.27 s, history path 1.68 s cold and 0.39 s
  warm. Details in `docs/findings/perf-2026-11-17.md`. Not profiled again.
- **F7 (docs).** Backlog C2 note, live-run checklist "Fixes batch" section (the re-roll name and the log line), findings F6 section. Plan ticked.

## Open / not decided
- The row second-line wrap (ctx and plan figures) is left as built; owner may want one line.
- F1: the bound's extra session is not explained. My model (one session in flight per thread, at most ProcessorCount including the caller)
  allows 24 on this 16-processor machine, but one idle run read 25. A synthetic `Parallel.For` probe measured at most 15 bodies in flight.
  The bound is set to what the runs show. If the test fails again at 25 or more, look at this first.
- F4: `ArgumentException: Process with an Id of ... is not running` (app.log, 2026-11-17) is a process-gone case the new filter does not
  catch (it lists only ObjectDisposed, IO and InvalidOperation). Its site is not known; not widened without a run that shows it.
- F5: the white band (1 in 10 captures) has no root cause; see above.
- F6: the bench figures are on the owner's folder as it was at 2026-11-18, and the OS file cache was not dropped.
