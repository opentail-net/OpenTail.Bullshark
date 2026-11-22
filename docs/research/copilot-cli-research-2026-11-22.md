# GitHub Copilot CLI: research for the provider track (2026-11-22)

Status: research notes. Read-only: nothing built, no session started, no model call made. Three research subagents (Haiku, effort xhigh) reported; this file keeps what was checked on this PC and what each report added. Clean-room: concept notes only. No code, identifiers or procedure carried from the example projects.

Sources: the installed CLI (`copilot --help`, `copilot help ...`, the shipped package files, names and counts only), the local `~/.copilot` folder (names, sizes and key names only; no values, no text), the reference folder `C:\Git\split\examples`, and the vendor documentation as the research agent read it (not re-fetched here).

## Verified on this PC (2026-11-22)

- **Three installs, three versions.** With auto-update on, `copilot --version` reports `1.0.104-3` (the auto-updated copy). With `--no-auto-update` it reports the version bundled in the executable that runs: **`1.0.34`** for the WinGet `copilot.exe` (first on PATH: `where copilot`), and **`1.0.51`** for the npm `copilot.cmd` (package `@github/copilot`; shims `copilot`, `copilot.cmd`, `copilot.ps1`). So the version a launch gets depends on which file runs and on the update setting. The plan treats this as an owner decision (G-O5).
- Home folder `~/.copilot`: `session-state/<id>/` in **52** folders: `events.jsonl` in 47 of them (5 hold only `workspace.yaml`, plus empty `checkpoints`, `files` and `research`), and `workspace.yaml` in every folder (keys: `id`, `name`, `user_named`, `summary_count`, `cwd`, `git_root`, `branch`, `repository`, `host_type`, `created_at`, `updated_at`); `session-store.db` (with `-wal` and `-shm`); `config.json`; `logs`; `plans`; `ide`; `command-history-state.json`.
- The package ships `schemas/session-events.schema.json` (JSON Schema, draft-07). It lists 78 event type names. The vendor's docs site does not document the event format. We read the schema's names only; we do not copy it into the repo.
- Event envelope (every line): `type`, `data`, `id`, `timestamp`, `parentId`.
- Event types present in the 47 local files: `session.start` (47), `session.model_change` (47), `system.message` (47), `user.message` (47), `assistant.turn_start` (47), **`assistant.turn_end` (47 of 47 folders)**, `assistant.message` (44 folders), `session.shutdown` (47), `session.error` (3 events), `session.info` (2 events). `session.idle` does not occur in these files.
- `session.start.data` keys: `sessionId`, `version`, `producer`, `copilotVersion`, `startTime`, `selectedModel`, `context`, `alreadyInUse`, `remoteSteerable`.
- `session.start.data.context` keys: `cwd`, `gitRoot`, `branch`, `headCommit`, `repository`, `hostType`, `repositoryHost`, `baseCommit`. The working folder is `cwd`.
- `user.message.data` keys include `content`, `transformedContent`, `attachments`, `interactionId`. `assistant.message.data` keys include `messageId`, `model`, `content`, `toolRequests`, `turnId`, `outputTokens`, `requestId`.
- `session.shutdown.data` keys include `shutdownType`, `totalPremiumRequests`, `totalApiDurationMs`, `sessionStartTime`, `codeChanges`, `modelMetrics`, `currentModel`, `currentTokens`, `conversationTokens`. Usage is in the files.
- Help flags confirmed: `-i, --interactive <prompt>` (interactive, starts with a prompt); `-p, --prompt <text>` (non-interactive); `-r, --resume [<value>]` (picker with no value; by id, name, or a 7+ hex prefix); `--continue` (most recent session); `--session-id <id>` (resume, or set the id of a new session); `-C <directory>`; `--add-dir`; `--model <model>` (`auto` allowed); `--reasoning-effort <none|minimal|low|medium|high|xhigh|max>`; `--allow-all-tools`; `--available-tools [<tools>...]`; `--excluded-tools`; `--output-format <format>`; `--share [<path>]`; `--plugin-dir`; `--acp` (Agent Client Protocol server); `--no-auto-update`; `--sync-session[=on|off]`.
- `--sync-session`: "Enable or disable read-only account history sync for this run only (omitted: use configured/default behavior)." **The default is not stated.** This is a privacy item (see Privacy below).
- Conflict noted by the research agent: the docs say `--continue` opens the picker; the local help says it resumes the most recent session. The local help is taken for 1.0.104-3. We do not use `--continue`.

## Corrections to earlier repo notes

- The 2026-11-19 note said the `events.jsonl` line schema is undocumented. The package ships a schema, so the names are known (verified locally). The vendor docs still do not list it.
- The screen-based notes (agent-manager's fixtures, pinned to CLI **1.0.114**) said Copilot has no turn-end marker. That is true of the screen, but **the file has `assistant.turn_end` in every session folder checked**. Finished state can come from the file, not from timing.
- The 2026-11-19 note said `-p` does not fire prompt hooks. The vendor docs say prompt-type hook entries do not fire under `-p`; other events under `-p` are not stated and were not run.

## What the research agents added

**Storage and resume (vendor docs and the local folder).** `COPILOT_HOME` replaces the base folder for config and state (help text; not run against a live session). `copilot --resume=<id>` resumes by id, name, or a 7+ hex prefix. The docs describe a picker in-session (`/resume`) with keys for delete and sort; we use neither.

**Launch.** A new session in a folder: `copilot -C "<folder>"` (and `--add-dir` for extra folders). A new session with a known id: `copilot --session-id=<uuid>` (the docs say it sets the id of a new session; **not yet run**, see the plan's probe P-C1). A prompt on start: `copilot -i "<text>"`. A summary call would use `-p` with `--output-format text`.

**Summaries and tools.** `--available-tools` restricts the tools the model can use; the syntax for "no tools" is not yet confirmed (probe P-C4). `-p` needs `--allow-all-tools` if tools are used; a summary must use none.

**Hooks (docs and the package code; not run).** Hook files live in the repo (`.github/hooks/*.json`), in the user folder (`~/.copilot/hooks/` or `$COPILOT_HOME/hooks/`), or under a `hooks` key in `settings.json`. The format is `{"version": 1, "hooks": {...}}`. The docs list 14 events in camelCase (`sessionStart`, `sessionEnd`, `userPromptSubmitted`, `preToolUse`, `postToolUse`, `permissionRequest`, `agentStop`, `notification`, and others). The docs show camelCase and do not say which form the payload uses. Stdin fields (from the package code): `hook_event_name`, `session_id`, `timestamp`, `cwd`, plus event fields. **Not captured from a live run.** Hook names disagree across the example projects (see below).

**Auth and quota.** Login: `copilot login` (browser locally; device code over SSH and in CI). Token variables in this order: `COPILOT_GITHUB_TOKEN`, `GH_TOKEN`, `GITHUB_TOKEN`. A classic `ghp_` token is not supported. Quota: `/usage`, `/context`, the `footer.showQuota` setting, `--max-ai-credits`. Per-turn usage is in the session files (`assistant.usage` events and `session.shutdown.modelMetrics`).

**SDK.** The `@github/copilot-sdk` package has `listSessions`, `resumeSession`, `getSessionMetadata`, `deleteSession` and `listModels`, plus in-process extension hooks. Its preview status and a reported issue about resumed sessions were seen only in search snippets; not verified.

**Licence (the CLI's own LICENSE.md in the package).** Proprietary. Section 1 grants a non-exclusive licence to install and run. Section 3 says the licence does **not** grant the right to "modify, adapt, translate, or create derivative works of the Software". Section 4 reserves all other rights. The Acceptable Use Policy's "scraping" definition is about extracting information from GitHub's **Service** by automated means; it does not mention local files. No clause on third-party tools reading the files the CLI writes was found. The Copilot product-specific terms page returned 404 when fetched. **This is not legal advice.** Our plan reads files the owner's own CLI writes on the owner's own machine, copies no code, and does not modify the CLI. That reading needs the owner's confirmation (plan item G-O1).

## Examples: what they show (clean-room, concept notes only)

Each row says what the example does with Copilot. None of them reads Copilot's session files or usage, so the design here comes from the files and the shipped schema, not from the examples.

| Project | Licence | What it shows for Copilot | Use for us |
|---|---|---|---|
| agent-manager | Apache-2.0 | Screen states from Copilot **1.0.114** fixtures (approval, trust, ask-user, picker, working, error, idle, and others). Launch with `--session-id`; resume with `--resume <id>`. | Ideas only. Screen parsing is **not** needed in v1, because the files carry the state. Our CLI is 1.0.104-3, older than the fixtures. |
| cmux | GPL-3.0-or-later, with BUSL parts; manifests Apache-2.0 | Screen patterns (herdr-derived, drift-prone). Hook installer that writes one owned file under `~/.copilot/hooks/` and asks first. The repo names hook events in two styles and its docs contradict each other. | Concept only. Do not copy files. Owner-file-with-confirmation is worth copying. |
| agent-governance-toolkit | MIT | Copilot CLI governance: pre-tool deny is the only point that blocks a tool call (checked in code, stub tests only). Post-tool and prompt hooks can only suppress or rewrite. A chat-server variant is not a CLI extension. | Relevant only if hooks are added later (plan stage G5). |
| tday | Non-standard ("Say No To Suicide Public License") | Launch only. The usage parser has a TODO for Copilot. | Skip, or concept only. |
| muxel | GPL-3.0 | Lists Copilot as a skills target. No session or usage reads. | Skip. |
| darvoza | MIT | Copilot appears only as an MCP client. | Skip. |

## Privacy

- **`--sync-session`**: "read-only account history sync". Its default is not stated. Every call the app makes will pass `--sync-session=off` (decision G-D6), and the config default is checked by a probe (P-C7). If the owner wants sync on, that is their decision.
- Session files hold private text. Tests use synthetic fixtures written by hand from the observed names. No real session text goes into the repo, a log, or a fixture.
- Summaries go to Copilot's own model only (the platform rule the owner set for Codex, applied to Copilot).

## Probes still needed (stage G0 of the plan)

- **P-C1** `--session-id` on a new session: is a session folder created before the first message, and does `session.start` carry that id? (Interactive start with no prompt, a few seconds, our own process killed. No model turn.)
- **P-C2** summary route: one `-p` call (prompt on stdin) with `COPILOT_HOME` pointing at a scratch folder we create, `--sync-session=off`, `--output-format text`. Checks: exit code, that the real home gains no `session-state` folder, that sign-in still works from the scratch home, and the scratch folder is removed. **This is one model call**, under the same allowance rule as the Codex probes (one `say hi`); the owner may veto it (G-O4).
- **P-C3 (partly answered, 2026-11-22):** `copilot --version --no-auto-update` runs the bundled version (1.0.34 for WinGet, 1.0.51 for npm; see above) and exits 0. Whether reading the version starts a download was **not** checked.
- **P-C4** the `--available-tools` value that means "no tools".
- **P-C5 (answered, 2026-11-22):** `session.title_changed` appears in **0 of 47** local folders. Titles therefore come from the first user message.
- **P-C6** the `--reasoning-effort` values that work for the default.
- **P-C7 (partly answered, 2026-11-22):** `config.json` has 32 key names outside the experiment cache (login and first-launch records, and feature flags). **None is a sync, share, telemetry, session, hook or model key.** So the file does not set the `--sync-session` default; the flag's help says the default is "configured/default behaviour", and the default itself is still not stated.
- **P-C8** a multi-line prompt through `pwsh -EncodedCommand` to a fake `copilot.ps1` (argument equals the text, as the Codex launch experiment did). No model call.

## Review additions (2026-11-22, after the Sonnet plan review; see the review notes)

- All 47 event files are **single-turn**: one `user.message`, one `assistant.turn_start`, at most one `assistant.turn_end`, and a routine `session.shutdown`. Multi-turn behaviour is therefore unverified.
- `ICliRunner.RunAsync` has **no environment parameter** (`Core/Claude/CliRunner.cs:18-23`), so `COPILOT_HOME` cannot be set for a call. The isolating flags are `--config-dir` and `--log-dir` (both in the help), with `--silent`, `--no-ask-user`, `--no-custom-instructions` and `--disable-builtin-mcps`.
- `ITerminalOpener.ResumeCommandLine(sessionId)` takes no folder (`Capabilities.cs:32`); the window sets the tab's folder.
- Codex runs its shim through `cmd.exe /d /c` (`CodexTerminalOpener.cs:47,60`); Copilot's terminal and summary calls do the same.
- A bare `copilot --session-id` start was not run against the owner's real home. The 5 folders with only `workspace.yaml` may come from earlier starts; they are the owner's files and are **not** deleted.
- `SessionSummary` has no git or branch fields (`Providers/SessionSummary.cs:7-17`).

## Probe results (2026-11-22, scratch folders only; names and counts only, no session text)

- **Summary route (one model call, the file route):** `copilot -p "<short prompt>"` in a scratch working folder that holds `transcript.txt`, with `--config-dir` and `--log-dir` in scratch, `--sync-session=off`, `--output-format text`, `--silent`, `--no-ask-user`, `--no-custom-instructions`, `--disable-builtin-mcps` and `--available-tools=view`. Exit 0; the reply was the one word in the file; the only tool called was `view`; the owner's `session-state` stayed at 52 folders; sign-in worked from the scratch config; 1 premium request on the default model `mai-code-2-flash`. The scratch folder was removed.
- **Stdin is not a prompt source:** `-p` requires a value (`error: a value is required for '--prompt <text>'`). An earlier stdin attempt most likely made no call; its output was not kept, so this is inferred.
- **`--config-dir` is deprecated** (stderr warning naming `COPILOT_HOME`). Kept for now; see plan R-C8.
- **Unknown resume id** (`-p`, scratch config, no model call): exit 1, `No session, task, or name matched '<id>'`, and the CLI's own options for resuming by id, by name or with the picker. `--name=<name>` names a new session.
- **No-tools flag syntax:** `--available-tools=view` is accepted; `none`, empty and `[]` also parse, but their meaning is not verified, so only `view` is used.
- **Version read** (`--version --no-auto-update`): bundled version; the top level of `~/.copilot` did not change; the package cache was not checked.
- **Which copy runs:** `where copilot` lists the WinGet `copilot.exe` first, then npm `copilot` and `copilot.cmd`.
- **Fields (counts only):** 47 of 47 user messages carry `transformedContent`, with a length different from `content` in each case (what it holds was not read). Of 52 `workspace.yaml` files, 47 have a non-empty `name`; none has `user_named` true (so `name` is generated); 5 folders have no `events.jsonl`. `session.title_changed` is absent from the local folders.
- **Not run:** P-C1 (session id at launch needs a pseudo-terminal, which Git Bash does not provide); P-C8 (a fake-shim launch test, to be written with the launcher in stage G1.7).
