# Review notes, Copilot provider G1 to G3 (range b62b0cf^..HEAD)

Reviewer: Sonnet. Read-only review; no code, test or plan edited.

## Must fix
None found.

## Should fix
S1. Start launcher uses a different Copilot copy than the terminal, and fails for exe-only installs.
- `Core/Copilot/CopilotStartLauncher.cs` (script line `Get-Command copilot -CommandType ExternalScript`) vs `CopilotTerminalOpener.cs` (`cmd.exe /d /c copilot`, which resolves the first PATH hit).
- On this PC the WinGet `copilot.exe` is first on PATH (plan G0.1); the launcher still picks the npm `copilot.ps1` (a different bundled version from the one the setup check reports). On a machine with only `copilot.exe` the default resume mode (last turns) can never start: the tab prints "shim not found" and exits 1.
- `App/SessionWindow.CopilotResume.cs` `StartCopilotFromText` then sets "Started a Copilot session from ..." although nothing started, and `CopilotSetupCheck` shows Ok for the same install. Fix: the setup check should warn when no `copilot.ps1` is found (the start route needs it), and the status line should not claim a start it cannot know about (or the launcher should fall back to the exe).
S2. Start text lost if the folder vanishes between the check and the launch. `CopilotStartLauncher.CommandLine`: `Remove-Item` on the prompt file runs before `Set-Location`, with `$ErrorActionPreference = 'Stop'`. A missing folder ends the script after the file is deleted. Cheap fix: `Set-Location` before `Remove-Item`.
S3. The start-up sweep invariant is not true for every window. `SessionWindow.xaml.cs` sweeps only when `_supervisor is not null`, but a watcher window (guard held by another instance) or a `NoPass` window can still run a summary (`MakeCopilotSummaryAsync` has no guard check) and creates folders under the shared `CopilotScratch`. A later instance that takes the guard (G-D15 says "removes every marked folder") deletes a folder another window is using; the reverse case leaves a failed-delete transcript copy forever in a window that never sweeps. Fix: refuse a summary when `_supervisor` is null, or sweep only folders whose marker is older than a few minutes plus on cancel.
S4. Cancel deletes before the process is dead. `Core/Claude/CliRunner.cs` calls `process.Kill(entireProcessTree: true)` and rethrows without waiting for exit; `CopilotSummarizer.SummariseSessionAsync` then deletes in `finally` with 5 x 200 ms retries. A tree that is slow to die can keep `config`/`log` files locked, leaving the transcript copy (Copilot writes session state into the scratch config) until the next guarded start. G-D15 says "after the process tree is dead". Cheap fix: `WaitForExitAsync` (short bound) after Kill in the runner, or a longer retry for the cancel path.

## Notes (5)
N1. `ShowOpenCopilotTab` shows an ended tab for the same session id instead of starting a resume; the owner must close the ended tab first (G2.9 not built; same shape as Codex).
N2. `FireAndForget.Observe` logs `inner.Message`. For the Copilot resume screen only unexpected exception types reach it (the dialog and `BuildCopilotStartTextAsync` catch the rest), but an IOException message holds a path (folder, not session text). Low risk.
N3. `CopilotScratchFolder.Create` is outside the `try` in `SummariseSessionAsync`; a failure after the folder exists leaves it for the sweep.
N4. `_copilotSummaryRunning` stays true until the cancelled call's delete retries finish (up to about 1 s), so a quick change of level then Make summary can show "summary running". Harmless.
N5. Test `ACancelledCallMakesNoFolderAndACancelDuringTheCallRemovesTheFolder`: the pre-cancelled half fails in `Task.Run(..., ct)` before any folder is made, so it proves nothing about scratch cleanup; the second half is the real check, but the fake throws instantly, so it does not cover a lock held by a live process.

## What was checked
1. Privacy: every AppLog.Write in Core/Copilot (none) and App/*Copilot* (counts, types, exit text only); exception messages (CopilotSummaryException text fixed; ArgumentException texts static; FileNotFoundException names the id only and reaches the UI, not the log); status lines; summary file name (owner's own folder, as Codex); fixtures (synthetic). Clean apart from N2.
2. Scratch: work/config/log are siblings; config and log are not inside work; deleted in finally including cancel; only marked folders removed; sweep runs in the window constructor before any call (but see S3, S4).
3. Command lines: terminal new and resume, setup version read, summary, launcher script all carry `--sync-session=off`; resume has no folder; `RequireFolder` refuses quote, %, !, control, trailing backslash, applied to the scratch parent and sub-folders. The launcher goes through pwsh, not cmd, and quotes with doubled single quotes (all five quote characters).
4. Principle 3: no stdin, no terminal write, no send anywhere. Writes are only the Starts file and the two summary files. The tab starts only after the owner presses Start.
5. Threading: no GetAwaiter().GetResult() or .Result in Copilot code; setup check, history load, transcript read, summary and start text all run through Task.Run or the async dialog path, and continuations return to the UI context.
6. Claude/Codex: shared edits only add Copilot branches (ProviderChoice, ProviderId, ProviderPaths, MarksOf, Rail, Tabs, Search, SetupCheckDialog optional async parameter). The Rail.cs line 825 test now also holds for Copilot tabs; `SessionLibrary.ProviderView` is generic by key prefix. I found no change to a Claude or Codex result.
7. Failure paths: missing shim (exit 1 -> status, folder deleted), timeout, non-zero exit, empty reply, cancel, missing folder (refused twice), no events file (FileNotFoundException -> status), damaged workspace.yaml (counted, listed), pwsh missing (status). All end in a status line. Exceptions outside the caught list (for example Win32Exception when cmd.exe is missing) are caught by the dialog and shown by type.

## Could not check
Real Copilot behaviour: whether the `view` tool is confined to the working folder (a transcript with injected text asking for `..\config` is untested), the real `copilot.ps1` shim's handling of quotes and new lines in `-i` text (P-C8 uses a recording fake), the summary through cmd.exe (R-C9), and any on-screen behaviour. Tests were not run.

## Fix record

Coder pass, 2026-11-22. Nothing committed or pushed; the plan is not edited. Line endings kept per file (CopilotScratchFolder.cs is CRLF; the others are LF).

Test counts after the pass (before: App.Tests 265 on each target; Core.Tests 2883 on net10.0 and 2897 on net11.0):

- Core.Tests net10.0: 2889 passed, 0 failed, exit 0.
- Core.Tests net11.0: 2903 passed, 0 failed, exit 0.
- App.Tests net10.0-windows10.0.19041.0: 267 passed, 0 failed, exit 0.
- App.Tests net11.0-windows10.0.19041.0: 267 passed, 0 failed, exit 0.

Existing tests changed only where the new behaviour moves what they pin: the launcher's script line positions (CopilotStartLauncherTests.cs, ExpectedScript and two index checks) and two setup-check fixtures that now add copilot.ps1 (CopilotSetupCheckTests.cs). No assertion was removed or weakened.

### S1 (partly done)

- Done: `CopilotStartLauncher.FindShim` (CopilotStartLauncher.cs), with four tests. `StartCopilotFromText` refuses before the start file is written when no copilot.ps1 is on PATH, with the line `CopilotResumeRules.NoShimOnPath` (SessionWindow.CopilotResume.cs, CopilotResumeRules.cs). The setup check warns when copilot.ps1 is missing after a successful version read (CopilotSetupCheck.cs), with two tests.
- Not done: the fall-back to copilot.exe the review also offered. The launcher still runs only copilot.ps1, and the terminal still runs copilot through cmd.exe (CopilotTerminalOpener.cs, unchanged), so on a PC with both files the two can still run different copies.

### S2 (done)

- CopilotStartLauncher.cs, `CommandLine`: Set-Location now runs before Remove-Item. The class and `CommandLine` doc comments say the same order.
- Test: `TheFolderIsSetBeforeThePromptFileIsDeletedSoAMissingFolderKeepsTheText`.

### S3 (done for the summary path)

- SessionWindow.CopilotResume.cs, `BuildCopilotStartTextAsync`: a new summary is refused when `_supervisor` is null, with `CopilotResumeRules.SummaryNeedsGuard`. The kept-summary path and the last-turns path are not refused.
- Test: `TheSummaryNeedsGuardLineIsSetAndSaysItWantsTheLock`.
- Not changed: the sweep (SessionWindow.Copilot.cs, SessionWindow.xaml.cs). Only a window that holds the guard now makes scratch folders. A window that takes the guard later (the `_supervisor = taken` path) still does not sweep at that moment.

### S4 (partly done)

- Done: `CopilotScratchFolder.DeleteAsync` now makes `AsyncDeleteAttempts` = 15 attempts with the same 200 ms pause (14 pauses, about 2.8 s). The cancel path reaches it: CopilotSummarizer.cs line 102 calls `DeleteAsync` in `finally`. The sync `Delete` used by the start-up sweep keeps 5 attempts. No timing test was added.
- Not done: the short wait for the process tree after Kill in CliRunner.cs. The brief bars that file, so it is unchanged.

### Notes N1 to N5 (not done, not in this pass)

- N1 (`ShowOpenCopilotTab`), N2 (`FireAndForget` message), N3 (`CopilotScratchFolder.Create` outside the try, CopilotSummarizer.cs), N4 (`_copilotSummaryRunning` timing) and N5 (the cancel test, CopilotSummarizerTests.cs): all left as they were.

### What I could not confirm in the review

- I did not open CliRunner.cs, so the review's claim that it rethrows without waiting for exit is unchecked here.
- S2 and S3 match the code. The old `DeleteAttempts` (5) and `RetryPause` (200 ms) match S4's "5 x 200 ms".
- The brief's wording for the shim line says "Only copilot.exe was found", which is not true when no copilot program is on PATH. Kept as given.
