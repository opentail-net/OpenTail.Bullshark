# Provider kit refactor: one implementation of the shared parts, before the next provider (plan, 2026-11-23)

Status: **plan, nothing refactored yet.** Owner's direction (2026-11-23): refactoring before the next provider is accepted, because the later cost is higher. Basis: the duplication measurement of 2026-11-23 (Copilot files against their Codex templates, after renaming), and the start-script defect found in the **Codex** launcher (the Copilot launcher already runs Set-Location before Remove-Item). Claude's behaviour does not change in this plan.

## Goal

Each piece that every CLI needs is written once: the start launcher, the summary inputs and wording, the process run, the folder rules for a command line, the resume text, and the App's rail grouping, search and transcript view. A provider brings only what is specific to its CLI. A provider that needs a different route (a file-reading CLI, a stdin CLI, an HTTP CLI) uses the pieces it fits and writes its own for the rest. Nothing is forced through a base class.

## Non-goals

- Claude's code. Its rail, supervisor and launch are a different design. A later plan, with characterisation tests, decides that (R9).
- Any change of behaviour in Codex or Copilot, except the defect this plan names (the Codex launcher's script order and shim check, R0).
- New providers, new UI, new features.

## Measured basis (2026-11-23, line-level diff after renaming Copilot to Codex)

| File pair | Lines (Copilot) | Differing lines after rename | Reading |
|---|---|---|---|
| App `RailGroups` | 83 | 0 | identical: share |
| App `Search` | 39 | 0 | identical: share |
| App `SearchView` | 109 | 0 | identical: share |
| App `TranscriptView` | 215 | 0 | identical: share |
| App `TranscriptText` | 48 | 3 | near-identical: share |
| App `ResumeRules` | 137 | 64 | differs in wording and modes: parameterise |
| App `ResumePrompt.xaml.cs` | 356 | 80 | parameterise (R6) |
| App `SessionWindow.*` glue | 290 to 501 | 38 to 490 | different: share helpers only (R7) |
| Core `ResumeText` | 95 | 13 | near-identical: share |
| Core `SummaryText` | 38 | 21 | wording only: share with the provider name |
| Core `SummaryInput` | 52 | 43 | same cap and cut logic: share |
| Core `StartLauncher` | 162 | 63 | same script shape, same defect: share (R1) |
| Core `TerminalOpener` | 62 | 66 | same folder rules, different command: share the rules (R3) |
| Core `Summarizer` | 122 | 138 | different route (Copilot: scratch and file; Codex: stdin, session folder): share the run and the cap, not the route (R2) |
| Core `SetupCheck` | 84 | 69 | different checks: share the PATH lookup only |

About 480 App lines are identical and about 150 Core lines are near-identical. The rest is each CLI's own route.

## Contracts this must keep (do not change their members)

- Core/Providers: `IHistorySource`, `ITranscriptReader`, `ITerminalOpener`, `ISupervisedLauncher`, `ILiveSource` (Capabilities.cs); `ProviderId`, `ProviderChoice`, `ProviderPaths`, `SessionKey`, `SessionSummary`, `ProviderFeatures`.
- Core: `ICliRunner.RunAsync` and `ProcessCliRunner` (Claude/CliRunner.cs). Shared with Claude: no change to its code.
- Core/Resume: `SummaryFiles`, `ResumeText` (Claude's). Read them; do not change them.
- Behaviour: Codex and Copilot test counts must not fall; each existing assertion stays, except where a stage says it moves with the new shared code.

## Stages (each ends with both targets green, a local commit, and counts recorded)

**R0. Codex launcher defect (fix in the working tree, 2026-11-23; not yet committed). Entry condition for R1: R0 committed.** Script order: Set-Location before Remove-Item. Shim check: `CodexStartLauncher.FindShim` and a refusal before the start file is written (`CodexResumeRules.NoShimOnPath`). Tests updated for the new order and the shim. Gate: Core and App counts up by the new tests only.

**R1. One start launcher. Done 2026-11-23 (committed with this note).** Core.Tests 2947 (net10.0) and 2961 (net11.0), App.Tests 268 on each full target, all exit 0. The script text of both providers is byte-identical to before: 588 command lines compared (14 folders x 14 prompts x 3 variants). Deviations: `Get-Command` keeps the bare name (`codex`, `copilot`) as before, and the message keeps the shim's file name; the path checks run inside the shared builder; the Codex model check runs before the path checks; `SingleQuoteChars` is a public string. New `Core/Providers/CliStartLauncher.cs`: the script builder (order fixed once), `FindPwsh`, `FindShim(shimName)`, `WriteStart`, `StartsFolderBeside`, `RemoveOldStarts`, the quoting helpers and the path checks. The caller passes the final call as a string, so the launcher does not know which CLI it starts: Codex passes `& $shim.Source -m '<model>' $prompt` (or without `-m`), and Copilot passes `& $shim.Source -i $prompt --sync-session=off`, so the flag stays on every Copilot line. Entry condition: R0 committed. `CodexStartLauncher` and `CopilotStartLauncher` become thin wrappers: their shim name, their argument builder and their model handling (Codex's `-m`). Tests: a shared test file for the script (order, quoting with the typographic quotes, paths with spaces, `%`, `&`, `$`, the missing-shim line), the cleanup rule, and the shim search; each wrapper keeps its own argument test. Gate: no existing assertion removed; counts up by the shared tests.

**R2. Shared summary pieces. Done 2026-11-23 (committed with this note).** Core.Tests 3022 on each target (2993 before: 29 new shared tests, no existing one changed), App.Tests 269 on each full target, all exit 0. The level texts were checked byte for byte: a temporary dump of every text before and after, compared with `cmp`, identical (the temporary test and its files were deleted). Shared: the cap, the cut, the notice and the join (`Providers/Summary/SummaryInputBuilder.cs`); the level texts, with the CLI phrase as a parameter (`SummaryInstructions.cs`); the run sequence (`CliProcessRun.cs`). Kept per provider: the rendering of messages, the phrase, the failure type (the App catches `CodexSummaryException` and `CopilotSummaryException` by type), the stdin choice, the data line and prompts, and the Copilot scratch folder. Open: `CliProcessRun.TimedOutMessage` is fixed at "five minutes", matching both providers; a provider with another timeout needs that message as a parameter. New `Core/Providers/Summary/`:
- `SummaryInputBuilder` (the cap of 120,000 characters, the oldest text cut first, the notice, the render of messages): replaces `CodexSummaryInput` and `CopilotSummaryInput`, which keep their names as thin wrappers or are removed where nothing else uses them.
- `SummaryInstructions.For(level, cliPhrase)`: the three levels and their word limits. The shared function produces the level text; `CodexSummaryText.Instructions` and `CopilotSummaryText.FileText` stay as the public entry points and return **byte-identical** strings to today (their tests pin the exact text). `cliPhrase` is the full phrase the text uses: "Codex CLI session" and "GitHub Copilot CLI session". The two files differ in 21 lines, so the parameter is not a name swap.
- `CliProcessRun`: run a command with a timeout and cancellation, read the exit code and the output, and reject an empty reply. It takes a factory for the failure, so each provider keeps its own exception type: the App catches `CodexSummaryException` and `CopilotSummaryException` by type (`SessionWindow.CodexResume.cs:178,182`; `SessionWindow.CopilotResume.cs:176,181`), and those catches are not edited. The failure text names the exit code only.
- The route stays per provider: Codex keeps stdin and its session folder; Copilot keeps the scratch folder with its marker, `work`, `config` and `log` (G-D2, G-D15). The scratch folder code stays Copilot's, unless Codex later needs it.
- Gate: the existing summary tests pass unchanged, including the exception-type and exact-text assertions; the App's exception catches are not edited.

**R3. Shared command-line rules. Done 2026-11-23 (committed with this note).** Core.Tests 2993 on each target (2965 before), App.Tests 269 on each full target, all exit 0 (the App net11.0 run once hit the known `RunGuardTests` flake, counting claude.exe processes of other sessions; rerun passed). Deviations: `FolderOption` was added (the `-C "<folder>"` form, with the folder check inside it); `CopilotTerminalOpener.RequireFolder` was removed, not kept as a wrapper, since no test called it and Core has no InternalsVisibleTo; Codex's resume shape check was found identical to Copilot's and now calls the shared one. Done **before R2**, because R2's summarizers call the folder rule. `Core/Providers/CommandLineRules.cs`: `RequireFolder` (no double quote, no percent sign, no exclamation mark, no control character, no trailing backslash), `CmdWrap(program, args)` where the program goes through `cmd.exe /d /c`, and the session-id UUID shape used by command lines. Codex's opener, Copilot's opener and `CopilotSummarizer` (which calls `CopilotTerminalOpener.RequireFolder`, internal) change their calls. Owner of the checks: the command line keeps the UUID shape check; the readers keep their own path-safety checks (`CopilotFiles.RequireSafeId`), which are not moved. Gate: the existing opener and summarizer tests pass unchanged.

**R4. Shared resume text. Done 2026-11-23 (committed with this note).** Core.Tests 3040 on each target (3022 before: 18 new tests, no existing test changed), App.Tests 269 on each full target. Byte identity: every public entry point of both classes was dumped before and after over 8 synthetic line sets, with turn counts, cap pairs, levels and session ids; the dumps (38,836,487 bytes each) compared identical. Moved to `Core/Resume/ProviderResumeText.cs`: `SplitTurns`, `Tail`, `Cap`, `Joined`, `Render`, `RenderLine`, and `Compose` (the header takes the CLI's full phrase). Deviations: `Tail` and `Render` take the transcript kind word `assistant` as a parameter (both CLIs use the same word, so it is one value today); the shared class is public, since Core has no InternalsVisibleTo. Kept per provider: the public entry points and their defaults, and the phrases "Codex CLI" and "GitHub Copilot CLI". New file `Core/Resume/ProviderResumeText.cs`. Claude's `Core/Resume/ResumeText.cs` is read only and is not edited. The helpers duplicated in both resume-text classes are named and move into the new file: `SplitTurns`, `Tail`, `Cap`, `Joined` and `Render`. The header takes the full phrase ("the Codex CLI" or "GitHub Copilot CLI"), because the two headers differ in wording, not only in the name. `CodexResumeText` and `CopilotResumeText` keep their public entry points and call the shared code. Gate: their tests pass unchanged.

**R5. App: shared rail, search and transcript view.** New `App/Providers/`: `ProviderRailGroups` (replaces `CodexRailGroups` and `CopilotRailGroups`), `ProviderSearch` and `ProviderSearchView` (replace the two copies), `ProviderTranscriptText` and `ProviderTranscriptView` (replace the two copies, with the provider label as a parameter). The TabKinds stay per provider; only the classes they show are shared. The window partials `SessionWindow.CodexSearch.cs` / `SessionWindow.CopilotSearch.cs` (38 differing lines) and `SessionWindow.CodexTranscript.cs` / `SessionWindow.CopilotTranscript.cs` (39) are **not** changed in R5; R7 decides whether any of their glue moves. Gate: both App targets green and counts unchanged. The view classes (`SearchView`, `TranscriptView`) have no tests, so their gate is a build plus a read of the diff against both originals; a smoke test is added only where a pure helper exists.

**R6. App: resume rules and dialog.** `ProviderResumeRules` (modes, levels, turns, the messages) takes a wording record (the CLI name, the cost line, the status texts). `CodexResumeRules` and `CopilotResumeRules` become wording records plus the shared rules. The dialog XAML stays per provider (its layout may differ); its code-behind calls the shared rules. Gate: the resume tests pass.

**R7. App: window helpers.** Only the parts that are the same in both window partials: the terminal exit watch (`WatchProviderExit`), the row-menu builder for a provider's rows (the items, their order and their labels as parameters), and the tab open for a provider terminal. Each partial keeps its own actions. The Core exit classification (`CopilotExit`, 16 of 41 lines differ, and it already reuses `CodexExit`'s record) is left as it is. Gate: App counts unchanged; a manual read of the diff against both partials.

**R8. Review and the playbook.** A Sonnet review of R1 to R7 as one diff range. Dispositions go in its review log and in this plan. Update `docs/plans/provider-onboarding-playbook.md`: the kit's pieces, the rule that a new provider uses them where they fit, and the check that a fourth CLI fits the seam on paper before any piece is added.

**R9. Claude (not in this plan).** Claude's launch, summary, rail and search would move to the kit only after characterisation tests describe its current behaviour. That needs its own plan and the owner's decision.

## Risks

- **Over-abstraction.** The pieces are shared only where the two CLIs already match (measured above). Where they differ, the route stays per CLI.
- **Silent behaviour change.** The gates are counts plus unchanged assertions. A moved assertion must be named in the commit.
- **Claude.** No Claude file changes in R0 to R8. Shared files that Claude also reads are read only: `Core/Claude/CliRunner.cs`, `Core/Resume/ResumeText.cs`, `Core/Resume/SummaryFiles.cs`, `App/SessionWindow.Search.cs` and `App/SessionWindow.xaml.cs`. The App window files are not edited by R5 or R6; R7 may touch only the partial files of Codex and Copilot.
- **Stage order.** R0, then R1 (entry: R0 committed), then R3, then R2 and R4 (both use R3), then R5 and R6, then R7, then R8.
- **Test target names.** App tests use the full target names (the short name runs zero App tests and exits 0).

## Checklist for each stage (the gate)

- [ ] Both Core targets green; counts recorded before and after.
- [ ] Both full App targets green; counts recorded before and after.
- [ ] No existing assertion removed without its move named in the commit.
- [ ] No Claude file changed.
- [ ] Local commit with the attribution lines; not pushed.
