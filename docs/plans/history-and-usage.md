# Phase: history and usage

Status: in progress (2026-11-14). Owner away; the phase runs without questions. Decisions are recorded here and in `docs/findings/FINDINGS.md`.

## Goal

Let the owner see what happened in past and current sessions, and what it cost in tokens, without sending anything to a model.

- A **transcript index** in Core: one digest per session and correct global totals, computed locally.
- A **History tab** in the OpenTail.Bullshark window (not a terminal): session headings from the index, a read-only transcript loaded page by page, tool output collapsed, and the resume options shown but not wired.
- A check that **ccusage** counts correctly (done: F35), and an evaluation of **claude-scrollback** for later.

## Non-goals (this phase)

- No model calls, and no summaries made by a model. Summaries are a later decision with a shown cost.
- No resume action is wired. The options are shown, with their context size, and disabled with a note.
- claude-scrollback is not installed into the product. It is evaluated and recorded.
- No pushes. Commits are local, per step, on branches, merged when the tests pass.

## Contracts this must satisfy

Transcript files (`~\.claude\projects\<encoded cwd>\<sessionId>.jsonl`, one JSON object per line; F10):

- `type` is `user`, `assistant`, `system` (subtype `turn_duration` carries `durationMs`), `attachment`, `mode`, `permission-mode`, `session-owner`, `file-history-snapshot`, and others. Unknown types are kept but not counted.
- Assistant records: `message.id` (message identity), `message.model`, `message.content` (blocks: `text`, `thinking`, `tool_use`, `tool_result`), `message.usage` (`input_tokens`, `output_tokens`, `cache_creation_input_tokens`, `cache_read_input_tokens`), top-level `requestId`, `timestamp`, `uuid`, `parentUuid`.
- One API response appears as several records (one per content block) with the same usage. Counting each record double-counts (F34).
- The same message can appear in more than one transcript file (F35).

ccusage (the reference for token counts, F35):

- Counts assistant usage once per `message.id:requestId`, first occurrence wins, across all files. Records missing either key are counted each time.
- `sessionId` in its reports is the project folder name, not the UUID.
- Offline cost is 0. This project shows tokens, not dollars (F17).

Existing code this touches:

- `TranscriptReader.Summarize` (Core/Activity). Callers: tests only (the window does not use it yet). Behaviour is mapped below.
- `TranscriptSummary`, `TokenUsage`. Public records; their shape can change, because nothing outside Core uses them yet.
- `SessionWindow` (App). It has session tabs (`ExtendedTerminalControl` per tab, Tag = session id). The History tab must not change how those tabs work.

### Existing behaviour of `TranscriptReader.Summarize`, branch by branch

| Branch | What it does now | Problem | Change in this phase |
|---|---|---|---|
| Blank line | skipped | none | keep |
| Invalid JSON | skipped | none | keep |
| Valid record | `records++` | counts all record types | keep, plus unknown types are not counted as turns |
| `timestamp` present and parsable | `lastTimestamp` set | none | keep |
| `type == system`, `subtype == turn_duration` | `turns++` | none | keep |
| `type == user` | `users++` | none | keep |
| `type != assistant` or no `message` | `continue` | none | keep |
| assistant `model` | `lastModel` set | none | keep |
| content `tool_use` | `tools++` for every block | counted per record, so repeats inflate it | count once per unique message |
| content `text` | `lastText` set | none | keep |
| `usage` present | adds to `usage[model]` for **every record** | **double counts** (F34) | count once per (`message.id`, `requestId`) across all files (F35) |

## Decisions

- **D12 Counting rule.** Usage is counted once per `message.id:requestId`, first occurrence wins, across all transcript files. This matches ccusage exactly (F35). Records without either key are counted once each.
- **D13 Context size.** The context a session carries = `input_tokens + cache_read_input_tokens + cache_creation_input_tokens` of its last assistant message. It is the size a resume would load on the next turn (estimate; verify against a resumed session in a later phase).
- **D14 History tab is read-only.** Resume options are shown with their context size and are disabled ("wired in a later phase").
- **Resume wired (owner, 2026-11-15; supersedes the disabled buttons in D14).** "Resume in full…" resumes the same session with its whole conversation, after the prompt shows its size. "Resume from summary…" starts a new solo session in the same folder, from a minimal, normal or complete summary (Haiku, low effort, one call), or from the last turns only (no model call). The turns are copied word for word, with tool results on one line each, and capped at about 4,000 tokens (D-S11). A full resume is recorded in the rail but is not supervised.
- **D15 Paging.** The transcript view loads the last 200 records first, and earlier pages on request. Tool results are shown as one line unless expanded.
- **D16 Session identity.** The index keys sessions by the UUID in the file name, not by the folder name.

## Open questions (to test in this phase where possible)

- Does a long transcript record compaction boundaries? If yes, the context at the last turn is smaller than the raw totals suggest. Search the records for compaction markers.
- How does a `SendMessage` appear in the transcript (tool name, input shape, the receiving side)? It is needed for a handoff log. Test on the demo transcripts.
- Are usage values identical across the records that share a `message.id:requestId`? (The fix assumes yes; a test checks it on real data.)

## Checkboxes

- [x] H0 Plan (this document).
- [x] H1 ccusage accuracy check against the raw transcripts (F35). Result: global de-duplication matches ccusage 15.1.0 exactly.
- [x] H2 Fix `TranscriptReader` double counting (F34, F35, F36) (merged 2026-11-14): count once per `message.id:requestId`, with tests on synthetic fixtures that repeat a message across records and across two files.
- [x] H3 (done 2026-11-14; agrees with ccusage, F37) `TranscriptIndex` in Core: one digest per session (UUID, turns, tool calls, files written, last message, first and last timestamps, context size at the last turn, tokens split cache-read / cache-write / fresh input / output) and global totals. Tests on fixtures. A run on the real projects folder compares the totals with ccusage 15.1.0 (test script, not part of the unit tests).
- [x] H4 (done 2026-11-14; screenshot-checked, F38) History window in its own process (`OpenTail.Bullshark.History.exe`), opened from the History button: headings from the index, read-only transcript paged (D15), tool results collapsed, resume options disabled (D14). Compile check, then a screenshot check of the window.
- [x] H5 (done 2026-11-14, F39; `docs/later/claude-scrollback.md`) Evaluate claude-scrollback for later (read its source from PyPI; record licence, what it reads, whether it could be a bolt-on). Not installed.
- [x] H6 Findings and outcome recorded (F34 to F40); merged to `main`.

## Risks

- Transcripts are large (774 MB across 119 files; the largest 73 MB). The index must read files as streams and must not load a whole file into memory.
- Transcripts hold the owner's code and conversations. Everything here stays local.
