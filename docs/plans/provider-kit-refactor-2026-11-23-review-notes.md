# Review notes: provider kit refactor plan (2026-11-23)

Reviewer: Sonnet. Plan: `docs/plans/provider-kit-refactor-2026-11-23.md`. Read against the code on disk.

## Checks that passed
- **Measured table:** re-measured 14 rows (Copilot normalised to Codex, CR stripped, `diff -w`, counting `<`/`>` lines). All match: RailGroups, Search, SearchView, TranscriptView 0; TranscriptText 3; ResumeRules 64; ResumePrompt.xaml.cs 80; ResumeText 13; SummaryText 21; SummaryInput 43; StartLauncher 63; TerminalOpener 66; Summarizer 138; SetupCheck 69. Line counts match too. SessionWindow range "38 to 490" matches (Search 38, Transcript 39, Resume 192, main 490).
- **Contracts:** every named member exists in the named file (Capabilities.cs holds IHistorySource, ITranscriptReader, ITerminalOpener, ISupervisedLauncher, ILiveSource; ProviderId, ProviderChoice, ProviderPaths, SessionKey, SessionSummary, ProviderFeatures; ICliRunner and ProcessCliRunner in Claude/CliRunner.cs; SummaryFiles, ResumeText in Core/Resume).
- **Route claims (R2):** Codex summarizer uses stdin plus the session folder; Copilot uses the scratch folder (work, config, log). Correct.
- **R3 rule list:** Codex inlines the same five checks (incl. `!`) in `NewCommandLine`, so sharing `RequireFolder` does not change Codex behaviour.

## Must fix
1. **Plan line 3 and R1 (line 48) are wrong about the defect.** `CopilotStartLauncher.CommandLine` (Copilot/CopilotStartLauncher.cs:~66-68) already does Set-Location before Remove-Item, and already has `FindShim`. Only Codex had the wrong order and no shim check. Fix: say "defect found in the Codex launcher; Copilot already has the right order", and drop "the order fix in R1" from Non-goals (line 12) since R0 owns it.
2. **R2 changes public exception types without saying so.** `CliSummaryException` replaces `CodexSummaryException` and `CopilotSummaryException`. These are caught by type in `App/SessionWindow.CodexResume.cs:178,182` and `SessionWindow.CopilotResume.cs:176,181`, and asserted in `CodexSummarizerTests.cs` and `CopilotSummarizerTests.cs`. The gate "route tests unchanged" cannot hold. Fix: list those four sites and two test files as edited in R2 (App catch sites are App code, outside R2's "Core" scope), or keep the two names as subclasses of a shared base.
3. **R2 gate counts the wrong thing for `SummaryInstructions.For(level, cliName)`.** Codex and Copilot wording differ in more than the name (Copilot's API is `FileText(level)`, Codex's is `Instructions(level)`; 21 differing lines). Fix: state that the exact strings are pinned by the existing tests and must be byte-identical, and name `CopilotSummaryText.FileText` and `CodexSummaryText.Instructions` as the callers.
4. **R4 shares less than the duplication.** `CodexResumeText` and `CopilotResumeText` also duplicate `SplitTurns`, `Tail`, `Cap`, `Joined`, `Render`/`RenderLine`. The plan's `ProviderResumeText` covers only compose. Also the header differs ("Codex CLI" vs "GitHub Copilot CLI"), so the parameter is the full header phrase, not "CLI name". Fix: list the helpers and say which move.

## Should fix
1. **Missing duplicate (Core):** `CodexExit.cs` vs `CopilotExit.cs` (41 lines, 16 differing after rename) is not in the table or any stage. Add it, or name it as out of scope. Its twin App watcher (`SessionWindow.CopilotExit.cs`, copy of `WatchCodexExit` in `SessionWindow.Codex.cs`) is covered by R7's "exit watch".
2. **Missing duplicates (App):** `SessionWindow.{Codex,Copilot}Search.cs` (100/82 lines, 38 diff) and `...Transcript.cs` (97/78, 39 diff) glue are in no stage. R5 moves the classes they call, so these files must be edited. Add them to R5 or R7.
3. **Shared window files:** `SessionWindow.Search.cs` (lines 50-51, 79-85) and `SessionWindow.xaml.cs` (268-269) are shared with Claude and call the per-provider search methods. Say they change only if call sites are renamed, and otherwise read-only.
4. **R5 gate is not checkable for SearchView and TranscriptView:** no test references `CodexSearchView`, `CopilotSearchView`, `CodexTranscriptView` or `CopilotTranscriptView` (grep of tests/). "Tests pass with names changed" is vacuous for them. Fix: gate on build plus a manual diff read, or add a smoke test.
5. **R1 argument function:** the two scripts differ in the call (`-m model $prompt` vs `-i $prompt --sync-session=off`) and Copilot's `SyncOff` constant lives on `CopilotTerminalOpener`. Say the shared builder takes the final call line as a string, so `--sync-session=off` stays in every Copilot line.
6. **R3:** `CopilotTerminalOpener.RequireFolder` is `internal` and is called by `CopilotSummarizer`; the shared one must be reachable from both. Also the session-id shape regex exists in both openers and also `CodexTranscriptReader.RequireSafeId` and `CopilotFiles.RequireSafeId` (a separate id check, not the same rule): say which is shared.
7. **R0 state:** the plan says R0 is in progress; the working tree already shows an uncommitted `CodexStartLauncher.cs` and its test file changed. R1 depends on R0 being committed first; add that as R1's entry condition.
8. **R4 wording:** "`Core/Resume/ResumeText.cs` ... Add `ProviderResumeText`" reads as an edit to Claude's file. Say a new file, `Core/Resume/ProviderResumeText.cs`.

## Claude files
No stage edits a Claude file. Shared and read-only: `Claude/CliRunner.cs`, `Resume/ResumeText.cs` (Compose is Claude's; CutMiddle, Cap constants are reused), `Resume/SummaryFiles.cs`, `SessionWindow.Search.cs`, `SessionWindow.xaml.cs`. The plan lists the first three only; add the two window files. Not duplicated: `ToolVersionProbe.cs` references Codex command lines and is Claude-adjacent; it is not touched.

## Order of stages
R0 then R1 correct. R2, R3 independent. R2's Copilot summarizer calls `CopilotTerminalOpener.RequireFolder`, which R3 moves: R3 before R2, or R2 keeps the old call. R4 and R5 independent. R6 needs nothing from R5. No stage depends on a later one except that R2-R3 point.

## Behaviour changes the plan does not say
- Exception type change (Must fix 2).
- Copilot's `CopilotSummaryInput.MaxInputChars` is a public const referenced by `CopilotSummarizer.MaxInputChars`; Codex's is the reverse. Keep both const names if tests use them.

## Not checked
Test counts and green status (no test run). App XAML and the contents of the Codex/Copilot `SessionWindow` partials beyond sizes and diff counts. `ProviderResumeRules` wording differences (64 lines). The Test-*Resume.ps1 scripts for references to renamed classes.

Notes count: 2 (minor wording items under should-fix 8 and the const-name remark).
