# OC4 review notes (opencode, 9f01ad4..HEAD)

Reviewer: Sonnet 5.5, read-only (no build, no test, no program run). Range: c0992ad, fa29900, 073cd71, 92d09c7, 32d47d0, 69dd106, 8400f96, 69840af.

## Must fix
None found.

## Should fix
1. **UI-thread database open.** `App/SessionWindow.OpenCodeTranscript.cs:60` calls `OpenCodeDatabase.Open` synchronously before the first await, so it runs on the UI thread (up to the 5 s lock wait). The resume screen does it right (`SessionWindow.OpenCodeResume.cs:~205`, Task.Run). Fix: wrap in `await Task.Run(...)`.
2. **No start-up sweep of scratch and start files.** Plan section 4 row "Scratch folders and start-file cleanup at start-up" and OC-D16: no code sweeps `OpenCodeScratch` (a folder left after a kill or a lock past 15 retries stays; `OpenCodeSummarizer.cs` DeleteScratchAsync) and old start files (they hold session text) are removed only when a resume screen opens (`SessionWindow.OpenCodeResume.cs:51`). Copilot sweeps at start (`SessionWindow.Copilot.cs:435`, `xaml.cs:258`). Fix: add a marked-folder sweep and the start-file sweep beside `SweepCopilotScratch`, or record it as an open item.
3. **Inherited config can outrank the deny.** `OpenCodeSummarizer.cs:41,64` use `OPENCODE_CONFIG` (a file). Per the plan's own source notes, `OPENCODE_CONFIG_CONTENT` is applied after it, so an inherited `OPENCODE_CONFIG_CONTENT` with allow rules would win. Fix: also set `OPENCODE_CONFIG_CONTENT` to `ConfigText` in `EnvironmentFor` (cheap, and it is what plan OC-D10 named). I could not verify the precedence without running.
4. **Summary session lands in the rail.** `opencode run --dir <scratch work>` writes a root session whose folder is then deleted; the rail lists all root sessions (`OpenCodeHistorySource.cs` SessionsSql), so each summary adds a row with a missing folder. The owner accepted the row (OC-O4), not necessarily the rail clutter. Fix: tell the owner on the cost line, or hide rows whose folder is under `OpenCodeScratch`.
5. **cmd.exe metacharacters in the summary folder.** `OpenCodeSummarizer.cs:129` and `Arguments` rely on `RequireFolder`, which does not refuse `&`, `^`, `(`, `)` in a path with no space (ArgumentList then does not quote it, and the line goes through `cmd /c`). Only the state-file folder is affected. Fix: refuse those characters for this call.
6. **Copilot and Codex texts changed outside a plan decision.** Commit 69840af and 8400f96 (`CopilotResumeRules.cs`, `CodexResumeRules.cs`, `SessionWindow.CodexResume.cs`, `SessionWindow.CopilotResume.cs`) changed refusal wording. The change is correct (the preview is closed), but section 11 does not record it. Fix: add a line to section 11.

## Notes
- Plan text is out of date: OC-D10 (plan line 26) says `OPENCODE_CONFIG_CONTENT` and a pwsh pipe; the code uses `OPENCODE_CONFIG` plus `OPENCODE_DISABLE_PROJECT_CONFIG` and the runner's stdin. Update the plan.
- `OpenCodeTranscript.cs:80` shows `ex.Message` on screen only (not logged); acceptable under the privacy rule, but the status line at :81 uses the type, so make the tab match.
- `SessionWindow.Continue.cs:204` `ContinueScreenRefusalOf` is a pass-through to `ContinueDecision.ScreenRefusal`; remove or use.
- The double-click resume (`SessionWindow.OpenCode.cs` ResumeOpenCode) refuses a missing folder; the resume screen recreates it. Same as Copilot.
- The permission key `{"permission":{"*":"deny"}}` cites byte offsets in `opencode.exe` (`OpenCodeSummarizer.cs:~31-40`); not re-verified here.

## Checked (file, what)
1. Logs: all 21 `AppLog.Write` calls in `SessionWindow.OpenCode*.cs` and shared lines of the range: counts, enum status name, lengths, exception types only. `RememberResumeMode` logs provider and mode. Core OpenCode has no logging. No path, id, title or message logged.
2. SQL: `OpenCodeHistorySource.cs` (3 SELECTs), `OpenCodeTranscriptReader.cs` (2 SELECTs, `@sessionId` parameter), `OpenCodeDatabase.cs` (`PRAGMA query_only = ON`, three `PRAGMA table_info`). `ConnectionString`: Mode=ReadOnly, Pooling=False; no immutable. Every open goes through `OpenCodeDatabase.Open` (history, transcript, probes). Grep of src for INSERT/UPDATE/DELETE/CREATE/DROP/ATTACH: none in opencode code.
3. Summary: stdin via runner; prompt has no quote, percent, ampersand or exclamation mark; per-call scratch, empty `work`, removed in `finally` (marked folders only); empty output throws (`CliProcessRun.Output`); status shows `ex.Message`, which is a fixed text (exit code, timeout or empty). `--dangerously-skip-permissions` appears only in two `DoesNotContain` tests.
4. Start from text: single-quoted `Quoted()` folder, prompt read from the file into `$prompt`; cost line `LastTurnsCostLine` before Start; Summary mode shows the summary line, then adds the start line once a preview exists; folder made again or refused (`EnsureSessionFolder`).
5. Id: `IsValid` is checked in `ResumeCommandLine`, `ShowOpenCodeResume`, `ResumeOpenCodeInFull`, the reader and the list; refusal texts carry no id.
6. Continue: mode passed to `ShowOpenCodeResume`; nothing starts on open (summary only by Make summary).
7. Marks: `MarkViewOf`/`MarksOf`, all opencode writes use `ChangeMarkOf(ProviderId.OpenCode, ...)`; Edit writes the stored mark, not the effective one.
8. Threading: no Wait/Result/GetResult in new code; see should-fix 1.
9. Tests: fixtures use temp databases, fake runners; no `ProcessCliRunner`, no default `OpenCodeSummarizer()`, no real path; `FindShimOnPath` tests pass an explicit path.

Not checkable without running: real opencode permission behaviour, WAL read-while-write, native SQLite on both targets, exit codes.
