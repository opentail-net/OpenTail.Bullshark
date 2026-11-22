# Provider: opencode (npm CLI 1.4.3)

Date: 2026-11-22. Version 2 (after the plan review in `provider-opencode-2026-11-22-plan-review-notes.md`). Status: **plan, no code yet.** Research: `docs/research/opencode-2026-11-22.md` (committed locally, `773f23e`). Process: `docs/plans/provider-onboarding-playbook.md`. Owner decisions of 2026-11-22: the target is the npm CLI 1.4.3; the history is read **from the database, read-only**; each summary is **one model call on the owner's opencode login**; wire everything in, the summary last, and **not run for the owner**.

## 1. Goal and parity target

Add opencode as a fourth provider: list its sessions in the rail, read their transcripts, search them, resume a session in a terminal tab, start a new session from text (Last turns), and (last) summarise a session.

**Parity target:** the Copilot provider (the most recent template), feature for feature (section 4). Left out, with the reason:
- Writing to opencode's own tables: never, by this app's reader. (A summary run by opencode itself writes a session row; see OC-O4.)
- Running opencode's server: not used (owner decision 2 chose the database).
- The newer build's tables (`session_v2`, `session_message`, `session_part`): not read in this version (OC-O3).
- Supervision (the app running an opencode session for the owner): not in this version.

## 2. Decisions (made without the owner, each with its reason)

- **OC-D1 Target build and version.** The npm CLI 1.4.3 (owner confirmed). The version is read from the package file `node_modules\opencode-ai\package.json` beside the shim the setup check finds, **with no process started** (the review found that a `--version` run may write logs, migrate or update; see research §3). A `1.4.x` version is Ok. Any other version is a Warning naming it. A shim with no such package file (for example the desktop CLI) is a Warning: "version not read: this is not the npm install".
- **OC-D2 Reads.** The database is opened **read-only**: `Mode=ReadOnly` and `PRAGMA query_only = ON` on the connection; only `SELECT` statements, with parameters, from a fixed list; no other statement type is ever sent (a test checks the statement list). Busy timeout set. `immutable=1` is never used (it would ignore a live write). The path follows opencode's rule (research §3): an absolute `OPENCODE_DB` when set is used as is; a relative or `:memory:` value is **refused** with a status line ("OPENCODE_DB is not an absolute path; not supported"); when unset, the data folder is `XDG_DATA_HOME\opencode` if that is set, else `%USERPROFILE%\.local\share\opencode`. The relative-value join is **ASSUMED** (research did not verify it), so the app does not rely on it. A test proves the database, its `-wal` and its `-shm` files are unchanged by a full read.
- **OC-D3 Which sessions.** Root sessions only (`parent_id IS NULL`). The CLI's own list is per project; the rail shows all folders, one row per root session.
- **OC-D4 Archived.** `time_archived` not null: marked archived, as the other providers do.
- **OC-D5 Messages.** Messages in `time_created`, then `id` order; a message's role is `data.role` (user or assistant). Its text is its `part` rows, in `time_created`, then `id` order, whose `data.type` is `text`, using `data.text`. Parts whose `data.synthetic` is true are left out (VERIFIED at OC0: the program's own transcript skips them). Parts whose `data.ignored` is true are **kept**, as the program's own transcript keeps them (VERIFIED at OC0; the owner's words stay visible). Tool and other parts are left out ("messages only"). The line format and the 5,000-line cap follow the Copilot reader (the shared `TranscriptLimits`).
- **OC-D6 Times.** Integer milliseconds since the epoch (research §6, VERIFIED from the program's code).
- **OC-D7 Schema guard and rail line.** The reader checks that the `session`, `message` and `part` tables exist and that the columns in OC-D5 exist (PRAGMA table_info). A database without them gives the status "history not available: the database is not an opencode 1.4 database". Exceptions are caught by type (`SqliteException`). The rail shows one line naming the schema read ("opencode 1.4 tables").
- **OC-D8 Resume.** A terminal tab: `cmd.exe /d /c opencode -s <id>`, working folder = the session's folder. **Before the folder is used:** the folder must exist; if it is missing, Start is blocked with the folder notice (the same rule as the other providers, as the owner decided on 2026-11-22). The id is checked against the shape verified at OC0: `^ses_[0-9a-f]{12}[0-9A-Za-z]{14}$` (the program itself checks only the prefix `ses`; the full shape is derived from its generator). The check lives in opencode's own module, not in the shared `CommandLineRules` (which is for Claude and Codex ids and must not change).
- **OC-D9 Start from text (Last turns, and the cross-platform start).** `opencode <folder> --prompt <text>`, with the text in a start file that the start route reads (the Codex and Copilot route). **`--prompt` is submitted at once: a start from text makes one model turn on the owner's login.** The pre-Start cost line for Last turns says this in those words (not "No model call"). The post-Start line says it too. Owner item OC-O5 asks whether the owner accepts it.
- **OC-D10 Summary (last; built, not run for the owner).** The transcript is sent on **stdin**, not on the command line: the start file holds it, and a pwsh step pipes it into `opencode run`. The prompt is a short argument. `--dir` is an **empty scratch folder** beside the state file (so the model has no tools in the owner's project). Stdin is never inherited: the Core process runner is given an empty string to redirect it. OC0 found (VERIFIED): without the flag, the defaults **allow** reads, edits and shell commands inside the folder and **reject** anything outside it. So the summary runs with an inline configuration that **denies every tool** (`OPENCODE_CONFIG_CONTENT`, which the program reads, research §3; the permission key's exact form is verified by grep in OC1 before use). The summary's text is the only output the app uses. One model call; the cost line says so before the call (the summary's line is its own, not Last turns').
- **OC-D11 Never** pass `--dangerously-skip-permissions` (research §2; it skips the tool's permission prompts).
- **OC-D12 Identity.** Provider id `opencode`; ui.json key `opencode`; display name `opencode`, lower case, as the CLI names itself.
- **OC-D13 Sign-in.** Not handled by the app: the owner's existing opencode login is used by opencode itself.
- **OC-D14 Logs.** The provider name, counts and the exception **type** only. Never an exception's message, a path, an id or a title.
- **OC-D15 A new dependency.** `Microsoft.Data.Sqlite` in the Core project, read-only. The native SQLite engine it uses is checked on both Core targets (net10.0 and net11.0) at OC1, and its licence is read at OC5 and recorded in `THIRD-PARTY-NOTICES.md`.
- **OC-D16 Structure.** Copy the Copilot provider's App structure (rail, search, transcript tab, row menu, exit watcher, setup row, resume screen, scratch and start-file cleanup, kept summaries, marks and tags, the provider paths, the behaviour settings, the resume mode policy, the summary instructions, the resume text, the setup dialog, the target menu, the remembered choice, the rail text and rules), renamed. Shared window files get branches only.
- **OC-D17 The shim.** The setup check finds the `opencode` shim on PATH the same way the Copilot check does, and reports which copy it is (the npm `opencode.cmd`, or another). The start route uses the same copy. The desktop CLI (1.3.11) and the desktop app are not the target.

## 3. Owner items (defaults are safe to run on)

- **OC-O1 Target build:** the npm 1.4.3 (confirmed). Default as OC-D1.
- **OC-O2 Summary model.** The summary uses the model in the owner's opencode config. Default: that model. The owner can change it in opencode, not in the app.
- **OC-O3 Newer build.** The owner's database also holds the newer build's tables, which this version does not read. Default: not read. A later version reads them after the owner says which build is daily.
- **OC-O4 Summary writes a session.** `opencode run` creates a session and writes it into the owner's `opencode.db` (opencode's own write, not this app's), and it uses the owner's login from the owner's data folder. Redirecting the data folder to scratch would stop the login from being found unless the credential file is copied, which this plan does not do. **Default: the summary runs with the owner's own data folder, so a session row is added to the owner's database.** The owner decides before the summary is enabled; until then the summary stays off.
- **OC-O5 Start from text makes a model turn.** Last turns and the cross-platform start send a prompt that opencode submits at once: one model turn on the owner's login per start. **Default: accepted, with the cost line saying so before Start.** The alternative is to limit Last turns to the owner's own resume, with no start from text.

## 4. Parity table (Copilot feature → step → status)

| Copilot feature | Step | Status |
|---|---|---|
| Rail rows from a history source | OC1 (`OpenCodeHistorySource`, read-only) | planned |
| Transcript page reader | OC1 (`OpenCodeTranscriptReader`) | planned |
| Terminal new and resume command lines | OC1 (`OpenCodeTerminalOpener`) | planned |
| Setup check (version read, no process) and the shim | OC1 (`OpenCodeSetupCheck`, OC-D1, OC-D17) | planned |
| Provider class (`IProvider`, the three capabilities) | OC1 (`OpenCodeProvider`) | planned |
| Provider identity, ui.json key, `ProviderChoice` | OC1 (`ProviderId.OpenCode`, `ProviderChoice` at `:13` and `:22`) | planned |
| Provider paths and the behaviour settings | OC1 (`ProviderPaths`, `BehaviourSettings`) | planned |
| Scratch folders and start-file cleanup at start-up | OC2 (only in the window that holds the state lock) | planned |
| Kept summaries and their files | OC3 | planned |
| Marks, tags, pins and the remembered mode | OC2 and OC3 (the existing code, by the provider key) | planned |
| Rail, search, transcript tab, row menu, exit watcher | OC2 (App partials, renamed) | planned |
| Setup row and the setup dialog | OC2 | planned |
| Rail text and rules, the resume text, the summary instructions | OC1 (Core) and OC2 (App) | planned |
| Resume screen: full, last turns | OC3 (`OpenCodeResumePrompt`) | planned |
| Start-from-text route (pwsh start file) | OC3 (OC-D9) | planned |
| Summary (`copilot -p` through a scratch folder) | OC3 (`opencode run`, stdin, empty `--dir`; built, not run; OC-O4) | planned, last |
| Target chooser, the target menu, the cross texts | OC3, once the start route exists | planned |
| Remembered choice and the Continue action | OC3 (by the provider key) | planned |
| Licence notice | OC5 | planned |

## 5. Contracts this must satisfy (read in the code, 2026-11-22)

- `Core/Providers/Capabilities.cs:10` `IHistorySource.ListAsync(ct)`; `:20` `ITranscriptReader.ReadPageAsync(sessionId, firstLine, count, ct)` → `TranscriptPage`; `:26` `ITerminalOpener.NewCommandLine(folder)`, `ResumeCommandLine(sessionId)`. `:39` `ISupervisedLauncher` and `:47` `ILiveSource`: optional, not implemented (as Codex and Copilot).
- `Core/Copilot/CopilotProvider.cs:11` the template class.
- `Core/Providers/ProviderId.cs:7` the record struct; static instances at `:10`, `:13`, `:16`. Add `OpenCode = new("opencode")`.
- `Core/Providers/ProviderChoice.cs` `Parse` at about `:13` and `TextOf` at about `:22` (the review corrected these from `:14` and `:24`): add the opencode branch in both.
- `Core/Providers/SessionKey.cs:7` `SessionKey(ProviderId, string)`; `Parse` at `:32`.
- `Core/Copilot/CopilotTerminalOpener.cs:12` (the template; `CmdWrap` in `CommandLineRules.cs`).
- `Core/Copilot/CopilotSetupCheck.cs:61` `RunAsync(...)`: the version-read pattern, with the two-runner shape.
- `Core/Copilot/CopilotHistorySource.cs:32` the class line; `:44` `ListAsync`; `:48` `ListDetailedAsync`; the counters `SkippedFiles`, `SkippedLines`, `EmptySessions`.
- `Core/Claude/CliRunner.cs:59,89-90`: the process runner redirects stdin only when `standardInput` is not null. OC-D10 passes an empty string.
- Copilot's resume window (`App/SessionWindow.CopilotResume.cs:84-92`) recreates a missing folder (the owner's rule of 2026-11-22); the opencode window follows it.
- Scope of the App change, recounted on 2026-11-22: **48 App source files** name the Copilot provider in some way (18 with `ProviderId.Copilot`), and **9 Core files** outside `Core\Copilot\` do. The plan's earlier figure of 23 was wrong. OC2 lists each file.
- Database facts used (research §6, VERIFIED from the program's code): `session(id, parent_id, directory, title, time_created, time_updated, time_archived)`, `message(id, session_id, time_created, data)`, `part(id, message_id, session_id, time_created, data)`, `project(id, worktree)`. `project` is not used by this version.

## 6. Existing-behaviour map (file level)

| Template file | New file | Copy / new / not copied |
|---|---|---|
| `Core/Copilot/CopilotProvider.cs` | `Core/OpenCode/OpenCodeProvider.cs` | copy, renamed |
| `Core/Copilot/CopilotHistorySource.cs` | `Core/OpenCode/OpenCodeHistorySource.cs` | new reader (read-only SQLite), the same list shape |
| `Core/Copilot/CopilotTranscriptReader.cs` | `Core/OpenCode/OpenCodeTranscriptReader.cs` | new (OC-D5) |
| `Core/Copilot/CopilotTerminalOpener.cs` | `Core/OpenCode/OpenCodeTerminalOpener.cs` | copy, renamed; new commands (OC-D8, OC-D9) |
| `Core/Copilot/CopilotSetupCheck.cs` | `Core/OpenCode/OpenCodeSetupCheck.cs` | copy, renamed; version from `package.json` (OC-D1) |
| `Core/Copilot/CopilotExit.cs`, `CopilotStartLauncher.cs` | `Core/OpenCode/*` | copy the start and exit rules |
| `Core/Copilot/CopilotSummarizer.cs` | `Core/OpenCode/OpenCodeSummarizer.cs` | new (`opencode run`, stdin, empty `--dir`; OC-D10); not run |
| `App/SessionWindow.Copilot*.cs` (about 8 files) | `App/SessionWindow.OpenCode*.cs` | copy, renamed |
| `App/CopilotResumePrompt.xaml(.cs)` | `App/OpenCodeResumePrompt.xaml(.cs)` | copy, renamed; the cost lines are the opencode ones (OC-D9, OC-D10) |
| The 48 App files and 9 Core files that name Copilot | the same files | branches only, added (OC2) |

## 7. Stages

- **OC0 Local checks (no model call, no process, no write).**
  - (a) The session id shape: read the program's id generator in the embedded JavaScript of `opencode-windows-x64\bin\opencode.exe` (grep, not run). Record the prefix and the alphabet here.
  - (b) The TextPart shape and the `synthetic` flag (grep of the program's message module).
  - (c) What `run` may do without the permission flag in an empty folder: read the program's permission code (grep). Record the answer.
  - (d) The package version read path (the `package.json` beside the shim).
  - Gate: each of (a) to (d) is VERIFIED from the code, or is written as an owner question.
- **OC1 Core.** `ProviderId.OpenCode`, `ProviderChoice`, the history source, the transcript reader, the terminal opener, the setup check, the provider class, `Microsoft.Data.Sqlite` (read-only). Tests on synthetic fixture databases built in a temp folder, with the v1 tables (no owner data). The read-only guarantee is tested: the statement list, `query_only`, and unchanged bytes of `opencode.db`, `-wal` and `-shm` after a full read. Also a WAL database fixture (the `-wal` file present). Gate: Core tests pass on net10.0 and net11.0 (before and after counts recorded); the Claude, Codex and Copilot tests are unchanged; the native SQLite engine works on both targets (a test that opens a fixture on each).
- **OC2 App.** The rail, search, transcript tab, row menu, exit watcher, setup row, the provider choice, the scratch and start-file cleanup in the window that holds the state lock, the rail's schema line (OC-D7). Gate: App tests pass on both full targets (before and after counts recorded); the existing tests unchanged, except where a test names a provider list that now has a fourth provider (named in the report).
- **OC3 Resume screen.** Full resume (terminal). Last turns: a start from text (OC-D9), with its own pre-Start cost line that says it makes a model turn. The summary path (OC-D10) is built, with a fake runner in the tests, **not run**, and **disabled until OC-O4 is decided**. The cross-platform targets are wired once the start route exists. Gate: tests with fakes only; no test starts a process; the cost-line strings are tested (no "No model call" text on the opencode start).
- **OC4 Review.** A Sonnet reviewer over OC0 to OC3: privacy (no path, id, title or message in a log, a file name, or a command line); the read-only guarantee; the permission flag never passed; stdin never inherited; no model call in any test; the Claude, Codex and Copilot behaviour unchanged. Dispositions in the plan.
- **OC5 Licence and commit.** `THIRD-PARTY-NOTICES.md` for Microsoft.Data.Sqlite and its native engine (licence read from the packages, not assumed). The four suites on the committed tree. A personal-data scan that stops the push on any hit. Push on the owner's go-ahead.
- **Summary (last).** Enabled only when OC-O4 is decided. We do not run a summary on the owner's behalf.

## 8. Risks

- **R1 Shared database.** The owner's opencode builds share one database. The reads show what 1.4.3 wrote; the newer tables are empty here. Mitigation: the rail's schema line (OC-D7).
- **R2 Read-only with the WAL.** Reads while opencode writes: the `-wal` and `-shm` files must be accessible to a read-only connection, and a long read transaction can hold back a checkpoint. Not verified on the owner's machine: it is checked at the owner's screen check, not by a test. Mitigation: busy timeout; a status line if the database cannot be opened; no `immutable=1`.
- **R3 A start from text makes a model turn** (OC-D9, OC-O5). Mitigation: the pre-Start cost line.
- **R4 The summary costs money and writes a row** (OC-D10, OC-O2, OC-O4). Built last, disabled until the owner decides.
- **R5 Schema drift.** A later opencode build changes the tables. Mitigation: the schema guard (OC-D7) and the version warning (OC-D1).
- **R6 A new dependency.** Its licence and native engine are checked at OC1 and OC5, not assumed.
- **R7 A shim that is not the npm copy.** The setup check names the copy (OC-D17).

## 9. Review disposition (plan review, v1, 2026-11-22)

| Review item | Disposition |
|---|---|
| Must 1: the transcript on a command line; the summary's `--dir` gives tools in the owner's project | Fixed: OC-D10 (stdin via pwsh; empty scratch `--dir`; OC0(c) checks the permission behaviour before the summary is enabled). |
| Must 2: `run` writes a session row into the owner's database | Fixed by decision: OC-O4 (owner decides; the summary is off until then). |
| Must 3: the version read may start a process that writes | Fixed: OC-D1 reads `package.json`; no process. |
| Must 4: the Last-turns cost line says "No model call" | Fixed: OC-D9 and OC3 (a provider-specific line, tested). OC-O5 asks the owner. |
| Must 5: the relative `OPENCODE_DB` join stated as VERIFIED | Fixed: OC-D2 marks it ASSUMED and refuses a relative value. |
| Should: read-only is more than "no write verb" | Fixed: OC-D2 (`Mode=ReadOnly`, `query_only`, statement list, unchanged-bytes test); R2. |
| Should: the id shape and a leading `-` | Fixed: OC-D8 (opencode's own check; the shared `CommandLineRules` unchanged). |
| Should: message and part order, synthetic parts, the cap | Fixed: OC-D5. |
| Should: the schema check and the rail line | Fixed: OC-D7; OC2 adds the rail line. |
| Should: parity table gaps | Fixed: section 4 now lists each Copilot item. |
| Should: the file count (48 App, 9 Core, not 23) | Fixed: section 5. |
| Should: the shims | Fixed: OC-D17. |
| Should: the start-turn model call as an owner item | Fixed: OC-O5. |
| Should: stdin inheritance (`CliRunner.cs:59,89-90`) | Fixed: OC-D10 (an empty string is passed). |
| Should: native SQLite on both targets | Fixed: OC-D15 and OC1's gate. |
| Note: the `ProviderChoice` line numbers | Fixed: section 5. |
| Note: "like the CLI's own list" | Fixed: OC-D3. |
| Note: `project` never used | Fixed: section 5 says so. |
| Note: OC-D14 is the exception type only | Fixed: OC-D14. |
| Note: OC5's "scan" undefined | Fixed: OC5 names the scan and the stop-on-hit. |

## 10. OC0 results (2026-11-22)

Read from the program's code (`opencode-windows-x64\bin\opencode.exe`, grep only; nothing run; no database or session content opened). Notes kept outside the repository.

- **Session id (VERIFIED):** prefix `ses`; the program checks only that prefix. The generated shape is `ses_` + 12 lowercase hex + 14 base62 (30 characters), with a time part. Derived check: `^ses_[0-9a-f]{12}[0-9A-Za-z]{14}$` (OC-D8).
- **Text part (VERIFIED):** fields `id`, `sessionID`, `messageID`, `type` ("text"), `text`, and optional booleans `synthetic` and `ignored`. The program's own transcript skips `synthetic` and keeps `ignored`; the reader does the same (OC-D5).
- **`run` without the permission flag (VERIFIED):** stdin is read to the end when it is not a terminal and appended to the message; an empty message is exit 1. Defaults allow reads, edits and shell inside the folder, and reject paths outside it. Exit 0 on success; exit 1 on the listed failures. **A session error is printed to stderr and does not change the exit code**, so an empty stdout is a failure whatever the exit code says (OC-D10).
- **Package (VERIFIED):** `version` "1.4.3" in the package file; `bin` `opencode` at `./bin/opencode`; the native binary is in the optional `opencode-windows-x64` package. The npm folder holds `opencode`, `opencode.cmd` and `opencode.ps1` (OC-D1, OC-D17).

Decisions changed by these results: OC-D5 (ignored parts kept), OC-D8 (the derived id check), OC-D10 (summary with every tool denied through the inline configuration, empty output is a failure).
Still not verifiable without running: the real exit codes, the effective permissions under the owner's configuration and stored approvals, and whether the stdin read blocks in practice.
