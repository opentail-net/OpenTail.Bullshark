# Activity from the transcripts (2026-11-16)

Status (2026-11-16): TA1 to TA5 are built in this session, uncommitted, each with its checks (results at the end of the Steps). What is not built, and why, is listed under Not built. Decisions taken without the owner (the owner was away) are marked **D-A** and can be changed.

## Goal

Show what each turn of a session did, what failed, and where the context was compacted, from the transcripts alone. This is the part of the README's activity panel (P6) that a past session can give. The live part of P6 (busy, blocked, last action) reads the supervisor's state and waits for the review's Phase 4.

One system: the transcript reader and what the History and the transcript view show from it.

## Freeze

The review of 2026-11-15 freezes new CLI switches, worktrees, the orchestrator and new patterns until its Phase 3 run passes. This plan keeps inside it:

- It reads transcripts. It starts no session, calls no model, and changes nothing in the supervisor, `ClaudeCli`, `state.json`, `runs.json` or `library.json`.
- Resume text is not changed. Its output is checked by a test before and after each slice.

## Contracts this must satisfy

Read in the code on 2026-11-16.

- **`TranscriptReader.Summarize(lines, ledger, fileName)`** (Core/Activity/TranscriptReader.cs): one pass; a blank or invalid line is skipped; it never throws on a bad record; returns `TranscriptSummary`. New members are optional and go at the end.
- **`SessionDigest`** (Core/Activity/TranscriptIndex.cs): a positional record. The new member `FailedToolCalls` is optional and goes at the end. Callers: `TranscriptIndex.Build` (named arguments), the test helper in `SessionLibraryTests`.
- **`TranscriptIndex.Build(root)`**: one digest per file, oldest first, with one shared `UsageLedger`.
- **`TranscriptLine`** (Core/Activity/TranscriptPager.cs): `Number, Kind, Time, Text, Collapsed`, a record. `From(number, json)` returns null for a record that is not shown. Kinds in use: `you`, `claude`, `tool`, `result`, `turn`. New members are optional and go at the end.
  - `TranscriptPager.ReadPage` uses `From`. Its line numbers are file line numbers, one-based. The turn facts must use the same numbers (checked by a test in TA3).
  - `HistorySearch` searches only `you` and `claude` lines (HistorySearch.cs:119). Unchanged.
  - `ResumeText.RenderLine` (ResumeText.cs:147-153) maps `you`, `claude`, `tool` and `result` to text, and returns null for every other kind. `SplitTurns` starts a turn at each `you` line. So a new kind is left out of resume text, and a new member is not read there.
- **`TranscriptView`** (App/TranscriptView.cs): `ShowAsync(digest, focusLine, title)` loads one page. `LoadEarlier` renders the pages before it. `Render(line)` colours by kind with a default branch (`_ => _palette.Text`).
- **`FactsTip`** (App/SessionWindow.Rail.cs:640): builds the tooltip's lines from the digest. The tooltip already shows the recap, the custom title and the compactions.
- **Test hooks**: App options are parsed in `SessionWindow.xaml.cs`; `--rail-filter` and `--shot` are the model for a new one.

## Vendor facts used (checked 2026-11-16)

Observed on the 138 transcripts under `~\.claude\projects`. The vendor does not document these record shapes, so the reader must tolerate their absence.

| Fact | Where it was checked | Use |
|---|---|---|
| A `tool_result` block in a `user` record has `is_error` (true in 78 files) and `tool_use_id` | `grep` count over all files; one record printed | failed tool calls |
| Each tool call has exactly one `tool_result` (1742 calls, 1742 results in one file) | script over one large session | pairing a failure with its call |
| Every `assistant` and every `tool_result` record has a `timestamp` (3963 and 1742 in one file) | script | later: per-tool duration |
| `system` with `subtype` `turn_duration` has `durationMs` (2389 records) | `grep` | turn duration |
| `system` with `subtype` `compact_boundary` has `compactMetadata` with `trigger`, `preTokens`, `postTokens` | `grep` of one record | the compaction line |
| A user record that is a typed prompt has string content or a `text` block and no `tool_result` | record order in one session | turn start |

Order of records in a turn (one session with 6078 counted records): a typed `user` record, then `assistant` records (`thinking`, `text`, `tool_use`), then `user` records with `tool_result`, repeated, then the `system` record `turn_duration`, then the next typed `user` record.

Context window, from Anthropic's models overview (platform.claude.com/docs/en/about-claude/models, read 2026-11-16): 1M tokens for Claude Fable 5.2, Opus 5.5, Sonnet 5.5, Haiku 5.5 and the other current 5-family models; 200K for Haiku 4.5, Sonnet 4.5 and Opus 4.5 and older. Third-party guides (for example tinyfish.ai, getunblocked.com) say Claude Code can be capped at 200K with `CLAUDE_CODE_DISABLE_1M_CONTEXT`. That is not checked in the CLI's own documentation. It matters for the context meter, which is parked below.

## Research for use

| Idea | Use for the owner | Research | Decision |
|---|---|---|---|
| Turn facts: duration, tools, failures, files written | see what a turn did, and what failed, without reading the tool output | the shapes above; README P6; session-library "other ideas" | **Build now** (TA2, TA3) |
| Failed tool calls | spot a loop that keeps failing | `is_error` verified in 78 files | **Build now** (TA1, TA2, TA4) |
| Compaction line in the transcript | see where the context was cut, and how far | `compact_boundary` verified; the count of files is in `session-library.md` | **Build now** (TA2, TA4) |
| Per-tool duration | find a slow command | timestamps on every assistant and result record | Later: a plan of its own, small |
| Timeline with cache-cold gaps | see why a resume costs more | the cache rule and its measurement (F49) exist in `CacheRule` | Later: needs a timeline view |
| Context meter on live rows | see how close a session is to a compaction | docs: 1M tokens for the current models; third-party: the CLI can be capped at 200K | **Parked**: verify the CLI setting first |
| Export one session to HTML | share or archive one session | prior art: claude-code-log (daaain), claude-code-transcripts (Simon Willison), claude-code-session-exporter (inventivaglobal). `docs/later/claude-scrollback.md` says: revisit when the owner asks; use only the generator, never the server | **Parked** until the owner asks |
| Which sessions changed this file | find the session that wrote a file | `FilesWritten` is already in every digest (`Write`, `Edit`, `MultiEdit`, `NotebookEdit`) | Later: needs an entry point |
| Usage by day, folder and model | track token use over time | the ledger has model and file, not time: no timestamp per usage record is kept | Later: needs a ledger change |
| Search inside tool results | find a command that was run | `HistorySearch` leaves them out on purpose (history-search.md) | Not now |
| Attention board, alerts, Windows notifications, login check, end run, attach on demand | live status and control | the supervisor, and the review's Phase 4 | **Frozen** until Phase 3 passes |
| Launch profiles, brief library, message queue, reviewer setting, trivial-work rule | new run behaviour | new patterns and run start, frozen by the review; the nudge rule (D5) | **Frozen** |
| Orchestrator (P8) | recovery | the README: started only after P1-P7 | **Frozen** |

Sources read: the Anthropic models overview (above); the prior-art pages (https://github.com/daaain/claude-code-log, https://simonw.github.io/claude-code-transcripts/, https://github.com/inventivaglobal/claude-code-session-exporter); third-party context-window guides (https://www.tinyfish.ai/blog/claude-code-context-window, https://getunblocked.com/blog/claude-code-context-window/). The third-party guides disagree with each other and are not used for any build decision.

## Decisions (taken while the owner was away; to confirm)

- **D-A1 Failure shown on its result line, not in the text.** A `result` line of a failed call keeps its text and kind, and gets a `Failed` flag. The view colours it. Resume text does not read the flag, so the verbatim tail does not change.
- **D-A2 Turn facts go on the `turn` line.** The line that says "turn finished" gets the facts. No new header line.
- **D-A3 Compaction is its own line** (kind `compact`), in the transcript only. Not in resume text, not in search.
- **D-A4 Turn facts are computed when a transcript opens**, off the UI thread, from the whole file, once. No cache: the history loads in 3.6 s for 137 files (history-search.md), and L6 says to cache only when measured.
- **D-A5 A turn starts at a line the reader shows as `you`.** This is the rule resume text already uses (`SplitTurns`), so the two agree.
- **D-A6 Test hook `--open-transcript <session id>`.** The App opens that past session's transcript after the history loads. It exists so that the on-screen check can be a screenshot, as `--rail-filter` is. It reads the owner's own transcripts, as the rail already does.
- **D-A7 Context meter and HTML export stay parked** (see Research).

## Steps

### TA1 Core: failed tool calls

Branch table for `TranscriptReader.Summarize` (the rows that change; all others are kept):

| Branch | Now | Change |
|---|---|---|
| `user`, string content | `users++`; first text | keep |
| `user`, list, block `tool_result` with `is_error` true | `users++` only (a tool result counts as a user record today; kept, see Not changed) | `failed++` once per unique `tool_use_id` |
| `user`, list, block `tool_result`, `is_error` absent or false | `users++` | keep |
| `assistant`, block `tool_use` | counted once per id; the name goes in `toolNames` | no change |

- [x] `TranscriptSummary.FailedToolCalls` (optional, end) and `SessionDigest.FailedToolCalls` (optional, end), set by `TranscriptIndex.Build`.
- [x] Tests (fixtures in `tests/.../Activity/TurnActivityTests.cs`): a failed call is counted; a call whose result appears in two records is counted once; a result with no `is_error` is not counted; a failure whose call is not in the file is still counted; a file with no failures has 0.
- [x] Rail tooltip (`FactsTip`): "N tool calls failed" when N > 0.

### TA2 Core: the result flag and the compaction line

Branch table for `TranscriptLine.From`:

| Branch | Now | Change |
|---|---|---|
| `user`, list, `tool_result` | `result`, collapsed, preview | add `Failed` = `is_error` true (D-A1); text unchanged |
| `system`, `compact_boundary` | null (not shown) | `compact` line: "conversation compacted (auto): 967,530 → 14,951 tokens" (D-A3) |
| all others | as now | keep |

- [x] `TranscriptLine` gets `bool Failed = false` (optional, end). Positional constructor calls still compile.
- [x] `From` sets `Failed`; a `compact` line is produced. Tests: the flag; the compaction line text, with and without `preTokens`; `trigger` missing → text without it.
- [x] Resume text unchanged: a test builds `ResumeText.Tail` from lines with a failed result and a compaction, and checks that the output equals the output from the same lines without those two (the `compact` line and the flag must not change it).

### TA3 Core: turn facts

- [x] `TurnFacts` (Core/Activity/TurnFacts.cs): `TurnFact(EndLine, StartLine, Start, End, Prompt, ToolCalls, FailedCalls, ToolNames, FilesWritten, DurationSeconds, Compactions)`. `Read(IEnumerable<string> lines)` numbers lines from one, as `TranscriptPager` does, and uses `TranscriptLine.From` to decide a turn start (D-A5).
- [x] A turn ends at its `turn_duration` record (`EndLine` = that line, `DurationSeconds` from `durationMs`). A turn that has no `turn_duration` ends at its last record before the next start, or at the end of the file, and has `DurationSeconds` null.
- [x] Tool calls and failures are counted once per id within a turn. Files written use the same tools as `TranscriptReader.FileWritingTools`.
- [x] Tests: one turn with two tools, one failure and a duration; a turn with no `turn_duration`; two turns; a duplicated assistant record; a blank line before a turn (the line numbers must match `TranscriptPager`: a test opens the same fixture with `TranscriptPager` and checks that the `turn` line's `Number` equals the turn's `EndLine`).

### TA4 App: the transcript view and the test hook

- [x] `TranscriptView`: a `result` line with `Failed` is drawn in a red (dark `#D16969`, light `#A31515`); a `compact` line in the muted colour, italic.
- [x] `TranscriptView`: when a transcript opens, `TurnFacts.Read` runs on a pool thread over the file; the `turn` lines already drawn get their facts appended in place, and later pages use the facts that are there. The text reads: `turn finished (19.1 s) · 4 tools, 1 failed · wrote 2 files`. Facts are omitted when they are zero, except the duration.
- [x] `--open-transcript <session id>` (D-A6): after the history loads, the window opens that session's transcript. Missing id: a status line, no crash.
- [x] Screenshot: `scripts/Test-Transcript.ps1` runs the built App with `--no-pass`, a scratch state folder, `--open-transcript` on a session that has a failure and a compaction, and `--shot`. The screenshot is for a person to look at; the script checks that the app exits 0 and writes it.

### TA5 Check and review

- [x] `dotnet test` on the Core tests (the Live tests filtered out), with the new counts written here.
- [x] App builds; the one existing warning (MainWindow.xaml.cs) is the only one.
- [x] Re-read this plan against the code: the branch tables, the resume-text check, the line numbers. Record what was checked and what was not.

## Results (2026-11-16)

- **Core tests:** 325 pass with the Live tests filtered out (308 before this plan, plus 10 for TA1 and TA2 and 7 for TA3). The new test files: `tests/.../Activity/TurnActivityTests.cs`, `tests/.../Activity/TurnFactsTests.cs`.
- **App:** builds with no new warning. The one existing warning (`MainWindow.xaml.cs`) is still the only one.
- **Screenshot (`scripts/Test-Transcript.ps1`):** all four checks pass. On screen, the "turn finished" line reads `turn finished (31.3 s) · 6 tools, 1 failed`. The session was a test session in the owner's projects folder. Its last page had no failed result, so the red colour was not seen.
- **Library check (`scripts/Test-Library.ps1`):** 6 of 6 pass after the window's startup change.
- **Resume text:** unchanged; a literal test (`TheResumeTextIsUnchangedByTheNewLines`) checks it.
- **Changed files:** `Core/Activity/TranscriptReader.cs` (count, visibility of the file-writing tool list), `Core/Activity/TranscriptIndex.cs` (digest member), `Core/Activity/TranscriptPager.cs` (`Failed` flag, `compact` line, `IsFailedResult`), `Core/Activity/TurnFacts.cs` (new), `App/TranscriptView.cs` (colours, facts, load), `App/SessionWindow.Rail.cs` (tooltip, `OpenHistorySessionAsync`), `App/SessionWindow.xaml.cs` (`--open-transcript`, startup order). New script: `scripts/Test-Transcript.ps1`.

## Not built

- Context meter on live rows: parked (D-A7). The vendor's page gives 1M tokens for the current models; the CLI cap is unchecked.
- HTML export of one session: parked (D-A7).
- Per-tool duration and the timeline with cache-cold gaps: later, as the Research table says.
- Which sessions changed a file, and usage by day, folder and model: later.

## Not changed

- `UserMessages` counts tool-result records as user messages. It has done so since H3. The rail and the stats show it; changing it would change the numbers the owner has seen. Recorded, not changed.
- The live part of P6 (busy, blocked, last action): frozen.
- Resume text: unchanged, and checked by a test (TA2).

## Not verified

- The screenshot is read by eye, not by a test. Hover tooltips (the failure count in the rail) and clicks are not driven. The red colour of a failed result is not seen: no failed result was on the screenshot's page.
- Turn facts add one whole-file read per transcript opened, on a pool thread (D-A4). Its cost on the largest transcript (73 MB) was not measured.
- Turn facts for a session with a resume (`Continues session:`) that was made by a full resume: no such session was examined.
- Whether a `compact` line changes the line numbers of the pager: it does not (a line is still one record), and TA3's test checks it.
- Per-tool duration and cache-cold gaps: not built (see Research).

## Questions for the owner (answer when back; the build does not wait for them)

1. Is the red for a failed result right, and is the turn line with the facts the right place (D-A1, D-A2)?
2. Should "wrote N files" count only the files a turn wrote, or the session's files (the current `FilesWritten` is the session's)? The build uses the turn's files.
3. Export one session to HTML: still parked. Ask when you want it.
