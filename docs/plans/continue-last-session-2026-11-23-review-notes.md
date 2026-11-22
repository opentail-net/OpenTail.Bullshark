# Continue C1 to C6: review notes (C8, 2026-11-23)

Reviewer only; no code, test or plan edits; nothing built or run. Commits read: db280e4, bdd9e5b, c3112b1, d938198, bd8c9db, 176a748.

## Must fix
None found. Checked:

1. **Principle 3.** No write to a process input, ConPTY or terminal anywhere in C1 to C6. `SessionWindow.Continue.cs` only calls `ActivateTab`, `ResumeAsync` / `ShowCodexResume` / `ShowCopilotResume` (screens that wait for Start) and `SetStatus`. Opening a screen with Summary makes no call (tests, C4).
2. **Privacy of added log lines.** `continue: N sessions in scope X, one chosen from <provider>, mode M` (Continue.cs): counts, provider name, mode name only. `resume mode remembered: <provider> <mode>` (Resume.cs): clean. `library not changed: <ExceptionType>` (Library.cs, C6 fix): clean. Core C1 to C3 adds no AppLog; the only throws are `ArgumentOutOfRangeException(nameof(...))`. Titles and paths appear only in status lines (`SetStatus` does not log; verified `SessionWindow.xaml.cs:830`).
3. **Library writes.** `RememberChoice.Apply` is `(existing ?? new LibraryEntry()) with { ResumeMode = mode }`: nothing else changes. Key: `ApplyMark` -> `UpdateProvider(provider, ...)`, the provider's own key, via `ChangeMarkOf`, which returns false without writing when `_libraryError` is set. Cancel: `Choice` is null and each consumer returns before the write (Resume.cs, CodexResume.cs, CopilotResume.cs). Unticked: `Remember` false. Copilot Full path and the start path each write once. The edit-dialog path (`WithDetails`) uses `with`, so the mode survives.
4. **Claude changes, each against a recorded decision.**
   - `ResumePrompt.xaml.cs` ctor: optional `initialMode`; null keeps the old `fullRecommended ? Full : Summary`. Result changes only when a caller passes a mode (Continue). Decision: CN-D9 / optional starting mode.
   - `ResumePrompt.xaml` + `.cs`: Remember tick, unchecked, returned in `ResumeChoice`. Decision: owner 2026-11-23. Does not alter results unless ticked.
   - `SessionWindow.Resume.cs`: optional `initialMode` (plain resume passes null); write after Start when ticked. A remembered mode later changes Claude's start only through Continue.
   - `SessionWindow.xaml`: button added, the ComboBox margin moved to a wrapping DockPanel (same 8,8,8,0). Layout only.
   - `SessionWindow.Palette.cs`: one ungated entry. Decision: palette entry.
   - `SessionWindow.Library.cs`: log line now type only (privacy fix, no behaviour change).
   No Claude result changes without a recorded decision.
6. **Chooser and decision.** Agree with CN-D1 to D5: provider scope vs all; null default mode passes through; precedence mark > setting > null; `IsSubagent == true` only; archived by `provider:id` mark; null activity sorts last; ordinal key tie-break. Codex last activity is Core's (rollout), the chooser only reads `LastActivity`.
7. **Copy match.** Core `OpenTail.Bullshark.Core/Claude/ClaudeProvider.cs:55-65` and App `SessionWindow.Continue.cs:171-181`: same ten fields in the same order. They match today.
8. **Plan accuracy.** C1 to C6 paragraphs match the code. One gap: C5 does not mention that the Codex/Copilot list can be stale (see S1).

## Should fix
- **S1. Stale or empty Codex/Copilot list.** `LoadCodexHistoryAsync` (`SessionWindow.Codex.cs:446`) returns at once when `_codexLoading` is true (Copilot likely the same). `ContinueListAsync` then reads `_codexSessions` without waiting: it uses a stale list, or, before the first read, refuses with "history could not be read". Fix: await the running load (or refuse with "still loading").
- **S2. Exception text in the log.** `FireAndForget.Observe` logs `inner.Message` (`FireAndForget.cs:18`). The Continue task and the screens now run through it; an IOException message can carry a path. Fix: log the type only, or strip the message for these tasks.
- **S3. No timeout.** `_continuing` stays true if `ListAsync` never returns, so Continue is refused until restart. Fix: a cancellation token with a timeout.
- **S4. Remember after a refused start** (recorded decision (1) in the plan): a refused or failed start still keeps the mode. Acceptable; revisit if the start methods return a result.
- **S5. Status text.** `Not saved: {ex.Message}` and `_libraryError` show the library path in the status line (not a log). Consider type only.

## Notes: 3
- Double screen: each screen sets `_resuming` / `_codexResuming` / `_copilotResuming` before any await after the check, so a second press is refused with a status line; `_continuing` guards the listing. No UI-thread block: listings are awaited, screens are modal as before.
- The "Continue: picked" status is replaced by the screen's first line (plan deviation 1).
- Claude's "Recommended: in full" text stays beside a checked Summary radio (plan open item).

## Not checked, for the live check C7
The window is not unit-tested. C7 must cover: the button and palette entry in light and dark; Continue with a Claude, Codex and Copilot session each (screen opens on the right radio, no summary call); an open tab is shown, not resumed; a missing folder refusal; scope All; Continue pressed while the Codex rail is loading (S1); an unreadable library (refusal, no write); Remember ticked then Start, then Continue again (mode used, other mark fields intact, correct provider key in the library file); Cancel and unticked write nothing; the log file shows no title, id or path.
