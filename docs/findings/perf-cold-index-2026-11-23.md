# Cold index read, measured again on 2026-11-23

Top-10 item 5. The owner's question 6 in the status doc asks whether the first index read (no cache, about 1.35 s on the owner's data, over the 1 s line) is accepted or planned. This note gives the number, and where the time shows up.

## Measurement

Tool: `tools/OpenTail.Bullshark.Bench`, `--only index --repeat 3` (no `--cache`, so each build reads every transcript), Release, net10.0. It prints counts and times only, never a file or session name.

Machine: 16 logical processors, Windows 11. The owner's projects folder: 42 project folders, 350 transcripts, 1188.3 MB, 8 over 20 MB, largest 60.2 MB.

| Run | Time | Peak working set | Sessions | Messages counted |
|---|---|---|---|---|
| 1 | 1.633 s | 103 MB | 350 | 38255 |
| 2 | 1.261 s | 123 MB | 350 | 38256 |
| 3 | 0.561 s | 140 MB | 350 | 38256 |

Runs 1 and 2 are the cold read: the app has no cache, so it parses every transcript. Run 3 is faster because the OS file cache is warm for the same files. The 2026-11-18 note gave a cold median of 1.44 s. The median of runs 1 and 2 here is 1.45 s, so the figure has not moved.

## Where it runs

The App builds the index on a pool thread (`SessionWindow.Rail.cs`, `Task.Run(() => ActiveClaude.BuildIndexWithMemo(...))`), so the UI thread is not blocked by the read. A cache file makes the normal start warm. The cold read happens on the first run, or after the cache is missing.

So the user-visible cost is that the rail's first population is about 1.3 s late after a cache miss. It is not a freeze.

## Decision for the owner (not changed)

- Accept about 1.3 s on a cache miss, or
- plan the next step from the 2026-11-17 notes: a parser that reads the UTF-8 bytes without building a DOM. The JSON parse is most of the cold read there too.

No code was changed for this measurement.
