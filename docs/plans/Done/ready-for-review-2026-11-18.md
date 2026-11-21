# "Ready for review" lane, 2026-11-18 (ideas D4)

Goal: a coder that finished (`done`) with changes is not a problem and not "blocked", but it is the thing the owner comes back for. Show it in
its own rail section, "Ready for review", apart from Needs you, until the owner has looked at it. No sound, no flash (it is not urgent), no
effect on `AttentionList`. Idea from Claude Code agent view (Needs input / Ready for review / Working / Completed), Linear Triage and Warp's
status model (see `ideas-2026-11-18.md`; researcher-reported).

## What exists (read first)

- `SessionWindow.Attention.cs`: builds the Needs-you rail section (`NeedsYouKey`, `LiveRow`, `StateWithWaiting`); `UpdateNeedsYouCount`.
- `AttentionList.From` / `AgentInfo` (`EffectiveState`: working, blocked, done, stopped, failed, ...), `SessionRecord` (names, roles), `SessionDigest` (`FilesWritten`, `LastActivity`, `LastAssistantText`).
- Changes tab (`SessionWindow.Changes.cs`, `ChangesView`): per-session git changes; the owner "looks at a result" there or in the session tab.
- `UiFile.Read/Set` (ui.json) for small persisted settings; `SessionWindow.Away.cs` shows how a timestamp is stored and read (`lastSeen`).
- Rail building: `SessionWindow.Rail.cs` (`BuildRail`/sections; find where Needs-you is inserted).

## Contracts this must satisfy

| Member | Promise |
|---|---|
| `AttentionList.From/Gained` | **Unchanged** (and its tests). Review items are a separate pure list; they never join Needs you, never count in `UpdateNeedsYouCount`, never flash or sound. |
| Principle 3 | Nothing is sent to a session. "Mark reviewed" only hides the row. |
| Persistence | Reviewed marks live in ui.json (`reviewed`: object sessionId -> ISO time of the session's `LastActivity` when marked). Missing/unreadable = nothing reviewed (never an error). A session is ready again when its `LastActivity` is later than the mark (it did more work). |
| UI thread | The list is built from in-memory records, listing and the window's transcript index; no disk read per pass except the ui.json read the window already does for other keys (cache it per pass or per change, do not read it per row). |

## Design decisions (AFK; change if you disagree)

- D1 Ready = the session is in the current runs' records, is listed as `done`, its role is a coder (a lead/planner that is `done` is not "ready for review": check how `SessionRecord`/specs name roles; if a role cannot be told, use "not the first session of the run"), has at least one file written (digest `FilesWritten.Count > 0`), and its `LastActivity` is later than the reviewed mark (or there is no mark).
- D2 Section "Ready for review (N)" sits right under Needs you, hidden when empty. Rows: session name, "N files · finished HH:mm". Row menu: "Mark reviewed", "Open Changes", "Open session tab". Opening the session's Changes tab marks it reviewed (the owner looked); opening only the session tab does not.
- D3 Order: oldest finished first (the one waiting longest).
- D4 The Away digest (D1 of the ideas) is unchanged. The palette gets one action "Mark all ready sessions reviewed" only if that is a few lines; otherwise leave it.

## Steps

- [x] 1. Core `Supervisor/ReviewQueue.cs`: pure `From(records, agents, digests, reviewed)` -> items (name, session id, files count, finished at). `ReviewedMarks` helper: parse from and write to a `JsonObject` (ISO times), tolerant of bad entries.
- [x] 2. Tests: done coder with files = ready; done planner = not; done coder with no files = not; working/blocked/stopped = not; reviewed mark equal to `LastActivity` = hidden; later `LastActivity` = ready again; no digest for the session = not ready; session not in records ignored; order oldest first; bad marks in the JSON ignored.
- [x] 3. App: the section in the rail, menu, marking (ui.json through `UiFile`), hook in the Changes-tab opening; build the list per pass in the existing pass-refresh path. Do not touch `AnnounceAttention`.
- [x] 4. Docs: tick this file, tick backlog D4, one line in `session-summary-2026-11-17.md`, "Decisions" section for anything you chose.
- [x] 5. One commit, only these files, `git status` first. Do not push. (Committed; not pushed.)

## How to check

`dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` (1092 passing now) and `dotnet build OpenTail.Bullshark.slnx -nodeReuse:false` (App; `dotnet test` alone does not build it). Say plainly that the section, its menu and the mark-on-Changes were not seen in a running window.

Result (2026-11-18, coder): `dotnet build OpenTail.Bullshark.slnx -nodeReuse:false` 0 errors (1 warning, in `MainWindow.xaml.cs`, not ours). `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` exit 0, 1108 passed (1092 + 16 new in `ReviewQueueTests`). Not seen in a running window: the section, its menu, the mark on Changes, the palette action.

## Decisions (coder, 2026-11-18)

- **Source of the facts (changes D1's "digest").** The window's transcript index (`_history`) is read only on window open, on 🔄 and on an Away return, so a coder that finished after the last read would never show. The lane uses the reads the pass already makes: files = the transcript's writes from the writes cache (`ReadWrites`); finished = the transcript file's last write time (one stat per finished coder per pass). Both count each file once, as the index does (`TranscriptReader` and `FileOverlap` add to a set first). Read on a pool thread in `PassAndRefreshAsync` (`ReviewSourcesOf`). Not yet compared with the index on real transcripts.
- **"Finished HH:mm"** is the transcript's last write, so it can differ by a few seconds from the moment the state changed to `done`.
- **Role** is `SessionRecord.Role == Coder`. No "first session of the run" fallback was needed.
- **`done`** is read from the pass listing with completed sessions (`_allAgents`), as the merge check reads it.
- **Marks** are read from ui.json once, at window start (`InitReview`), and kept in memory; each mark writes the `reviewed` object and keeps the other keys. A mark with no pass source (the coder is not done, or its transcript was not read) is stamped with the current time.
- **Opening Changes marks the session**, for any session, after the Changes tab is active (so the rail redraw leaves the pane alone).
- **Open session tab** uses the palette's `GoToSession`; it does not mark.
- **First run**: every finished coder with files in the current state records, and no mark, is shown until marked. No migration. "Mark all ready sessions reviewed" in the palette clears them.
- **Palette action** added (D4 said "only if a few lines"): "Mark all ready sessions reviewed", listed only when some coder is ready.
- **Rows** show the owner's name (`LiveTitle`) and the file count and finish time. No activity line, no ⚠ flags.
- **Placement**: under Needs you, on the live filters only (All, Running), as Needs you is.
- **Marks are not pruned.** One entry per session id.
- **Open idea (not built):** a mark is written with the pass's time, so a transcript write in the seconds between the pass and the click shows the coder again. Not seen happening.
