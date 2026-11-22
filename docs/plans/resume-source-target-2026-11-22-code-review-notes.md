# Code review, resume source and target, S4 (range 2a7f0e9..afcc4c3)

Reviewer: Sonnet 5.5, read-only (diffs and files read; nothing built or run).

## Must

None found. The owner rules hold on the paths read (details under "Checked").

## Should

1. **A refused or failed Claude-target run is silent on the status line.** `SessionWindow.CrossStart.cs:63` (`if (launch is null) return;`): `StartRunAsync` (`SessionWindow.xaml.cs:~995-1020`) reports a refusal or a run-store failure on the setup page only. The owner, on the resume screen's window, sees nothing. Fix: if `launch is null`, `SetStatus("Not started: see the setup page.")` (or let `StartRunAsync` return the reason). Plan section 6 B1 already lists it for S5.
2. **Claude screen cost line and pickers are wrong for a Codex target.** `ResumePrompt.xaml.cs:~242` says "The new session runs at {Model} {Effort}" whatever the target; a Codex start uses Codex's default (RS-D12), and the Claude model/effort pickers stay enabled. Same on the Codex screen: `CodexResumePrompt.xaml.cs:305` makes Start wait for `ModelReady` and the model row stays, though a cross start ignores `choice.Model`. Fix: when the target differs, hide or disable the pickers and say "at Codex's default".
3. **Privacy, pre-existing log lines on the cross-reachable paths** (not changed in the range, but the standing rule applies): `SessionWindow.CodexResume.cs:211` logs `ex.Message` (FileNotFound/IO messages carry paths; Copilot's twin logs the type only); `SessionWindow.Resume.cs:416` `summary not made: {ex.Message}`; `:61` `resume failed: {ex}`; `:34` `patterns not read: {ex.Message}`; `SessionWindow.xaml.cs` `run not written: {ex.Message}`. Fix: log `ex.GetType().Name` only, as Copilot does.
4. **A whitespace summary throws on the Claude screen.** `ProviderResumeText.ComposeFrom` throws `ArgumentException`; `StartFromTextAsync` (`SessionWindow.Resume.cs:414`) catches only `ClaudeCliException`, so the outer catch (`:58-63`) shows "Not resumed: ..." and logs the stack. Codex and Copilot catch it. Fix: also catch `ArgumentException` there.

## Notes

- Remember is written after a refused cross start (`SessionWindow.Resume.cs:103`, `CodexResume.cs:114`, `CopilotResume.cs:~105`). This is RS-D7 as written, and the mark is the source's own mode, so it is not wrong.
- `CrossTargetCheckAsync` runs `FolderCheck.Check` and the PATH scans (`FindPwsh`, `FindShim`) on the UI thread; the same-platform starts already do.
- Whether WPF keyboard navigation can select a disabled ComboBoxItem was not verified. If it can, `StartCrossAsync` (route) and the builders refuse Copilot anyway.
- `ResumeTargetBox` gives a null tooltip on enabled items: fine.

## Checked (file, what)

1. Privacy: every `AppLog.Write` added in the range (CrossStart.cs, CodexResume.cs, CopilotResume.cs, Resume.cs, xaml.cs run line): counts and provider names only. The two fixes (`summary saved as a file`, `run ... written`) are clean. No title, path, id or text. Tab title and status lines carry the source title (UI only, not logged).
2. Link checks: `StartCrossAsync` writes no library entry, no mark, no "Continues session" (`ComposeFrom` has no id); the Codex tab key is `"new-" + Guid` (`CodexResume.cs:323`); the Claude route is a fresh solo run with no session link; Copilot is refused by `CrossStartRoute.For` even if the constant flips.
3. Cost: `SummaryForAsync` (Resume.cs:317) returns the kept summary when it matches; the target checks run before it (`:286-290`, Codex `:165`, Copilot `CopilotResume.cs:~150`). A target change calls nothing (`OnTargetChanged` in all three prompts) and leaves the kept summary; the box is disabled while a request runs. One call at most per start on every path.
4. `ChosenTarget` is used in `Selected()` and in Start for all three prompts; Full and Last turns give the source. The Full branch is taken before the cross branch in all three handlers.
5. Same-platform branches: the Claude diff moves the summary into `SummaryForAsync` with the same code (log line aside); Codex and Copilot same-platform code is untouched except that the Codex start was split into an overload with the same checks in the same order.
6. Failure paths: preview hidden means `_previewText` is null, so Start is off on Codex/Copilot; Claude's Start rebuilds from the current target, so no stale text. Refusals set status or the preview message.
7. No `.Result`/`.Wait()` in the new code.
8. Chooser: Copilot enabled only on the Copilot screen; disabled with `RefusalReason` on the others.
9. Plan gaps: findings 1 and 2 (RS-D12 wording, section 5 "cost lines").

Not checkable without running: the real Claude, Codex and Copilot starts, long-prompt behaviour, the XAML layout, and ComboBox keyboard behaviour.
