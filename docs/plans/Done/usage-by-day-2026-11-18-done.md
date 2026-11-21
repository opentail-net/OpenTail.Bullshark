# Tokens by day and by project (2026-11-18) (done items)

> Split from [usage-by-day-2026-11-18.md](../usage-by-day-2026-11-18.md) on 2026-11-21. These items are checked in the original; the open items stay there. The headings above each item are its place in that plan.

## Steps (one coder, Core first)
- [x] 1. Core, new file `Setup/UsageReport.cs`: public records `UsageDay(DateOnly Day, TokenUsage Total, string? TopProject)`,
  `UsageProject(string Project, TokenUsage Total, int ActiveDays)`,
  `UsageReport(DateOnly From, DateOnly To, IReadOnlyList<UsageDay> Days, IReadOnlyList<UsageProject> Projects, int OtherProjects, TokenUsage Total)`
  and a helper `NewTokens(this TokenUsage)` = Input + Output + CacheCreation. Internal static
  `UsageReport.From(IReadOnlyList<FileRecords> reads, string projectsRoot, DateTimeOffset now, int days, TimeZoneInfo zone)` implementing rules 1-6. The
  "Other" row is the last element of `Projects` named "Other", with `OtherProjects` = how many projects it holds (0 = no Other row); document that shape.
- [x] 2. Core, `LimitHistory`: add `ReadReportWithCache(projectsRoot, now, cachePath, days = DefaultDays, zone = null, log = null)` returning a small public
  record `LimitsReport(IReadOnlyList<LimitHit> Hits, IReadOnlyList<LimitDay> Days, UsageReport Usage)`. It lists the files and calls `RecordsOf` **once**, then
  computes the hits (existing logic, extracted from `Compute` into a private method taking the `reads`; no behaviour change, including the early empty return for
  hits only) and the usage report. `Read`/`ReadWithCache` call the same extraction. Why: one read of the transcripts for both, same file set (see contracts).
- [x] 3. Core tests, new `UsageReportTests` (copy the fixture helpers `Stamp`, `Usage(...)` from `LimitHistoryTests`; temp folders; UTC zone unless stated):
  a. messages on two days land on their own days; every day of the 30 is listed, zero days included, newest first;
  b. a message across midnight (first record 23:59, second 00:01 next day, same key) counts once on the earlier day, with the larger output;
  c. the same key in two files counts once (the project is the first file by ordinal path), largest per field kept;
  d. two projects: per-project totals, top project of a day, active days;
  e. a subagent file (`proj/sess/subagents/agent-1.jsonl`) belongs to `proj`;
  f. more than 10 projects: 10 rows + Other, and the rows add up to the total (New and CacheRead separately);
  g. a message before the first day or after `now` is left out;
  h. invariant: sum of days == Total == sum of projects (incl. Other), for a mixed fixture;
  i. invariant vs the limit history: one hit with all its messages inside the 5 h before it and inside the 30 days: `LimitHit.TokensBefore` equals `UsageReport.Total`
     (the four kinds), through `ReadReportWithCache`;
  j. cache: `ReadReportWithCache` with a cache path gives the same report as without, and `ReadWithCache` then `ReadReportWithCache` on the same cache file do not
     rewrite it (the cache file's write time is unchanged by the second call; look at `LimitHistoryCacheTests` for how reads are shown not to repeat);
  k. no hits at all: hits/days empty and the usage report still filled;
  l. a garbage line is skipped; no usage at all gives all-zero days and no projects; a zone other than UTC puts a message near midnight on the local date.
- [x] 4. App: `SessionWindow.Limits.cs` calls `ReadReportWithCache`; `LimitsView.Show(LimitsReport)` (grep for other callers of `Show(hits, days)`; keep an overload only
  if something else needs it). The page gets two new blocks below the hits: "Usage per day (local date)" (rows: date, New, Cache read, a bar for New scaled to the
  busiest day, top project) and "Top projects, last 30 days" (rows: project, New, Cache read, share, active days). Same look as the existing tables (Consolas 12, grid),
  tooltips with the four kinds, a caption: "Tokens as the transcripts report them, counted once per message. New = input + output + cache written. Cache read is
  shown apart. Not money, and not the same as what counts toward a limit." Both blocks show when there are no hits; the "No limit hits" caption stays for the hits part
  only. Theme: the same `ApplyTheme` muted brush for notes; the bar a plain `Rectangle` in the palette text colour at reduced opacity. Read stays on the pool thread.

- [x] 6. `dotnet build src/OpenTail.Bullshark.App -nodeReuse:false`, then `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false`. If a quick throwaway timing is easy, time one
  cold and one warm `ReadReportWithCache` on the owner's real transcripts and report the seconds, then delete the scratch code; if not, say it was not measured.
