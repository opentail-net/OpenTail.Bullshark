# Limit status bar and limit history, 2026-11-18 (backlog A2 + A4)

Builds on `limit-reset-start-2026-11-18.md` (gate evidence there: transcript entry with `apiError: "usage_limit_reached"` and
`apiErrorParams.rate_limit_info {status, resetsAt (unix seconds), rateLimitType}`; the entry's `timestamp` is ISO UTC; text like
"You've hit your 5-hour limit · resets 1:10pm (Europe/London)"). Seen `rateLimitType`: `five_hour`, weekly, session, model limits. Do not hard-code a list;
show the raw type, with a friendly label only for `five_hour` ("5-hour") and `weekly`/`seven_day` ("weekly") and the raw string otherwise.

## Contracts this must satisfy

| Member | Promise |
|---|---|
| `LimitReset.Find/FromLine/StartFor` (Core/Setup/LimitReset.cs) | Unchanged behaviour and tests (13). Refactor only to share the tail reader; `Find` must still return the latest future `resetsAt` among `rejected` entries. |
| `TranscriptFile.Open`, `TranscriptJson` | Only way to read transcripts; bad line / unreadable file skipped, never fatal. |
| `UsageLedger` (Core/Activity) | Usage is counted once per message id. Use it for "tokens before a hit"; read its API first, do not re-implement counting. |
| UI thread rule | Any transcript scan runs in `Task.Run`; the status bar refresh is throttled (at most once per 60 s) and never blocks a pass. |
| Storage page / `StorageReport` pattern | Read-only report in Core, page in the window opened from the status bar. Copy this pattern for A4. |
| Privacy | The report shows counts, times, types, token totals. No prompt or message text. |

## A2. Status bar: active limit

- [x] Core `LimitStatus` (new, `Core/Setup/LimitStatus.cs`): `Active(projectsRoot, now)` -> list of `(string Type, DateTimeOffset ResetsAt)` for every rejected, still-future limit found (latest per type), sorted by `ResetsAt` descending (the one that governs first). Reuse the tail reader of `LimitReset` (make a shared internal `LimitEntries` reader that yields `(Timestamp, Type, Status, ResetsAt)` per limit line; `FromLine` stays). Why: the status bar needs the type and all active limits, `Find` returns only an instant.
- [x] App: a status-bar text, hidden when no limit is active: "5-hour limit resets 13:10" (use `StartTime.Describe`, local time); with two limits: "weekly limit resets Nov 19 09:00 (5-hour also active)". Tooltip: full list. Refresh: on window load, then at most every 60 s via the existing timer/pass hook (look at how the status bar updates the Storage button / pass status; do not add a second timer if one exists), in `Task.Run`, cached result; when the cached reset time passes, the text disappears without a scan.
- [x] Tests: two types active -> both returned, governing first; one in the past -> excluded; non-rejected -> excluded; same type twice -> latest kept; empty/missing root -> empty.

## Decisions

- A2-D1 The text uses `StartTime.Describe`, so a reset on another day reads `2026-11-19 09:00`, not `Nov 19 09:00` as the example above had it. The plan named `Describe` for the time, so the format follows it.
- A2-D2 "Latest per type" means the largest `resetsAt` of that type among the entries still in force, not the most recently written entry.
- A2-D3 The refresh rides the existing 15 s pass tick (and the 3 s trust tick, which also calls the pass). The scan starts at most once a minute, after the load. Nothing new is added to the timers.
- A2-D4 `LimitReset.FromLine` keeps its signature (the contract names it) but has no caller now: `Find` reads entries through `LimitEntries.Tail`. It is internal, so it is left in place; it can be deleted if the owner prefers.
- A2-D5 The shared reader is `Core/Setup/LimitEntries.cs` (internal). `LimitReset.Find` and its 13 tests are unchanged in behaviour.

## A4. Limit history page (read-only)

- [ ] Core `LimitHistory.Read(projectsRoot, now, days = 30)` -> `(IReadOnlyList<LimitHit> Hits, IReadOnlyList<DayRow> Days)`. `LimitHit`: Time (entry timestamp), Type, ResetsAt, TokensBefore (tokens from `UsageLedger` over the 5 hours before the hit across ALL transcripts, counted once per message; if that is expensive, count only transcripts modified in the window, and say so in the doc comment). One hit = one distinct `(Type, ResetsAt)` pair (the CLI may write the same limit entry several times as the session retries; collapse them, keeping the earliest timestamp). `DayRow`: local date, hits per type. Files: all `.jsonl` (subagents included) modified within `days`; full read allowed here (not tail), streaming, pre-filter `usage_limit_reached`.
- [ ] App: a "Limits" page opened from the status bar next to Storage (copy the Storage page wiring): a table of hits (time local, type, resets, tokens in the 5 h before, with a thousands separator) and a per-day summary; a footer line: "Counts only sessions on this computer. Other machines and claude.ai chat use the same limit and are not shown." Empty state: "No limit hits in the last 30 days."
- [ ] Tests (temp tree): duplicate entries collapse to one hit; two types on one day; hit outside `days` excluded; tokens-before counts only messages in the 5 h before and counts a message id once; a file with garbage lines is skipped line by line; locked file skipped.

## Steps for the coder (order)

1. Read `limit-reset-start-2026-11-18.md`, `Core/Setup/LimitReset.cs`, its tests, `Core/Activity/UsageLedger.cs`, `Core/Storage/*` (StorageReport) and the Storage page + status bar wiring in `src/OpenTail.Bullshark.App` (grep `Storage`).
2. A2 Core + tests, run the Core tests. 3. A2 App. 4. A4 Core + tests. 5. A4 App. 6. Update docs (this file ticked, backlog A2/A4 ticked, one line in `session-summary-2026-11-17.md`).
7. Commit per item (A2 one commit, A4 one commit), only your files, `git status` first. Do not push.

## How to check

`dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` (1020 passing now; report exact counts and exit code), the App builds. Say plainly that the status text and the page were not seen in a running window.
