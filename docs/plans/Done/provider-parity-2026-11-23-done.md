# Provider parity (2026-11-23) (done items)

> Split from [provider-parity-2026-11-23.md](../provider-parity-2026-11-23.md) on 2026-11-23. These items were finished when they moved; the open items stay in the plan. The decisions are in the plan.

## Checklist (done items)

- [x] PP-D1: verified in code and by the suites on net10.0 and net11.0. Status doc and README corrected.
- [x] PP-D2: `CopilotBundles` and the setup-check acceptance, with tests (`CopilotBundlesTests`); `CopilotTargetEnabled = true`; the chooser and cross-start tests updated to the enabled rules.
- [x] PP-D3: Codex refusal in `ResumeCodexAsync`. Not unit-tested (the window is not under test); checked by reading and by the build.
- [x] PP-D4: `IMessageTextSource` on Codex, Copilot and opencode; `MessageTextCache`; the matcher overload; the three windows. Tests: `MessageTextSearchTests` (matcher and cache), `CopilotMessageTextsTests` (provider text, Copilot only). Codex and opencode `MessageTextsAsync` are not unit-tested; they reuse the transcript readers' code paths.
- [x] PP-D5: `CopilotUsageReader` with `CopilotUsageReaderTests` (5 tests); `LimitsView.ShowCopilot`; the window reads it in `RefreshLimitsAsync`.
- [x] Docs: README provider rows and bullets; `00-current-work.md` R8 note.

## Verification log (2026-11-23, this checkout)

Build: `dotnet build` of the App for net10.0-windows10.0.19041.0: 0 errors.

| Suite | Target | Result |
|---|---|---|
| OpenTail.Bullshark.Core.Tests | net10.0 | 3367 passed, 0 failed, exit 0 |
| OpenTail.Bullshark.Core.Tests | net11.0 | 3367 passed, 0 failed, exit 0 |
| OpenTail.Bullshark.App.Tests | net10.0-windows10.0.19041.0 | 681 passed, 0 failed, exit 0 |
| OpenTail.Bullshark.App.Tests | net11.0-windows10.0.19041.0 | 681 passed, 0 failed, exit 0 |

Counts before this work: Core 3355 on each target and App 675 on each (from the earlier round). New tests: Core +12 (6 Copilot setup and bundle, 1 message text, 5 usage), App +6 (message search).
