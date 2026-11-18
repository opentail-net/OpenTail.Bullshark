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

- [ ] 1. Core `Reports/AwayDigest.cs` (or `Activity/`, follow where similar pure builders live): pure `Build(IReadOnlyList<SessionRecord> records, IReadOnlyList<SessionDigest> digests, IReadOnlyList<AgentInfo> agents, IReadOnlyList<EventEntry> events, IReadOnlyList<LimitHit> limitHits, IReadOnlyList<AttentionItem> attention, DateTimeOffset since, DateTimeOffset now)` returning a record with the rows, the quiet session names, the events and the limit hits, plus `HasNews`. Why pure: testable without disk or UI.
- [ ] 2. Core: an `EventLog` reader for events since a time if missing (bad lines skipped, newest 200 at most).
- [ ] 3. Tests (temp folders where disk is needed): active vs quiet sessions split by `since`; sessions not in the run records are ignored; subagent digests ignored; last text trimmed to 300 chars; events and limit hits filtered by period and ordered; `HasNews` false when nothing happened; the needs-you mark; bad event line skipped; missing digest for a record (session never produced a transcript) shows state only.
- [ ] 4. App: `AwayView.cs` (copy the pattern of `LimitsView`/`StorageView`: a UserControl with `ApplyTheme`, `Show(...)`, a "Got it" button raising an event), `SessionWindow.Away.cs` (build in `Task.Run`, open per D2, write `lastSeen` per D1/D4 through `UiFile`), a status-bar "Away" button next to Limits (wire like the Limits button in `SessionWindow.xaml`, `SessionWindow.Tabs.cs` `TabKind`, `SessionWindow.Rail.cs` `ApplyTheme`). Reuse the index the window already holds (find how Search/Limits get digests); do not build a second index.
- [ ] 5. Docs: tick this file, tick backlog D1, one line in `session-summary-2026-11-17.md`, a Decisions section here for each choice the coder had to make.
- [ ] 6. One commit, only these files, `git status` first. Do not push.

## How to check

`dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` (1038 passing now). The solution builds, App included. Say plainly that the page, the button and the automatic opening were not seen in a running window.
