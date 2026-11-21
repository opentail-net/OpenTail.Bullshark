# Round 10, 2026-11-17: speed on the owner's real transcripts (done items)

> Split from [round10-performance-2026-11-17.md](../round10-performance-2026-11-17.md) on 2026-11-21. These items are checked in the original; the open items stay there. The headings above each item are its place in that plan.

## Items
- [x] **1. Measure.** A small console benchmark (new project `tools/OpenTail.Bullshark.Bench`, added to the solution, no packages) that times, on a given projects root (default: the real one, read-only): `TranscriptIndex.Build`, `HistorySearch` for three queries (a common word, a rare word, a Windows path), `StorageReport`, `EffortReport` and `TurnFacts` on the 3 largest files, `TranscriptPager.Open` + first/last page on the largest file, and one overlap/budget pass over 5 sessions. Report wall time and peak working set. Write results to `docs/findings/perf-2026-11-17.md` (numbers and sizes only).

- [x] **4. Record.** Add a finding (next F number) summarising the data size, the slow spots found and what changed; update the README Plans row and this Status line. (F64; README row; this status.)

## Decisions made while building
- Item 1: the counts at measuring time were 163 transcripts and 861.9 MB, not the planner's 162 and 866 MB (the owner's sessions
  changed during the day). The bench runs each step in its own process, twice, in Release, and the findings keep sizes and times only.
- Item 1: the bench prints no file, folder or session name, and a failure prints the exception type only (a message can hold a path).
  Its Windows-path query is a generic path that does not occur in the transcripts, so it is the worst case (every line parsed).
