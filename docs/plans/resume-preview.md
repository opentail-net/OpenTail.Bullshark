# Resume prompt: split button with "Preview the prompt" (2026-11-20)

Owner ask: from the Resume dialog, preview only the prompt (what the new session would be given), without resuming. Owner's idea: a split button, "Resume ▾", whose menu reveals the preview. Agreed in chat; the owner said "Go on then".

**Status (2026-11-23): the built steps are in [Done/resume-preview-done.md](Done/resume-preview-done.md).** They were built in the repo (commits from 6f5ae09 on) and checked on 2026-11-23: Core 3367 passed on net10.0 and net11.0; App 681 passed on both. Open here: the live checks R1 to R7 (section C8 of the sitting), which need the owner at the screen, and the summaries (step 2).

## Steps (open)

- [ ] Live checks R1 to R7: in [live-run-checklist.md](live-run-checklist.md), section C8, run in the owner's sitting after this build.

## Open items (owner)
- None blocking. Assumption recorded: the previewed summary is saved as a file at preview time (decision 4).

## Summaries (open from the review of 2026-11-15, phase 2)

The summaries folder, the handover, and the measurement are the same feature as the start text in the built steps ([Done/resume-preview-done.md](Done/resume-preview-done.md)). Step 2 waits for the owner's sitting, section B, where "resume from a summary" must pass first.

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
