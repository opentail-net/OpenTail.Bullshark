# Plan: setup, sessions rail and history

Status: planned and reviewed against the code (2026-11-15, section 9). Nothing in this plan is built yet, except where a checkbox says so. Owner decisions are marked **D**; ideas that are still assumptions are marked **assumed**.

## 1. Goal

One window that shows how sessions are set up (HOW) and what they did (WHAT), without two separate worlds.

- A **setup page** where the owner picks a pattern, models and effort per role, the brief per role (saved per pattern), and the folder.
- A **sessions rail** on the left. It lists running sessions and collapsible history, grouped by folder.
- A **pane** on the right. It shows a live terminal for a running session, a read-only transcript for a past one, or the setup page.

## 2. What is invented here, and how sure we are

| Idea | Status | Basis |
|---|---|---|
| Rail on the left, pane on the right | assumed | common layout; to be judged on screen |
| Patterns as data: plan > code > review, plan > many coders > review, plan / code | **D** (owner) | owner's own list; extensible. The word "review" is open question 7 |
| Models and effort chosen per role | verified options | `claude --help`: aliases `opus`, `sonnet`, `fable`, `haiku`; levels `low` to `max` |
| Prompts (briefs) saved per pattern | **D** (owner) | owner's choice |
| Folder as an overtypable dropdown of trusted folders | **D** (owner) | trust record in `~/.claude.json` (13 trusted, 11 git repositories) |
| Rail groups sessions under their folder; folder name shown once per group | **D** (owner) | owner's choice |
| Warm and cold labels on sessions, cut-off one hour (with five-minute lifetimes read from the data, D-S27) | measured (F49) | 1-hour cache writes (180M tokens vs 5M for 5 minutes). The earlier "77%" figure had no recorded method and is replaced: under one hour, 0.075% of 25,300 pairs rewrote the context; after an hour, 81% of 47 pairs |
| A persisted run: pattern, folder, full spec list | assumed (D-S12) | `SessionRecord` holds none of it (SessionRecord.cs:7-15), so a restarted window cannot rebuild the run |
| Several coders with unique names, and a name scheme | **D** (owner, 2026-11-15) | D-S13; F33 (duplicate names) |
| Decision agent suggests the setup from a goal | later | `docs/later/dynamic-configuration.md`; never launches without the owner confirming |
| Summary for resume | built 2026-11-15 | Haiku at low effort for the summaries, one call each; the owner may change the model (D-S11) |

## 3. Contracts this must satisfy

Existing code the plan touches. Line references were read on 2026-11-15 (section 9). Each entry says what callers rely on, so changes keep them working.

- **`SessionSpec`** (Core/Supervisor): `Role, Name, WorkingDirectory, Model, Effort, Brief, PermissionMode, ResumePrompt`. The setup page produces these. The supervisor and the window consume them. A pattern becomes a list of these.
  - **Peers.** Today `PlannerBrief` names `CoderName` directly (SessionSpec.cs:42-46), `CoderBrief()` takes no peer list (SessionSpec.cs:49-53), and the default `ResumePrompt` says "the other session" (SessionSpec.cs:22). With N coders, each brief and each resume prompt takes its peer names as an argument. Solo has no peer, so it gets its own brief and resume prompt that name none. **Contract change.**
  - **One permission mode per run.** A message between sessions in different modes is held for the owner's approval (F22; the comment at SessionSpec.cs:30-32). Every session in a run uses the same mode (today `auto`). S4 and S5 must enforce this.
- **`SessionRecord` and `StateStore`** (Core/Supervisor):
  - `SessionRecord` holds `Role, SessionId, Name, StartedAt, Restarts, RestartTimes, LastState, LastAlert` (SessionRecord.cs:7-15). It has no folder, model, effort or brief. The run store (below) holds those.
  - `StateStore.Load` builds a dictionary keyed by `Role` (SessionRecord.cs:40). Two coder records would make it throw `ArgumentException`. The key must become `Name`.
  - `StateStore.Save` sorts by `Role` (SessionRecord.cs:48). That changes with the key.
  - The file is already a JSON list with `Name` in every record. **No file rewrite is needed.** The change is in code, and the S1 test uses the current file's shape.
- **`SupervisorPolicy.Decide`** (Core). It matches the record to the agent by `SessionId` (SupervisorPolicy.cs:56), not by name. Branches, verified 2026-11-15:
  - no record: Start (SupervisorPolicy.cs:53-54);
  - agent missing: a RestartLoop alert if there were 3 or more restarts in the last hour, otherwise Resume (SupervisorPolicy.cs:59-66);
  - agent `blocked`: one Blocked alert, unless `LastState` was already `blocked` (SupervisorPolicy.cs:71-77);
  - otherwise: nothing (SupervisorPolicy.cs:80).
  Keep every branch for every session in a run.
- **`SupervisorRunner.CheckOnceAsync`** (Core). It keys records by `spec.Role` (SupervisorRunner.cs:40, 46, 63, 76-77, 86, 94-97). With several coders, each coder's record overwrites the others. **Contract change:** key by `Name`. Start calls `StartBackgroundAsync` with `--name spec.Name` (ClaudeCli.cs:51), then `ResolveSessionIdAsync`. Resume passes `ResumeSessionId` and the spec's `ResumePrompt`.
- **`ClaudeCli`** (Core/Claude):
  - `ResolveSessionIdAsync` matches on the short id (`a.Id == shortId`, ClaudeCli.cs:73), not on the name. Duplicate names do not make the supervisor read back the wrong session.
  - Names still matter for `attach`, `logs` and `stop` when given a name (F14), and for peer messages sent by name (F22, F33). So names must be unique within a workspace.
  - `stop` and `rm` take the 8-character id (F14). `ShortIdOf` is the first 8 characters of the session id (ClaudeCli.cs:85-86).
  - `ListAgentsAsync` (the `state` and `status` fields), `StartBackgroundAsync`, `LogsAsync`, `StopAsync`, `RemoveAsync` as before.
- **`TrustCheck`** (Core/Trust): `Check(folder, claudeJsonPath)` (TrustCheck.cs:53) returns a `TrustResult` of Trusted, NotTrusted (with the repository root) or NotARepository (TrustCheck.cs:17). `OpenClaudeForTrust` only opens Claude. OpenTail.Bullshark never writes the trust record (F28).
- **`TranscriptIndex`, `SessionDigest`, `TranscriptReader`, `UsageLedger`, `TranscriptPager`** (Core/Activity):
  - `SessionDigest.ProjectFolder` is the encoded folder name, `Path.GetFileName(directory)` (TranscriptIndex.cs:67). There is no working-directory field. The rail's grouping (D-S3) needs a key that live sessions and history share. Decoding the encoded name is not safe, because folder names can contain hyphens. The rule is verified on real folders (F44): `FolderKey` (Core/Folders) applies it to a working directory. `FolderKey` only groups a run's folder with the history groups. It does not find a transcript: `TranscriptReader.FindTranscript` searches by session id. A session that moved into a worktree shows as its own group (F44).
  - The index is built when the owner presses "Show history" (open question 8, closed; `SessionWindow.Rail.cs`). Nothing is read before that.
  - Counting rule: once per `message.id:requestId`, largest value kept (F34 to F36).
- **`SessionWindow`** (App):
  - `Specs` is hardcoded to the two default specs (SessionWindow.xaml.cs:49-53). S5 replaces it with the active run.
  - One `ExtendedTerminalControl` per session, keyed by `SessionId` (`EnsureTerminal`). All terminals sit in one cell of the pane. Only the chosen one is Visible; the others are Hidden, never Collapsed (F45). Closing the window detaches all of them (`DisconnectConPTYTerm`).
  - The Trust tab appears when a folder is untrusted (F31). Paste is handled in the window (SessionWindow.xaml.cs:187-208).
  - History moved to the rail in S3. The transcript view is `TranscriptView`, which keeps the History tab's rendering unchanged.
- **Run store (new, D-S12):** `%LOCALAPPDATA%\OpenTail.Bullshark\runs.json`. It holds the active run: the pattern name, the folder, and the full `SessionSpec` list. The window reads it at start. A restarted window gets the same specs. Recent folders (D-S2) come from here, not from `state.json`.
- **`NudgePolicy`** (Core/Nudge): allowed only when the owner has not typed in the tab for 10 minutes, and the state is `done` or `busy` (NudgePolicy.cs:24-32). Never `blocked` or `working`.
- **`PlanFile.HandoffMessage`** (Core/Plan): the batch travels in the message. No file messaging between sessions (owner decision).
- **Rendering:** `RenderOptions.ProcessRenderMode = SoftwareOnly` must stay on (F38). Without it the WPF chrome paints blank on this machine.

## 4. Existing behaviour mapped, branch by branch

For the rail's state labels, every state the supervisor or ConPTY can show needs a row:

| State (source) | Meaning | Rail label | Notes |
|---|---|---|---|
| `working` (agents) | mid-turn | running, busy | |
| `done` (agents) | idle, finished its turn | running, idle | the session is alive |
| `busy` (agents, interactive) | interactive, in use | running | |
| `blocked` (agents) | waiting for input: a permission answer, a question, idle with no prompt, or waiting for a peer (F19, F21, F29) | blocked, with reason unknown | the reason is only known by opening it |
| not listed (agents) | gone | past (history) or lost | the supervisor resumes it |
| trust NotTrusted | repository not trusted | needs trust | the Trust tab opens |
| warm / cold (transcript timing) | cache state (see 2) | label on the row | derived, not from agents |

## 5. Decisions

- **D-S1 Layout.** Rail on the left, pane on the right. Confirm on screen (checkbox S7).
- **D-S2 Folder.** An overtypable combo: the trusted repositories first, most recently used first (from `runs.json`, D-S12), then Browse. Typing a path validates it (exists; repository root; trust state shown). An untrusted choice opens the Trust tab.
- **D-S3 Grouping.** The rail groups sessions under their folder. The folder name appears once per group, with the full path as a tooltip. Groups collapse. Live and history sessions share one folder key (S2).
- **D-S4 Patterns.** A pattern is data: how many coders, and the brief for each role. Roles are planner and coder only (`SessionRole`). Patterns differ by the number of coders: plan > code > review is one planner and one coder; plan > many coders > review is one planner and several coders. The third pattern is **solo**: one planner-role session that plans and implements. Its brief asks for both. The plan does not promise how it will plan before it works, and the setup page should not either. The word "review" is open question 7.
- **D-S5 Models and effort.** Chosen per role, from fixed lists taken from the CLI. Defaults: planner `opus`/`high`, coder `haiku`/`medium`. Fable and Sonnet are offered. Fable is marked as the most expensive.
- **D-S6 Prompts.** Saved per pattern, in `%LOCALAPPDATA%\OpenTail.Bullshark\patterns.json`. An edit applies to the next launch only. The page says that a running session keeps its brief.
- **D-S7 Warm and cold.** Warm under 50 minutes since the last request; cooling from 50 to 60; cold after 60. The row shows what a message would cost now, using the context size at the last turn. Thresholds are settings, and the index re-measures them.
- **D-S8 Start.** The window opens on the active run if there is one, otherwise on the setup page. This is the owner's "setup before any other tab" rule. The active run comes from `runs.json` (D-S12).
- **D-S10 Review is the planner's job (owner, 2026-11-15).** There is no separate reviewer. The planner reviews every coder report, and the setup page has no reviewer row. This settles open questions 1 and 2.
- **D-S11 Summary size and tail (owner, 2026-11-15).** A resume summary has four levels: minimal, normal, complete, and trimmed. Trimmed is the chosen level plus the last N turns (default 10, selectable), copied word for word. Tool results in the tail are collapsed to one line each. The tail is capped at about 4,000 tokens: when it is over, the oldest turn is dropped first, and the latest message is never cut.
- **D-S12 Run store (planner's decision 2026-11-15, built in S1).** Every run from setup is written to `runs.json` beside `state.json`: pattern, folder, permission mode, and the full spec list. The window opens on the newest run (D-S8). The supervisor keeps the sessions of every run alive. `state.json` stays the supervisor's record per session. Reason: `SessionRecord` holds none of the spec, so a restart cannot rebuild the run without it.
- **D-S13 Names (owner confirmed, 2026-11-15).** The one-coder default keeps the names `ovc-planner` and `ovc-coder`. Records keyed by name therefore still match after the key change. Each later run adds a suffix from its run id, for example `ovc-coder-2f1a`. With several coders the names are `ovc-coder-2f1a-1`, `-2`, and so on. The suffix stops F33 duplicates. Today records are keyed by `Role`, so a rename orphans nothing; the risk starts with the key change in S1.
- **D-S14 One permission mode per run.** Every session in a run uses the same mode, currently `auto` (F22).
- **D-S15 Peers named in briefs.** Each brief and resume prompt names its peers. Solo names none.
- **D-S16 Coders name the planner only (planner's decision, built in S1).** A coder reports to the planner, so it needs no other coder's name. The planner names all of its coders.
- **D-S17 Legacy run (built in S1).** When `runs.json` does not exist, the existing pair is written to it as the run `legacy` (plan > code, `auto`, the launch folder). Those sessions stay supervised, so the first run from setup does not drop them. Risk: section 8.
- **D-S18 Solo model (planner's decision, built in S1).** Solo is a planner-role session, so it takes the planner's model and effort (`opus`, `high`, D-S5). The owner may change this on the setup page.
- **D-S13 clarified.** Every run from setup gets a suffix, including one-coder runs. Only the legacy pair keeps the plain names.
- **D-S19 Coder count (planner's decision, built in S4).** plan > many coders takes 2 to 6 coders. The cap is 6, a first guess; the owner can change it.
- **D-S20 Files beside the state file (built in S4).** `patterns.json` and `runs.json` sit in the folder of the state file (D-S12, D-S6). A scratch `--state` therefore isolates every write, and a test run cannot touch the owner's files.
- **D-S21 Owner's extra text (planner's decision, built in S4).** The owner's text is added after the base brief, under "From the owner:". The base brief stays: the peer names (D-S15), the handoff by message, and "do not ask questions" (F21). The preview shows the full brief and the full resume prompt of every session.
- **D-S22 Start saves, and launches nothing (built in S4).** Start checks the run and saves the pattern's settings for the next launch. A running session keeps the brief it started with (D-S6). Launch and the run file came in S5 (D-S23 to D-S26).
- **D-S23 Legacy run (built in S5).** It is created only when `runs.json` is missing and the state file holds the default pair. Its folder is the planner's `cwd` from `agents --json`, else the coder's, else the launch folder. Migration runs once, at the first pass.
- **D-S24 Untrusted folders (built in S5).** Each untrusted repository root gets its own Trust step and rail item. Its runs wait, and every other run goes on (`TrustGate`). Start on an untrusted folder opens that root's Trust step and writes no run. The owner presses Start again after trust.
- **D-S25 Failed starts (built in S5).** A failed start is reported as an alert and recorded with no session id, so it is not retried on every pass. A failed resume counts as a restart, so three failures in an hour raise the restart-loop alert. The owner retries with "Retry failed starts" in the bottom bar.
- **D-S26 No run, no start (built in S5).** With no run in `runs.json`, the supervisor starts nothing and logs once. The window opens on the setup page.
- **D-S27 Lifetime from the data (built in S6).** A session's lifetime is read from its last cache write (ephemeral 1 hour or 5 minutes), or from the nearest earlier write. With no write seen, the one-hour limits apply and the label says so. The five-minute limits are 4 and 5 minutes, the same 10-minute margin as the one-hour ones (D-S7). F49 gives the evidence.
- **D-S28 One definition of "last request" (built in S6).** It is the last assistant record with usage, for running and past sessions alike. History rows read their transcript tails once when the history loads. The digest's `LastActivity` is not used for labels, because it counts every record.
- **D-S29 Tokens only (built in S6).** The labels say "reads about N cached tokens" or "rewrites about N tokens". No money figure is shown (F17).
- **D-S30 Rail theme (owner, 2026-11-15, S7 review).** Two modes: dark (near black background) and light (very light grey with a touch of blue). The owner switches with the mark at the top of the rail. Every rail colour comes from one palette, so text is never black on dark grey. The choice is saved in `ui.json` beside the state file.
- **D-S31 History section (owner, 2026-11-15, S7 review).** One section, labelled "History", at the bottom of the rail, with the 🔄 mark beside the label (no button). Past sessions are grouped by folder under it. The live folders stay above. The owner asked for the label at the top of the section; a move to the top of the rail is a one-line change if the owner wants it.
- **D-S32 Rail width (owner, 2026-11-15, S7 review).** The rail's width is proportional, so the splitter sets it and the window resizes it. Names too long for the rail are cut off, with the full text in the tooltip. There is no sideways scrollbar. The owner accepts cut-off text.
- **D-S33 Temporary folders kept out of the way (owner, 2026-11-15).** The history folders under the Windows temp folder (test and scratch runs) form one group, "Temporary folders · N sessions", collapsed at first. Live sessions are not moved: they stay in their folders. Folders outside the temp folder stay visible, including the scratch folder under Documents; whether that one joins the group is the owner's call.
- **D-S34 Rail filter (owner, 2026-11-15; replaces the collapsed group of D-S33).** A text mark at the top of the rail, "show: …", cycles through four views: all (live folders and history, without the temporary folders; the default), running (live folders only), history (history only, without the temporary folders), and temporary (only the temporary folders). The owner does not want temporary folders shown by default. The filter is not kept between launches. Tested: the all view and the temporary view by screenshot. Not tested: clicking the mark, because the workstation was locked during the test and the lock screen took the clicks; the mark uses the same click path as the theme mark, which is also not tested by click.
- **D-S35 No advisor; the coder may ask the planner (owner, 2026-11-15).** The advisor tool is not used: each consult re-reads the whole session at the advisor's rate, uncached (the docs), so it costs most in long sessions. Instead the coder may send its planner one short question by message and keep working on what it can. It does not stop to wait: a session that waits for a peer shows as blocked (F29), and the supervisor alerts on that. The planner answers in one short message and says what it decided. Built in the briefs (`SessionSpec`, `RunPlan`); the test for the wording passes. Not tested live: whether a coder asks at the right moment, and how often the blocked alert then fires.
- **D-S36 Reviewer setting (planner's note, owner, 2026-11-15; not built yet).** The reviewer is a per-run setting shown as a child row under the planner, with the same model choices as the others, plus "self". "Self" means the planner reviews in its own session, with no subagent. It is the baseline, and it costs the most: the planner's context grows with every review. The setup page marks it with an asterisk and a footnote saying that. The choice only adds one sentence to the planner's brief. The plan is the reviewer's reference: each batch carries its acceptance criteria.
- **D-S37 Trivial work (proposed, owner, 2026-11-15; not built).** Trivial: a one-file edit of at most 20 lines that no test covers and no contract changes, checked by one build or test; or one simple command that changes nothing outside the repo. Otherwise it goes to the coder. The planner names the case and the check it ran.
- **D-S39 Tabs across the top (owner, 2026-11-15; built).** The rail stays. A tab strip across the top, as in Windows Terminal, shows what is open: one tab per live session, one setup tab, and one transcript tab. "+" opens the setup, and there is one setup tab. Clicking a tab selects its rail item, and clicking a rail item opens or shows its tab, so the two always agree. Closing a tab only hides it: a live session keeps running, and the rail still lists it. Live sessions get a tab as they appear, so you can see what is running. The setup is never buried: "+" is always there.
- **D-S38 Reviewer default (owner, 2026-11-15).** Sonnet is the default reviewer; self is for a one-batch task. Live result, one batch, Haiku planner: self used fewer tokens than a Sonnet subagent (output 4,110 against 7,024; cache read 718,896 against 910,678), and the planner context was about the same (53,050 against 55,491). The subagent should pay off only when the planner's context grows large, which is the owner's typical profile.

- **D-S40 Rename to OpenTail.Bullshark (owner, 2026-11-15; built in a copy, not committed).** The product, projects, namespaces, window title, executable and env gates (OPENTAIL_BULLSHARK_*) use the new name. New runs are named bullshark-planner-<id> and bullshark-coder-<id>; this replaces the ovc- prefix in D-S13. The legacy pair keeps ovc-planner and ovc-coder, because state.json is keyed by them. The data folder moves to %LOCALAPPDATA%\OpenTail.Bullshark by a one-time copy on the first default launch, only when the new state.json is missing; the old folder stays. A run given --state never copies data.

## 6. Open questions (owner)

1. Closed (owner, 2026-11-15): the folder is chosen per run, with the last folder pre-filled.
2. Closed (owner, 2026-11-15): review is the planner's job (D-S10), so the planner sees every report.
3. Coders on the same files need separate worktrees (F26). Is that allowed, and who cleans them up?
4. Closed (owner, 2026-11-15): the name scheme in D-S13.
5. Alert channel when away (D10): Windows notification, or the Teams path in OpenTail.Orca?
6. Closed (owner, 2026-11-15): the third pattern is named **solo**. No self-planning behaviour is promised.
7. Closed (owner, 2026-11-15, "i think it's ok"): the names stay as written, "review" included (`PatternSpec` in `RunPlan.cs`).
8. Closed, changed by the owner (2026-11-15, S7 review): the history loads when the window opens, in the background, and again on the 🔄 mark next to "History". It is no longer read only when opened.

## 7. Checkboxes

- [x] S0 This plan, with contracts and the invented-ideas table.
- [x] S0b Review against the code, 2026-11-15 (section 9). Corrections made: the `TrustCheck` signature, the migration wording, the peer-name and permission-mode contracts, the run store, and the record key. Not yet built.
- [x] S1 Core (built 2026-11-15; 119 Core tests pass; decisions D-S16 to D-S18; findings F41 to F43): patterns as data (`PatternSpec`, `RunPlan`, `RunNames`). Run store (`RunStore`, D-S12). Supervisor records keyed by `Name` (`SupervisorRunner`, `StateStore`). Not built here: the window and `Program.cs` still use their own specs (S5). Tests:
  - the current `state.json` shape loads, and the running pair is matched after the key change;
  - a second coder record does not throw in `StateStore.Load`;
  - the one-coder default keeps its names (D-S13), so a rename test is an orphan test;
  - the three shipped patterns;
  - N coders named uniquely;
  - a solo brief names no peer.
- [x] S2 Core (built 2026-11-15; 130 Core tests pass; F44): folder validation (`FolderCheck`: exists, repository root, trust state, recent folders from `runs.json`) and `FolderKey`: one key for a working directory and its transcript folder name. Test `FolderKey` on the real folders under the projects folder before relying on the encoding. Tests.
- [x] S3 (built 2026-11-15; F45, F46) Window shell: rail (grouped by folder, collapsible groups, live and history sections, history loaded when opened, open question 8) and pane (terminal, transcript or setup). Test: a hidden terminal is kept alive, not disposed, when the pane shows a transcript (acceptance changed by the advisor, 2026-11-15, to a counter command, not a session; see F45). Proved: a hidden terminal keeps its process, the output written while hidden, and its keystrokes. Not verified: a real `claude attach` while hidden (pending the owner, or one later test session). The history items and the Trust step are compiled and read, not run.
- [x] S4 Setup page (built 2026-11-15; 145 Core tests pass; screenshot checked with PrintWindow; the self-test still passes): pattern choice, model and effort per role, brief extra per role (saved per pattern), overtypable folder with validation, Start. One permission mode per run (D-S14). `SetupPlan` (Validate, Build), `PatternStore` (Beside the state file), `SetupView` (the page). Start checks and saves; it starts nothing and writes no run file. Not done here: launch (S5); Browse compiles (`OpenFolderDialog` exists) but was not clicked; a Trust step for a chosen folder is a message only (the step comes in S5). Model and effort pairs are not verified (S9c).
- [x] S5 (built 2026-11-15; 156 Core tests pass with the fake CLI; the window's live launch is NOT run, pending the owner; F48) Launch from setup: Start writes the run to `runs.json` and hands the specs to the supervisor. Replaces `SessionWindow.Specs` and the second spec list in `Program.cs` (lines 54-58, F43). Tests:
  - a plan > code run (one planner, one coder) starts two sessions with unique names;
  - a plan > many coders run starts one planner and N coders with unique names;
  - restarting the window gives the same specs.
- [x] S6 (built 2026-11-15; 175 Core tests pass; F49, F50) Warm and cold labels on running and past sessions (D-S7). Tests on fixtures. The real-transcript check is `OpenTail.Bullshark.Supervisor --cache-report` (F49): the one-hour warm bucket rewrites 0.075% of 25,300 pairs (passes the 0.5% bar); the one-hour cold bucket rewrites 81% of 47 pairs; the five-minute cold limit is not judged (1 pair). Not verified: the labels on screen (see F50).
- [ ] S7 Screenshot review of the whole window with the owner. Decide D-S1 and the open questions.
- [ ] S8 Later, not in this plan: decision agent proposes a pattern from a goal (`docs/later/dynamic-configuration.md`).
- [ ] S9 Evaluate CLI switches not yet in use (added 2026-11-15). Each switch is tested on its own, in a scratch folder, with Haiku and small prompts. Every test writes a finding (F41 onward). Each switch ends with one decision: adopt, park, or drop.
  - [ ] S9a `--worktree <name>`: two sessions write the same file in separate worktrees without clashing; `git worktree list` shows them; removal through `claude rm` leaves no stray worktree. Works if all three hold.
  - [ ] S9b `--fork-session` with `--resume`: the new session has a new id; the original transcript's size and last line are unchanged; the fork continues from the old context. Works if all three hold.
  - [ ] S9c `--effort <level>`: the same prompt at `low` and `high` gives different output token counts, read from the transcript. Also check that haiku accepts each level.
  - [ ] S9d `--add-dir` and `--restricted`: a session in one folder cannot read a file in another folder without `--add-dir`; with `--restricted`, record which tools disappear (Bash is expected to). Decide whether coders can use it.
  - [ ] S9e `--fallback-model`: the flag is accepted, and the behaviour when the first model is unavailable is recorded, if it can be forced without cost. Otherwise park it, with the reason.
  - [ ] S9f `--autocompact`: optional, because it needs a long session. Park it unless a cheap test exists.
  - [ ] S9g `--output-format stream-json --include-hook-events` in print mode: one headless prompt; the events parse as JSON lines. Relevant to the rail's live status.
  - [ ] S9h `--from-pr`: only if a pull request exists to test against. Otherwise park it.
  - Cost limit: at most five short test sessions in total, on Haiku at low effort. Stop and record when the limit is reached.
  - Owner's sessions are never touched. Test sessions are named `ovc-test-*` and removed afterwards.

## 8. Risks

- **Hiding a terminal** might stop the session's display or its process. S3 tests it before anything else is built on it.
- **Layout width.** Collapsing the rail resizes the terminal. Check that it reflows cleanly.
- **Key change and names.** Keying records by name orphans running sessions only when a name changes. D-S13 keeps the default names. The S1 test covers the old-name case. Renaming a peer in a live run would leave its brief pointing at a name that no longer exists.
- **Legacy folder (D-S17).** The legacy run is written with the launch folder. If the existing sessions run in another folder, a resume starts them in the wrong one. Not verified: compare each session's `cwd` in `agents --json` before S5 relies on the folder.
- **Start resolved late (S5, not measured).** If `--bg` succeeds but the session id is not read back, the start counts as failed, and the session may run with no record. The next pass does not see it. Count such cases in a real run.
- **Resource (from S3, not measured).** All recorded terminals sit in the pane, and each starts its `claude attach` when the window opens, including the "Other sessions" records. The old tab control loaded only the selected tab. Count the attach processes on the next run that has records, and compare with the number of sessions.
- **Scope.** Patterns, setup, rail and warm labels are four features. Build them in the checkbox order, and stop after each one for a screenshot check.

## 9. Review of 2026-11-15

Checked by reading the code, and cross-checked with the advisor (Opus 5.5, the conversation's advisor at the time):

- Read: `SessionSpec.cs`, `SessionRecord.cs` (including `StateStore`), `SupervisorPolicy.cs`, `SupervisorRunner.cs`, `NudgePolicy.cs`, `ClaudeCli.cs`, `TranscriptIndex.cs`, `TrustCheck.cs` (signature), `SessionWindow.xaml.cs`, `SessionWindow.xaml`, `SessionWindow.History.cs`, `HistoryPanel.cs`, and findings F14, F22, F26, F33 in `docs/findings/FINDINGS.md`.
- Checked and not a problem: `ResolveSessionIdAsync` matches by short id, so duplicate names do not make the supervisor read back the wrong session. An earlier concern said it did; the code says otherwise.
- Corrected in the plan: the `TrustCheck` signature; the migration wording (no file rewrite); the peer and permission-mode contracts; the run store; the `Role` key and `StateStore.Load`.
- Not read yet: `AgentInfo.cs`, `TranscriptReader.cs`, `TranscriptPager.cs`, `PlanFile.cs`, the body of `TrustCheck.Check`, `CliRunner.cs`, `Program.cs`. Read them before S1 and S2.
- Not verified: how Claude encodes a working directory into a transcript folder name (S2 test); how a removed session shows in `agents --json` (S1 test); the rail's history loading cost (open question 8).
- Built: nothing in this plan. Checkboxes S1 to S9 are unchecked.
