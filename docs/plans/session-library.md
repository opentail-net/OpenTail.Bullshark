# Session library (2026-11-16)

> Split on 2026-11-21: the done items moved to [Done/session-library-done.md](Done/session-library-done.md). This file keeps the open items.

Status (2026-11-16): L1 to L3 are in commit 72cee28. Committed on 2026-11-16 in two commits: the L5 core (1a2a1ff), then L4 and the L5 screens (the commit after it). L4 is built and checked where no click is needed; the clicks are for the owner. The on-screen items of L5 are built and not checked. The one real resume is not run. L6 is not to be built.

Make past sessions easy to recognise, sort and find again: better titles and a recap taken from the transcripts for free, then the
owner's own name, pin, archive, tags and note for each session, then a link from a resumed session to the one it came from.

This plan covers one system: what the rail and the history know about a past session. The other ideas from the 2026-11-16 list
are named at the end and get their own plans.

## Where this came from

- The ideas list of 2026-11-16 (other tools were surveyed; the owner asked for "visualise, search, organise").
- The owner, 2026-11-16: "session names are not great". Today a title is the first message the owner typed (`SessionTitle`).
- The Fable review, `docs/plans/review-2026-11-15.md`. Its idea "cache the history index by path, size and time" is slice L6
  here. Its Phase 1 is built (commit 51b51e7). Its Phases 2 to 4 are open, and it asks for a freeze on new CLI switches,
  worktrees, the orchestrator and new patterns until its Phase 3 run passes. This plan stays inside that freeze: it starts no
  session, calls no model, and changes nothing in the supervisor, the stores `state.json` and `runs.json`, or the CLI calls. Its
  one touch on resume is a line of text (L5).

## What the transcripts already hold (observed 2026-11-16, CLI 2.1.325)

Counted with `grep` over the 126 transcript files directly under the project folders of `~\.claude\projects`. These record
types are not documented by the vendor, so the reader must work when they are absent or change shape.

| Record | Example | Files that have it | Use |
|---|---|---|---|
| `"type":"ai-title"` with `aiTitle` | "Claude command execution options" | 70 of 126 | a title written by Claude Code itself. It is repeated many times in one file (838 times in one), so the last one wins. |
| `"type":"custom-title"` with `customTitle` | "ovc-coder" | 46 | the session's name (`--name`). It is a role name, the same for many sessions. |
| `"type":"system"`, `"subtype":"away_summary"`, `content` | "The goal is a history search for the rail. The filter and light theme are built, but… Next: …" | 40 | a short recap written by Claude Code, with a timestamp. Free: no call of ours. |
| `"type":"system"`, `"subtype":"compact_boundary"`, `compactMetadata.preTokens` / `postTokens` / `trigger` | 968,714 to 34,807, auto | 24 | where the context was compacted. This answers the open question in `history-and-usage.md`. |

## Contracts this must satisfy

Read in the code on 2026-11-16.

- **`TranscriptReader.Summarize(lines, ledger, fileName)`** (Core/Activity/TranscriptReader.cs): one pass over the lines; a
  blank or invalid line is skipped; never throws on a bad record. Returns `TranscriptSummary`, a record whose later members are
  optional with defaults. New members are added the same way, so the existing tests and callers compile unchanged.
- **`SessionDigest`** (Core/Activity/TranscriptIndex.cs): a positional record; `FirstUserText` is the last, optional member.
  `Title` is `FirstUserText`, or "Session " and the short id. Used by the rail, `SearchView`, `ResumeAsync` ("Resume: {Title}")
  and `HistorySearch`. New members are optional and go at the end.
- **`TranscriptIndex.Build(root)`**: reads every `*.jsonl` oldest first by creation time, with one shared `UsageLedger`, so a
  message copied into a later file is counted against the file it first appeared in (F35). Any cache must keep that rule (L6).
- **`HistorySearch.Run(sessions, query, includeSubagents, cancellationToken)`**: every word must appear somewhere in the
  session; only "you" and "claude" lines are searched; newest first; at most 100 sessions; three newest hits each; a file that
  cannot be read is left out; cancellation throws. `SessionMatch(Session, MatchingMessages, Hits)`.
- **`FileLock.Hold(path)` and `FileLock.WriteAtomic(path, text)`** (Core/Supervisor/FileLock.cs): a named mutex per full path,
  10 s wait, then `TimeoutException`; a write goes to a unique temporary file and is moved over the file.
- **`RunStore`** is the pattern for a store: `Load()` under the lock; `Update(change)` reads again inside the lock, then writes;
  a missing file is empty; an unreadable file throws `StoreUnreadableException` and nothing is written.
- **Files beside the state file**: `runs.json`, the pattern store (`PatternStore.Beside`) and the theme (`ThemeStore`) sit in the
  folder of `--state`. A new file must sit there too, so a run with its own state keeps its own data (review finding 16).
- **The rail** (App/SessionWindow.Rail.cs): `RefreshRail()` rebuilds every item on each refresh (every 15 s and on demand).
  `FindItem` matches a row by kind and key only, so one session must have one row. `RestoreSelection` keeps the pane for a search
  or a transcript tab. `_expanded` remembers what is collapsed, by key.
- **`RailFilter`**: `All`, `Running`, `History`, `Temporary`. `ShowsLive` is true for All and Running. `ShowsHistory` is true
  for every filter but Running. `FilterLabel` has a default branch that returns "All", so a new value needs its own branch.
- **`ResumeText.Compose(summary, level, tail, turns)`** (Core/Resume/ResumeText.cs): two callers, `SessionWindow.Resume.cs:148`
  and two tests. Its text becomes the task of a solo run, which is passed to `claude` as a command-line argument (review
  finding 11).
- **The app only reads `~\.claude`.** It never writes there. This plan keeps that.

## Existing behaviour of the history rows, branch by branch

`HistorySection` and `HistoryFolderItem`, as read.

| # | Branch | Now | Change |
|---|---|---|---|
| 1 | `_history` is null | one muted row: "reading the transcripts…" or "not read yet" | keep |
| 2 | a search is active (`IsShownInHistory`) | only the sessions that matched are listed | keep; an archived session that matched is listed, muted (D-L5) |
| 3 | grouping | by `ProjectFolder`; folders ordered by their newest session | keep, below the new Pinned group |
| 4 | temporary folders | listed only by the Temporary filter | keep; the Archived filter follows the same rule as All |
| 5 | session already open (`openIds`) | no ⏩ on its row | keep, in the Pinned group too |
| 6 | sessions in a folder | newest first, the first 200 | keep; pinned sessions are not counted in the 200 |
| 7 | row title | `digest.Title`, plus " · subagent" | the title by the order in D-L1 |
| 8 | second line | mark, age, short id, "N msgs", ⏩ at the right | keep; tags are added after "N msgs" |
| 9 | tooltip | newest search match, the cache label, the file path | add the recap, the note, and "continues …" when known |
| 10 | first folder | expanded by default; the owner's collapse is kept | keep; Pinned is expanded by default |
| 11 | live rows (`RefreshRail`) | title is `record.Name`; a failed start is a muted row | the owner's name when one is set (D-L1); failed starts unchanged |

## Decisions

Recommended by the assistant. The owner has not confirmed them.

- **D-L1 Title order.** The owner's name; else the `ai-title`; else the first message (today's title); else "Session " and the
  short id. `custom-title` is not used as a title, because it is a role name; it is shown in the tooltip.
- **D-L2 One file.** `library.json` beside the state file, keyed by session id. Each entry: `Name`, `Pinned`, `Archived`, `Tags`,
  `Note`, `Colour`. An entry with nothing set is removed on write. Entries are never removed because a transcript has gone.
- **D-L3 An unreadable `library.json` does not block the rail.** The rail shows without the owner's marks, the status line says
  the file cannot be read, and nothing is written until it can be.
- **D-L4 Pinned.** One "Pinned" group at the top of the History section. A pinned session is listed there only, not also under
  its folder (one row per session, see the rail contract). It shows in All and History.
- **D-L5 Archived.** Hidden in All, History and Temporary. A new filter "Archived" lists them. A search still finds them and
  lists them muted. Archive only hides: no file is moved or deleted.
- **D-L6 Tags.** Free text, lower case, separated by spaces or commas when typed. No fixed list.
- **D-L7 Search reads the library too.** A word of the query may be found in the owner's name, tags or note of a session. Such a
  session can match with no matching message; its result says "matched its name, tags or note".
- **D-L8 Lineage is in the transcript, not in a store.** The resume text gets one more line, `Continues session: <full id>`. The
  index reads it from the first message of the new session. Nothing has to be remembered while the new session starts, and a
  closed run cannot lose it. Only sessions resumed after L5 carry it. A full resume keeps its session id, so it needs no link
  (whether its transcript file stays the same is not verified, F20).
- **D-L9 No new dependency.** JSON with `System.Text.Json`, as the other stores. No database.
- **D-L10 Edit in one dialog.** A right click on a session row opens a menu: Pin or Unpin, Archive or Unarchive, Edit…. "Edit…"
  opens one small dialog with the name, the tags, the note and the colour, in the style of `ResumePrompt`.
- **D-L11 Colour (added 2026-11-16, from another app's feature list).** A session may have one colour from a fixed list of
  eight, each with a dark-theme and a light-theme shade in `RailPalette`. "None" is the default. It shows as a thin bar at the
  left of the session's rail row and under its tab title. It does not replace the tab's state dot, which already uses green,
  amber and grey for working, blocked and other; the list avoids those three.

Decided while building (2026-11-16; accepted by the planner, git-fb):
- **Pin and Archive on history rows only.** A live row's menu offers Edit only. A running session is not archived from the rail.
- **Archived rows are muted** (opacity 0.55) where they show: in a search, and under the Archived filter.
- **Tags are also at the top of a row's tooltip**, because a narrow rail cuts the second line, where they also are.
- **The start text names the source session** on its first line, `Continues session: <id>`. This changes the prompt's text; the CLI call does not change. A resume in full keeps its id and writes no line.

## Steps

### L1 Free facts from the transcripts (Core)

Check: `dotnet test` passes. Checked 2026-11-16: `dotnet test tests/OpenTail.Bullshark.Core.Tests --filter "FullyQualifiedName!~Live"`
passes 262 (254 before this slice, plus the 8 new tests).

Real folder, read-only, `scripts/Check-Titles.ps1`, 2026-11-16 (7 seconds):
- transcript files: 137, of which 11 are in subagents folders (the grep count in "What the transcripts already hold" was 126 files;
  the folder has grown since, and the title and custom-title counts did not change)
- files with an `ai-title`: 70 of 137; with a `custom-title`: 46; with an `away_summary` recap: 39 (grep: 40); with a
  `compact_boundary`: 23 (grep: 24); with a user message: 137
- the ten newest titles were read by eye: all ten read as fair descriptions of their sessions. Their content is not written here.
- finding (corrected 2026-11-16): the reader already skips a first message that starts with `<` (`SessionTitle.Clean`), so a `<local-command-caveat>` record never becomes the title. The first wording of this finding was wrong: the check script read the raw record, not what the reader reads. See question 6.

### L2 The rail shows them (App)

- [ ] The transcript view's heading and the resume prompt's heading use the same title. By code reading only: both read
      `digest.Title`. Not seen on screen: a transcript opens with a click, and clicks were not simulated.
- [ ] Checked on screen with a temporary state folder: a session with an `ai-title`, one without, and a subagent row. Partly: an
      `ai-title` row and a row without one were seen. No subagent row is in the screenshot.

Check: the screenshot shows the three rows; the titles of the first two differ as expected. Result, 2026-11-16 (a scratch
state folder, `--no-pass`, `--shot`): the row "OpenTail.Bullshark review..." (session 29b84a3b, has an `ai-title`) and the row
"Hello from git-86..." (session f8045ed8, has no `ai-title`; its first message is the title) differ, as expected. No subagent
row is visible, so the three-row check is not complete.

### L3 The library store (Core)

Check: `dotnet test` passes. Checked 2026-11-16: the Core suite passes 270 with the Live tests filtered out. The eight tests are in
`tests/.../Library/SessionLibraryTests.cs`: the six named, plus `TheEntryIsKeptAcrossALoad` and
`BesideTheStateFileIsLibraryJsonInTheSameFolder`.

### L4 Organise in the rail (App)

- [ ] The row menu and the edit dialog (D-L10), on history rows and on live session rows. Not on folders, headings or failed starts.
      Built: `RowMenu` and `EditMark` in `SessionWindow.Library.cs`; the dialog is `LibraryEditDialog.cs`. Not seen: the menu and the
      dialog need clicks, which are for the owner. Decision to confirm: Pin and Archive are on history rows only. A live session is
      not archived from the rail, so the live row's menu offers Edit only.
- [ ] Titles use `TitleFor`; tags show on the second line; the note shows in the tooltip. Seen: "Plan for L4" in the Pinned group, in
      the dark and the light theme. Built, not seen: the tags on the second line are cut at the default rail width. They, and the note,
      are at the top of the row's tooltip, which needs a hover (for the owner).
- [ ] The colour bar on the rail row and on the tab (D-L11), in both themes. Seen: the rail row's bar (Blue) in the dark and the light
      theme. Built, not seen: the tab's top bar (`DrawStrip`), because a tab needs a click. The Red session is not in the visible part
      of the list.
- [ ] Checked on screen with a temporary state folder: rename, pin, unpin, archive, the Archived filter, unarchive, a tag, a note, a
      colour in the dark and the light theme; then close and reopen the window and see them again. Seen: the Archived filter; a colour
      in both themes; a fresh start reads `library.json` and shows the Pinned group, which is the reopen. Not done: rename, pin, unpin,
      archive, unarchive, a tag, a note and a colour changed by clicks (for the owner). Their writes are unit-tested in
      `SessionLibraryTests`.

Check: the on-screen list is not complete; the boxes above say what is. `scripts\Test-Library.ps1` passes 6 of 6 with no clicks
(2026-11-16, scratch state folders).

### L5 Search and lineage

- [ ] `SearchView` shows "matched its name, tags or note" for a match with no messages. Built (`SearchView.cs`). Not seen: a search needs
      typing, which is for the owner.
- [ ] The rail tooltip says "continues: <title>". The transcript view shows "Continued from…" and "Continued in…" links. Built
      (`ContinuesTip`, `ShowLineage`, `LineageLink`). Not seen: a hover and a transcript click are needed, and no continuation exists yet
      to show the links.

Check: `dotnet test` passes: 287 with the Live tests filtered out (2026-11-16). One real resume is not run. Its box waits for the owner's
word. The on-screen boxes above are unticked until a click or a hover is checked.

### L6 Later, only if measured: a cache for the index

Reading every transcript took 3.6 s for 137 files (830 MB), measured 2026-11-16 (`history-search.md`), off the UI thread.

- [ ] Measure the load again when the history is twice that size, or when the owner finds the start slow. Build only then.
- [ ] If built: a cache keyed by path, size and last write time. The trap: a file's usage depends on the shared ledger and on
      the files read before it (F35), so a cached digest alone is not enough. The cache must also keep each file's message keys
      with their usage, or the totals must be recomputed from them. The test is that the totals equal an uncached build.

## Questions for the owner

1. Is `ai-title` wanted as the default title (D-L1)? It is Claude Code's wording, not yours. *Used as recommended; not yet confirmed
   by the owner.*
2. One row per pinned session, under Pinned only (D-L4)? *Used as recommended; not yet confirmed.*
3. Should a search list archived sessions (D-L5)? *Used as recommended; not yet confirmed.*
4. May L5 run one real resume in a scratch folder as its check? *Open. The L5 resume box stays unticked until the owner says yes.*
6. The first user message is often a `<local-command-caveat>` record (from `/model` or `/effort`). Should the title fallback skip it? *Answered 2026-11-16: no change needed. The reader already skips it (see L1). The test is `ACaveatIsNotTheFirstMessage`.*

## Not verified

- The four record types are observed on this machine's files with one CLI version. Their shape in other versions is unknown.
- Whether an `ai-title` changes during a session was not looked at; the plan takes the last one either way.
- Whether a context menu on a rail row draws correctly beside the embedded terminal. The filter menu already uses a
  `ContextMenu` in the rail, which suggests it does; it is checked on screen in L4.
- Whether a full resume writes to the same transcript file (F20 is open).
- The on-screen checks need a session with each record type in the scratch copy.

## Other ideas, each for its own plan

Not part of this plan. The order is the assistant's recommendation.

| Idea | Note |
|---|---|
| Attention board and alerts: cards for running, waiting, blocked; a Windows notification | the review's Phase 4 and D10. Waits for the review's Phase 3. |
| Hooks in place of polling | a new Claude Code surface (hooks in settings), not checked yet. Decide it with S9, after the review's freeze. |
| Activity panel (README P6): per prompt, the tools called, which failed, how long the turn took | reads transcripts only. `turn_duration` is already read. Per-tool duration and the failure flag are not checked in the format yet. |
| Context meter on each live row | the context size is already known per session. The window size per model must be verified before a percentage is shown. |
| Reorder tabs, split the pane, a second window | see `docs/plans/tabs-and-panes.md`. |
| Launch profiles: a folder, a pattern and its models as a one-click start | touches run start, so it waits for the review's Phase 3. Setup already remembers models per pattern. |
| Session timeline with the compaction marks and the cache going cold | reads transcripts only. `compact_boundary` gives the marks. |
| Files touched, and "which sessions changed this file" | `FilesWritten` is already in the digest. |
| Search inside tool calls | `HistorySearch` leaves them out on purpose; it would be a switch on the search. |
| Usage view by day, folder and model | in tokens, not money (F17). The ledger has the data. |
| History as a tool for the sessions themselves (MCP) | new surface; decide after the freeze. |
| Export one session to HTML | see `docs/later/claude-scrollback.md`. |
| Fork from a point; a worktree per coder with diff review | under the review's freeze. Fork is not verified against the CLI. |
| Message queue; brief library | needs the nudge rule (D5), which the review lists as unused code. |
