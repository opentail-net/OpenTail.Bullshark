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
- [ ] 3. Per-file cache for the limit entries: key `(path, length, last write UTC)` -> the limit entries of that file (`LimitEntries` rows) and, for the token windows, whatever per-file usage records `LimitHistory` needs, so an unchanged file is never re-read. Only changed or new files are read. Look at `LimitHistory`, `LimitEntries`, `TranscriptReader.UsageRecords` and `IndexCache` first.
- [ ] 4. Tests: cache hit does not re-read (count reads through a seam or by file timestamps), changed file re-read, corrupt cache ignored, locked cache write never throws, results identical to the uncached path on a temp tree with several files (compare against the existing tests' expectations).
- [ ] 5. Re-measure and record before/after. Docs: tick this file; backlog line; one line in `session-summary-2026-11-17.md`.
- [ ] 6. One commit (two if the Bench tool changed), only these files, `git status` first. Do not push.

## How to check

`dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` (1167 passing now). If the full build fails only on locked App output files (a running App), build the App to a scratch OutDir and say so; do not stop any process.

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
