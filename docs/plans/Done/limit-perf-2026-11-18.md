# Limit scans: measure, then make them cheap, 2026-11-18

Why: `LimitHistory.Read` (Limits page, and the Away digest on every return) reads whole transcripts of the last 30 days: about 816 MB on the owner's
data, **never timed**. `LimitStatus.Active` / `LimitReset.Find` (status bar, once a minute) read the last 256 KB of each transcript of the last 8 days
(113 files, 390 MB). The owner wants it right before a real window run (migraine, cannot test; build and measure, adjust later).

## Contracts this must satisfy

| Member | Promise |
|---|---|
| `LimitHistory.Read`, `LimitStatus.Active`, `LimitReset.Find` / `FromLine` / `StartFor` | Same results and same public signatures; all existing tests pass unchanged. |
| `TranscriptFile` | Only way to read transcripts; a bad line / locked or vanished file is skipped. |
| Privacy | A cache holds counts, times, types and token totals only; no message text. |
| Cache files | Next to the other caches (`Activity/IndexCache.cs` is the pattern: key from a fresh stat; never fails the caller; locked atomic save; a corrupt cache is ignored and rebuilt). |

## Steps

- [x] 1. **Measure first, no code change.** Use `tools/OpenTail.Bullshark.Bench` (read how it measures; add a `limits` mode there if missing, or a throwaway console under the scratchpad folder, not in the repo) to time on the owner's real `~/.claude/projects`: `LimitHistory.Read(root, now)` cold and warm, `LimitStatus.Active`, `LimitReset.Find`, three runs each, and record file counts, MB read and times in this file under "Measurements". Say what else was running (RAM, processes). Done: the Bench got a `limits` step (no Core code changed); numbers under "Measurements".
- [x] 2. **Decide by the numbers.** If `LimitHistory.Read` warm takes under 1.0 s and `Active` under 100 ms, write "no change needed" with the numbers and stop (do not add code for its own sake). Otherwise do 3-5. Decided: `LimitHistory.Read` warm 2.4 s is over 1.0 s, so 3-5 are done. `Active` 48 ms is under 100 ms, so the status path gets no cache.
- [x] 3. (Built as described under "Built" below.) Per-file cache for the limit entries: key `(path, length, last write UTC)` -> the limit entries of that file (`LimitEntries` rows) and, for the token windows, whatever per-file usage records `LimitHistory` needs, so an unchanged file is never re-read. Only changed or new files are read. Look at `LimitHistory`, `LimitEntries`, `TranscriptReader.UsageRecords` and `IndexCache` first.
- [x] 4. (11 tests in `LimitHistoryCacheTests.cs`, all passing.) Tests: cache hit does not re-read (count reads through a seam or by file timestamps), changed file re-read, corrupt cache ignored, locked cache write never throws, results identical to the uncached path on a temp tree with several files (compare against the existing tests' expectations).
- [x] 5. Re-measure and record before/after (section "After" below). Docs: tick this file; backlog line (`backlog-2026-11-18.md`, item E1); one line in `session-summary-2026-11-17.md`.
- [x] 6. Two commits (the Bench step with the before numbers, then the cache), only these files, `git status` first. Not pushed.

## How to check

`dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` (1167 passing before; 1178 passing after this change, exit 0). If the full build fails only on locked App output files (a running App), build the App to a scratch OutDir and say so; do not stop any process. The App was built to a scratch OutDir too (`dotnet build src/OpenTail.Bullshark.App/... -o <scratch>`), as a second check.

## Measurements

**Before** (step 1, 2026-11-18, the code as committed in `00b86ac`, Bench `--only limits --repeat 3`, Release build in the scratchpad).

- Machine: 16 logical processors, 64 GB RAM, 36 GB free before the runs; CPU about 3% during them. Running and left alone: Visual Studio (devenv, with `OpenTail.Bullshark.App` under its debugger, about 340 MB), Chrome, Claude Code windows, DevHub. Nothing was stopped. One process, three runs per step.
- Data (the owner's `~/.claude/projects`, read only): 42 project folders, 184 transcripts, 831.5 MB, 6 over 20 MB, largest 60.2 MB.
- Windows: the history reads 183 transcripts, 793.9 MB (30 days and the 5-hour token window); the status reads 118 transcripts, 343.4 MB, of which the tails are 29.0 MB.

| Read | Files | Run 1 | Run 2 | Run 3 | Notes |
|---|---|---|---|---|---|
| `LimitHistory.Read` | 183 | 3.021 s | 2.416 s | 2.426 s | Each file is read twice in full (limit entries, then usage): about 1.6 GB of reads. |
| `LimitStatus.Active` | 118 | 0.056 s | 0.048 s | 0.047 s | Tails only. Limits in force: 1. |
| `LimitReset.Find` | 118 | 0.048 s | 0.047 s | 0.048 s | Reset in force: yes. |

- Hits 10, days 7. Peak working set 73-77 MB.
- "Cold" is run 1 in a fresh process. The OS file cache may still hold the files, so a true cold read (after a reboot) was not measured. The code keeps no cache, so runs 2-3 differ from run 1 only by the OS cache.
- Decision (step 2): `LimitHistory.Read` at 2.4 s warm is over the 1.0 s bar, so steps 3-5 go ahead. `LimitStatus.Active` at 48 ms is under 100 ms: the status path gets no cache.

**After** (step 5, the cache build, Bench Release, `--only limits --repeat 3 --cache <scratch file>`, the scratch cache removed before run 1).

- Data this run: the history window read 183 transcripts, 795.1 MB (before: 793.9 MB); the owner's live sessions were writing, so the two runs are on slightly different data. All transcripts: 832.7 MB.
- Machine: 36 GB free, CPU 2-10% during the run, same processes as before, nothing stopped.

| Read | Run 1 | Run 2 | Run 3 | Notes |
|---|---|---|---|---|
| `LimitHistory.Read` (no cache; one pass per file now) | 2.611 s | 1.623 s | 1.672 s | Was 3.021 / 2.416 / 2.426 s. Each file is read once. |
| `ReadWithCache` (App path) | 1.739 s | 0.123 s | 0.109 s | Run 1 builds the cache; runs 2-3 read it and re-read only changed files. |
| `LimitStatus.Active` (unchanged) | 0.056 s | 0.063 s | 0.056 s | Under 100 ms. |
| `LimitReset.Find` (unchanged) | 0.047 s | 0.045 s | 0.046 s | |

- Identical: the cached result equals the uncached result (hits 10, days 7). Checked by the Bench, which prints only "yes" or "no".
- Cache file: 13.2 MB for 183 transcripts. It holds counts, times, message keys (`message id:request id`), model names and token totals; no message text (test `TheCacheHoldsNoMessageText`).
- Peak working set: uncached 105 MB (before 73-77 MB, because the usage records are now kept in memory for the count); cached 143-175 MB, in the Bench process.
- Decision: the App's Limits page and Away digest now call `ReadWithCache`, whose warm read is 0.11 s, under the 1.0 s bar. The public `Read` (no cache; used by the tests and the Bench) is 1.6 s warm, still over 1.0 s: its signature cannot take a cache path, so it stays the reference. The status bar keeps `Active` and `Find` as they were (under 100 ms).

## Built

- Core `LimitHistory.cs`: `Read` keeps its signature and results; each transcript is read once (limit entries and usage in one pass), and the work is shared with `ReadWithCache`. New public method `ReadWithCache(projectsRoot, now, cachePath, days, zone, log)`: an addition, not a change to an existing signature.
- Core `LimitHistoryCache.cs` (new, internal): the file `cache/limit-history.json` beside `transcript-index.json` (the App's `LaunchOptions.LimitHistoryCachePath`). Key per transcript: path, length and last write time (UTC ticks), taken before the read. A changed, new or unreadable transcript is read; a transcript that left the window is dropped at the next save. A damaged cache or another build's (the reader id is the module id) is not used and is rebuilt. A save waits 1 s for the file's lock (short, so a read does not stall behind another window's save); a failed or locked save is said to the log, not thrown. Temporary files older than 10 minutes are swept.
- App: `LaunchOptions.LimitHistoryCachePath`; `SessionWindow.Limits.cs` and `SessionWindow.Away.cs` call `ReadWithCache` with `AppLog.Write`. `SessionWindow.LimitStatus.cs` (`LimitStatus.Active`) and `SetupView.xaml.cs` (`LimitReset.Find`) unchanged.
- Bench: `limits` step (uncached and, with `--cache`, cached, with the identity check). Counts only.
- Tests: `LimitHistoryCacheTests.cs`, 11 tests (identical results cold and warm, with an offset and milliseconds kept; unchanged transcript not read; grown and same-length-changed transcripts read; corrupt cache; another build's cache; unwritable cache path; cache held by another writer; no message text in the cache; deleted transcript pruned; unreadable transcript not cached). Existing `LimitHistoryTests` pass unchanged.
- Full suite: `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false`: 1178 passed, 0 failed, exit 0 (1167 before, plus 11). The App built to a scratch folder (`-o`), since the running App holds its output; it compiled, with one warning already in `MainWindow.xaml.cs`.
- Not done: the App was not run in a window, so the Limits page and the Away digest were checked by build and tests only; no true cold read (after a reboot) was measured; the cache's effect on the owner's first Limits page after the update is one cold read of about 1.7 s.
