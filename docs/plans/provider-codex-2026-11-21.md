# Provider seam and a Codex rail (plan, 2026-11-21)

Status: **plan, nothing built.** It turns `provider-seam-design-2026-11-19.md` (tiers 0-4) into steps and adds what the Codex research found. Research report: scratchpad `codex-research.md` (Haiku researcher, 2026-11-21; each item marked VERIFIED/ASSUMED there; the orchestrator has re-checked only the items marked "checked" below).

## Goal
A dropdown at the top of the rail: **Claude | Codex**. Choosing one swaps what the rail lists (live rows, history, search, tags). Tabs already open keep their own provider and are untouched. Claude behaviour and its 2000+ tests do not change. Principle 3 holds: the window never types into a session or accepts anything for the owner.

## Review amendments (ChatGPT review of both plans, 2026-11-21; each point checked by the orchestrator)
Accepted and written into the plan below; the plan is **not ready for a coder until stage B has reported**. New order: A1 (the map) -> **B (spike)** -> amend this plan from B -> A2-A6 -> C.
- **R-1 Live rows by cwd+time are unsafe** (two terminals in one folder). Valid. Stage C does **not** claim Codex live rows. C shows Codex history (reliable thread ids), plus Codex terminal tabs the window owns. A tab is tied to a thread id only if B finds a reliable way to capture the id at creation (a SessionStart hook, `codex exec --json`, or app-server). Otherwise there is no live status for Codex.
- **R-2 `SessionDigest` is not provider-neutral** (it has `LimitStop`, `ContinuesSessionId`, `LastRecap`, `Compactions`, `AiTitle`), and **0 is not unknown**. Valid (checked: `TranscriptIndex.cs`). A2 defines a neutral `SessionSummary` (fields with the same meaning in every provider; counts nullable, null = unknown) plus a provider-specific extension. Claude's digest maps onto it without loss; the existing UI keeps reading the digest for Claude until each screen is moved. No Codex concept is forced into a Claude-shaped field.
- **R-3 Typed identity.** Valid. A2 adds `SessionKey(ProviderId, SessionId)`, used by the rail, the tab and terminal dictionaries, the library, tags/notes/archive, caches and persisted state; the persisted string is `claude:<id>` / `codex:<id>`. A1's map lists every dictionary or file keyed by a bare session id.
- **R-4 Migration vs downgrade conflict.** Valid: "one-way" and "old build still reads it" cannot both hold if keys are rewritten. Decision (default, owner may change): the new build writes a **new versioned file** (`library.v2.json`) and **leaves `library.json` untouched**; it reads `library.json` as `claude:` entries when v2 is absent. A downgrade keeps working on the old file but does not see marks made after the upgrade (said in the README). A5's test: an old-format file keeps its tags and notes, and its bytes are unchanged after the new build runs.
- **R-5 App-server vs raw files.** Valid. Stage B compares the two (B5). Whether an app-server instance can see threads opened by separate interactive CLI processes is **unknown**; do not assume it. Approval boundary: a test asserts the watcher never sends an approval or a denial.
- **R-6 Zstd.** Checked locally 2026-11-21: `ZstandardStream` is in the **net11.0** reference assemblies and **not** in net10.0 (grep of both `System.IO.Compression.dll` references). So net11 has a BCL route; net10 needs a package, or Codex history is net11-only. B1 decides; the dependency stays out of Core if possible (a separate `OpenTail.Bullshark.Providers.Codex` assembly).
- **R-7 Compressed paging.** Valid: a zstd stream is not random access. B1b must show tail read, paging, find and cross-file search on a large rollout, memory use, and **no plaintext copy or temp file on disk** (privacy rules).
- **R-8 Capabilities.** Valid: avoid a growing list of booleans. A provider implements only the interfaces it has, and the UI asks whether the provider offers one. "Open a terminal" and "start a supervised session" are different interfaces.
- **R-9 Documents.** `provider-seam-design-2026-11-19.md` is marked partly superseded (its header). Ollama naming (tier 4) is deferred, unscheduled.

## Decisions taken (owner, 2026-11-21) and assumptions
- Dropdown swaps the rail only; no mixed lists. (Owner.)
- Later providers must fit the same seam. (Owner.) Design the interface against **three** providers on paper (Claude, Codex, one of opencode/Copilot/aider) so it is not shaped by two.
- A1 (assumption): Codex has no `--bg` equivalent. Its live rows are **terminal tabs the window owns** (a Codex process in an embedded terminal), not supervised background sessions. Stage C ends there; supervision is stage E and optional.
- A2 (assumption): the supervisor loop, `claude stop/respawn`, trust, daemon health and limit-wake stay Claude-only and are hidden for Codex by capability flags.
- A3 (decision for the owner, section "Open decisions"): adding packages to Core (zstd, SQLite).

## Facts that shape the plan
Checked by the orchestrator in code (2026-11-21):
- `Core` has **no NuGet packages**; `App` has WPF-UI.Terminal and CommunityToolkit.Notifications. Targets: net10.0 and net11.0 (SDK 11.0.100 installed).
- 48 Core files and 22 `ClaudeCli` users reference Claude. Paths go through `ClaudePaths` (6 call sites: `ProjectsRoot`, `ConfigDir`, `ClaudeJson`, `ArchiveRoot`), which is a small, good seam.
- Live rows = `SessionRecord` from the supervisor (`claude --bg`), terminal = `claude attach` (`SessionWindow.xaml.cs:617-632`, `DetachedSessionWindow.cs:29`). History rows = `TranscriptIndex.Build(projectsRoot)` -> `SessionDigest` (`SessionWindow.Rail.cs:662`).
- `SessionLibrary` keys are bare session ids (tags, notes, archive); the design notes said `provider:sessionId`.

Researcher-reported, **to be re-checked in stage B** (a spike, before any plan item depends on them):
- Codex rollouts are `~/.codex/sessions/YYYY/MM/DD/rollout-*-<uuid>.jsonl.zst`: **62 of 63 files here are zstd-compressed**, 1 plain. Lines: `{timestamp,type,payload,ordinal}`; types `session_meta`, `turn_context`, `event_msg`, `response_item`, `world_state`.
- `~/.codex/state_5.sqlite` table `threads` (id, rollout_path, cwd, title, name, model, reasoning_effort, tokens_used, archived, created/updated_at, first_user_message, preview...) is a ready session list. `session_index.jsonl` holds names. `thread_history_1.sqlite` has turns with status (completed, failed, inProgress, interrupted).
- Token usage and **rate limits** are in rollout `event_msg` (`token_count`: `rate_limits.primary{used_percent, window_minutes, resets_at}`), so Limits is possible later.
- `codex app-server` (JSON-RPC, stdio/unix/ws) has `thread/list`, `thread/read`, `thread/status/changed` with `waitingOnApproval` / `waitingOnUserInput`, and `account/rateLimits/read`. That is the only source of a **Needs-you** signal; approvals are not in the files. Approval requests go only to clients that set `requiredHumanApproval`: a watcher must not answer them (principle 3).
- No JSON session list on the CLI; no `--name`; effort via `-c model_reasoning_effort=`; `-C <dir>`, `-m`, `codex resume <id|name>`, `codex fork`.
- Schema format is not promised by OpenAI (format drift risk, R2).

## Contracts this must satisfy (stage A writes these as code)
What the app relies on today, per area. A provider must meet each row or declare the capability false.

| Area | What callers rely on today (source) | Seam member | Codex |
|---|---|---|---|
| History list | `TranscriptIndex.Build(root, cachePath, log)` -> `SessionDigest` list, skipped list, totals; threads; cache keyed by file stat; one bad file skipped, never fatal (round 12) | `IHistorySource.Build(cache, log, ct)` | rollout + `threads` row; zstd read; same skip rule |
| Digest fields | SessionId, FilePath, ProjectFolder, IsSubagent, First/LastActivity, turns, user/assistant counts, ToolCalls/Names, FilesWritten, LastAssistantText, LastModel, ContextTokens, Usage, FirstUserText, titles, recap, compactions, continues-id, failed tool calls | `SessionDigest` stays the neutral type; unknown = null/0 | map from `threads` + rollout; recap/compaction null |
| Transcript view | `TranscriptPager`, `TranscriptFind`, `TranscriptReader.Summarize(lines)` on lines of Claude JSONL | `ITranscriptReader` (page, find, summary) | own reader over decompressed lines |
| Search | pre-filter + scan of JSONL on several threads, cancellable | `IHistorySource.Search` | same scan on decompressed text; label "Codex only" |
| Live list | `ClaudeCli.ListAgentsAsync` -> `AgentInfo` (state, status, waitingFor, cwd, startedAt) | `ILiveSource.ListAsync` | none in A1; tabs only. Stage E: app-server `thread/loaded/list` |
| Launch / resume | `BackgroundLaunch` -> `claude --bg ... --resume`; id read back from `agents --json` (F18) | `ISessionLauncher` | `codex -C dir -m m [-c model_reasoning_effort=e]` and `codex resume <id>` in a terminal tab; id read from `threads` |
| Attach command | `ClaudeCli.AttachCommandLineOf(id)` | `AttachCommand(session)` | `codex resume <id>` |
| Paths | `ClaudePaths.*` | `ProviderPaths` (home, history root, archive root) | `CODEX_HOME` or `~/.codex` |
| Library keys | bare session id | `provider:sessionId`; **migration**: existing keys read as `claude:` | `codex:<uuid>` |
| Limits / cache / effort / magic rename / storage | Claude transcripts only | capability flags | Limits yes later (rate_limits); rest hidden |
| Trust, daemon, respawn, supervisor, limit-wake, scheduled runs | Claude only | flags `CanSupervise`, `HasTrust`, `HasDaemon` | all false |

## Existing-behaviour map (to fill row by row before stage A codes; never from memory)
Stage A item A1 produces `provider-seam-map-2026-11-21.md`: one row per `Claude*` / `ClaudePaths` / `ClaudeCli` use in the 48 Core files and the App partials, with the decision: *neutral already / moves behind the interface / stays Claude-only (flagged)*. The grep is the input; no row is skipped.

## Stages (each ends green, commits, pushes if tests pass)

### Stage A: the seam, Claude only (pure refactor; no new behaviour)
- [ ] A1 Write the map above. *Why:* the plan's size claims (48 files) are counts, not a design.
- [ ] A2 Add `Core/Providers`: `ProviderId`, `SessionKey`, a neutral `SessionSummary` (R-2), `IHistorySource`, `ITranscriptReader`, `ITerminalOpener`, `ISupervisedLauncher` (Claude only), `ILiveSource` (optional), `ProviderPaths`. Capabilities are the interfaces a provider implements (R-8). **Written after stage B**, so it reflects what Codex really offers. *Why:* the contracts table, as code; keep them small and read-only where possible.
- [ ] A3 `ClaudeProvider` wraps the existing classes with **no change to their logic**. *Why:* existing tests must pass untouched.
- [ ] A4 The window holds an `ActiveProvider`; rail, search, storage and palette go through it. A tab records the provider that opened it. *Why:* "open tabs unaffected" is a property of the tab, not of the dropdown.
- [ ] A5 `SessionKey` everywhere a bare id is used (R-3); the library moves to a new versioned file and the old file is left byte-for-byte untouched (R-4). *Why:* tags and notes must survive, and a downgrade must not lose or corrupt the old file.
- [ ] A6 Fake provider in tests (a second, in-memory provider) so the dropdown and the interfaces are tested without Codex: identity collisions (same id in two providers), tab ownership after a provider switch, a missing interface, and the migration and downgrade behaviour (R-4).
- Check: full suite same count + new tests, 0 failures; `git diff --stat` reviewed; screenshot script shows an unchanged rail. Reviewer: Sonnet (high risk: keys, tests' shape).

### Stage B: Codex spike (read-only, no UI)
- [ ] B1 Decode one real rollout: can .NET read `.zst`? Options: BCL Zstandard on net11 (**unverified**; net10 lacks it), or a small MIT package (ZstdSharp.Port; licence and version to confirm). Report which, with the command and result. *Why:* decides O1 and whether net10 can stay a target.
- [ ] B2 Read `state_5.sqlite` `threads` read-only while Codex runs (WAL, `mode=ro` / immutable?). Options: Microsoft.Data.Sqlite (MIT) or parse `session_index.jsonl` + rollout headers only (no SQLite). Report locking behaviour with a Codex process running. *Why:* O2.
- [ ] B3 Field map: rollout -> `SessionDigest`; list which fields are null. Fixture files with **synthetic content only** (privacy: no real transcripts in the repo).
- [ ] B1b Paging check (R-7): tail read, page, find and cross-file search on a large compressed rollout; memory; no plaintext copy on disk.
- [ ] B5 Compare the two read paths (R-5), one row each: **(a) files + SQLite:** readable while Codex runs, schema and locking, format drift. **(b) `codex app-server` (stdio, read-only):** `thread/list` and `thread/read` without resuming; do threads opened by separate interactive CLI processes appear, and with what status; what a client sees when an approval is pending (and prove nothing is answered).
- [ ] B6 Session identity (R-1): how to learn the thread id of a terminal the window just opened (hook, `exec --json`, app-server, or a `threads` row matched by something unique that we pass in). Report whether any reliable method exists.
- [ ] B4 Decide from B1-B6, record in this plan, and amend A2 and C (neutral summary, identity, interfaces). Stop here if the dependency cost is not acceptable.

### Stage C: Codex provider, read-only + terminal launch
- [ ] C1 `CodexHistorySource` (list, digest, skip bad files, cache keyed by stat, as round 10-12).
- [ ] C2 `CodexTranscriptReader` (view, find) and search; label "Codex only".
- [ ] C3 `CodexTerminalOpener`: a new tab runs `codex -C <dir> [-m ..]`; resume runs `codex resume <id>`. The tab is **owned by the window and tied to a thread id only if B found a reliable capture** (R-1). **No matching by cwd and time**, and no live-row claim otherwise. No supervisor, no `--bg`.
- [ ] C4 Dropdown at the top of the rail (name, remembered in ui.json, default Claude). Hidden features per flags; the empty states say why ("Codex has no background sessions").
- [ ] C5 Setup check: `codex` on PATH and version, with the same wording rules as the Claude check.
- [ ] C6 Tests: reader on synthetic fixtures, drift (unknown line type -> skipped, not fatal), dropdown swap keeps tabs, keys.
- Check: tests, then screenshots with a scratch state; one real run by the owner (Codex is installed here, so the owner can open a real history).

### Stage D: Codex extras (each optional, own decision)
- [ ] D1 Limits page for Codex from `rate_limits` in rollouts.
- [ ] D2 Effort/usage report for Codex (tokens by model from `token_count`).
- [ ] D3 Tags/notes/archive work on `codex:` keys (should come free from A5; verify).

### Stage E: supervision of Codex (only if the owner wants it; separate plan)
- Via `codex app-server` over stdio: `thread/status/changed` for working / `waitingOnApproval` / idle, giving a real Needs-you row. Read-only watcher; never answers an approval. Needs: a JSON-RPC client, `initialize` shape and the approval reply shape from the generated schema (researcher left these open), and a decision on `requiredHumanApproval`.

## Risks
- R1 Core gains its first packages (zstd, maybe SQLite): licence, size, trimming, supply-chain review. Mitigation: B1/B2 first; prefer BCL/no-SQLite if it works.
- R2 Codex file formats are unpromised and change (the `_5` in `state_5.sqlite` suggests versioned schemas; not verified). Mitigation: a version/format check, skip-on-unknown, fixtures, "Codex history may be incomplete" state.
- R3 Privacy: Codex transcripts hold the same private text. Redactor, diagnostics allowlist and log rules apply unchanged; no real transcript in tests or logs.
- R4 Mixed features: Search, Limits, Storage, effort assume one provider. They follow the dropdown and show a provider label; no cross-provider totals (a total of mixed token types is wrong).
- R5 Stage A touches many files in a repo that has an uncommitted pop-out change; commit or set aside first (owner's files; do not stage).
- R6 Scope: product name and README say Claude Code supervision. Wording changes only after C works (design-notes question 1).

## Open decisions for the owner
- O1/O2: accept new packages in Core (zstd, SQLite) if the spike needs them? Default: only if B1/B2 find no BCL route; Microsoft.Data.Sqlite MIT, ZstdSharp.Port MIT (both to confirm).
- O3: library downgrade (R-4): default is a new versioned file with the old file untouched; marks made after the upgrade are not seen by an older build. Alternative: accept that a downgrade loses the new marks. Say if you want something else.
- O4: do tier 0 (plain "New terminal" tab) now, as part of C3, since Codex live rows need it anyway (default yes, one mechanism).
- O5: stop after B for a diff-size look, as the 2026-11-19 design suggested (default yes).

## Delegation and review
- Orchestrator (this session) writes the map (A1) and the interfaces (A2) itself: they are the contracts. Haiku coder gets A3-A6 as one batch with tests, then B (spike, report only), then C as one batch.
- Sonnet review after A (diff range, contracts table, key migration) and after C. Findings go in `provider-codex-2026-11-21-review-notes.md`.
- Estimate (range, driven by A1's count and B's dependency result): A about 1-2 days coder time; B half a day; C 2-4 days; D each about 1 day; E a week or more.
