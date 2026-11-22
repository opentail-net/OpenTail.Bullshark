# Copilot provider: branch map of the Codex files copied in stage G1 (G1.0)

Written by the G1 coder from reading each Codex file (2026-11-22), before its Copilot copy was written. One row per code branch. "Copilot" says what the copy does with that branch. Rows marked **changed** are the deliberate differences; rows marked **reused** use a Codex helper instead of a copy.

## Codex/CodexHistorySource.cs (copied as Copilot/CopilotHistorySource.cs, G1.2)

| Codex branch (line) | Codex behaviour | Copilot |
|---|---|---|
| `List` L39: history root missing, or compressed reading off (net10.0, C-D2) | empty list; nothing walked | empty list when `session-state` is missing; no net10.0 rule (Copilot files are plain text) |
| `ReadIndex` L60: index missing | no names | **changed**: no index file; `workspace.yaml` gives the name (G-D3) |
| `ReadIndex` L61-89: index unreadable | `SkippedFiles++`, names so far kept | **changed**: an unreadable `workspace.yaml` is `SkippedFiles++`; the session is still listed from `events.jsonl` |
| `ReadIndex` L67: overlong line | `SkippedLines++` | same, for `events.jsonl` (**reused** `RolloutReader.Lines`, 16 MB cap, G-D18) |
| `ReadIndex` L72: blank line | skipped, not counted | same |
| `ReadIndex` L74: not a JSON object | `SkippedLines++` | same |
| `Summarise` L107: overlong line | `badLines++` (added to `SkippedLines` only when listed) | same |
| `Summarise` L112: blank line | skipped | same |
| `Summarise` L113-118: not a JSON object | `badLines++` | same |
| `Summarise` L122: read fails (not cancel) | `SkippedFiles++`, null | same; cancellation propagates |
| `Summarise` L129: no records, or no id | `SkippedFiles++`, null | **changed**: `events.jsonl` with no `session.start` id, or an id that differs from the folder name, is `SkippedFiles++` (damaged), null |
| (none) | - | **new**: no `events.jsonl` is an empty session: `EmptySessions++`, null, not listed (G-D3) |
| `Summarise` L137: title, index name wins | index name, else first user text cut | **changed**: `workspace.yaml` `name` when not blank, else first user text collapsed and cut to 80 (G-D16). The name is used as it is (not cut), per G-D16 |
| `Summarise` L138-148: SessionSummary fields | counts: `SawResponse` decides null vs count | **changed**: counts are always numbers (the whole file is read, so a missing event is 0, G-D16); `IsSubagent` and `Compactions` null (G-D19) |
| `Cut` L157: null or at most 80 | unchanged | same |
| `Cut` L159: first dropped char is a low surrogate | cut one earlier | same |
| `Collapse` L166-168: null; blank after trim | null | same |
| `MessageText` L174: no object, or no `content` | null | **changed**: `user.message` text is `data.content` as a string only (G-D17) |
| `MessageText` L176: string content | the string | same |
| `MessageText` L180: array content | text parts joined by a space | not used (Copilot content is a string per the research note) |
| `MessageText` L191: other kind | null | same (null) |
| `RolloutFacts.Add` L242: a timestamp | first and last | `startTime` gives first; `updated_at` gives last (G-D19) |
| `Add` L251: `session_meta` | id (first), folder (first), subagent mark | **changed**: `session.start`: id `data.sessionId` (first), `data.startTime`, `data.context.cwd` (first), `data.selectedModel` |
| `Add` L258: `turn_context` | last folder and model | **changed**: `session.model_change` `data.newModel` (last) wins over `selectedModel` (G-D19) |
| `Add` L264: `response_item` user | count + first text when not injected | **changed**: `user.message` counted (every one); first `data.content` kept for the title |
| `Add` L278: `response_item` assistant | count | **changed**: `assistant.message` counted |
| `Add` L284: `compacted` | count | not used (Compactions null) |
| (none) | - | **new**: `assistant.turn_end` counts `Turns` in `CopilotSessionInfo` (not in `SessionSummary`) |
| `IsSubagentMarked` L293-300 | subagent marks | not copied: `IsSubagent` is null in v1 (G-D19) |
| other record types L249 | ignored | same |

## Codex/CodexTurns.cs (G1.3)

| Branch | Codex | Copilot |
|---|---|---|
| `FromRollout` L18-25: a line that is not a message | skipped | same |
| `FromRollout`: a message | `TranscriptLine(count+1, label, time, text)` | same, kinds `you` and `copilot` |
| `ReadSession` L37: unsafe id | `ArgumentException` | same (`RequireSafeId`) |
| `ReadSession` L39: no rollout | `FileNotFoundException` | same, for a missing session folder |
| `ReadSession` L41: overlong line | dropped before parsing | same |

## Codex/CodexMessages.cs (G1.3, G1.2 text rule)

| Branch | Codex | Copilot |
|---|---|---|
| `ReadMessage` L76: empty line | null | same |
| `ReadMessage` L78: not JSON | null | same |
| `ReadMessage` L81: type not `response_item` | null | **changed**: type must be `user.message` (`you`) or `assistant.message` (`copilot`); other events, including system and tool events, are skipped |
| `ReadMessage` L82: no payload object | null | **changed**: `data` must be an object |
| `ReadMessage` L84-90: role not user or assistant | null | not used (the type decides) |
| `ReadMessage` L91: no text | null | same: a message with no `content` text is not in the transcript |
| `ReadMessage` L92: injected context (user) | null | **not copied**: Copilot has no injected-context prefixes in the research note; G1.8 says none |
| `TextOf` L102-116: `content` string | `StringOf` (trimmed) | same |
| `TextOf` L107-113: `content` array | text parts joined by newline | not used (see `MessageText` above) |
| `TextOf` L115: other kind | null | same |

## Codex/CodexTranscriptReader.cs (G1.3)

| Branch | Codex | Copilot |
|---|---|---|
| `ReadPageAsync` L142: unsafe id | `ArgumentException` (before any read) | same |
| `ReadPageAsync` L146: no rollout | `FileNotFoundException` | same (no session folder or no `events.jsonl`) |
| `RequireSafeId` L155-161: empty, whitespace, unsafe char, `..`, control char | `ArgumentException` | same (copied rule set) |
| `FindRollout` L172: no history root | null | not needed: a missing folder is `FileNotFoundException` |
| `FindRollout` L173-177: UUIDv7 day folders | searched first | **not copied**: Copilot folders are direct, not dated (G-D3) |
| `ReadLines` L256-257: negative first line or count | clamped to 0 | same |
| `ReadLines` L265: line inside the page | added | same, over messages (not file lines) |
| `ReadLines` L267: page full | stop | same |
| `ReadLines` L269: `FirstLine = min(start, seen)` | clamp | same; `TotalLines` null (as Codex) |
| cap | none in Codex reader | **new**: at most 5,000 messages are read (P7) |

## Codex/CodexSummarizer.cs (G1.4)

| Branch | Codex | Copilot |
|---|---|---|
| `ExecArguments` L47: model outside pattern | `ArgumentException` | not used: there is no model argument (G-O3, Copilot's default model) |
| `SummariseAsync` L60: timed out | "the summary timed out after five minutes" | same text |
| `SummariseAsync` L61: exit code not 0 | "the summary failed (exit N)" | same; only the exit code is named |
| `SummariseAsync` L63: output empty after trim | "the summary came back empty" | same |
| `SummariseAsync` L62: output | trimmed stdout returned | same: the reply is stdout (verified 2026-11-22, G0.6) |
| `SummariseAsync` cancel | propagates, not wrapped | same |
| `SummariseSessionAsync` L75: unsafe id | `ArgumentException`, no read | same |
| `ReadInput` L83-84: no rollout | `FileNotFoundException` | same |
| `Prompt` L95: `</transcript>` in text | slash escaped | **not used**: the transcript is a file, never on the command line (G-D1) |
| `Timeout` five minutes | constant | same (`TimeSpan.FromMinutes(5)`) |
| `MaxInputChars` 120,000 | constant | same (S-D3) |

## Codex/CodexSummaryInput.cs (G1.4)

| Branch | Codex | Copilot |
|---|---|---|
| `Build` L123: cap not larger than notice plus separator | `ArgumentOutOfRangeException` | same |
| `Build` L127-130: a message that renders | kept in order | same, over `TranscriptLine` (`you: ` / `copilot: `) |
| `Build` L133: whole text within cap | returned as is | same |
| `Build` L139-146: cut, newest kept while they fit | oldest dropped | same |
| `Build` L149: nothing fits, newest kept | `CutMiddle(newest, budget)` | same |
| `Build` L150: result | notice, separator, body | same notice text (`CutNotice`) |
| `Rendered` L157-158 | `label: text`, or null | same over `TranscriptLine`; no null case needed |

## Codex/CodexSummaryText.cs (G1.4)

| Branch | Codex | Copilot |
|---|---|---|
| `Instructions` Minimal | 120 words; goal, done, open | same limit; wording says GitHub Copilot CLI session |
| `Instructions` Normal | 400 words | same limit |
| `Instructions` Complete | 1,200 words | same limit |
| `Instructions` other value | `ArgumentOutOfRangeException` | same |

## Codex/CodexTerminalOpener.cs (G1.5)

| Branch | Codex | Copilot |
|---|---|---|
| `NewCommandLine` model (L30-33) | model checked and added | **not copied** (no model dropdown, G-D9) |
| `NewCommandLine` L34: blank or null folder | `ArgumentException` | same |
| `NewCommandLine` L36: double quote | refused | same |
| `NewCommandLine` L38: exclamation mark | refused | same |
| `NewCommandLine` L40: percent sign | refused | same |
| `NewCommandLine` L42: control character | refused | same |
| `NewCommandLine` L44: trailing backslash | refused | same |
| `NewCommandLine` L46-48: result | `cmd.exe /d /c codex -C "<folder>"` | **changed**: `cmd.exe /d /c copilot -C "<folder>" --sync-session=off` (G-D8, G-D11) |
| `ResumeCommandLine` L58: null or not a UUID | `ArgumentException` | same |
| `ResumeCommandLine` L60: result | `cmd.exe /d /c codex resume <id>` | **changed**: `cmd.exe /d /c copilot --resume=<id> --sync-session=off` (no `-C`; G-D12) |
| `PickerCommandLine` | picker, the R7 ladder | **not copied** (gap, parity row 19) |

## Codex/CodexSetupCheck.cs (G1.6)

| Branch | Codex | Copilot |
|---|---|---|
| `Run` L88-89: PATH null | process PATH | same |
| `Run` L91: not found on PATH (`.exe`, `.cmd`, `.ps1`) | Problem row "not found on PATH: install ..." | same extensions; wording names Copilot CLI |
| `Run` L96: found | Ok row "found: <path>" | **changed**: `RunAsync` also reads the bundled version: `cmd.exe /d /c copilot --version --no-auto-update --sync-session=off` (G-D13, G-D8; the `--sync-session=off` on a version call is not verified by a run, because no copilot process is started in G1). A version that does not parse is shown as not read (Ok row); a non-zero exit or a timeout is a Warning row. The status is written before the path, so a clipped row keeps it. The 20-second timeout is an assumption |

## Codex/CodexStartLauncher.cs (G1.7)

| Branch | Codex | Copilot |
|---|---|---|
| `StartsFolderBeside` | `Starts` beside the state file | same |
| `WriteStart` L41-49: folder missing | created | same |
| `WriteStart` L44: name | local time, 8 hex digits, `.txt` | same |
| `WriteStart` L46: exists already | `CreateNew` throws | same |
| `CommandLine` L64-65: blank or control or line separator path | `ArgumentException` | same (`RequirePlainPath`) |
| `CommandLine` L66-67: model | checked | **not copied** (G-D9) |
| `CommandLine` L69: model or no model | run line | **changed**: `& $shim.Source -i $prompt --sync-session=off` |
| `CommandLine` L73: shim check | `codex.ps1` | `copilot.ps1` |
| `CommandLine` L74: no shim | message names the kept path, exit 1, prompt file kept | same, with `Copilot` wording |
| `CommandLine` L75-77: read, delete, set location | same order | same |
| `FindPwsh` L92: blank search path | null | same |
| `FindPwsh` L96: folder holds `pwsh.exe` | full path of the first | same |
| `RemoveOldStarts` L108: no folder | 0 | same |
| `RemoveOldStarts` L114: name not the start shape | skipped | same |
| `RemoveOldStarts` L115: newer than cutoff | kept | same |
| `RemoveOldStarts` L118-128: delete; IO or access error | removed; a locked file is kept | same |
| `Quoted` L142-152: single quotes (5 kinds) | doubled | same |

## Codex/CodexResumeText.cs (G1.8)

| Branch | Codex | Copilot |
|---|---|---|
| `SplitTurns` L173: a `you` line or the first line | new turn | same |
| `Tail` L191: turns or cap not positive | empty | same |
| `Tail` L193: over token cap, more than one turn | oldest removed | same |
| `Tail` L196: over character cap, more than one turn | oldest removed | same |
| `Tail` L197: one turn left | `CutMiddle` to the cap | same |
| `Compose` L209: source id set | "Continues session" line | same, with the `copilot:` label of the session key in the caller |
| `Compose` L210: header | "...earlier Codex CLI session that had gone cold..." | **changed**: "...earlier GitHub Copilot CLI session..." |
| `Compose` L211: summary and level set | summary section | same |
| `Cap` L231: summary over cap | cut with a note | same |
| `Compose` L217: turns > 0 and a tail | tail section | same |
| `RenderLine` L241-243: `you` / `codex` | prefix | `you` / `copilot` prefix |
| `RenderLine` other kind | null (left out) | same |
| Injected-context prefixes | none in this file (they are in `CodexMessages`) | **not copied** (G1.8: none for Copilot) |

## Codex/CodexProvider.cs (G1.5)

| Branch | Codex | Copilot |
|---|---|---|
| ctor `paths ?? ForCodex()` | default paths | same, `ForCopilot()` |
| `Available` (net10.0 false) | window hides Codex on net10.0 | **not copied**: Copilot history is plain text, so it is offered on both targets (parity row 1) |
| `HasRollout` | search for the id | **not copied**: resume ladder is a gap (parity row 19) |
| interfaces | `IProvider, IHistorySource, ITranscriptReader, ITerminalOpener` | same four (`IProvider` and the three) |

## New in G1 (no Codex counterpart)

- `Copilot/CopilotFiles.cs`: shared helpers (events path, the id check copied from `CodexTranscriptReader.RequireSafeId`, collapse and cut, time parse, trimmed values).
- `Copilot/CopilotScratchFolder.cs`: scratch folders for the summary call (G-D15): `CopilotScratch` beside the state file, marker first, delete only when marked, retries a file lock five times (200 ms apart, an assumption), startup removes marked folders older than one day.
- `Copilot/CopilotSummaryInput.cs` and `CopilotSummaryText.cs`: the messages cut to 120,000 characters (the newest kept), and the level instructions written to `instructions.txt` with the data line.
- `Copilot/CopilotSummarizer.cs`: the call (`/d /c copilot -p ... --sync-session=off ...`), with `new ProcessCliRunner("cmd.exe")` as the default runner.
- Deliberate deviation: the summary reads all messages (no 5,000 cap), so the newest text is kept; the transcript tab and its pages use the 5,000 cap.

## Reused, not copied

- `Codex/RolloutReader.cs`: `Lines` (line splitter with the 16 MB cap), `TryParseObject`, `StringOf`, `MaxLineLength`. Used as they are (internal helpers in the same assembly, no change). Copilot's own file opening (`FileShare.ReadWrite | FileShare.Delete`) stays in the Copilot code, because `RolloutReader.Open` is for `.zst` as well.
- `Resume/ResumeText.cs`: `CutMiddle`, `EstimateTokens`, the cap constants, `SummaryLevel`.
- `Resume/SummaryFiles.cs`: unchanged, used by G3 (not in G1).

## Not copied in G1 (plan rows)

- `CodexSessionTime.cs`: Copilot ids carry no time (all 52 local folder names are UUIDv4: version nibble 4, counted, names not printed). `FirstActivity` is `session.start.startTime` (G-D19).
- `CodexHistoryQuery.cs`, `CodexModelList.cs`, `CodexUsageReader.cs`, `CodexUsageMemo.cs`, `CodexExit.cs`: parity rows 19, 20, 21, and G2.8.
