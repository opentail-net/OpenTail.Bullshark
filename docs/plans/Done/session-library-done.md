# Session library (2026-11-16) (done items)

> Split from [session-library.md](../session-library.md) on 2026-11-21. These items are checked in the original; the open items stay there. The headings above each item are its place in that plan.

## Steps
### L1 Free facts from the transcripts (Core)
- [x] `TranscriptSummary` and `SessionDigest` gain optional members: `AiTitle` (the last `ai-title`), `CustomTitle` (the last
      `custom-title`), `LastRecap` and `LastRecapAt` (the newest `away_summary`), `Compactions` (count of `compact_boundary`).
- [x] `SessionDigest.Title` follows D-L1 without the owner's name (that comes in L3): `AiTitle`, else `FirstUserText`, else the id.
- [x] Tests on fixtures: `TheLastAiTitleWins`, `ATranscriptWithNoAiTitleKeepsTheFirstMessage`, `TheNewestRecapIsKept`,
      `CompactionsAreCounted`, `AnAiTitleRecordWithNoTextIsIgnored`. Checked in `tests/.../Activity/TranscriptFactsTests.cs`, with
      three more: `TheTitleIsTheAiTitleThenTheFirstMessage`, `TheCustomTitleIsNotTheTitle`, `TheTitleFallsBackToTheShortId`.
- [x] A check on the real folder, in a script, not a unit test: `scripts/Check-Titles.ps1` (read-only; see below).

### L2 The rail shows them (App)
- [x] The row title uses the new `Title`: seen on screen. The tooltip adds the recap (with its age), the `custom-title` when there
      is one, and "compacted N times" when N is above 0. Built as `FactsTip` in `SessionWindow.Rail.cs`. Not seen on screen: a
      tooltip needs a hover, and hovering was not done. Keep this box open until a hover is checked.

### L3 The library store (Core)
- [x] `SessionLibrary` in `Core/Library/`: `Load()`, `Update(change)`, the entry record, following `RunStore` (lock, atomic
      write, missing file is empty, unreadable file throws `StoreUnreadableException`). `Beside(stateFile)` gives its path. Built: the property is `FilePath`, because `Path` would hide `System.IO.Path`.
- [x] `TitleFor(digest, entry)` applies D-L1 in full. `TagsOf(text)` parses the typed tags (D-L6). Both are static methods on
      `SessionLibrary`.
- [x] Tests: `AMissingFileIsAnEmptyLibrary`, `AnUnreadableFileThrowsAndIsNotOverwritten`, `AnEmptyEntryIsRemoved`,
      `TwoUpdatesBothLand`, `TheOwnersNameComesFirst`, `TagsAreSplitAndLowerCased`.

### L4 Organise in the rail (App)
- [x] The Pinned group (D-L4) and the Archived filter (D-L5), with its own `FilterLabel` branch. Seen: the Pinned group at the top of
      History (dark and light); the archived session hidden in All (dark); the Archived filter, started with `--rail-filter archived`,
      lists it muted (light).
- [x] An unreadable library follows D-L3. Seen: the rail shows; a notice "library.json cannot be read" is at the top of History; the log
      has the line "library not read"; the file is not overwritten (`scripts\Test-Library.ps1`, check 6).

### L5 Search and lineage
- [x] `HistorySearch.Run` takes an optional `Func<SessionDigest, string?>` giving a session's extra text (D-L7). Tests:
      `AWordInTheOwnersNameMatches`, `AWordInATagMatchesWithNoMatchingMessage`, `EveryWordIsStillNeeded` (`HistorySearchExtraTextTests.cs`).
      The window passes the owner's name, tags and note (`SearchableText`), read from a snapshot of the marks.

- [x] `ResumeText.Compose` takes the source session id and writes the `Continues session:` line (D-L8). Tests:
      `ResumeContinuesLineTests` (the first line; no line without a source; read back by `SessionTitle.ContinuesIdOf`). The resume passes
      the id (`SessionWindow.Resume.cs`). The two existing tests compile unchanged, because the parameter is optional.
- [x] `TranscriptReader` reads that line from the first user message into `SessionDigest.ContinuesSessionId`. Tests:
      `TheContinuesLineIsRead`, `ALineLikeItLaterInTheSessionIsIgnored` (`TranscriptLineageTests.cs`).

- [x] A session resumed from a summary has no title from its start text (`SessionTitle.Clean` skips "Continues session:" and "This session
      continues…"). Its title is "↪ " and the earlier session's title when it has no `ai-title` and no name:
      `SessionLibrary.TitleFor(digest, entry, continued)`, tested in `LineageTitleTests.cs` (five tests). Not seen on screen: no resumed
      session exists.
