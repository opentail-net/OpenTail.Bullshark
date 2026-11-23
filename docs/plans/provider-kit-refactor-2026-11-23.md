# Provider kit refactor: one implementation of the shared parts, before the next provider (plan, 2026-11-23)

Status: **R0 to R7 built and committed (2026-11-23); R8 and R10 are open; R9 (Claude) needs its own plan.** Owner's direction (2026-11-23): refactoring before the next provider is accepted, because the later cost is higher. Basis: the duplication measurement of 2026-11-23 (Copilot files against their Codex templates, after renaming), and the start-script defect found in the **Codex** launcher (the Copilot launcher already runs Set-Location before Remove-Item). Claude's behaviour does not change in this plan.

## Goal

Each piece that every CLI needs is written once: the start launcher, the summary inputs and wording, the process run, the folder rules for a command line, the resume text, and the App's rail grouping, search and transcript view. A provider brings only what is specific to its CLI. A provider that needs a different route (a file-reading CLI, a stdin CLI, an HTTP CLI) uses the pieces it fits and writes its own for the rest. Nothing is forced through a base class.

## Non-goals

- Claude's code. Its rail, supervisor and launch are a different design. A later plan, with characterisation tests, decides that (R9).
- Any change of behaviour in Codex or Copilot, except the defect this plan names (the Codex launcher's script order and shim check, R0).
- New providers, new UI, new features.

## Contracts this must keep (do not change their members)

- Core/Providers: `IHistorySource`, `ITranscriptReader`, `ITerminalOpener`, `ISupervisedLauncher`, `ILiveSource` (Capabilities.cs); `ProviderId`, `ProviderChoice`, `ProviderPaths`, `SessionKey`, `SessionSummary`, `ProviderFeatures`.
- Core: `ICliRunner.RunAsync` and `ProcessCliRunner` (Claude/CliRunner.cs). Shared with Claude: no change to its code.
- Core/Resume: `SummaryFiles`, `ResumeText` (Claude's). Read them; do not change them.
- Behaviour: Codex and Copilot test counts must not fall; each existing assertion stays, except where a stage says it moves with the new shared code.

## Stages

R0 to R7 are built and committed. Their text is in [Done/provider-kit-refactor-2026-11-23-done.md](Done/provider-kit-refactor-2026-11-23-done.md), with the measured basis that led to them.

- [ ] **R8. Review and the playbook.** A Sonnet review of R1 to R7 as one diff range. Dispositions go in its review log and in this plan. Update `docs/plans/provider-onboarding-playbook.md`: the kit's pieces, the rule that a new provider uses them where they fit, and the check that a fourth CLI fits the seam on paper before any piece is added.

- [ ] **R10. Codex tab close and rail row, unified (owner, 2026-11-23: "sooner or later").** Two pieces stayed per provider in R7: the Codex tab close (`SessionWindow.Tabs.cs`, a Claude-side file, so the stage that touches it needs Claude's close path characterised first) and the Codex rail row (`SessionWindow.Codex.cs` rows, about 25 lines, with a different rail kind, title wording and resume call). Plan: characterise Claude's close and row behaviour with tests; then move the close and the row into the provider-neutral helpers of R7, with each provider's wording and actions as parameters; byte-identical strings; no Claude behaviour change. Scheduled after the continue feature.

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
