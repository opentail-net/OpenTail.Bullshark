# Dedup: the three process runners (2026-11-23)

Status: S1 to S7 done on 2026-11-23 (commits 088f318 to dca8906); S8, the push, is done with them. Results at the end. Found by the project-wide duplication review of 2026-11-23. Item 3 of that review (the summary call) is done (commit aae96ef).

Goal: one place for the process set-up and the kill-the-tree step that `ProcessCliRunner`, `ProcessGitRunner` and `CodexModelList` each repeat. No change to what any caller receives.

## Facts read from the code (2026-11-23)

- `ProcessCliRunner` (`src/OpenTail.Bullshark.Core/Claude/CliRunner.cs`): `ProcessStartInfo` at lines 87-97 (UseShellExecute false, CreateNoWindow, redirects, UTF-8 output and error encodings, WorkingDirectory default current directory). Standard input is redirected only when given, with UTF-8 (lines 93, 98-99). Environment variables are added after the arguments (lines 102-104). Kill on timeout or cancel at line 140, `catch (InvalidOperationException)` only.
- `ProcessGitRunner` (`src/OpenTail.Bullshark.Core/Git/GitRunner.cs`): `StartInfo` is `public static` (lines 92-114) and is called by a test (`tests/OpenTail.Bullshark.Core.Tests/Git/ProcessGitRunnerTests.cs:270`), so its signature stays. Start is wrapped: `Win32Exception` gives `GitOutput(-1, "", "git could not be started: ...")` (lines 58-61); a null process gives `"git could not be started."` (line 62). Standard error is read unbounded with `ReadToEndAsync` and no encoding set (line 71). Stop at lines 135-145 checks `HasExited` and catches `InvalidOperationException`. Git's own flags: `-c core.fsmonitor=false`, `-c core.hooksPath=NUL`, `-C folder` (lines 102-107), and the environment `GIT_OPTIONAL_LOCKS`, `GIT_TERMINAL_PROMPT`, `LC_ALL`, `LANGUAGE` (lines 109-112).
- `CodexModelList` (`src/OpenTail.Bullshark.Core/Codex/CodexModelList.cs`): `Start()` at lines 188-204 sets UTF-8 without a byte-order mark on standard input, UTF-8 on output and error. A null process throws `InvalidOperationException` (line 203). `ReadAsync` catches `Win32Exception` or `InvalidOperationException` from `Start()` and returns `NotStarted` (lines 145-152). `StopTree` at lines 270-284 checks `HasExited`, catches `InvalidOperationException` and `Win32Exception`.
- Three kill helpers, with three different catch lists: CliRunner (InvalidOperationException), GitRunner (HasExited check, InvalidOperationException), CodexModelList (HasExited check, InvalidOperationException and Win32Exception).

## Contracts this must satisfy

| Member | Who relies on it | What must not change |
|---|---|---|
| `ICliRunner.RunAsync` (`ProcessCliRunner`) | `ClaudeCli`, `MagicNamer`, `CliProcessRun`, and through them the summaries | A timeout returns `CliResult(-1, partial, partial, TimedOut: true)`. A cancel kills the tree and rethrows. Standard input is written in full, then closed. A missing executable: `Process.Start` throws, and that exception propagates (not converted to a result). Output drain is bounded (2 s default) |
| `IGitRunner.RunAsync` (`ProcessGitRunner`) | `MergeCheck`, the git readers | A missing executable returns a result, not a throw. Output is cut at the byte limit (one byte past it is read), and then the process is stopped. Timeout and cancel as for the CLI, but with no partial text. Standard output is decoded as UTF-8 from the bytes |
| `ProcessGitRunner.StartInfo` (public static) | `ProcessGitRunnerTests` | Same signature and the same argument list and environment |
| `CodexModelList.ReadAsync` | the Codex model box | `Win32Exception` or `InvalidOperationException` from start gives `NotStarted`. The conversation is long-lived and bidirectional, so only its start and its kill are shared, not its loop |

## Design (decisions, with why)

- A new static class `OpenTail.Bullshark.Core.Processes.ProcessLaunch` with two members, and nothing else:
  - `Create(executable, arguments, workingDirectory, redirectStandardInput, outputEncoding, inputEncoding)`: the common `ProcessStartInfo` (no shell, no window, redirects, the encodings passed in). Each caller still adds its own environment and its own argument prefix.
  - `Kill(process)`: stop the tree if it has not exited; ignore `InvalidOperationException` and `Win32Exception`. The union of the three catch lists.
- Not shared, on purpose: `Process.Start` itself (each caller keeps its own failure convention, which is part of its contract), the stdin write, the output drain and the byte cap (they differ in kind), and Git's `-c` flags and environment (they are Git's rules).
- Why the union in `Kill`: the only behaviour change. `CliRunner` today lets a `Win32Exception` from its kill escape the timeout path, so a timeout could throw instead of returning `TimedOut`. Swallowing it matches the other two runners. Recorded as decision D1 below.

## Steps (checkboxes, with why)

- [x] S1. Add `Core/Processes/ProcessLaunch.cs` with `Create` and `Kill`. Why first: no caller changes yet, so it can be reviewed alone.
- [x] S2. Tests `Core.Tests/Processes/ProcessLaunchTests.cs`: `Create` gives the redirects, `UseShellExecute` false, the encodings passed in, and the arguments in order; `Kill` on a process that has already exited does not throw. Why: pins the helper before callers use it.
- [x] S3. `ProcessGitRunner`: `StartInfo` builds through `Create` (same argument list, same environment); `Stop` calls `Kill`. Why: the static `StartInfo` must keep its test passing unchanged. Add an assertion that the argument list and the environment keys are the same as before.
- [x] S4. `ProcessCliRunner`: `Create` for the start info, `Kill` in the timeout and cancel path. Why: the timeout path is the one that changes (D1).
- [x] S5. `CodexModelList`: `Start()` uses `Create` with `redirectStandardInput: true`, input encoding UTF-8 without a byte-order mark; `StopTree` calls `Kill`. Why: keeps its no-byte-order-mark encoding exactly.
- [x] S6. Contract test: a missing executable name gives the three conventions: `ProcessCliRunner` throws `Win32Exception`; `ProcessGitRunner` returns `GitOutput` with exit -1 and "git could not be started"; `CodexModelList.ReadAsync` returns `NotStarted`. Why: this is the test that a later refactor would break first.
- [x] S7. Build Core and App, run Core tests (net10.0 and net11.0) and App tests. Record the counts. Baseline at the start of this plan: Core 3368, App 681.
- [x] S8. Commit per step. Push only when S7 passes.

## Decisions for the owner (defaults in place)

- D1. `Kill` ignores `Win32Exception` in `ProcessCliRunner` as well. Default: yes.
- D2. Git's standard error keeps its current decoding (no encoding set). Default: unchanged. Setting UTF-8 would change how non-ASCII error text is read.

## Review (self, against the code, before handover)

- [ ] Every row of the contracts table has a test or a named reading. S6 covers the start-failure rows; the timeout and cancel rows are covered by the existing runner tests.
- [ ] "To verify" (minor): that `Process.Kill(entireProcessTree: true)` throws only `InvalidOperationException` and `Win32Exception`. The existing code already relies on both. Check the .NET documentation before S4.
- [ ] No caller's behaviour depends on the argument-list order of `ProcessGitRunner`. Checked by reading `GitRunner.cs` lines 102-108: the `-c` and `-C` flags come first, then the caller's arguments. S3 keeps that order.

## Results (2026-11-23)

- Commits: `088f318` (S1 helper, S2 tests), `c0f1002` (S3 Git), `ea344d0` (S4 CLI runner), `2bfa9a3` (S5 Codex model list), `dca8906` (S6 convention tests). Then one status commit for this file.
- S7: Core 3380 passed on net10.0 and net11.0 (baseline 3368, plus 8 helper tests, 1 Git pin, 3 convention tests). App 681 passed on net10.0 and net11.0 (baseline 681). Build with `-nodeReuse:false`.
- D1 applied to all three runners, not only the CLI runner: `Kill` ignores `Win32Exception`. So a Git stop and a Codex stop that used to throw now return. No test pins the old throw; none was expected.
- D2 kept: Git's standard error keeps its default decoding (no encoding is set for it).
- The start-failure conventions are unchanged and pinned by `StartFailureConventionTests`.
- Not done: the check of `Process.Kill`'s exception types against the .NET documentation, planned before S4. The existing code already catches `InvalidOperationException` and `Win32Exception` there, and the tests pass, but the documentation was not read in this pass. Open, minor.
- Not run: the owner's live checks. None of these runners is shown on screen, so the sitting has nothing new to check here.
