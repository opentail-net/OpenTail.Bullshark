# "While you were away" digest, 2026-11-18 (ideas D1)

Goal: when the owner comes back to the window after being away, one page says what happened per session and what the supervisor did, so
they can carry on without reading every tab. Read-only. Nothing is sent to any session (principle 3). Idea from Slack Recap and Devin
Session Insights (see `ideas-2026-11-18.md`; researcher-reported).

## Data we already have (read the code, do not re-implement)

- `SessionDigest` (Core/Activity/TranscriptIndex.cs): `LastActivity`, `FilesWritten`, `LastAssistantText`, `FailedToolCalls`, `Turns`, `Usage`, `AiTitle`.
- `EventLog` (Core/Supervisor/EventLog.cs, `events.jsonl` beside the state file): the supervisor's own events (restarts, alerts). Read its writer and format first; add a read method if there is none, tolerant of bad lines.
- `LimitHistory.Read` / `LimitStatus` (Core/Setup): limit hits in a period.
- `SessionRecord`, `AgentInfo` / `agents --json` listing: names, roles, current state. `AttentionList.From`: who needs the owner now.
- `UiFile.Read/Write` (App/UiFile.cs): small per-owner settings in `ui.json` (see how `notify` is read). Use it for `lastSeen`.

## Contracts this must satisfy

| Member | Promise |
|---|---|
| `TranscriptFile` / `TranscriptJson` | Only way to read transcripts and event lines; a bad line or unreadable file is skipped, never fatal. |
| `AttentionList.From` | Unchanged. The digest never creates attention items and never plays a sound. |
| UI thread rule | The digest is built in `Task.Run`; the page only draws. |
| Privacy | The page shows counts, times, file names, states, and the last assistant text trimmed to 300 characters (it is the owner's own transcript, shown only on their screen; it is not copied anywhere). Not written to any log. |
| `UiFile` | A missing or unreadable `ui.json` means "no last seen": no digest on that start, never an error. |

## Design decisions (AFK; change if you disagree)

- D1 "Away" = the time since the window was last active: `lastSeen` is written to `ui.json` when the window is deactivated (`Deactivated`) and when it closes. Digest period = `lastSeen` .. now.
- D2 The digest opens by itself only when the window becomes active again after a gap of at least 30 minutes AND something happened in the period (any session active, any supervisor event, any limit hit). Otherwise nothing shows. A status-bar button "Away" always opens it for the last gap (period from `lastSeen` to now, minimum last 30 minutes when `lastSeen` is missing or recent).
- D3 Rows, one per session of the current runs (from state records), newest activity first: name and role, state now, last activity time, files written (count, first 5 names; total over the session, say so in the column title), failed tool calls, last assistant text (300 chars), "needs you now" mark if in the attention list. Below: supervisor events in the period (restart, alert, resume failed...) in time order, and limit hits in the period (type, resets at). Sessions with no activity in the period are listed in one muted line, not as rows.
- D4 Dismiss ("Got it") sets `lastSeen = now` so the same digest does not return. Closing the page by switching tabs does not.
- D5 No automatic send, no sound, no flash. It is a page like Storage and Limits.

## Steps

- [x] 1. Core `Reports/AwayDigest.cs` (or `Activity/`, follow where similar pure builders live): pure `Build(IReadOnlyList<SessionRecord> records, IReadOnlyList<SessionDigest> digests, IReadOnlyList<AgentInfo> agents, IReadOnlyList<EventEntry> events, IReadOnlyList<LimitHit> limitHits, IReadOnlyList<AttentionItem> attention, DateTimeOffset since, DateTimeOffset now)` returning a record with the rows, the quiet session names, the events and the limit hits, plus `HasNews`. Why pure: testable without disk or UI.
- [x] 2. Core: an `EventLog` reader for events since a time if missing (bad lines skipped, newest 200 at most).
- [x] 3. Tests (temp folders where disk is needed): active vs quiet sessions split by `since`; sessions not in the run records are ignored; subagent digests ignored; last text trimmed to 300 chars; events and limit hits filtered by period and ordered; `HasNews` false when nothing happened; the needs-you mark; bad event line skipped; missing digest for a record (session never produced a transcript) shows state only.
- [x] 4. App: `AwayView.cs` (copy the pattern of `LimitsView`/`StorageView`: a UserControl with `ApplyTheme`, `Show(...)`, a "Got it" button raising an event), `SessionWindow.Away.cs` (build in `Task.Run`, open per D2, write `lastSeen` per D1/D4 through `UiFile`), a status-bar "Away" button next to Limits (wire like the Limits button in `SessionWindow.xaml`, `SessionWindow.Tabs.cs` `TabKind`, `SessionWindow.Rail.cs` `ApplyTheme`). Reuse the index the window already holds (find how Search/Limits get digests); do not build a second index.
- [x] 5. Docs: tick this file, tick backlog D1, one line in `session-summary-2026-11-17.md`, a Decisions section here for each choice the coder had to make.
- [x] 6. One commit, only these files, `git status` first. Do not push.

## How to check

`dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` (1038 passing now). The solution builds, App included. Say plainly that the page, the button and the automatic opening were not seen in a running window.

Result, 2026-11-18: built. `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` passed 1053 of 1053 (exit 0); `dotnet build` of the whole solution, App included, had 0 errors. The page, the button and the automatic opening were not seen in a running window.

## Decisions

The plan's D1-D5 are unchanged. These are the choices the coder made while building.

- I1 `lastSeen` is written with `UiFile.Set` on the UI thread, as the theme and zoom saves are, on Deactivated, on Closing and on Got it. A window that stays active is not written, so a crash while it is active leaves the older time (open item).
- I2 The digest reads the window's transcript index again first, through `LoadHistoryAsync` (the 🔄 path, with its cache). The index was read only at start, on 🔄 and after a storage move, so a session that ran during the absence would look quiet. No second index is built.
- I3 Events: `EventLog.ReadSince(since, 200)` gives the newest 200 events of any kind from the period start. A bad line is skipped; a file that cannot be read gives its other lines and the failure goes to the log; a lock that times out gives none, also logged. The digest then leaves out `state` and `other` events, so a busy period can list fewer than 200.
- I4 The plan's `EventEntry` is the existing `SupervisorEvent`.
- I5 A row is a session whose main transcript had activity in the period (last activity between the period's start and now), or a session with no transcript yet whose record started in the period (state only). Every other session is a quiet name. A quiet session that needs you is named "(needs you)" in the quiet line, and gets no row.
- I6 Subagent transcripts count for nothing. A transcript with no record is ignored. A record with no session id (a start that failed) is left out: the rail shows it under Needs you.
- I7 `HasNews` is true when there is a row, an event or a limit hit. A quiet session that needs you is not news by itself.
- I8 The last reply: white space collapsed, then cut to 299 characters and "…" (300 in all). It is not logged.
- I9 Files: the first five file names (file name only), and the count of all distinct files the session wrote, as the index lists them.
- I10 Limit hits: `LimitHistory.Read` with its own 30-day default, filtered to the period. A gap longer than 30 days shows the hits of the last 30 days only. The first-hit rule is the Limits page's.
- I11 Periods: the button uses `AwayDigest.PeriodStart` (the last-seen time, but at least 30 minutes back). A return opens the page only when `AwayDigest.IsReturn` (a last-seen time exists and the gap is at least 30 minutes) and `HasNews`; its period is last-seen to now. If the index is not read yet, a return waits and runs when `LoadHistoryAsync` finishes; a button press says the transcripts are still being read.
- I12 A return that opens the page selects the Away tab over the pane that is showing. Got it writes last-seen as now and closes the Away tab. The button and the tab write nothing (D4).
- I13 The Away tab is not in `RestoreSelection`'s early-return list, as the Limits and Storage pages are not. If the selected rail item disappears at a refresh, the pane falls back as it does for those pages.

Open, not done:
- The page, the button and the automatic opening were not seen in a running window.
- Last-seen is not written while the window is active (I1); after a crash the next start can show activity the owner already saw.
- The digest reads 30 days of limit entries on each build (the Limits page does the same). Its time was not measured.
