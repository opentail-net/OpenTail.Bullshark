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

- [x] Core `LimitHistory.Read(projectsRoot, now, days = 30)` -> `(IReadOnlyList<LimitHit> Hits, IReadOnlyList<DayRow> Days)`. `LimitHit`: Time (entry timestamp), Type, ResetsAt, TokensBefore (tokens from `UsageLedger` over the 5 hours before the hit across ALL transcripts, counted once per message; if that is expensive, count only transcripts modified in the window, and say so in the doc comment). One hit = one distinct `(Type, ResetsAt)` pair (the CLI may write the same limit entry several times as the session retries; collapse them, keeping the earliest timestamp). `DayRow`: local date, hits per type. Files: all `.jsonl` (subagents included) modified within `days`; full read allowed here (not tail), streaming, pre-filter `usage_limit_reached`.
- [x] App: a "Limits" page opened from the status bar next to Storage (copy the Storage page wiring): a table of hits (time local, type, resets, tokens in the 5 h before, with a thousands separator) and a per-day summary; a footer line: "Counts only sessions on this computer. Other machines and claude.ai chat use the same limit and are not shown." Empty state: "No limit hits in the last 30 days."
- [x] Tests (temp tree): duplicate entries collapse to one hit; two types on one day; hit outside `days` excluded; tokens-before counts only messages in the 5 h before and counts a message id once; a file with garbage lines is skipped line by line; locked file skipped.

- A4-D1 The page's tokens are the four kinds added together (input, output, cache written, cache read), with the breakdown in the cell's tooltip. The plan did not say which kinds; cache reads are usually the largest part of the sum, so the number is not known to be what the limit counts.
- A4-D2 The files read for the tokens are the `.jsonl` files last written since five hours before the window starts (the earliest a message in the window can be in). This is exact, not an approximation, so no "say so" is needed; each file is read in full, and a file that fails part way gives none of its messages.
- A4-D3 Only `rejected` entries are hits. Other statuses are read by the shared reader and left out here.
- A4-D4 A hit's `Time` is the earliest entry of its (type, reset). A hit with no readable timestamp is left out. Hits are newest first, and days are newest first, with days that have a hit only.
- A4-D5 `Read` takes one optional parameter, the time zone for the local date (the computer's zone when not given). The plan's signature is kept, with that addition, so the day test can use a fixed zone.
- A4-D6 The usage of each record is read by `TranscriptReader.UsageRecords` (internal), which uses the reader that `Summarize` uses (`LineFacts`), so a message key and its usage mean the same as in the rest of the app. It skips lines without the word "usage" before parsing.
- A4-D7 `TokensBefore` is a `UsageLedger` per hit. Each message goes to every hit whose window (the five hours up to the hit, both ends in) holds it; a message id counts once per hit (the largest value per field, as `UsageLedger` keeps it).
- A4-D8 `SessionWindow.Rail.cs` gained one line, `_limits.ApplyTheme(palette)`, so the page follows the theme. It is outside the files the plan named.
- A4-D9 The Limits page is in the tab strip as the key "limits", with a Limits button next to Storage in the status bar.

## Steps for the coder (order)

1. Read `limit-reset-start-2026-11-18.md`, `Core/Setup/LimitReset.cs`, its tests, `Core/Activity/UsageLedger.cs`, `Core/Storage/*` (StorageReport) and the Storage page + status bar wiring in `src/OpenTail.Bullshark.App` (grep `Storage`).
2. A2 Core + tests, run the Core tests. 3. A2 App. 4. A4 Core + tests. 5. A4 App. 6. Update docs (this file ticked, backlog A2/A4 ticked, one line in `session-summary-2026-11-17.md`).
7. Commit per item (A2 one commit, A4 one commit), only your files, `git status` first. Do not push.

## How to check

`dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` (1020 passing now; report exact counts and exit code), the App builds. Say plainly that the status text and the page were not seen in a running window.

## Result (2026-11-18)

- A2 (commit `a470bf8`): `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` passed 1030, failed 0, exit 0 (1020 + 10 `LimitStatusTests`).
- A4 (its own commit): passed 1038, failed 0, exit 0 (1030 + 8 `LimitHistoryTests`). The 23 `LimitReset`/`LimitStatus` tests were run again after the A4 Core change.
- Build: the whole solution, App included, 0 errors. One warning, CS8601 in `MainWindow.xaml.cs(23)`, which was there before this work.
- Not checked: the status-bar text, its tooltip, the Limits button and the page in a running window. The cost of the scans on the owner's transcripts was not measured (the status scan reads at most 256 KB per recent transcript once a minute; the history reads whole transcripts of 30 days plus five hours, on a click).
