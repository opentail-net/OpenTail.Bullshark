# Resume prompt: split button with "Preview the prompt" (2026-11-20)

Owner ask: from the Resume dialog, preview only the prompt (what the new session would be given), without resuming. Owner's idea: a split button, "Resume ▾", whose menu reveals the preview. Agreed in chat; the owner said "Go on then".

## Goal
The Resume button becomes a split button. The main part resumes exactly as now. The ▾ part opens a menu:
- **Preview the prompt…** shows the exact start text in the dialog (read-only, scrollable), with a size line.
- **Copy the prompt** puts that text on the clipboard (clipboard only; nothing is sent to any session: principle 3).

## Facts read from the code (2026-11-20)
- `ResumePrompt.xaml(.cs)`: choices are `FullChoice`, `SummaryChoice` (levels Minimal/Normal/Complete in `LevelRow`), `TailChoice` (turns in `TurnsBox`). `Selected(out turnsValid)` builds a `ResumeChoice(Mode, Level, Turns, Model, Effort)`. Buttons: `Resume` (style `ResumeGo`, based on the shared `GoButton`), `Cancel` (IsCancel, IsDefault). The window is `SizeToContent="WidthAndHeight"`, content width 580.
- `SessionWindow.Resume.cs`: `ResumeOnceAsync` loads `(lines, folder)`, shows the dialog, then `StartFromTextAsync(digest, folder, lines, choice)`. That method (a) when `choice.Level` is set, calls `_cli.SummariseAsync(ResumeText.Instructions(level), ResumeText.SummaryInput(lines, digest.FilesWritten))` and `SaveSummaryFileAsync`, (b) fits the text with `ResumeText.Fit(summary, level, lines, turns, digest.SessionId, text => SetupPlan.LongestCommandLine(RequestFor(text)))`, (c) validates and `StartRunAsync(request)`.
- `ResumeText.Fit(...)` returns `FittedStart(Text, CutCharacters, Fits)`. It is pure given a summary, so the tail-only preview is exact and free.
- `ResumeMode.Full` uses `--resume <id>` with no prompt: there is nothing to preview.

## Design (decisions, with why)
1. **Tail-only preview: free and exact.** Build with `ResumeText.Fit(null, null, lines, turns, id, ...)`. Same function Resume uses, so what is shown is what is sent.
2. **Summary preview: one Haiku call, kept.** Preview makes the summary once and shows summary + last turns. The dialog keeps it as `KeptSummary(Level, Text)`. Pressing Resume afterwards **reuses it** (no second call, no different text). Changing the level drops it (it no longer matches). Why: otherwise the owner pays twice and the text can differ. If the owner cancels after a preview the one call is spent; the menu item's tooltip says so.
3. **Full: the Preview and Copy items are disabled** with a tooltip "Resuming in full sends no prompt."
4. **Save the previewed summary** as a file exactly as Resume does (`SaveSummaryFileAsync`), once, at preview time; Resume then does not save it again. Why: it is already paid for.
5. **Where it shows:** a read-only `TextBox` (monospace, wraps, max height about 220 with a vertical scrollbar) in a panel that is collapsed until a preview exists, placed above the buttons. A one-line header above it: "Prompt the new session starts with: about N tokens (C characters)." plus "K characters were cut to fit the command line." when `CutCharacters > 0`, and a red-ish note when `Fits` is false ("too long for the command line even with its latest turn cut"). The tokens figure is characters / 4, labelled "about".
6. **Stale preview:** any change of choice, level, or turns collapses the pane (the kept summary stays only while its level is the selected one). Model and effort do not change the text, so they do not collapse it.
7. **While a preview runs:** the split button, menu items and radio rows are disabled and the status line (`CostText` or a new small line) says "Summarising: one model call…". Closing the dialog cancels the call (CancellationToken, if `SummariseAsync` takes one; if not, report it and let the call finish but ignore its result).
8. **Split button look:** built from the shared styles so it matches. Left part = Resume (green `GoButton` family, rounded left corners only); right part = a narrow ▾ part (same green, a 1 px divider between, rounded right corners only). Tooltip on ▾: "Resume options: preview the prompt". `AutomationProperties.Name` on both parts. The menu is a `ContextMenu` opened under the ▾ part (the app's `ContextMenu`/`MenuItem` styles in `Styles/Controls.xaml` apply). Keyboard: Tab reaches both parts; Enter/Space on ▾ opens the menu. Resume keeps its click behaviour; Cancel stays IsCancel/IsDefault.
9. **No behaviour change for plain Resume** apart from reusing a kept summary.

## Contracts this must satisfy
- `ResumeChoice` record: keep as is (other code builds it). The kept summary travels separately: `ResumePrompt.KeptSummary` (nullable record `KeptSummary(SummaryLevel Level, string Text, bool Saved)`), read by `ResumeOnceAsync` after `ShowDialog()` and passed to `StartFromTextAsync` as an optional parameter. `StartFromTextAsync` uses it only when `kept.Level == choice.Level`.
- The dialog gets the preview work as a delegate so it stays free of the CLI: `Func<ResumeChoice, CancellationToken, Task<ResumePreview>> previewAsync` where `ResumePreview(string Text, int CutCharacters, bool Fits, KeptSummary? Summary)`. `ResumeOnceAsync` builds it from `lines`, `digest`, `folder`, `_cli`.
- The text built for the preview and the text built for the real start come from **one shared method** in `SessionWindow.Resume.cs` (extract it from `StartFromTextAsync`), so they cannot drift. The real start still does `SetupPlan.Validate` and trust checks after it; the preview does not start anything.
- `ResumePrompt`'s existing constructor parameters keep their order; add the delegate as a new optional last parameter (null = no preview: the menu items are disabled; keeps any other caller working).
- Principle 3: preview and copy write to the clipboard and the screen only. Nothing is typed into, or started in, any session by Preview.
- Privacy: the previewed text is the owner's own transcript text shown to the owner. It must not be written to `app.log` (log only "preview built, N chars", never the text).

## Steps (checkboxes, with why)
- [ ] Read first: `src/OpenTail.Bullshark.App/ResumePrompt.xaml`, `ResumePrompt.xaml.cs`, `SessionWindow.Resume.cs`, `Styles/Controls.xaml` (GoButton at about line 82, ContextMenu/MenuItem at about line 633-660), `Core/Resume/ResumeText.cs`, and how `_cli.SummariseAsync` is declared (does it take a CancellationToken?).
- [ ] Core: add `KeptSummary` record and a small pure helper `ResumePreviewText` in `Core/Resume` with `Header(int characters, int cutCharacters, bool fits)` (the size line) and `KeptSummary.MatchesLevel(SummaryLevel?)`. Why: testable without WPF.
- [ ] Core tests (`tests/OpenTail.Bullshark.Core.Tests/Resume/`): header for no cut, cut, not fitting; tokens = characters/4 rounded; a kept summary matches only its own level and never matches null.
- [ ] App: extract the shared "build the start text" method from `StartFromTextAsync` (inputs: digest, folder, lines, choice, optional kept summary; output: fitted text, the summary made or reused). Real start uses it; preview uses it. Why: one source of truth.
- [ ] App: `ResumePrompt` gets the preview pane, the split button, the menu, the stale-collapse rule, the busy state, cancel on close, the `KeptSummary` property.
- [ ] App: `ResumeOnceAsync` passes the delegate and the kept summary through; `StartFromTextAsync` skips the Haiku call and the file save when the kept summary matches.
- [ ] App.Tests (headless, existing `UiThread` helper): construct `ResumePrompt` with a fake delegate; assert Preview is disabled for Full, enabled for Tail/Summary; a tail preview shows the text and header; changing the level collapses the pane and drops the kept summary; Resume after a preview returns the kept summary; a delegate that throws shows a message and does not crash.
- [ ] App.Tests: one test that renders the dialog (with a preview showing) to a PNG with `RenderTargetBitmap` into a **unique scratch folder you create** (`%TEMP%\bullshark-resume-preview-<guid>`), and prints the path so the orchestrator can look at it. Do not delete it.
- [ ] Build App with 0 errors; run Core.Tests and App.Tests in full; report counts and exit codes.
- [ ] Live checks R1 to R7: in [live-run-checklist.md](live-run-checklist.md), section C8, run in the owner's sitting after this build.

## Rules for the coder (carry these)
- DELETION RULE: delete only what you created in this task, with a unique self-created name (include a guid). List it and read any note file in a separate step before deleting; never chain a listing and a delete; never `rm -rf` a fixed short name in a shared folder. Do not touch `%TEMP%\bullshark-app-tests-*` folders you did not create this run.
- Never start a claude session, never run `claude respawn`, never kill a process you did not start. Tests must not resume the owner's sessions (the app already refuses supervision under a test host; do not weaken that).
- Work on `main`. Do not commit or push: the orchestrator reviews the diff, then commits.
- Keep the plain Resume and Cancel behaviour. Do not restyle other dialogs.
- Report: changed files, tests with counts and exit codes, the PNG path, anything not done, anything you were unsure about.

## Open items (owner)
- None blocking. Assumption recorded: the previewed summary is saved as a file at preview time (decision 4).

## Summaries (open from the review of 2026-11-15, phase 2)

The summaries folder, the handover, and the measurement are the same feature as the start text above. Step 2 waits for the owner's sitting, section B, where "resume from a summary" must pass first.

- [ ] Summaries folder. Decided 2026-11-16 (owner and planner), in two steps. Step 1 is built (2026-11-16, uncommitted): `SummaryFiles` in Core, tests in `SummaryFilesTests.cs`, and the window keeps a copy of each summary it makes (`SaveSummaryFileAsync`). Step 2 is open. Two choices made while building, both for the planner to check: the session name in the file name is the title the rail shows (the owner's name, else the transcript's title), cleaned of characters a file name cannot hold and cut to 60 characters, because the CLI name of a past session is often a role name; and a second summary of the same session, day and level gets " (2)" rather than overwriting the first.
      Step 1, now: every summary the app makes is also saved as a file in the `Summaries` subfolder of the app's data folder.
      Nothing else changes: the resumed session still gets the summary in its start text, as today. A file that cannot be
      written is logged and does not stop the resume.
      Step 2, after the Phase 3 run: the start text names the file instead of carrying the summary. This changes what a resumed
      session starts from, and the resume has not yet been seen working with the real CLI, so the run comes first. The "measure
      first" box below belongs to this step. Each summary is saved as a markdown file in `%LOCALAPPDATA%\OpenTail.Bullshark\Summaries\` (proposed location). The file title carries the date, the session name, the level and the short session id, in that order, for example `2026-11-16 ovc-planner complete 5bc2852c.md`. The session name comes before the level because the session is what the owner looks for. A fixed date-first order keeps files sorted by day; the cost is that the name is slightly harder to parse, which is accepted. The start text names the file's path, not the summary, so the 6,000-character command-line cap no longer applies to the summary. The new session reads the file.

- [ ] Handover: the same file is the handover to another session.

- [ ] Measure first: Complete is written to a 1,200-word limit and cut at 6,000 characters. Measure a real Complete summary before deciding whether Complete is recommended for long sessions (the owner said "possibly").

- [ ] Open: the tail still goes on the command line (up to 4,000 tokens, roughly 16,000 characters). Decide whether the tail goes into the file too.

- [ ] Linked to finding 15: the summary call's working folder can be the Summaries folder.

Not placed in any phase yet: finding 15 (the summary call runs in the app's own folder, with that folder's settings). Proposed fix: run it from a folder under the data folder. Decide in Phase 3, which checks the History.
