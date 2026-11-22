# Review notes: provider-opencode-2026-11-22 (plan review, 2026-11-22)

Code paths are under `src\OpenTail.Bullshark.Core\` and `src\OpenTail.Bullshark.App\`; the plan writes `Core\...` and `App\...` without the prefix (note).

## Must

1. **OC-D10 summary route injects text and can touch the owner's folder** (plan 2; `Claude\CliRunner.cs:59-90`, playbook "Prompts"). The transcript goes on a command line to `cmd.exe` and `opencode.cmd`; quotes, `%`, `&` in message text break the line (command injection) and 32k can be exceeded. `--dir <session folder>` gives the model its tools in the owner's project (opencode's default permissions are not read in the research). Fix: feed the text on stdin through `pwsh` and `opencode.ps1`, use an empty scratch folder for `--dir`, and verify in OC0 what `run` may do without `--dangerously-skip-permissions`.
2. **`run` writes to the owner's database** (research 2: "Creates a session"). The plan does not say that the summary adds a session row (and a log) in the owner's `opencode.db`. Fix: state it as owner item with the cost line, or run with `OPENCODE_DB` set to a scratch file after reading exe:994721 (the migration check there may itself write).
3. **OC-D1 "version read only, no model call" is unverified.** The research ran nothing; `--version` was not read from the code. Startup may write logs, run a migration or auto-update the install (Copilot's read passes `--no-auto-update`, `CopilotSetupCheck.cs:46`). Fix: read the version from `opencode-ai\package.json` instead, or prove in OC0 that `--version` exits before any startup work.
4. **Cost line contradicts OC-D9** (`App\Providers\ProviderResumeRules.cs:40-41,68-72`). Last turns shows the shared "No model call" line, but Start sends `--prompt`, which auto-submits a model turn. The plan's "as the Copilot start line does" is the after-start `StartedLine` (`:170-177`), not a line shown before Start. Fix: a provider-specific cost line for last turns and for the summary start (summary call plus start turn = two calls); make the OC3 gate test those strings.
5. **VERIFIED claim not in the research (OC-D2).** "A relative `OPENCODE_DB` is joined to the data folder" is not in research 3 or 6 (only absolute or `:memory:` as is). Fix: remove or mark ASSUMED; treat `:memory:` as no database.

## Should

- **Read-only is thin (OC-D2, OC1, R2).** "No write verb" misses PRAGMAs (journal_mode, user_version), ATTACH, VACUUM. Fix: `Mode=ReadOnly`, `PRAGMA query_only`, an allow-list of statements, parameters only, and a test that the db, -wal and -shm bytes are unchanged. Open risks: a read connection can create `-shm`/`-wal`; a long read transaction blocks opencode's checkpoint; journal mode is not in the research; pages read at different times may disagree. Never `immutable=1`. Keep transactions short. R2 says "until S5", a stage that does not exist, and no stage tests a WAL database.
- **OC-D8**: say what happens with a missing folder or a path that is a file. The Copilot code recreates it (`SessionWindow.CopilotResume.cs:84-92`, `RefusalFor`); the plan is silent. The id shape must refuse a leading `-` (flag injection) and needs a change in shared `CommandLineRules.cs:93-138` (UUID only), which the plan does not list.
- **OC-D5**: order of messages and parts is unstated (`time_created`, then `id`); TextPart fields such as synthetic or ignored are not read (research cites 590071-590073 only). Add paging, the 5,000 cap (`TranscriptLimits`) and the line format as a contract.
- **OC-D7/R1**: check columns as well as tables, catch `SqliteException` by type, and add the promised "rail says which schema" to a stage.
- **Parity table is incomplete.** Missing: scratch folder and start-up cleanup (`CopilotScratchFolder`), Starts cleanup, kept summary files, marks and library tags, `ProviderPaths.ForOpenCode`, `BehaviourSettings`, `ResumeModePolicy`, `SummaryInstructions`, `ProviderResumeText`, `SetupCheckDialog`, `ResumeTargetMenu`, `RememberChoice`, `CopilotRailText`, `CopilotResumeRules`, `ProviderRailGroups`.
- **Section 5 "23 files" does not reproduce.** My read-only grep finds 48 App source files mentioning Copilot (35 not named Copilot, 18 with `ProviderId.Copilot`) and 9 Core files outside `Copilot\`. Fix: list the files.
- **Shims**: not recorded. Here: `opencode`, `opencode.cmd`, `opencode.ps1` in `%APPDATA%\npm`. The start route needs `opencode.ps1`; the 1.3.11 CLI and the desktop `opencode-cli.exe` may be first on PATH.
- **Start-turn model call** was decided without the owner (playbook: model calls need a go-ahead). Add an owner item.
- **Stdin (OC-D10)**: `ProcessCliRunner` redirects stdin only when `standardInput` is not null; the plan must say "pass an empty string", since null inherits the handle of a WPF process.
- **OC-D15**: native SQLite on both Core targets and App tests must be checked.

## Notes

- Line cites: `ProviderChoice` Parse/TextOf are about `:13` and `:22`, not `:14`/`:24`; `CopilotHistorySource.cs:32` is the class line, not the list method.
- OC-D3 "like the CLI's own list": the CLI lists one project; the rail lists all.
- `project(id, worktree)` is listed but never used.
- OC-D9: the `--prompt` claim is right (research 2, "auto-submitted").
- OC-D14 should say: exception type only, never `Message` (connection text can carry the path).
- OC5 "the scan" is undefined; OC1-OC3 gates should name full target names and before/after counts.
- Owner decisions (header) are quoted faithfully; no change of meaning found.

## Checked

Plan, research, playbook, `Capabilities.cs`, `ProviderId.cs`, `ProviderChoice.cs`, `SessionKey.cs`, all five Copilot files, `CommandLineRules.cs`, `CliRunner.cs`, `CliStartLauncher.cs`, `ProviderResumeRules.cs`, the Copilot resume window file. Table and column names match the research (subset). Not checked: opencode's real behaviour (nothing run, the exe not read), the owner's database, tests, builds.
