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

- [ ] 1. **Measure first, no code change.** Use `tools/OpenTail.Bullshark.Bench` (read how it measures; add a `limits` mode there if missing, or a throwaway console under the scratchpad folder, not in the repo) to time on the owner's real `~/.claude/projects`: `LimitHistory.Read(root, now)` cold and warm, `LimitStatus.Active`, `LimitReset.Find`, three runs each, and record file counts, MB read and times in this file under "Measurements". Say what else was running (RAM, processes).
- [ ] 2. **Decide by the numbers.** If `LimitHistory.Read` warm takes under 1.0 s and `Active` under 100 ms, write "no change needed" with the numbers and stop (do not add code for its own sake). Otherwise do 3-5.
- [ ] 3. Per-file cache for the limit entries: key `(path, length, last write UTC)` -> the limit entries of that file (`LimitEntries` rows) and, for the token windows, whatever per-file usage records `LimitHistory` needs, so an unchanged file is never re-read. Only changed or new files are read. Look at `LimitHistory`, `LimitEntries`, `TranscriptReader.UsageRecords` and `IndexCache` first.
- [ ] 4. Tests: cache hit does not re-read (count reads through a seam or by file timestamps), changed file re-read, corrupt cache ignored, locked cache write never throws, results identical to the uncached path on a temp tree with several files (compare against the existing tests' expectations).
- [ ] 5. Re-measure and record before/after. Docs: tick this file; backlog line; one line in `session-summary-2026-11-17.md`.
- [ ] 6. One commit (two if the Bench tool changed), only these files, `git status` first. Do not push.

## How to check

`dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` (1167 passing now). If the full build fails only on locked App output files (a running App), build the App to a scratch OutDir and say so; do not stop any process.
