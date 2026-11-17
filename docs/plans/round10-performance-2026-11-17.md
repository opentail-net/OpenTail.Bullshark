# Round 10, 2026-11-17: speed on the owner's real transcripts

Status: not started.

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

- [ ] **1. Measure.** A small console benchmark (new project `tools/OpenTail.Bullshark.Bench`, added to the solution, no packages) that times, on a given projects root (default: the real one, read-only): `TranscriptIndex.Build`, `HistorySearch` for three queries (a common word, a rare word, a Windows path), `StorageReport`, `EffortReport` and `TurnFacts` on the 3 largest files, `TranscriptPager.Open` + first/last page on the largest file, and one overlap/budget pass over 5 sessions. Report wall time and peak working set. Write results to `docs/findings/perf-2026-11-17.md` (numbers and sizes only).
- [ ] **2. Fix what is slow.** Anything over 1 s on this data, or over 300 MB peak, gets a fix (streaming instead of whole-file reads, caching by path+length+write time, avoiding repeated JSON parsing, parallel per-file work where it is safe for the ledger). Each fix: its own commit, a test that proves the output is unchanged (synthetic fixture), and the before/after numbers in the perf file.
- [ ] **3. Window start.** Time the window from start to the history rail being filled, with `--no-pass`, a scratch `--state` and the real `--claude-home` (read-only), using the existing screenshot option to end the run. If over 3 s, find why (log timestamps) and fix it in the same way as item 2. Record before/after.
- [ ] **4. Record.** Add a finding (next F number) summarising the data size, the slow spots found and what changed; update the README Plans row and this Status line.

## Decisions made while building

(builder writes here)
