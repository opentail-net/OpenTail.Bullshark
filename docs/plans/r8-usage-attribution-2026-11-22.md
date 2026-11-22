# R8 part 1: Codex usage reader and the attribution rule (2026-11-22)

Status: planned and built by the coder on 2026-11-22 for orchestrator review. Not committed. Row R8 in [00-current-work.md](00-current-work.md);
concept note C in [../research/concept-notes-index-usage-2026-11-22.md](../research/concept-notes-index-usage-2026-11-22.md).

## Goal

Read Codex usage from its rollouts (one record per session file) and count both agents' usage per local day with one attribution rule
(`usage.attribution`), without double counting. Part 1 is the Core reader and the rule. Part 2 (the window's usage view, the App's call
sites and the wiring of `behaviour.json` into them) is not in this task.

## Contracts this must satisfy

- `UsageLedger` (Activity/UsageLedger.cs): one entry per message key; each field keeps the largest value across copies (F36). `Max` in
  `UsageReport` does the same per field. Kept.
- `TokenUsage` (Activity/TranscriptReader.cs): `Input` is the uncached input; `CacheCreation` and `CacheRead` are the cache buckets;
  `Output` is output. `UsageTokens.NewTokens` = Input + Output + CacheCreation; CacheRead is shown apart. Codex records follow the same
  meaning (below).
- `UsageReport.Build` (Setup/UsageReport.cs): a message is counted once across files (the first file in path order gives its project; the
  largest value per field); it is left out when its time is after `now`, or its day is outside the window; the day top project ties break by
  name; the project rows add up to the total; "Other" holds the projects past ten.
- Existing day totals under `event` equal today's report for the same input. Oracle: the existing `UsageReportTests` (14 facts), run with
  `usage.attribution = event`. Those tests keep their expectations and their fixtures; only the helper passes the attribution.
- `LimitHistory.HitsOf` (tokens before a hit): unchanged. It stays on Claude's per-message event times and Claude's records only. Codex
  usage never enters a Claude limit hit (a Codex rate limit is another limit).
- `LimitHistoryCache`: format and reader id unchanged (Claude records are not changed). Codex is not cached in part 1 (see decision 9).
- App call sites (`SessionWindow.Limits.cs`, `SessionWindow.Away.cs`): not changed. They get the defaults (`lastUpdate`, reasoning as
  output) and no Codex root, so their numbers change only through the default attribution.

## Field facts (verified by reading the owner's rollouts; counts only, no content)

Read with PowerShell (read-only) on the owner's Codex sessions folder: 64 rollouts, 5 plain `.jsonl` (the rest, 59, are `.zst`).
The `.zst` files were not decoded by PowerShell (no zstd there); they are checked in the scratch run through the Core reader (step 8).

- **Event that carries usage:** top-level `type` = `event_msg`, `payload.type` = `token_count`. Its time is the top-level `timestamp`
  (present on every line of the five files).
- **Totals:** `payload.info.total_token_usage` has `input_tokens`, `cached_input_tokens`, `cache_write_input_tokens`, `output_tokens`,
  `reasoning_output_tokens`, `total_tokens`. `payload.info.last_token_usage` has the same fields as the delta of one event.
  `payload.info` was an object on every event in the five files (0 null); across all 64 files, 6 events have a null `info`. The reader skips a null or missing `info`.
- **Cumulative:** yes. In the five files, 4,082 token-count events; the total never decreases in any file (0 decreases). Each file's first
  total equals its first `last_token_usage` (5 of 5): each file starts from zero, no carry-over seen. The scan of all 64 files (step 8 of the results) gives 62 of 62 files with token-count events.
- **Repeated events:** 50 events (3 files) repeat the previous total (increase 0) while `last_token_usage` is non-zero. Summing
  `last_token_usage` would overcount; the last running total does not.
- **Cached input is inside input:** yes. `total_tokens` = `input_tokens` + `output_tokens` on all 4,082 events (0 exceptions);
  `cached_input_tokens` is below `input_tokens` on every event (for example, the last total of one file of 2,532 events: input 352.7 M, cached 344.5 M).
  So the uncached part is `input_tokens` - `cached_input_tokens`, and it is counted once.
- **Reasoning is inside output:** yes, by the same identity: `total_tokens` does not add `reasoning_output_tokens` on top of
  `output_tokens`. So `reasoningAsOutput` = true means "use `output_tokens` as it is"; false means "output minus reasoning".
  `info.last_usage_includes_reasoning` is true on every event that has it (a boolean; not used).
- **Cache write:** `cache_write_input_tokens` is 0 on all events in the five files. Its place (inside input or not) is not verified; the
  reader takes it as inside input, like cached input (assumption, decision 2).
- **Model:** top-level `type` = `turn_context`, `payload.model` on every such line (334 of 334; 4 distinct values, not recorded here). The model
  in effect at a total is the last `turn_context` model before it (same as `CodexHistorySource`).
- **Folder:** top-level `type` = `session_meta`, `payload.cwd` on the session record (5 of 5). The reader keeps only its last segment.
- **Other usage-like record:** top-level `type` = `token_usage_record` (with `usage`, `turn_token_usage`, `thread_token_usage`) appears in 3 of
  the 5 files (1195, 268 and 2 lines), not in the other 2. It is not the token-count event and is not read (reading it as well would
  double count). In the scan of all 64 files it appears in 36 of them (the `.zst` files included).
- **Rate limits** are in the token-count event (`payload.info` sibling `rate_limits`): not used.

## Decisions

1. **One record per session file.** Its usage is the last valid running total in the file. A valid total is a `token_count` event whose
   `payload.info.total_token_usage` is an object with `input_tokens` and `output_tokens` as non-negative integers. Any other event is
   skipped (no figures). A file with no valid total gives no record. A total with no own timestamp takes the last timestamp seen before it;
   with none at all it is skipped.
2. **Mapping to `TokenUsage`.** `Input` = `input_tokens` - `cached_input_tokens` - `cache_write_input_tokens` (clamped at 0; the last term is an
   assumption, always 0 on the owner's data). `CacheRead` = `cached_input_tokens`. `CacheCreation` = `cache_write_input_tokens`.
   `Output` = `output_tokens` when `usage.reasoningAsOutput` is true; `output_tokens` - `reasoning_output_tokens` (clamped at 0) when false.
   Reasoning is dropped from the report when false, with no separate count (a choice; the reader's record carries no reasoning field).
3. **Times.** The record carries `FirstTotal` (time of its first valid total) and `LastTotal` (time of its last valid total). The model is the
   `turn_context` model in effect at the last valid total. One session with two models keeps one record, under the last model.
4. **Claude times.** A Claude session file's `FirstRecord` and `LastRecord` are the first and last time among its usage records (assistant
   messages with usage and a time). Not every line's time: the limit history reads usage lines only (cheaper), and the cache keeps the
   same fields. The rule is stated here so the day rule is clear.
5. **Attribution (`usage.attribution`)**, one time per session, applied to each usage record (copy) before the dedup:
   `lastUpdate` = `LastRecord` (the file's last usage time), `start` = `FirstRecord`, `event` = the record's own time (for Codex the one record's time is
   `LastTotal`). Then, as today, the copies of one message are combined: largest value per field, and the earliest attributed time among its
   copies. The window filter is applied after that, once per message. A message first written before the window stays out of it (the same
   rule as today's `event`); this is the documented consequence of "the earliest copy places a message".
6. **Codex keys.** Each Codex record's key is `codex:` plus its file path (in memory only; never written). No Codex record is merged with another
   file's record (one record per file, decision 1). A Codex session's project is the last segment of its `cwd`, or `(codex)` when none is named.
7. **Reasoning and the default.** `usage.reasoningAsOutput` defaults to true (BehaviourSettings), `usage.attribution` to `lastUpdate`. The
   knobs already exist in `UsageSettings` and `BehaviourStore` (read in code): nothing is added to them. The reader takes the flag as a
   parameter; the wiring from `behaviour.json` is part 2.
8. **Codex is read from its rollouts only** (no other feed). Codex records enter `UsageReport.Build` through the same record shape as Claude's.
   A Codex file is read only when its last write time is at or after the window's start (a file last written earlier has no record in the
   window under any rule). A compressed rollout is read only when this build can decode it (net11.0); on net10.0 it is skipped and counted
   in the log line.
9. **No cache for Codex in part 1.** Replace, do not append: each call reads the Codex files in the window again. This is a cost (the owner's
   rollouts are 191 MB in total) and is a risk for part 2; the scratch run measures it.
10. **Dedup before the window filter** (Build's order, unchanged): copies are combined by message key first, then the time filter is
    applied to the combined message. A copy in an earlier file (in time) decides a message's time, as today.
11. **`event` for Codex** equals `lastUpdate` for Codex, because each Codex session is one record at its last total. Splitting a Codex session
    across its events (per-event deltas) would contradict decision 1 and is not built.
12. **Hits are Claude-only and keep event times** (decision 'Contracts'): the five-hour window of a hit is "messages written in the five hours
    up to the hit" (LimitHistory's documented rule). Whether the attribution rule should also apply to that window is an open question (see
    Open items).

## Checklist

Each box says what was done and why. Ticks are set after the build and the tests.

- [x] Read the contracts above (TokenUsage, UsageLedger, UsageReport.Build, LimitHistory, LimitHistoryCache, RolloutReader, CodexHistorySource's model and folder rules, BehaviourSettings/BehaviourStore usage keys). Why: the report's day rule and the cache must keep their meaning.
- [x] Verify the owner's Codex fields and the identities (cumulative, cached in input, reasoning in output, repeats, zero start). Why: the mapping in decision 2 depends on them; the evidence is in the field facts.
- [x] `CodexUsageReader` (new, public static, Codex/CodexUsageReader.cs): `FromLines(path, lines, reasoningAsOutput)` (pure over lines) and `ReadFile`, `ReadFolder(historyRoot, since, reasoningAsOutput, log)`. Reuses `RolloutReader.Lines`, `TryParseObject`, `StringOf`, `TimestampOf`, `Files`, `Open`, `CanDecodeCompressed`. Why: the same line and time parsing as the rest of the Codex code; no second parser for the same lines.
- [x] `CodexSessionUsage` record (in the same file): Path, Project, Model, Usage, FirstTotal, LastTotal. Why: the one shape the report needs for a Codex session.
- [x] `UsageAttributionRule.TimeOf(attribution, eventTime, first, last)` (new, Setup/UsageAttributionRule.cs; public, pure). Why: one place for the knob's meaning; tested for all three values.
- [x] `UsageReport.Build` takes the Codex sessions and the attribution: per copy, the attributed time; combine as today; filter as today. Why: decisions 5, 6, 10.
- [x] `LimitHistory.ReadReportWithCache` gets two optional parameters: `usage` (UsageSettings, default `new UsageSettings()`) and `codexHistoryRoot` (default null = no Codex). The hits are unchanged (Claude only). Why: the App calls keep compiling and keep their behaviour apart from the default attribution; part 2 passes the Codex root.
- [x] Existing `UsageReportTests`: the helper passes `UsageAttribution.Event` (the oracle). No expectation changed. Why: the day rule under `event` must equal the current report.
- [x] New tests `CodexUsageReaderTests` (synthetic lines): last total wins over three cumulative events; an event with no info, or no total, is skipped; a file with no valid total gives none; a partly written last line is skipped and the rest counts; reasoning counted as output when on and not when off; cached input counted once (Input + CacheRead = input_tokens); model from the last turn context before the total; a model change keeps one record; project from the cwd's last segment or `(codex)`; first and last total times; a negative or non-number total is skipped.
- [x] New tests `UsageAttributionTests`: the rule for lastUpdate, start and event; the default is lastUpdate (a message across midnight moves to the later day, where the event test of the same fixture keeps the earlier day); a message in two files counts once, with the earlier copy's attributed time and the larger values; Codex and Claude together in the report, with a Codex session counted once at its last total; `ReadFolder` gives nothing for a file last written before the window; a folder with no rollouts gives an empty list; the Codex record is not in the hits.
- [x] Build and test (Core net11.0 and net10.0 with `--filter "Category!=Live"`; App net11.0 and net10.0 windows, unchanged code, to confirm no break). Why: the orchestrator's gate.
- [x] Scratch run on the owner's real history (counts only, outside the repo; deleted after): the day rows and sessions that move between `event` and `lastUpdate`; `start` for the same; the Codex totals check on all rollouts, including the `.zst` ones (decreases, the identity, the start-from-zero check); the time of one `ReadFolder` call. Why: the orchestrator's report needs the numbers, and the `.zst` facts were not checkable with PowerShell.

## Results (counts only; the owner's real history, read on 2026-11-22; the Claude counts move a little between runs because the sessions are live)

- **Tests.** Core net11.0: 2697 passed, 0 failed, exit 0 (was 2668; +29). Core net10.0: 2684 passed, 0 failed, exit 0 (was 2655; +29). The new
  cases: `CodexUsageReaderTests` 19 (15 facts, one with 4 cases) and `UsageAttributionTests` 10. `UsageReportTests` (14) passes unchanged under `event`.
  App net11.0-windows: 145 passed, exit 0. App net10.0-windows: 145 passed, exit 0. Core warnings: none in the new code; the two xUnit1031
  warnings in `UiSettingsUpdateTests` and the App warnings (`MainWindow.xaml.cs`, two xUnit2013) were there before.
- **Claude, the day totals under `lastUpdate` against `event` (the default change).** Of 30 days, 28 have a different total. Of 266 transcripts
  with usage in the window, 27 have at least one message on another day. Of 34,471 messages, 8,019 move to another day (under `start`: 34
  transcripts, 6,469 messages). The window's total differs (messages at the window edge move in or out). The cross-check against the report
  holds (the scratch sum of the messages under `lastUpdate` equals the report's day sum).
- **Codex, in the window.** 64 rollouts (59 compressed). 5 sessions are in the window by file time; 3 of them have their last total before the
  window's first day and so count in no day; 2 count, with 4.07 M New tokens. Under `start`, 2 sessions move a day. Under `event` a Codex session
  is one record at its last total, so `event` and `lastUpdate` agree for Codex (0 moved). The report with Codex in equals the report without it plus
  the Codex sessions inside the window (checked).
- **Codex, every rollout (scan, net11.0, the `.zst` files decoded).** 62 files with token-count events, 2 with none; 24,093 token-count events, 6 with a
  null `info`; 0 files where the total decreases; 0 events where `total_tokens` is not `input_tokens` + `output_tokens`; 0 with a cache-written value; 0 with
  cached input above input; 0 with reasoning above output; 532 repeated events; 62 of 62 files start from zero. The scan took 1.7 s for all 64 files.
- **Cost.** One Codex read of the window (5 sessions, 350 ms); the full Codex read of the window on the Limits page (with the Claude read from the
  cache) about 0.5 s. Each call reads the Codex rollouts again (no cache, decision 9). Not measured on the App's path (the App does not pass a Codex root yet).

## Open items (decisions the plan does not settle)

- **Codex cost on each call.** The rollouts are read again on each call; the window read is about 0.35 s on the owner's data now and grows with
  the rollouts. Part 2 must add a cache or call it less often.

- **Hit window.** Should `usage.attribution` also decide the five-hour window of a limit hit (`TokensBefore`)? This build keeps the event rule there (the LimitHistory documented rule). The owner should say.
- **Codex in the App.** The App passes no Codex root, so Codex usage shows nowhere until part 2. Part 2 must also add a cache for Codex or measure the cost on the owner's data.
- **Cache write placement.** `cache_write_input_tokens` is always 0 in the files checked; its placement is an assumption until a file has a non-zero value.
- **Resumed sessions.** The five plain files start from zero. A resumed session that carries its earlier total in a new file would be counted twice across files; not seen in the checked files; the scratch run checks the `.zst` files for the same start-from-zero pattern.
- **Reasoning when false.** Dropped with no separate count (decision 2). A separate count would need a field on `TokenUsage`, which the existing contracts do not have.
