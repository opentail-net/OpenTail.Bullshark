# GitHub Copilot CLI as a third provider (plan, 2026-11-22; revision 2 after review)

Status: **plan, nothing built.** Research: `docs/research/copilot-cli-research-2026-11-22.md`. Review: `docs/plans/provider-copilot-2026-11-22-review-notes.md` (Sonnet, findings C1 to C22; each one's disposition is in the table at the end). Design rules come from `provider-codex-2026-11-21.md`; where this plan copies a Codex decision, it names it.

## Goal

Copilot sessions get the same features Codex has in the app: a rail of past sessions by folder, a read-only transcript tab, search by title, name, tag and note, tags and notes and archive and pin, a terminal tab for a new session or a resume, a resume screen with the same three modes (full, last turns, summary), an on-demand summary through Copilot's own model, a setup check, and a dropdown at the top of the rail. Claude and Codex behaviour does not change. Principle 3 holds: the app never types into a session or accepts anything for the owner.

**Parity target:** everything Codex has in the app, except the items the Codex plan left open (usage, model list, message-text search, tab-close semantics). Where the Copilot files give more than Codex's do, the plan says so as a bonus step, and it is optional.

## Decisions taken (defaults, made in AFK mode; the owner may overrule any of them)

- **G-D1 Summaries.** On demand only, one model call per press, through `copilot -p`. The transcript is a **file** in a scratch folder and the model reads it with the `view` tool, the only tool allowed; the prompt on the command line is short. Stdin is not a prompt source (`-p` requires a value), and a 120,000-character transcript does not fit a Windows command line, so the file route is the design. Copilot's own default model (G-O3). Input capped at 120,000 characters, oldest text first (Codex S-D3). Three levels with Codex's word limits (S-D4). Platform rule: Copilot sessions are summarised only by Copilot.
- **G-D2 Summary flags and isolation (C1, C4, C5, C6).** Every summary call passes: `--config-dir <scratch>` and `--log-dir <scratch>` (both in the CLI's help; the environment cannot be set, because `ICliRunner` has no environment parameter, `Core/Claude/CliRunner.cs:18-23` and `:53-63`), `--sync-session=off`, `--output-format text`, `--silent` (no usage stats in stdout), `--no-ask-user`, `--no-custom-instructions`, `--disable-builtin-mcps`, and no tools (the no-tools syntax is probe P-C4). The call runs in a **neutral scratch folder**, not the session's folder, so a repository's hooks, instructions and MCP config are not loaded from the session. The scratch config folder is the isolation: it holds a copy of the transcript while the call runs, so it is deleted (G-D15). **Verified 2026-11-22 (G0.6):** one call; the reply was the one word in the file; the only tool called was `view`; the owner's `session-state` stayed at 52 folders; sign-in worked from the scratch config; 1 premium request on the default model `mai-code-2-flash`. `--config-dir` prints a deprecation warning on stderr (R-C8).
- **G-D3 History source (C9).** `session-state/<id>/events.jsonl` for the messages, and the folder's `workspace.yaml` for the name, folder, activity times and `user_named`. No SQLite (Codex O2). No compression, so Copilot history works on **both** net10.0 and net11.0. A folder with only `workspace.yaml` (5 of 52 locally) is an **empty session**: counted and not shown, never "damaged".
- **G-D4 No screen reading in v1.** The files carry the state. Screen footers are not parsed: the example fixtures are for CLI 1.0.114, the CLI on this PC is older, and the screen format drifts (R-C5).
- **G-D5 Finished state is an assumption (C10).** Every local file is a single-turn session with a routine shutdown, so multi-turn `assistant.turn_end` behaviour and running or crashed sessions are **not verified**. Nothing in v1 depends on a finished state. G5.3 waits for a multi-turn capture.
- **G-D6 Tabs (C3).** A Copilot tab is owned by the window. It is tied to a session id only if probe P-C1 shows `--session-id` sets the id at creation (then a step better than Codex, whose tabs have no id link). Otherwise no id link, as Codex.
- **G-D7 No supervision in v1.** No Needs-you, no live rows, no hooks installed by the app (Codex O5). Hook payloads are captured later (G5.4).
- **G-D8 Privacy default (C7).** Every command line the app builds carries `--sync-session=off`, including the terminal tab, the resume, the launch file and the summary call. A test asserts it on every command string. Owner may change this (G-O2).
- **G-D9 Not in v1:** usage and limits (Codex SD1 and SD2), the model dropdown (Codex P9), the effort dropdown, and message-text search (Codex O3).
- **G-D10 Separate classes, no shared control.** Copilot gets its own classes, copied from the Codex ones and renamed. Codex and Claude files are not touched. The shared-control refactor (Codex P10) stays deferred.
- **G-D11 Command form (C2).** The terminal and the summary runner start `copilot` through `cmd.exe /d /c`, as Codex does, because the install is a shim (Codex `CodexTerminalOpener.cs:47,60`; `CodexSummarizer.cs:49`). The folder rules of Codex apply (no double quote, percent sign or exclamation mark; refused otherwise).
- **G-D12 Resume (C2, C15).** `ITerminalOpener.ResumeCommandLine(sessionId)` (`Capabilities.cs:32`) takes no folder, so the interface stays as it is: the resume command is `cmd.exe /d /c copilot --resume=<id> --sync-session=off`, and the **window** starts the tab in the session's folder, as Codex's resume does. A resume is refused, with a status line, when the id is not in the app's own list or the folder no longer exists. No silent fallback to a picker or the newest session (Codex R7 is a gap in v1, recorded in the parity table).
- **G-D13 Version read (R-C5).** The setup check reports the version of the copy the app will run, with `copilot --version --no-auto-update` (no update check). That is the **bundled** version and the check says so. Whether the read starts a download is probe P-C3.
- **G-D14 Launch file.** A new session with a prompt uses the PowerShell 7 launcher (Codex R-D6), calling `copilot.ps1 -i <text>` with `--sync-session=off`. The prompt is then on the process command line while it runs (C21), as with Codex; recorded in R-C3.
- **G-D15 Scratch folders (C6).** Each summary call gets a scratch parent under the app's state folder, beside `Starts` and `Summaries` (not `%TEMP%`): `CopilotScratch\<guid>\` with a marker file `opentail-scratch.marker`. The folder is deleted in `finally`, **after** the process tree is dead (cancel: kill the tree, then delete, with a short retry for file locks). Startup removes only marked folders older than one day. Nothing is deleted that lacks the marker.
- **G-D16 Titles, counts and nulls (C9, C18).** Title: the workspace `name` when it is not empty, else the first user message, cut at 80 characters. Counts are **zero** when the file was read and the event is absent (a known zero); **null** only when the value cannot be found at all (for example no model in the file). `Folder` is `cwd` from `workspace.yaml`, else `session.start.data.context.cwd`.
- **G-D17 Message field (C11).** Transcript and summary use `user.message.data.content` and `assistant.message.data.content`, the text as typed or as the model wrote it. `transformedContent` is not used until probe P-C10 shows what it holds (counts only).
- **G-D18 Read cap and cache (C16).** Lines over 16 MB are skipped and counted (as Codex's reader). No cache in v1: measure a full scan of the 52 folders first (C-D3 rule); add a cache only if the rail is slow.
- **G-D19 Shared record (C8, G-D10).** `SessionSummary` (`Providers/SessionSummary.cs:7-17`) is not changed. Its fields for Copilot: `Key` copilot:<id>; `Title` (G-D16); `Folder`; `FirstActivity` from `session.start.startTime`; `LastActivity` from `workspace.yaml` `updated_at`; `IsSubagent` **null** (not read in v1); `UserMessages` and `AssistantMessages` counted; `Compactions` **null**; `Model` from the last `session.model_change` (`newModel`) or `selectedModel`. Copilot's `gitRoot`, `branch` and `repository` live on a Copilot-only record (`CopilotSessionInfo`), not on the shared record, and are not shown in v1.
- **G-D20 Ended tabs (C14).** The ended state is copied from Codex (`CodexExit`, `SessionWindow.Codex.cs:628-691`), as `CopilotExit`: a tab whose process exits shows "(ended)" with "Ended; no exit code was reported." The exit code is not exposed by the terminal package, as for Codex.

## Owner decisions (open; the work above proceeds with the defaults)

- **G-O1 Licence reading.** The CLI licence (LICENSE.md, section 3) forbids creating derivative works of the software. The plan reads files the owner's own CLI writes, copies no code, and does not modify the CLI. Confirm before the Copilot provider is released. Not legal advice. G1 proceeds meanwhile.
- **G-O2 `--sync-session=off` on every call** (G-D8). A plain default; the owner may keep it or turn sync on.
- **G-O3 Summary model.** Default: Copilot's own default model (observed 2026-11-22: `mai-code-2-flash`; one premium request for a one-word read). The owner may pick a cheaper one once the list is known (`--acp`, G5.1). Each call uses the owner's Copilot allowance; premium requests are counted in `session.shutdown.totalPremiumRequests`.
- **G-O4 Probe P-C2 (one model call).** The same allowance rule the Codex probes used. **Status 2026-11-22:** run once on the file route (G0.6): 1 premium request, the default model, scratch removed. An earlier attempt piped stdin into `-p`; it most likely made no model call (`-p` needs a value; its output was not kept, so this is inferred). Veto now would switch Copilot summaries off in v1 (the resume screen offers "From the last turns" only).
- **G-O5 Which Copilot install the app runs.** Three on this PC, three versions: the WinGet `copilot.exe` bundles **1.0.34**; the npm `copilot.cmd` bundles **1.0.51**; with auto-update on, a downloaded copy reports **1.0.104-3**. Default: the copy `cmd.exe` resolves first (G0.2 finds it), shown with its bundled version in the setup check. The owner may pin one copy, or remove one.
- **Codex O1 to O7 apply to Copilot.** Insights and Settings (O1), tab close (O2), message-text search (O3), a per-day summary cap (O4), hooks (O5), Claude supervision while a Copilot view is shown (O6), and the app-server route (O7). Until the owner decides, Insights and Settings are unchanged for Copilot and the close behaviour is Codex's.

## Parity with the Codex side

"Gap" means the Copilot side deliberately does not match in v1, and why.

| # | Feature | Codex (plan step) | Copilot step | Status |
|---|---|---|---|---|
| 1 | Provider dropdown, three choices | C4 | G2.1 (on both targets; Codex is hidden on net10.0, C-D2, so Copilot is not) | planned |
| 2 | History rail grouped by folder | C1, C-D1 | G1.2, G2.2 | planned |
| 3 | Empty state says why and where | C4 | G2.2 (the session folder path) | planned |
| 4 | Live rows | none (C-D6) | none in v1; a finished state is a bonus only after G5.3 | same as Codex |
| 5 | Transcript tab, messages only, cap 5,000 | P7 | G1.3, G2.4 | planned |
| 6 | Search by title, name, tag, note | P6 step 1 | G2.3 | planned |
| 7 | Search of message text | P6 step 2 (O3) | not in v1 (G-D9) | gap, owner decision |
| 8 | Tags, notes, archive, pin, colour, rename under a provider key | D1, P4, P5, P11 | G2.3 under `copilot:<id>` | planned |
| 9 | Row menu | Codex row menu | G2.5, same items and order | planned |
| 10 | Resume in a terminal tab (full) | R-D9, C3 | G1.5 (`cmd.exe /d /c copilot --resume=<id>`; the tab starts in the folder, G-D12) | planned |
| 11 | Resume screen: full, last turns, summary | R5, R-D2 | G3.1 | planned |
| 12 | Kept summary, Copy, cancel, one call per press | R-D3, S-D5 | G3.1, G3.2 | planned |
| 13 | Start text from summary or last turns; launch file | R-D5, R-D6, R2, R3 | G1.7, G1.8, G3.2 | planned |
| 14 | Summary through the platform's own model | D2, S1-S3 | G1.4 (`copilot -p`, G-D2) | planned, **after P-C2** |
| 15 | Setup check (on PATH; version) | C5 | G1.6 (bundled version, G-D13) | planned |
| 16 | Palette: Claude-only items hidden | P1 | G2.6 | planned |
| 17 | Tab identity | B6: no route for Codex | G-D6; a bonus only if P-C1 passes (G2.9) | depends on P-C1 |
| 18 | Ended tab state | `CodexExit` | G2.8 (copied as `CopilotExit`, G-D20) | planned (was missing; C14) |
| 19 | Resume ladder: missing id, picker, newest in folder | R7, `CodexTerminalOpener.PickerCommandLine`, `CodexHistoryQuery` | **gap**: refused with a status line (G-D12); probe P-C11 records what the CLI does | gap (C15) |
| 20 | Model dropdown | P9 | not in v1 (G-D9); list from `--acp` later (G5.1) | gap |
| 21 | Usage, limits, effort report | SD1, SD2 (not built) | not in v1 (G-D9); usage is in the files (G5.2) | gap |
| 22 | Supervision, trust, daemon, limit wake, magic rename, changes | Claude only | Claude only (same) | n/a |
| 23 | Needs-you | none | none in v1 (G-D7) | gap, owner decision |
| 24 | Restore open tabs after restart | not restored (C-D5) | not restored (same) | same as Codex |
| 25 | Insights and Settings under the provider | O1 (open) | follows the owner's answer | open |
| 26 | Tab close: process end | O2 (open) | same as Codex | open |

## Contracts this must satisfy (read the code before each step; a coder checks the signature)

| Member | Where | What callers rely on | Copilot |
|---|---|---|---|
| `ProviderId` | `Core/Providers/ProviderId.cs:7-13` | static members `Claude`, `Codex`; `ToString` | add `Copilot = new("copilot")` |
| `ProviderChoice.Parse` / `TextOf` | `Core/Providers/ProviderChoice.cs:13,17` | know only `codex` and `claude`; `TextOf` falls back to Claude | add `copilot`; the fallback must not map Copilot to Claude |
| `ProviderPaths` | `Core/Providers/ProviderPaths.cs:10,16,20` | `ForClaude`, `ForCodex(env, userProfile)` | add `ForCopilot`: `COPILOT_HOME` if set, else `<user profile>/.copilot`; history root `session-state`; no archive root |
| `SessionKey` | `Core/Providers/SessionKey.cs:7,15,32` | `ToString` is `provider:id`; `Parse`; `HasProviderPrefix` | `copilot:<uuid>` |
| `SessionSummary` | `Core/Providers/SessionSummary.cs:7-17` | the row fields; null means unknown | fields as G-D19; **not changed** |
| `IHistorySource` | `Core/Providers/Capabilities.cs:10` | `ListAsync` returns rows | `CopilotHistorySource` (G1.2) |
| `ITranscriptReader` | `Capabilities.cs:20` | `TranscriptPage(Lines, FirstLine, TotalLines)`; the Codex tab reads messages | `CopilotTranscriptReader` returns messages; `TotalLines` may be null |
| `ITerminalOpener` | `Capabilities.cs:26-32` | `NewCommandLine(folder)`, `ResumeCommandLine(sessionId)` (no folder) | `CopilotTerminalOpener` (G1.5); the window sets the resume tab's folder (G-D12) |
| `ISupervisedLauncher`, `ILiveSource` | `Capabilities.cs:39,47` | presence decides the Claude-only features | **not implemented**; `ProviderFeatures.Has<T>` (`ProviderFeatures.cs:10`) returns false |
| `IProvider` | `Providers/IProvider.cs:6` | the object the window holds | `CopilotProvider` implements `IHistorySource`, `ITranscriptReader`, `ITerminalOpener` only |
| `ICliRunner.RunAsync(args, workingDirectory, timeout, ct, standardInput)` | `Core/Claude/CliRunner.cs:18-23` | arguments, working folder, timeout returns `TimedOut`, stdin written then closed | used with `ProcessCliRunner("cmd.exe")` and args `/d /c copilot ...`; **no environment parameter** (C1) |
| `SummaryFiles.FolderBeside` / `FileNameFor` / `Save` | `Core/Resume/SummaryFiles.cs:17,25,36` | one `Summaries` folder; the file name uses the first 8 characters of the id and no provider (C17) | reused unchanged; a collision gets " (2)" (`CreateNew`) |
| `SummaryLevel` and `CodexSummaryText` wording | `Core/Resume`, `Core/Codex/CodexSummaryText.cs` | three levels | copy the wording (G1.4) |
| Claude and Codex behaviour | existing tests | unchanged | the existing tests pass without edits |

## Existing-behaviour map (file level; each copy gets a branch-level map in its step, before the copy is written)

| Codex file (Core) | Copilot action | Step |
|---|---|---|
| `Codex/CodexHistorySource.cs` | new `Copilot/CopilotHistorySource.cs`: `events.jsonl` and `workspace.yaml`, no index | G1.2 |
| `Codex/CodexTurns.cs`, `CodexMessages.cs`, `CodexTranscriptReader.cs` | new `Copilot/CopilotTranscriptReader.cs`: `user.message` and `assistant.message` | G1.3 |
| `Codex/CodexSummaryInput.cs`, `CodexSummarizer.cs`, `CodexSummaryText.cs` | new Copilot summarizer (G-D2, G-D15) | G1.4 |
| `Codex/CodexTerminalOpener.cs` | new `Copilot/CopilotTerminalOpener.cs` (G-D11, G-D12) | G1.5 |
| `Codex/CodexSetupCheck.cs` | new: on PATH; bundled version (G-D13) | G1.6 |
| `Codex/CodexStartLauncher.cs` | new: launch file, encoded PowerShell 7 command calling `copilot.ps1 -i` | G1.7 |
| `Codex/CodexResumeText.cs`, `CodexSessionTime.cs` | new: resume wording; session time | G1.8 |
| `Codex/CodexExit.cs` | **copied** as `Copilot/CopilotExit.cs` (the ended state, G-D20) | G2.8 |
| `Codex/CodexModelList.cs`, `CodexUsageReader.cs`, `CodexUsageMemo.cs` | not copied in v1 (G-D9) | — |
| `Codex/CodexHistoryQuery.cs` (folder candidates, newest in folder) | not copied (gap, parity row 19) | — |
| `App/CodexRailGroups.cs`, `CodexSearch.cs`, `CodexSearchView.cs` | copied: `CopilotRailGroups.cs`, `CopilotSearch.cs`, `CopilotSearchView.cs` | G2.2, G2.3 |
| `App/CodexTranscriptText.cs`, `CodexTranscriptView.cs` | copied: Copilot versions | G2.4 |
| `App/CodexResumePrompt.xaml(.cs)`, `CodexResumeRules.cs` | copied: `CopilotResumePrompt`, `CopilotResumeRules` | G3.1 |
| `App/CodexModelChoices.cs`, `CodexModelPrompt.xaml(.cs)` | not copied in v1 (G-D9) | — |
| `App/ProviderSupport.cs` | extended for Copilot | G2.1 |
| `App/SessionWindow.Codex.cs` (incl. `WatchCodexExit`), `.CodexSearch.cs`, `.CodexTranscript.cs`, `.CodexResume.cs` | copied: `SessionWindow.Copilot*.cs` | G2–G3 |

## Stages (each ends green on net10.0 and net11.0; commits when its tests pass; no push without approval)

**Build rules (Codex plan, S5):** `--artifacts-path` under `C:\Users\Dmitri\.claude\jobs\8260bd19\tmp\artifacts`; `-nodeReuse:false`; do not stop or touch Visual Studio's processes. Check free RAM first. Measure the test baseline at the start of G1 (the Codex counts are older than the branch).

**Scratch rule for every probe in G0:** the probe's config and log go to a scratch folder we create (`--config-dir`, `--log-dir`), and the probe's sessions are never written to the owner's `~/.copilot/session-state`. A process a probe starts is stopped by the probe, and only that process. Each probe's command and result go in `docs/research/copilot-evidence/README.md` (names and counts only, no session text).

### Stage G0: probes (in this order; no owner session is written)

- [x] G0.1 **Answered 2026-11-22:** `where copilot` lists the WinGet `copilot.exe` first, then the npm `copilot` and `copilot.cmd`. So a shell that searches PATH in that order runs the WinGet exe (bundled 1.0.34) unless the owner changes it (G-O5). (`cmd.exe /d /c where` printed only the banner; not repeated.)
- [ ] G0.2 **P-C3.** `copilot --version --no-auto-update` (answered for the bundled version: 1.0.34 WinGet, 1.0.51 npm; 2026-11-22: the top level of `~/.copilot` did not change; the package cache was not checked). Still open: does it start a download? Compare the package cache folder before and after. *Feeds G-D13.*
- [x] G0.3 **P-C4 answered 2026-11-22 (parse only):** `--available-tools=view` is accepted (`--version` with it exits 0). `none`, an empty value and `[]` were also accepted by the parser, which does not prove their meaning, so only `view` is used. `view` is the read tool (the agent definitions list it). G0.6 called only `view`.
- [ ] G0.4 **P-C1 (deferred: needs a pseudo-terminal harness, which Git Bash does not provide; a coder runs it in a stage G0b; G2.9 stays a bonus until then).** Start `copilot --session-id=<new uuid> --config-dir <scratch> --log-dir <scratch> --sync-session=off` in a pseudo-terminal, no prompt, about five seconds; then stop the process we started. Check: a folder with that id appears **in the scratch config only**, what `session.start` carries (key names), and the scratch is then deleted. *Why:* the review (C3) found that a bare start leaves a folder in the real home, so this probe never runs without the scratch config. *Decides G-D6 and G2.9.*
- [x] G0.5 **P-C11 answered 2026-11-22 (no model call; scratch config):** `--resume=<unknown id>` with `-p` exits 1 and prints `No session, task, or name matched '<id>'`, then the options for resuming by id, by name, or with the picker, and `copilot --name=<name>` to name a new session. No session folder was written. The app's refusal (G-D12) matches this.
- [x] G0.6 **P-C2 answered 2026-11-22 (one model call; G-O4), file route:** from a neutral scratch folder holding `transcript.txt` (one word): `copilot -p "<short prompt naming the file>" --config-dir <scratch> --log-dir <scratch> --sync-session=off --output-format text --silent --no-ask-user --no-custom-instructions --disable-builtin-mcps --available-tools=view`. Result: exit 0; reply `hello`; the only tool called was `view`; sign-in worked from the scratch config; the owner's `session-state` 52 before and 52 after; 1 premium request on `mai-code-2-flash`; the scratch folder (with its marker) removed. The earlier stdin attempt most likely made no call (inferred). The step's original checks, kept for the record: from a neutral scratch folder: `copilot -p` with the prompt on stdin, `--config-dir <scratch>`, `--log-dir <scratch>`, `--sync-session=off`, `--silent`, `--no-ask-user`, `--no-custom-instructions`, `--disable-builtin-mcps`, `--output-format text`, the no-tools value from G0.3, and the default model. Check: exit code; stdout length and shape (names and lengths only); sign-in works from the scratch config (if it does not, the summary route is off in v1 and G-D2's isolation changes); the owner's real `session-state` gains no folder; read `totalPremiumRequests` and `currentModel` from the scratch session's `session.shutdown` (numbers and names only); then delete the scratch folder. *Blocked on G-O4 (default: go once).*
- [x] G0.7 **P-C10 answered 2026-11-22 (counts only):** all 47 `user.message` records have `transformedContent`, and its length differs from `content` in 47 of 47. What it holds was not read. `content` stays the field (G-D17); a text check of whether `transformedContent` contains `content` is still open.
- [x] G0.8 **P-C12 answered 2026-11-22 (counts only, 52 `workspace.yaml` files):** 47 have a non-empty `name`; **0** have `user_named` true, so `name` is generated, not set by the user; 5 folders have no `events.jsonl`. The title rule (G-D16) stands.
- [ ] G0.9 **P-C8 (deferred to the G1.7 tests: a fake shim, no model call).** Multi-line text through `pwsh -EncodedCommand` to a fake `copilot.ps1` that records its arguments (no model call). The argument must equal the text (new lines, quotes, `%`, `&`, `^`, `!`, a Unicode letter), and the launch file must be gone afterwards. *Decides G-D14's test.*
- [x] G0.10 **P-C5 answered 2026-11-22:** `session.title_changed` in 0 of 47 event folders. Titles use `workspace.yaml` `name` then the first user message (G-D16).
- [x] G0.11 **P-C7 answered 2026-11-22:** `config.json` has no sync, share, telemetry, session, hook or model key (names only). The `--sync-session` default is still not stated (G-D8 keeps `off`).
- [x] G0.12 **Multi-turn check (C10):** all 47 event files are single-turn (one `user.message`, at most one `assistant.turn_end`, a routine `session.shutdown`). Multi-turn behaviour is **unverified** (G-D5).
- [ ] G0.13 Record every result in the research note and in this plan; if a probe changed a decision, say which.

### Stage G1: Core, read-only (no UI)

- [ ] G1.0 Branch-level map for each Codex Core file the steps copy (one row per code branch, from the file, never from memory), in `docs/plans/provider-copilot-2026-11-22-map.md`.
- [ ] G1.1 `ProviderId.Copilot`; `ProviderChoice` for `copilot` (the fallback must not map it to Claude); `ProviderPaths.ForCopilot`; `SessionKey` for `copilot:`. *Tests:* parse and text round-trip; Claude and Codex unchanged; `ForCopilot` with and without `COPILOT_HOME`.
- [ ] G1.2a `Copilot/CopilotWorkspaceFile.cs`: a flat `key: value` reader for `workspace.yaml` (quotes stripped; unknown keys ignored; a line it does not understand makes the file unreadable, counted, not fatal). *Tests:* synthetic files; quoted values; a nested line; an empty file.
- [ ] G1.2 `Copilot/CopilotHistorySource.cs`: for each folder under `session-state`: `workspace.yaml` (G1.2a) for `name`, `cwd`, `updated_at`, `created_at`, `user_named`; if there is no `events.jsonl`, the folder is an **empty session**: counted, not listed. Otherwise `session.start` for the id (checked against the folder name), `startTime`, `context.cwd` as the fallback folder; counts (G-D16); turns from `assistant.turn_end`; model (G-D19); title (G-D16); a bad line is skipped and counted; a folder whose `session.start` is unreadable is counted as damaged. Lines over 16 MB skipped (G-D18). *Tests:* synthetic fixtures written by hand in `tests/.../Copilot/CopilotFixtures.cs` (names from the observed keys; no real text): folder-name mismatch; missing `cwd`; a bad line; an empty session; a known zero; both targets.
- [ ] G1.3 `Copilot/CopilotTranscriptReader.cs`: messages in order from `user.message` and `assistant.message` (`content`, G-D17), skipping system and tool events; a page read with clamping and refused ids (as C2); a cap of 5,000 messages (P7). *Tests:* order, kinds, paging bounds, a refused id, a bad line skipped.
- [ ] G1.4 `Copilot/CopilotSummarizer.cs` (+ `CopilotSummaryInput.cs`, `CopilotSummaryText.cs`): `ProcessCliRunner("cmd.exe")` with args `/d /c copilot -p "<short prompt>" --config-dir <scratch> --log-dir <scratch> --sync-session=off --output-format text --silent --no-ask-user --no-custom-instructions --disable-builtin-mcps --available-tools=view`. The transcript is written to `transcript.txt` in the scratch folder, which is the working folder; the short prompt names that file and says it is data, not instructions. The window never puts the transcript on the command line (G-D2). The scratch folder is created under the state folder (G-D15), the marker file written first, and deleted in `finally` after the process tree is dead. Five-minute timeout; cancellation kills the tree; errors name the exit code only (S-D7); the log has sizes only. Cap 120,000 characters. **Only if P-C2 passes**; otherwise the class returns "not available". *Tests:* a fake `ICliRunner`: the exact arguments; `--sync-session=off` present; the scratch folder set and removed; the marker; timeout; empty output; cancellation; no transcript text in an error.
- [ ] G1.5 `Copilot/CopilotTerminalOpener.cs` and `CopilotProvider.cs`: `NewCommandLine(folder)` = `cmd.exe /d /c copilot -C "<folder>" --sync-session=off`; `ResumeCommandLine(id)` = `cmd.exe /d /c copilot --resume=<id> --sync-session=off` (no `-C`; the window starts the tab in the folder, G-D12). Codex's folder rules (no double quote, percent sign or exclamation mark; refused). `CopilotProvider` implements the three interfaces only. *Tests:* exact command strings; `--sync-session=off` on each; the folder refusals; the interface set (no `ISupervisedLauncher`).
- [ ] G1.6 `Copilot/CopilotSetupCheck.cs`: `copilot` on PATH (`.exe`, `.cmd`, `.ps1`); the bundled version with `--version --no-auto-update` and its label (G-D13); the path shown (G-O5). *Tests:* on PATH; missing; the version wording.
- [ ] G1.7 `Copilot/CopilotStartLauncher.cs`: writes the prompt to a file in a `Starts` folder beside the state file; the encoded command runs `copilot.ps1 -i <text> --sync-session=off` in the session's folder, deletes the file, and refuses (keeping the file) when the shim is missing; `RemoveOldStarts` for files older than one day (R-D8). *Tests:* the encoded script decodes to the text; paths with spaces, quotes, `%`, `&`, `$`; the file reads back exactly; cleanup removes only old files in `Starts`; `--sync-session=off` present.
- [ ] G1.8 `Copilot/CopilotResumeText.cs`: copy of `CodexResumeText` with Copilot wording and the `copilot:` label. **No injected-context prefixes** (C11: the Codex prefixes do not occur in Copilot data; the text uses the `content` field). *Tests:* as R2, without the prefix cases.
- [ ] G1.9 Build and test: Core.Tests on net10.0 and net11.0; counts against the baseline; exit codes. No existing test changed.
- [ ] G1.10 Commit G1 (code with passing tests). Push: no (G4.3).

### Stage G2: App, rail and tabs

- [ ] G2.1 `ProviderStore`: a third choice saved in `ui.json` as `"provider": "copilot"`, on both targets. *Tests:* round-trip; an unknown value falls back to Claude.
- [ ] G2.2 `App/CopilotRailGroups.cs` and the rail: history by folder (copy of `CodexRailGroups`); the empty state says why and gives the path (`~/.copilot/session-state`, or `COPILOT_HOME`) and the count of empty sessions; "New Copilot session…". *Tests:* grouping and sort (subagents last inside a folder).
- [ ] G2.3 Library marks under `copilot:<id>`: tags, notes, archive, pin, colour, rename; search (`CopilotSearch.Matches`: titles, names, tags, notes; no minimum word length; a query of only `#` matches every tagged session). *Tests:* copies of the Codex search and library-view tests on Copilot keys.
- [ ] G2.4 `App/CopilotTranscriptText.cs`, `CopilotTranscriptView.cs`, `TabKind.CopilotTranscript`: read-only, messages only, scope line, Copy, time; no find (as Codex); cap 5,000. *Tests:* text; the cap message.
- [ ] G2.5 Row menu (`SessionWindow.Copilot.cs`): View transcript; Resume… (G3.1); Resume in a terminal tab; Pin; Tag; Rename; Archive; Colour. Same items and order as Codex.
- [ ] G2.6 Palette (P1): Claude-only entries built only when `ProviderFeatures.Has<ISupervisedLauncher>` (false for Copilot).
- [ ] G2.7 Tabs: keys from the tab's own provider (`KeyOf(OpenTab)`, C-D8); the window owns the tab; not restored after restart (as Codex).
- [ ] G2.8 `Copilot/CopilotExit.cs` (copy of `CodexExit`) and the ended state (G-D20): "(ended)" and "Ended; no exit code was reported." *Tests:* the classification copied from the Codex tests.
- [ ] G2.9 **Bonus, only if P-C1 passes:** a new Copilot tab starts with `--session-id=<new uuid>` and records the id; resuming an id with an open tab focuses that tab; an ended tab is not reused. *Tests:* the id at launch; the focus rule; the ended rule.
- [ ] G2.10 Build and test (App.Tests on both targets; counts; exit codes).
- [ ] G2.11 Commit G2 (with passing tests).

### Stage G3: resume screen and summaries

- [ ] G3.1 `App/CopilotResumePrompt.xaml(.cs)` and `CopilotResumeRules.cs` (copy of the Codex resume screen): modes Full, From the last turns (default; no call), From a Copilot summary (one call per press; levels; preview; Copy; kept summary; cancel closes and cancels the call). Cost line: "One Copilot call. Up to 120,000 characters of this session are sent." A change of mode or turns keeps the summary for the same level; a change of level drops it. Summary mode is hidden when P-C2 failed. *Tests:* copies of the Codex dialog tests (count of summaries, level change, cancel).
- [ ] G3.2 `App/SessionWindow.CopilotResume.cs`: the start text (summary through G1.4 when a level is chosen and no kept summary matches; the tail through G1.8); save through `SummaryFiles.Save` beside the state file; the start through the launch file (G1.7); `OpenCopilotTab(...)` for the tab, starting in the session's folder. **Refused with a status line** when the id is not in the app's list or the folder is missing (G-D12). One run at a time (`_resuming`). *Tests:* the refusals; pure helpers.
- [ ] G3.3 Screen check, scratch state: a script like `scripts/Test-CodexResume.ps1`, with a fake `copilot.ps1` and a fake summary source (no model call); screenshots, light and dark, for full, last-turns and summary modes. *Checked by the orchestrator by reading the PNGs.*
- [ ] G3.4 Build and test; commit G3 (with passing tests).

### Stage G4: review, build, commit

- [ ] G4.1 Sonnet review of G1 and G3 (contracts, privacy, the scratch isolation, the launch quoting). Findings go in a review-notes file for that stage, graded as CLAUDE.md step 4b says.
- [ ] G4.2 Fix the must-fix findings; rerun tests; record counts.
- [ ] G4.3 **Commits and pushes.** Commit each stage with passing tests. **Do not push** without the owner's approval: the branch has 18 unpushed commits and 8 docs-only commits from the Codex work, and a push sends all of them. Record it as an owner item.
- [ ] G4.4 Final report: what was built, test counts and exit codes, probe results, the owner decisions still open (G-O1 to G-O5 and Codex O1 to O7), and the screen checks nobody could run here (including the owner's real multi-turn session and a resume of a session that has a `vscode.metadata.json`, C19).

### Stage G5: optional, each its own owner decision

- [ ] G5.1 Model list through `copilot --acp` (Codex P9 analog); a model dropdown. Needed to answer G-O3 with a choice.
- [ ] G5.2 Usage from the files: `assistant.usage` events and `session.shutdown.modelMetrics` (Codex SD1 and SD2 analog).
- [ ] G5.3 Finished state for a tab whose id is known (G2.9), **only after a multi-turn capture** (G-D5): the tab shows "done" when `assistant.turn_end` appears; no timing guess.
- [ ] G5.4 Hooks (Codex O5 analog): capture real payloads for `permissionRequest`, `agentStop`, `sessionStart` in a scratch folder first; the names differ across the examples (camelCase in the docs and one example, PascalCase in another), so they are settled by capture. Installing a hook is the owner's decision. Only pre-tool hooks can block a call (agent-governance-toolkit, checked in code).
- [ ] G5.5 Effort dropdown (`--reasoning-effort`; P-C6 is run here, not in G0).
- [ ] G5.6 Optional: name new Copilot sessions with `--name=<name>` (shown by the CLI's own resume error text, P-C11), so a new tab's session has a readable name.

## Risks

- **R-C1 Format drift.** `events.jsonl` and `workspace.yaml` are written by the CLI and may change. Fixtures use the observed key names; unknown lines and folders are skipped and counted; the status line says "Copilot history may be incomplete" when a skip happens.
- **R-C2 The summary route (P-C2).** Verified 2026-11-22 (G0.6): sign-in works from the scratch `--config-dir`. If a later CLI version breaks that, summaries are off in v1. A crash can leave a scratch folder; startup removes only marked folders older than a day (G-D15).
- **R-C3 Privacy.** Session files hold private text. The Codex R3 rules apply: no transcript text in logs, errors or fixtures; the redactor and diagnostics allowlist unchanged; `--sync-session=off` on every command. The `-i` prompt (G-D14) and the terminal's command line show session text in the process list while the process runs; the summary prompt is on stdin, so it is not on the command line. The scratch folder holds a copy of the transcript while a call runs (G-D15).
- **R-C4 Licence (G-O1).** The reading of the CLI licence is not a lawyer's. G1 proceeds; release waits for the owner.
- **R-C5 Three installs, three versions** (G-O5). Fixtures and parsers are written against the **file format**, never a screen. The bundled version is shown in the setup check.
- **R-C6 Scope and wording.** The product name and README say Claude Code supervision; unchanged here (Codex design-notes question 1).
- **R-C7 Unknown resume behaviour.** `--resume=<id>` on a session that has `vscode.metadata.json` (all 47 local folders have one, C19) and what the CLI prints for an unknown id (P-C11) are unverified. The app refuses unknown ids itself, so the CLI's answer is not relied on.

- **R-C8 Deprecated flag.** `--config-dir` prints "deprecated; use the COPILOT_HOME environment variable" on stderr (seen 2026-11-22). The route is kept because it was verified. Moving to `COPILOT_HOME` (set inside the `cmd.exe` line) needs its own verified call first. The stderr text is not shown to the user.

## Review disposition (Sonnet review, 2026-11-22, C1 to C22)

| Finding | Where fixed |
|---|---|
| C1 no environment parameter | G-D2 (`--config-dir`, `--log-dir`); Contracts row for `ICliRunner` |
| C2 resume has no folder; no `cmd.exe` wrapper | G-D11, G-D12; G1.5 |
| C3 P-C1 writes into the real home | G0 scratch rule; G0.4 |
| C4 summary flags | G-D2; G1.4 |
| C5 summary runs in the session's folder | G-D2 (neutral scratch folder); G0.6 checks sign-in |
| C6 scratch copy of the transcript; no naming rule | G-D15; G1.4; R-C3 |
| C7 `--sync-session=off` missing from launcher and probe | G-D8; G1.5, G1.7; tests |
| C8 `gitRoot`/`branch` not in `SessionSummary` | G-D19 (Copilot-only record; shared record unchanged) |
| C9 `workspace.yaml` ignored; 5 empty folders | G-D3, G-D16; G1.2a, G1.2; G0.8 |
| C10 finished state from single-turn files | G-D5; G0.12; G5.3 waits |
| C11 Codex prefixes copied; `content` choice | G-D17; G1.8; G0.7 |
| C12 P-C4 after P-C2; which copy is on PATH | G0.3 before G0.6; G0.1 |
| C13 cost of P-C2 | G0.6 (reads premium count and model; default model read back, then deleted) |
| C14 `CodexExit` contradiction | G-D20; G2.8; map row |
| C15 Codex resume ladder not listed | parity row 19; G-D12; G0.5; G3.2 |
| C16 read cap and cache | G-D18 |
| C17 summary file name | Contracts row (8 characters, no provider); accepted |
| C18 "null when none" vs known zero | G-D16 |
| C19 `vscode.metadata.json` | R-C7; G4.4 owner check |
| C20 net10 dropdown wording | parity row 1 |
| C21 prompt on the command line | G-D14; R-C3 |
| C22 decision ownership | G-O2 is a plain default; G-O3 and G-O4 cost-only |
