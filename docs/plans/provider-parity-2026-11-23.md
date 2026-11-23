# Provider parity (2026-11-23)

Goal: Claude Code, Codex, Copilot and opencode offer the same features in the app, except the features that need Claude's supervisor (live status, restarts, needs-you from Claude's hooks). The owner asked for parity and asked the orchestrator to settle open choices and build, not to stop for answers. AFK mode: each choice below is recorded with its reason.

## Owner direction (2026-11-23)

- Codex on net10.0: "we need to do it". (Built already; see PP-D1.)
- Message-text search for Codex, Copilot and opencode: do it now.
- Copilot: use 1.0.104-3 for now.
- Missing saved session on resume: settle it.
- Copilot usage report (G5.2): do it.
- "Do as much as possible", "take a chance rather than build nothing".

## Decisions

- **PP-D1 Codex on net10.0: listed on both targets.** The earlier notes (C-D2 and O1 in `provider-codex-2026-11-21.md`, and R8 in `00-current-work.md`) said Codex is hidden on net10.0. The code already reads net10.0: `CodexProvider.Available` is true, `RolloutReader` decodes compressed rollouts with ZstdSharp.Port 0.8.8 on net10.0, and `ProviderTabTests.CodexIsAvailableOnThisTarget` asserts it. The stale notes are superseded by this record. Verified by the Core and App suites on both targets (below). Not checked: a live Codex session on net10.0 at the PC.
- **PP-D2 Copilot runs 1.0.104-3.** The WinGet `copilot.exe` is a launcher. Launched normally, it runs the newest completed bundle under `%LOCALAPPDATA%\copilot\pkg\win32-x64\<version>` (here 1.0.104-3). `copilot --version --no-auto-update` reports the launcher's bundled version, 1.0.34, which is why the setup check showed a flags warning. Built: `CopilotBundles.NewestVersion` finds the newest bundle with a `.extraction-complete` marker; `CopilotSetupCheck.RunAsync` (optional `bundleRoot`) turns a terminal row that is a Problem into Ok when that bundle has the flags; the app passes `CopilotBundles.DefaultRoot`. `ResumeTarget.CopilotTargetEnabled` is now true. Caveat, inferred and not run: the launcher's help lists `-C` and `--sync-session`, and the 1.0.104-3 folder is the newest bundle; a live session with the flags is still to be checked.
- **PP-D3 Missing saved session: refused.** When a Codex saved id is not found in its rollouts, the resume is refused with a status line and nothing opens. No picker tab, no newest-in-folder tab. Copilot and opencode already refuse when the id is not in the app's list. Built in `SessionWindow.Codex.cs` (`ResumeCodexAsync`). The ladder logic in `ResumeLadder.cs` is unchanged, so its tests still pass; its Picker and NewestInFolder steps are no longer reached from the app, and `resume.allowNewestInFolder` has no effect now. Follow-up: remove those steps and the setting, with their tests (about 60 references).
- **PP-D4 Message-text search for Codex, Copilot and opencode.** Built. Each provider exposes `MessageTextsAsync` through `IMessageTextSource` (Codex: `CodexTurns.ReadSession`; Copilot: `CopilotTranscriptReader.ReadSession` with no cap; opencode: a no-cap text read in `OpenCodeTranscriptReader`). `MessageTextCache` (App) keeps each session's texts keyed by session key, valid while the session's last activity time is the same; a failed read keeps an empty list for that activity time. `ProviderSearch.Matches` takes the texts as more fields. Each window runs the field search at once and then reads the missing sessions in the background; when the reads finish, the results are shown again only if the query is still the newest (version counter). Not in this round: a count of matching messages per row (Claude shows one).
- **PP-D5 Copilot usage report.** Built: `CopilotUsageReader` reads each session's last `session.shutdown` event (premium requests, per-model input and output tokens) and sums them per local day for the last `LimitHistory.DefaultDays` days. It is shown as its own table in the Limits view, captioned as Copilot's own unit. It is not added to the Claude or Codex token totals. Not cached: each Limits refresh reads the events files again.
- **PP-D6 Needs-you for Codex, Copilot and opencode: not built.** Claude's needs-you reads the hook files that Claude Code writes. The other three have no channel the app can read without guessing from transcripts. Permanent gap, with this reason.
- **PP-D7 Model list (Copilot `--acp`, Codex model dropdown): not built.** Needs the ACP protocol work, which is a separate plan.
- **PP-D8 Insights, Settings and tab close.** Defaults stand: Insights and Settings unchanged for the new providers; tab close as Codex (`CodexExit`), which Copilot and opencode follow.

## Checklist

- [x] PP-D1: verified in code and by the suites on net10.0 and net11.0. Status doc and README corrected.
- [x] PP-D2: `CopilotBundles` and the setup-check acceptance, with tests (`CopilotBundlesTests`); `CopilotTargetEnabled = true`; the chooser and cross-start tests updated to the enabled rules.
- [x] PP-D3: Codex refusal in `ResumeCodexAsync`. Not unit-tested (the window is not under test); checked by reading and by the build.
- [x] PP-D4: `IMessageTextSource` on Codex, Copilot and opencode; `MessageTextCache`; the matcher overload; the three windows. Tests: `MessageTextSearchTests` (matcher and cache), `CopilotMessageTextsTests` (provider text, Copilot only). Codex and opencode `MessageTextsAsync` are not unit-tested; they reuse the transcript readers' code paths.
- [x] PP-D5: `CopilotUsageReader` with `CopilotUsageReaderTests` (5 tests); `LimitsView.ShowCopilot`; the window reads it in `RefreshLimitsAsync`.
- [x] Docs: README provider rows and bullets; `00-current-work.md` R8 note.
- [ ] Docs in other plan files (`provider-codex-2026-11-21.md` C-D2 and O1; `provider-copilot-2026-11-22.md` G-O5, G-D12, parity rows 1, 19, 23; `provider-opencode-2026-11-22.md` section 11). Not edited here: those files have uncommitted changes from another session in this working tree. Update them when that work is committed.
- [ ] Live checks at the PC (owner): a Codex session on net10.0; a Copilot session through the WinGet launcher with the flags; the Copilot usage table with real days; the message search on a large history (time it).

## Verification log (2026-11-23, this checkout)

Build: `dotnet build` of the App for net10.0-windows10.0.19041.0: 0 errors.

| Suite | Target | Result |
|---|---|---|
| OpenTail.Bullshark.Core.Tests | net10.0 | 3367 passed, 0 failed, exit 0 |
| OpenTail.Bullshark.Core.Tests | net11.0 | 3367 passed, 0 failed, exit 0 |
| OpenTail.Bullshark.App.Tests | net10.0-windows10.0.19041.0 | 681 passed, 0 failed, exit 0 |
| OpenTail.Bullshark.App.Tests | net11.0-windows10.0.19041.0 | 681 passed, 0 failed, exit 0 |

Counts before this work: Core 3355 on each target and App 675 on each (from the earlier round). New tests: Core +12 (6 Copilot setup and bundle, 1 message text, 5 usage), App +6 (message search).

## Not checked

- No live run of any Copilot, Codex or opencode session in this round. The 1.0.104-3 behaviour and the usage numbers are from the files on this PC, not from a session started by the app.
- The message search has not been timed on a large history.

## Open follow-ups (recorded, not done)

- Remove the unreachable Codex ladder steps and `resume.allowNewestInFolder` (PP-D3).
- Needs-you (PP-D6) and the model list (PP-D7) for the non-Claude providers.
- A per-row count of matching messages (Claude shows one).
- Copilot usage: cache the events reads if the refresh is slow on a large history.
