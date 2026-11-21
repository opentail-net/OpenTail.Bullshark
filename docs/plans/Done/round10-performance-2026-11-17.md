# Round 10, 2026-11-17: speed on the owner's real transcripts

> Record. Restructured 2026-11-21: the open items of this plan are carried into the live files in start at [00-current-work.md](../00-current-work.md). The finished items are in the section at the end.

Status (2026-11-17, builder): items 1 and 4 done; item 2 partly done, item 3 partly done. Built on branch `round10-perf` (commits
`dc9e095`..`c6dc664`, one commit per fix), merged to `main` and pushed. Open: the first index read with no cache is still about 1.6 s
(over the 1 s line; a byte-level reader is the next step), and the window's own start was not timed, because its startup pass calls
the `claude` CLI. The numbers are in `docs/findings/perf-2026-11-17.md`; the record is F64 in `docs/findings/FINDINGS.md`.

Why: every speed check so far used small fixtures. The owner's `~/.claude/projects` holds 42 project folders, 162 transcripts,
866 MB, 7 files over 20 MB (planner, read-only `du`/`find`, 2026-11-17). The window reads these on start (history index), on every
pass (running facts, overlap, budgets, effort) and on demand (search, storage, transcript view).

## Rules

- Read the owner's transcripts **read-only**. Never write, move, copy or archive anything under `~/.claude`. Never commit any
  transcript content, file name, project name or session id from the owner's data: commit only code, synthetic tests, and timing
  numbers with sizes (MB, line counts).
- No Claude CLI calls except `--help`/`--version`; the app runs only with `--no-pass` and a scratch `--state`; it may use the real
  `--claude-home` read-only for timing (no clicks that move files; Storage archive must not be pressed).
- Own worktree and branch, `dotnet build` + `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` green before each commit, one
  commit per item, no push/merge, no new NuGet packages, public signatures kept.

## Contracts this must satisfy

| Member | Promise |
|---|---|
| `TranscriptIndex.Build` / `UsageLedger` | Same totals as before (each message counted once). |
| `HistorySearch` | Same results as before for the same query. |
| `FileOverlap` / `SessionWritesCache` | Same overlaps as a whole-file read. |
| `StorageReport` / `EffortReport` / `TranscriptPager` | Same output. |
| UI thread | No file or process work on it. |

## Items

- [ ] **2. Fix what is slow.** Anything over 1 s on this data, or over 300 MB peak, gets a fix (streaming instead of whole-file reads, caching by path+length+write time, avoiding repeated JSON parsing, parallel per-file work where it is safe for the ledger). Each fix: its own commit, a test that proves the output is unchanged (synthetic fixture), and the before/after numbers in the perf file.
  - Status: done, with a fix each: index in parallel (`334c8f7`), search pre-filter for escaped words (`34dfa37`), search in batches (`ef88740`), index cache (`c6dc664`). Open: the cold index read with no cache, about 1.6 s on this data.
- [ ] **3. Window start.** Time the window from start to the history rail being filled, with `--no-pass`, a scratch `--state` and the real `--claude-home` (read-only), using the existing screenshot option to end the run. If over 3 s, find why (log timestamps) and fix it in the same way as item 2. Record before/after.
  - Status: not done as written. The window's startup pass calls the `claude` CLI, so it was not run. The history path it waits for (index and tails) was timed with the bench: 1.8 s with no cache, 0.26 s with the cache. That is under 3 s, so no fix was made for it.

## Decisions made while building

- Item 2, index: the default `TranscriptIndex.Build` reads the files in parallel, each into its own ledger, and combines them in the
  one-after-another order. The one-after-another overload stays as the reference and for injected readers. Output is identical by
  test (every field, the model order, the skipped files), and a mutation of the usage rule fails it. Fresh-process median 3.88 s to
  1.57 s on the same snapshot. Peak rises from about 95 MB to 167-207 MB: under 300 MB, accepted.
- Item 2, index: the parse itself is now the cost (the probe: decoding lines 0.3 s, summarising 0.7-0.9 s on 16 threads). The
  GC mode, tiered JIT settings and skipping the text decode made no measurable difference, so they are not changed.
- Item 2, search: a word with a backslash or a quote was parsed line by line. Its longest plain run (`RawProbe`) is now the
  pre-filter. A message that holds the word holds that run unescaped in its raw line, so the matches do not change; a test compares
  twelve queries with a parse-every-line reference. Windows-path search 1.98 s to 0.89 s on the same snapshot.
- Item 2, search: the sessions are read in batches of 32 on several threads, and the results are taken one by one in the
  newest-first order, so the matches, the unreadable count and the stop at 100 are the ones the one-by-one loop gives. Tests cover a
  cap inside a batch and every unreadable session counted when all are read. Common word 0.95 s to 0.51 s warm (0.88 s cold); rare
  word 0.90 s to 0.34 s; Windows path 0.89 s to 0.35 s (same bench step, snapshot grown slightly between runs).
- Item 2, index cache (the plan's "caching by path+length+write time"): `TranscriptIndex.Build(root, cachePath)` keeps each transcript's
  summary and counted messages. An entry is used when the file's length and write time match and the cache names this build of the
  reader (its module id). Decisions inside it: (a) the cache lives beside the state file, so for the owner in
  `%LOCALAPPDATA%\OpenTail.Bullshark\cache`, not under `~/.claude`; it holds the last reply and the first prompt of each transcript
  (the same text the transcripts hold, now also on this disk, about 5 MB); (b) a file changed only in its contents, with the same
  length and write time, is not seen, which is the rule the plan names and a test pins; (c) an unchanged transcript that is now
  locked is served from the cache, where the reader would list it as skipped: the app only logs the skipped list, so the rail keeps
  the session; a changed locked transcript is left out as before; (d) the module id changes with the code, so a rebuild starts with a
  cold read once. Warm: index 1.6 s to 0.18 s, history path 1.8 s to 0.26 s on the same folder; `verify` gives identical hashes.
- Item 2, still over 1 s: the cold read with no cache (about 1.6 s). The next step is a byte-level reader, not done here.
- Item 3, window: the window cannot be run for timing. Its startup pass calls the `claude` CLI (`agents`, `daemon status`) even with
  `--no-pass`, and the brief allows no CLI calls. Item 3 times the history work the window waits for (index and tails) in the bench
  instead, and records that WPF start and the rail drawing are not measured.

## Done items (from the split of 2026-11-21)

## Items
- [x] **1. Measure.** A small console benchmark (new project `tools/OpenTail.Bullshark.Bench`, added to the solution, no packages) that times, on a given projects root (default: the real one, read-only): `TranscriptIndex.Build`, `HistorySearch` for three queries (a common word, a rare word, a Windows path), `StorageReport`, `EffortReport` and `TurnFacts` on the 3 largest files, `TranscriptPager.Open` + first/last page on the largest file, and one overlap/budget pass over 5 sessions. Report wall time and peak working set. Write results to `docs/findings/perf-2026-11-17.md` (numbers and sizes only).

- [x] **4. Record.** Add a finding (next F number) summarising the data size, the slow spots found and what changed; update the README Plans row and this Status line. (F64; README row; this status.)

## Decisions made while building
- Item 1: the counts at measuring time were 163 transcripts and 861.9 MB, not the planner's 162 and 866 MB (the owner's sessions
  changed during the day). The bench runs each step in its own process, twice, in Release, and the findings keep sizes and times only.
- Item 1: the bench prints no file, folder or session name, and a failure prints the exception type only (a message can hold a path).
  Its Windows-path query is a generic path that does not occur in the transcripts, so it is the worst case (every line parsed).
