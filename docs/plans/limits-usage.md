# Limits and usage: open work

Limit wake itself is in [limit-wake-2026-11-20.md](limit-wake-2026-11-20.md). That file still has uncommitted edits from another session, so it was left where it is. This file holds the open items beside it.

## Paused-on-limit attention reason (backlog A3)

- [ ] A3 (GATED, not built AFK). **"Paused on limit" attention reason**: a session whose last transcript entry is `usage_limit_reached` is shown as paused until HH:mm, not as stale/blocked, and raises no flash or sound (it is not the owner's to fix). Why: avoids false "needs you" and false restart-loop alerts. Goes through `AttentionList` (contract: new reasons only there). Must check the supervisor never restarts such a session in a loop. **Gate:** what `agents --json` lists (state) for a session sitting on a limit is not known: no sample in the owner's data. Needs one real hit with a Bullshark session running, then record the state value here before any code.

## Pre-limit warning (backlog B3)

- [ ] B3. Pre-limit warning ("you are at N% of a typical 5-hour budget"): needs a budget figure; none is exposed to us and `rate_limit_info` only appears when rejected. Do not invent a number. Revisit if a `status: allowed_warning` style entry shows up in transcripts (search the owner's transcripts for other `status` values first).

## Usage report: tokens by day and project

- [ ] 5. README: one line under the Limits page description. Add to `docs/plans/live-run-checklist.md`: the Limits page shows usage per day and top projects; numbers plausible
  against the Storage page; the page stays responsive on the owner's data.

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
- [ ] Not checked in a running window: the new tables' layout (long encoded folder names are not wrapped; the page scrolls vertically only).
- [ ] Dollars (needs a dated rate table from the owner; not built).
- [ ] A 14-day toggle (30 only).
- [ ] Per-model split (`IndexTotals.ByModel` is a separate idea, cost 2).
- [ ] Whether cache reads count toward the limit: unknown; the page does not say.

## API-key accounts and the limit reset


- Waking sessions already paused on the limit (native `quota_auto_resume_*`): not in this plan.
- [ ] API-key accounts (no `rate_limit_info`): not verified.
