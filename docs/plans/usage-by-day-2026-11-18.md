# Tokens by day and by project (2026-11-18)

> Split on 2026-11-21: the done items moved to [Done/usage-by-day-2026-11-18-done.md](Done/usage-by-day-2026-11-18-done.md). This file keeps the open items.

Idea: cost 1 of `Done/keys-and-find-2026-11-18.md` (researcher-reported; the facts below were read in the code by the orchestrator).
Goal: on the Limits page, a per-day table of the last 30 local days and a top-projects table, from the usage records the limit history
already reads and caches. Read-only; counts, times, model and project folder names only; no message text.

## Contracts this must satisfy (read in the code 2026-11-18)
- `LimitHistory` (Core/Setup/LimitHistory.cs): `Read`/`ReadWithCache` return `(IReadOnlyList<LimitHit>, IReadOnlyList<LimitDay>)`; the Away digest
  calls `ReadWithCache`. **Both signatures and results stay unchanged.** `Compute` returns `([], [])` early when there is no hit: the new report
  must NOT be skipped by that early return.
- `RecordsOf(files, cachePath, log)` loads the cache, restores or reads each transcript and **saves the cache holding exactly the files it was given**
  (`cached.Count != entries.Count || readNow`). The files come from `LimitEntries.Recent(root, since - 5h)` with `since = now - 30 days`. The usage
  report must use the SAME file set (same `since`, same 5 h margin), or two callers with different windows would rewrite the cache on every read.
- `FileRecords(Path, Limits, Usage)` / `UsageRecord(Time, Key, Model, TokenUsage)` are internal; a record without a time is not kept. Key is
  `message.id:requestId`, or `unkeyed:{file}:{line}` (unique per record).
- `UsageLedger` semantic to keep: one message counted once across files, the **largest value per field** is kept (F36), not the first. The first
  record of a message can hold a placeholder output count. The report needs a time and a project per message, which `UsageLedger` does not keep,
  so it gets its own small dictionary with the same Max rule; do not change `UsageLedger`.
- `LimitHistoryCache` format is unchanged (it already holds time, offset, key, model and the four token fields per record). No cache change.
- `TokenUsage(Input, Output, CacheCreation, CacheRead)`, `Add`, `Zero`.
- Project name: the first path segment under `projectsRoot` of the transcript's file (the encoded folder name, as `SessionDigest.ProjectFolder`
  shows it elsewhere, e.g. in the search view). A subagent transcript lives deeper (`<project>/<session>/subagents/agent-x.jsonl`) and belongs to
  that project. (`SessionDigest.ProjectFolder` is the immediate directory and is "subagents" for those: do not reuse it.)
- `LimitsView.Show(hits, days)` returns early with "No limit hits" and hides the tables: the usage tables must still show when there are no hits.
- Principle 3: nothing is sent to a session. Privacy: no text kept or logged.

## Rules of the report (decisions; assumptions marked)
1. Window: the 30 local days ending today, i.e. local dates `today-29 .. today` (zone = computer's zone, injectable for tests). A message belongs
   to the local date of its **earliest** record time (streaming records of one message can straddle midnight; earliest is deterministic).
   A message earlier than the first day is left out. Messages after `now` are left out.
2. A message is counted once across all files (key). Its model = the first seen; its project = the file it was **first seen in** (assumption:
   resumed sessions copy history, so the first file is the original). Sort the files by full path, ordinal, before counting, so the result does not
   depend on directory enumeration order.
3. Two figures per cell, never summed into one: **New** = input + output + cache written; **Cache read** = cache read. (Why: cache reads are
   typically the bulk of the four and the Limits page's single total hides the work done. Whether cache reads count toward the limit is unknown
   (assumption, not claimed); the page says "tokens as the transcripts report them".) Tooltip gives the four kinds.
4. Days list: **every** day of the window, newest first (as the hits table). Days with no usage are listed with zeros.
5. Top projects: by New descending, then name; at most 10 rows plus one "Other (N projects)" row when more exist, so the column adds up to the total.
   Per project: New, Cache read, share of total New (percent, one decimal; "0.0%" when total is 0), days with any usage.
6. Per day: also the top project of that day (name, by New; null when no usage).
7. Dollars: none (no rates in the repo; not invented).

## Steps (one coder, Core first)
- [ ] 5. README: one line under the Limits page description. Add to `docs/plans/live-run-checklist.md`: the Limits page shows usage per day and top projects; numbers plausible
  against the Storage page; the page stays responsive on the owner's data.

## Review (orchestrator)
Read the diff against the contracts above, especially: Compute's early return, one `RecordsOf` call, file ordering, Max-per-field, the midnight rule, "Other" arithmetic,
the view for zero hits, no text logged. Tests with counts.

## Open / not decided
- Step 5, README: not done. The README has no "Limits page description" to put a line under (its only Limits mention is the status paragraph
  at the top). The one-line change went to `docs/plans/live-run-checklist.md` instead. The orchestrator decides whether the README needs a line.
- Names in code: the builder is `UsageReport.Build`, not `UsageReport.From`, because the record already has a `From` property.
  `NewTokens` is the public extension `UsageTokens.NewTokens` (the App needs it).
- Model (rule 2): not kept. No output shows a model, so the report does not track one. The per-model split stays open (cost 2).
- A transcript written directly in the projects folder (no folder above it) is the project "(root)". Not in the rules; chosen by the coder.
- "Other" row: `ActiveDays` is the number of distinct days any Other project was active (a union, not a sum). The name "Other" is only a label:
  the App reads the last row as Other when `OtherProjects` > 0.
- Top project of a day: the largest New, ties by name. A day with messages whose New is 0 still gets a top project; a day with no messages gets none.
- The hits-per-day block of the page is now titled "Limit hits per day (local date)" (was "Per day (local date)"), so it is not mistaken for the usage table.
- Timing (scratch console app, read only, scratch cache, deleted afterwards; the OS file cache was probably warm, so no true cold read):
  `ReadReportWithCache` cold 2.72 s; warm 0.16 s; the old `ReadWithCache` warm 0.12 to 0.13 s. Owner's data: 10 hits, 30 days, 11 rows in the
  project table (10 projects and Other, which holds 26 projects).
- Not checked in a running window: the new tables' layout (long encoded folder names are not wrapped; the page scrolls vertically only).
- Dollars (needs a dated rate table from the owner; not built).
- A 14-day toggle (30 only).
- Per-model split (`IndexTotals.ByModel` is a separate idea, cost 2).
- Whether cache reads count toward the limit: unknown; the page does not say.
