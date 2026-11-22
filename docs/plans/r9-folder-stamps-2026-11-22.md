# R9: folder stamps and scoped history refresh (2026-11-22)

Status: built by the coder on 2026-11-22 and reviewed by the orchestrator (one fix: the signature uses the message count the row shows). Core 2668 (net11.0) and 2655 (net10.0), App 145 on each target, all passing. Not run on screen. Row R9 in [00-current-work.md](00-current-work.md). Concept note B in
[../research/concept-notes-index-usage-2026-11-22.md](../research/concept-notes-index-usage-2026-11-22.md).

## Goal

Keep the Claude session list (the history in the rail) current without re-reading every transcript. Today the history is read when the
window opens, on the 🔄 button, after a Storage move, and on return from Away. `index.refreshSeconds` (default 60; 0 = only on open) and
`index.stamp` (`countAndNewest`) are parsed into `BehaviourSettings.Index` and nothing reads them. This plan builds the reader's use of them.

Not in this task: a file watcher (the concept note's option is not taken); Codex history (its own source, `CodexHistorySource`); persisting
stamps to disk; changing the disk cache format.

## Contracts this must satisfy

- `TranscriptIndex.Build(projectsRoot)` and `TranscriptIndex.Build(projectsRoot, cachePath, log)` give the same index (existing contract, kept).
- `TranscriptIndex.BuildFull(projectsRoot, cachePath, log).Index` equals `Build(projectsRoot, cachePath, log)`.
- `TranscriptIndex.RefreshScoped(projectsRoot, memo).Index` equals `Build(projectsRoot)` for the same folder state, in the same session order,
  with the same totals and skipped list, except for the blind spot below.
- `Combine` is unchanged: reads are combined in listing order (CreationTimeUtc, then FullName), and each read carries its own ledger, so a
  reused read and a fresh read combine the same way.
- `IndexCache` is unchanged: `FormatVersion` stays 2. The scoped refresh never loads or writes `IndexCache`.
- A file whose fresh stat says it was written within `LiveWindow` (2 minutes) is always read again.
- A `TranscriptIndexMemo` is never changed after it is returned. Each refresh returns a new memo, so a background read and the window never
  share a mutable object.
- A stamp is taken from the listing before any file of its folder is read. A memo folder holds one stamp and the reads made after it.
- App: history reads run off the UI thread. The window's history fields change on the UI thread only.

## Decisions

1. **Folder** = `FileInfo.DirectoryName` (ordinal, case-insensitive key). The `subagents` directory is its own folder.
2. **Stamp** = (count of `*.jsonl` files in the folder, newest `LastWriteTimeUtc` ticks, total `Length`). Built from the same listing
   `ListFiles` already makes (no second listing, no extra I/O). Compared for equality.
3. **FULL** (window open, 🔄, after a Storage move, on return from Away) = today's cache build, unchanged in what it reads. It also returns
   the memo (stamps and reads) for the next SCOPED refresh. FULL never uses the memo, so it is the check for the blind spot below.
4. **SCOPED** (the periodic refresh): a folder whose stamp equals the memo's stamp keeps its reads, except that
   - a file whose fresh stat says it is live is read again (the live rule), and
   - a file whose stat says it no longer exists is read again (so it is skipped, as FULL skips it), and
   - a file whose last read failed is read again (a locked file that opens later must not stay skipped).
   A folder whose stamp differs, or that the memo does not have, is read in full, one read per file, with the same reader as FULL but without
   the disk cache (loading the cache file costs the whole file's parse, which is what SCOPED avoids).
5. **Blind spot (accepted by the owner, documented in `RefreshScoped` and tested):** a file replaced by a copy of the same length with an
   older write time, under an unchanged count and newest time, is missed by SCOPED until the next FULL. The listing already gives each file's
   length and write time, so a per-file compare would close the gap with no extra I/O; that is NOT done here, because the owner decided it.
6. **Memo** is in memory only, owned by the window. A restart runs FULL, which makes a new memo. No stamps are written to disk.
7. **Order** is part of the signature. `HistoryOrder.Sorted` uses a stable `OrderBy` with no tie-break, so sessions with equal activity
   keep the index order, and the rail shows that order. A reordering of the same sessions is therefore a visible change and gives a
   different signature. This differs from the example in the brief ("a reordering gives the same signature"); the brief left the choice to
   the coder. It is one line to flip in `SessionListSignature.Of` if the owner prefers the other.
8. **Signature** of the list = session id, LastActivity (UTC ticks, or "-"), AssistantMessages (the "N msgs" the row shows; changed by the orchestrator's review from Turns, which the row does not draw), Title, ProjectFolder (the rail's group key), and Cwd (the
   name the rail and Storage show). Each text field is length-prefixed, so the text is unambiguous. The brief's "folder" is read as both.
9. **Redraw**: a SCOPED refresh replaces `_history` and the facts silently. It redraws the rail (and the Storage names) only when the
   signature changed. It does not call `RefreshRail` at its start, and it does not set `_historyLoading` (the rail's signature includes that
   flag, so setting it would redraw the rail twice with no change). A separate `_historyRefreshing` flag guards it.
10. **Coalescing**: a FULL request during a SCOPED read sets `_historyFullPending`; when the SCOPED read ends, one FULL read starts. A SCOPED
    request during any read does nothing (the brief's guard). This follows concept note B's "one refresh at a time; requests during a run queue
    one follow-up".
11. **Trigger**: `_timer` (15 s) starts a SCOPED refresh when `index.refreshSeconds` > 0, the history has been read once, neither read is
    running, and the last read (FULL or SCOPED, by its finish time) is at least `refreshSeconds` old. So the refresh runs every 60 to 75 s.
12. **Failure**: a SCOPED read that throws keeps the last good list and memo, logs the exception type only, and sets the last-read time so
    the next try waits a full interval.
13. **Facts** (the cache-label facts, one 64 KB tail read per transcript): FULL reads them all, as today. SCOPED reads them only for the
    sessions whose file was read again, and keeps the previous facts for the others. Without this, every SCOPED refresh would re-read a tail
    of every transcript.
14. **Logs**: counts and step names only. No ids, titles, folders or paths. SCOPED logs `history refresh: N folders reused, M folders read
    (K files), changed yes|no`. A failure logs the exception type name only.

## Mapping of the reuse rule (SCOPED, one row per branch)

| Case | Folder stamp | Memo read for the file | Fresh stat | Result |
|---|---|---|---|---|
| Folder not in memo | - | - | - | read |
| Stamp differs | differs | any | - | read |
| Stamp equal, no memo read for this full name (renamed or new file) | equal | missing | - | read |
| Stamp equal, memo read failed | equal | error | - | read |
| Stamp equal, file gone at stat | equal | ok | not exists | read (skipped, as FULL) |
| Stamp equal, file live | equal | ok | written within 2 min | read |
| Stamp equal, file not live | equal | ok | older | reused as it is |

FULL: the same branches as today's cache build (cache entry with the same length and write time, not live, is taken; else read).

## Checklist

- [x] 1. Core `FolderStamps.cs`: `FolderStamp` (record struct: Count, NewestTicks, TotalLength) and `FolderStamps.Of(IEnumerable<FileInfo>)`
      grouped by `DirectoryName`. Why: the stamp must come from the listing alone, and be comparable for equality.
- [x] 2. Core `TranscriptIndexMemo.cs`: public `TranscriptIndexMemo` (per folder: stamp and its reads by full name; read-only once made)
      and public `TranscriptRefresh` (Index, Memo, ReadFiles, FoldersReused, FoldersRead). `TranscriptIndex.FileRead` becomes internal. Why:
      the memo must hold the reads the combine uses, and the window must learn which files were read again.
- [x] 3. Core `TranscriptIndex.cs`: `BuildFull` (the body of the cache build, moved; `Build(projectsRoot, cachePath, log)` returns
      `BuildFull(...).Index`). It takes the stamps before the cache is loaded and the reads start. Why: "commit order" (stamps before reads)
      and one code path for the cache build.
- [x] 4. Core `TranscriptIndex.cs`: `RefreshScoped(projectsRoot, memo)` with the reuse rule above. Why: SCOPED without the disk cache.
      The XML doc names the blind spot.
- [x] 5. Core `SessionListSignature.cs`: `SessionListSignature.Of(IEnumerable<SessionDigest>)`, pure, order-sensitive (decision 7).
- [x] 6. Core `ClaudeProvider.cs`: `BuildIndexWithMemo(cachePath, log)` and `RefreshIndex(memo)` next to `BuildIndex`, which is unchanged.
- [x] 7. App `SessionWindow.Rail.cs` `LoadHistoryAsync`: FULL through `BuildIndexWithMemo`; keeps the memo, the facts and the signature
      together; sets the last-read time in `finally`; a FULL request during a SCOPED read sets `_historyFullPending`. Existing callers
      unchanged. Why: FULL must keep today's behaviour exactly, plus the memo.
- [x] 8. App new `SessionWindow.HistoryRefresh.cs`: fields (`_historyMemo`, `_historyRefreshing`, `_historyFullPending`,
      `_historyReadAt`); `MaybeRefreshHistory` (called from the 15 s tick); `RefreshHistoryAsync` (SCOPED, redraw only on change, facts
      merge, log). Why: decisions 9 to 14.
- [x] 9. App `SessionWindow.xaml.cs`: the 15 s tick calls `MaybeRefreshHistory()` before the pass. No new timer. Why: decision 11.
- [x] 10. Tests (Core, temporary folders only): see the test list.
- [x] 11. Build and test: Core.Tests net11.0 and net10.0 (`--filter "Category!=Live"`); App.Tests net11.0-windows and net10.0-windows.
      Record counts and exit codes. Why: the brief's gates.
- [x] 12. Report: what the window's refresh does before and after; what was checked by reading only (the window was not run).

## Tests

Core only (the App has no test seam for the window; App.Tests counts must stay 145 on each target).

- `FolderStampTests`: a count change, a newest-time change and a length change each change the stamp; an unchanged folder gives an equal
  stamp; files in a folder and in its `subagents` folder are two stamps.
- `TranscriptIndexScopedTests`:
  - Differential: BuildFull on a tree, then RefreshScoped after each change (appended, not live; a new live transcript; a deleted transcript;
    a same-length replacement that changes the newest time). After each change the SCOPED index's full description equals `Build(root)`.
  - No change: SCOPED re-reads 0 files and reuses every folder (counts from the result).
  - A live file is re-read when its folder's stamp is unchanged (the file was written now, the memo was made just before).
  - A failed read is read again when the file can be read later (a file held open with no sharing when the memo is made).
  - Blind spot: a same-length replacement with an older write time under an unchanged count and newest time. SCOPED still shows the old text
    (documented, accepted); BuildFull (the cache build, which sees the write time change) shows the new text.
- `SessionListSignatureTests`: the same list gives the same signature; a changed LastActivity, AssistantMessages, title, folder or Cwd changes it; a
  removed or added session changes it; a reordering changes it (decision 7).

## Review notes

Coder self-check only; no separate review requested (brief: the orchestrator reviews the diff).
