# Live run checklist: the one sitting that needs the owner

Status: not started. Everything up to round 12 is on `main`, and no screen has been checked with live sessions. This file
puts the Phase 3 run (`review-2026-11-15.md`, section 6) and the gates from `session-summary-2026-11-17.md` in one order, so
one sitting covers them all. Expected time is about 1 to 2 hours (an estimate: it depends on how long the plan > code run takes).

What it costs: real `claude` sessions (use Haiku at low effort for the run), and one trust prompt you accept by hand.

## Before you start

- [ ] Build: `dotnet build OpenTail.Bullshark.slnx -nodeReuse:false`.
- [ ] Make a scratch folder for the run (a throwaway git repo: `git init`, one commit), and a scratch state folder in `%TEMP%`.
- [ ] In the scratch repo, run `claude` once and accept the trust prompt (this is also the first gate below).
- [ ] Start the window with `--state <scratch state folder>\state.json` so nothing touches the live state. Leave `--claude-home` at
  its default (the real one): the run needs the real CLI.
- [ ] Keep a second terminal open for `claude agents --json` after each step.

## Phase 3: the end-to-end run

After each step: run `claude agents --json`, count the attach processes (Task Manager, `claude.exe` with `attach`), and write one line.

- [ ] Start a plan > code run in the scratch repo. Watch one batch and one report.
- [ ] Close a tab only; then close a session.
- [ ] `claude stop` and `rm` one session; watch the resume (F20 is still open).
- [ ] Restart the window: the same tabs come back, and no new session starts.
- [ ] Restart the daemon (or the PC): note what is listed (finding 12; F63 says the daemon runs on demand).
- [ ] Resume in full; resume from a summary; look for the summary call in History (finding 15).
- [ ] Open the screens the rounds built and see them with live data: Needs you, Changes tab, merge check, effort panel, run
  timeline, Storage page (do not press Archive), file-overlap notice.
- [ ] Limits page (2026-11-18 usage plan): it shows usage per day and top projects under the hits; the numbers are plausible against the Storage
  page; the page stays responsive on the owner's data (the first open after a restart is the cold read).
- [ ] Ctrl+Shift+N with a terminal focused: does the chord reach the window (not the terminal), and does it show the next waiting session with the keyboard in its terminal?

Pass when: no name appears twice at any step, each step has its line, and F20 is answered.

## Tags and automatic naming (docs/plans/tags-and-auto-magic-2026-11-18.md)

- [ ] Tag filter: the rail's show menu lists the tags in use with their counts; a tag shows only its rows in History and the folders,
  the filter box reads "show: all · #tag", and a second click on the tag clears it. Needs you still lists every waiting session.
- [ ] Tag filter with a search: a row must match both. A tag that no session has any more clears the filter, with a status line.
- [ ] Palette: "Filter by tag #x" and "Clear tag filter" do the same as the menu.
- [ ] Magic rename on a named session ("Magic rename again"): the owner's tags stay, up to two tags are added, and the status line says so.
- [ ] Automatic naming: with "Toggle automatic magic naming" on, leave the window open on a never-named session of at least four messages
  until its cache goes from 🔥 to ❄ (about an hour, or five minutes for a five-minute cache). The status line says "Automatic: named N
  session(s) that went cold". A session that was already ❄ when the window opened is not named.
- [ ] Turn it off again: nothing that was waiting in the queue is named later.

## Night batch (docs/plans/night-batch-2026-11-19.md)

- [ ] N1 heartbeat: with the window open, `heartbeat.json` beside the state file changes about every 15 seconds (its "at" is recent). Close the window, wait two minutes, open it again: the Night page says "Last pass" with the age the file gives, and "Window/pass" is Check while the window was closed.
- [ ] N2/N6 preflight: Insights > Night shows ten rows and a verdict. Refresh updates "Last pass". A check that says "check" is one the owner can act on; "unknown" means the value could not be read (for example the login, before a live session was read).
- [ ] N2 Sleep row on this PC: the row reads "sleeps after N min on AC power" (or "never sleeps"), and N is the "Put the device to sleep" value under Windows Settings > System > Power & battery > Screen and sleep, on AC power. Compare, do not change it: keep-awake is not part of this batch.
- [ ] N2 Runs row: with a run waiting for a start time within 12 hours, the row names it and its start time; with none, "none waiting to start in the next 12 h".
- [ ] N4 quiet hours: in Settings, General, pick a window that contains now (for example one that starts an hour ago and ends an hour from now). Let a session start waiting: no sound, no flash, no toast; the rail's Needs you still lists it; the log says "needs you (quiet hours)". Pick Off: the next wait sounds again.
- [ ] N4 quiet hours and escalation: during the window a re-alert that falls in it gives no sound (the log says "again ... quiet hours"); after it ends, the session still waiting sounds again at its next interval.
- [ ] N4 Settings: the presets write `quietHours` in `ui.json` (an object, or null for Off); a window set by hand in `ui.json` shows as "Custom (HH:mm-HH:mm)" and is not rewritten by a click.
- [ ] N5 report: return to the window after at least 30 minutes away with something to say. `reports\away-yyyy-MM-dd-HHmm.md` appears beside the state file; the Away page says "Saved to reports\..." and "Open reports folder" opens it in Explorer. The file has the digest, a "Plans" section for sessions with a plan file, and a "Tokens in this period" line. A second return within the same minute gets a `-2` name, and nothing is overwritten.
- [ ] N5 cleanup: a file in `reports\` that is not named `away-yyyy-MM-dd-HHmm.md` (or with `-n`) is never deleted, even with more than 60 reports.
- [ ] N3/N6 palette: "Open Night (preflight)" in the command palette opens the Night sub-tab; `--open-tab night` (the screenshot option) does the same.

## UI polish batch (docs/plans/ui-polish-batch-2026-11-19.md)

- [ ] P1 check boxes: on Changes (a session with changed files) the "viewed" box is a small rounded box; ticked, it is filled blue with a white or dark check mark. The Setup box "Start when the usage limit resets" and the Storage transcript boxes look the same. Clicking each still works (the tick stays, the box follows the setting).
- [ ] P2 text boxes, lists and the zoom slider: a text box has a rounded edge that turns blue when it has the keyboard. The zoom slider in the status bar is a thin track with a round thumb; the zoom still steps in the same places.
- [ ] P3 scroll bars and tooltips: a dialog (Edit session, Instructions, Go to) has the same thin scroll bar as the window. A tooltip is a rounded box in the menu colour, wraps past about 420 px, and has no internal id in it.
- [ ] P4 combo boxes: Setup's Pattern, Coders, Folder (type a path: it still takes the typing), Planner, Reviewer and Assistant boxes, and the Timeline's Run and Session boxes open the same rounded list in both themes. The colour box in Edit session follows the theme.
- [ ] P5 keyboard: on the setup page, Tab goes through Task, Folder, Browse, Pattern, Coders and the models, and reaches Start last. On Insights and Settings, Tab reaches only the chosen pill; Left and Right move along the row.
- [ ] P6 Resume: the Resume button in the resume prompt is the same green as Start, not grey (the resume prompt was not in the screenshots: check it by hand).
- [ ] P7 contrast: in the light theme the "check:" labels on Changes are a dark amber that reads on the grey panels; in the dark theme the control edges (buttons, boxes, combo boxes) are visible without being bright. Numbers are in the plan.
- [ ] P8 tooltips: hover the Setup Reviewer box and its footnote mark. Each is one or two lines, with no "(S9c)" or "(D-S10)" in the text.
- [ ] P9 folder names on real data: the History folder header shows the folder's name (for example `Git` or `OpenTail.Bullshark`), not `C--Git-...`; its tooltip shows the full path. The Storage page's project rows show the same names. A group names a folder only from sessions whose working folder encodes to the group's own key: a group whose sessions all moved to a sub-folder keeps its raw key (for example `C--Git-Public`). The groups themselves are unchanged: each folder's sessions are under the same header as before.
- [ ] P9 cache: the first start after this build reads every transcript again (the cache from before is not used). The second start is quick and the names are the same.

## Insights and Settings (docs/plans/insights-and-settings-2026-11-18.md)

- [ ] Insights: the four sub-tabs (Away, Usage, Timeline, Storage) switch with the mouse and with Left and Right; the window reads the
  page again when it is shown; the sub-tab shown last comes back when the Insights button is pressed again.
- [ ] Settings: each switch and choice (notifications, automatic naming, the re-alert interval, theme) writes ui.json, and the page shows the
  value the file holds afterwards. A file that cannot be written shows "Not saved" and the old value.
- [ ] A return to the window with news opens Insights on Away; Got it closes Insights when the return opened it.
- [ ] Zoom: the slider, the percentage and the − and + at the bottom right of the status bar still zoom; Ctrl+plus, Ctrl+minus and Ctrl+0 do the same.
- [ ] The status bar on a 1366 px window keeps the zoom, Settings, Insights and Go to… at the right, with Retry, Respawn and Trust only when they show.
- [ ] Palette: Open Insights, Open Settings, and Open Timeline, Storage, Limits and Away open Insights on their sub-tab.

## Desktop batch: context meter, plan progress, toasts by event (docs/plans/desktop-codex-batch-2026-11-18.md)

- [ ] Context meter: a session with a transcript shows "ctx N" on its live row and its history row, after the state. A window of 200k with
  a last request of about 170k shows "⚠" after it; Settings, General, "1M" removes the mark. The tooltip still gives the cache's tokens.
- [ ] Context window in Settings: "200k (assumed)" is selected by default; a value set by hand in ui.json shows as "Custom (N)" and is not rewritten.
- [ ] Plan progress: in a session's Edit dialog, Browse… picks a Markdown file; a relative path is refused with a red line. The row shows
  "3/7" after the cache and state; ticking an item in the file and waiting for the next pass (or 🔄) changes it. Its tooltip gives "Next: ...".
- [ ] Plan file removed or cleared in Edit: the figure goes; the library still loads with the rest of the marks.
- [ ] Toasts by event: with the window minimised, each kind can be switched off under Settings, General, and its toast does not come
  (blocked, problems, notices). Sound and flash come as before for a wait whatever the switches say.
- [ ] Ready for review: a coder that finishes with changes gives one silent toast "Ready for review" when it joins the list, and none on
  the first pass after the window opens. Opening its Changes tab (or Mark reviewed) and no new work gives no toast.
- [ ] Usage limit reset: with a limit in force, the first pass after its reset gives one silent toast "Usage limit reset" with its name
  ("5-hour", "weekly"), and nothing when the window is in front.
- [ ] Toasts switched off as a whole (Toast switch off): the five kinds are dimmed and give no toast.

## Fixes batch (docs/plans/fixes-batch-2026-11-18.md)

- [ ] Magic rename again (row menu, a session with a magic name): the new name is not the old one. When the model repeats it, the name
  comes back with " - 18 Nov" (or " 2") after it. The old name is free again for other sessions.
- [ ] Closing the window (or a tab whose session ended) writes no "detach failed" line to `logs\app.log`. If one appears, note the
  exception and whether the session was still running.

## Features batch (docs/plans/features-batch-2026-11-18.md)

- [ ] Viewed marks: in Changes, tick two changed files; restart the window and open Changes again: they are still ticked, and "3 of 7 viewed"
  (or the count you have) is right. Edit a ticked file in the scratch repo: its tick clears and the count drops. "Clear" removes the session's ticks.
- [ ] Big-deletion check: delete a tracked file in the scratch repo, and empty a tracked file of most of its lines: each shows "check: ..." on its row,
  the count line says how many, and no sound or toast comes.
- [ ] Same-call loop: in a real transcript where a command failed three times in a row, the effort panel lists "Same call failed 3 times in a row"
  with its lines, and a click shows the first one. A run of fail, edit, pass is not listed.
- [ ] Group by tag: the show menu's "Group by: Tag" lists the History tag groups with their counts; a session with two tags is under both; "No tag"
  is last; "Group by: Folder" brings the folders back. Needs you and the tag filter behave as before.

## Gates (each needs one real step)

- [ ] **Worktree per coder** (additions item 3) and `--cwd` for listings: with the scratch repo trusted, rerun the gate in
  `additions-2026-11-17.md`.
- [ ] **`--bg --session-id <uuid>`**: does the session keep that id now? (F18 said no on 2026-11-14.)
- [ ] **A dead row's name**: does `claude --bg` accept a name that a dead listing row still holds? (round 8 B1.4)
- [ ] **`claude respawn --all`**: does it restart stopped sessions, and does a blocked session lose its prompt? (round 8 B3.2)
- [x] **`CLAUDE_CONFIG_DIR`**: where does `.claude.json` live when it is set? (round 3) Answered 2026-11-19 with claude 2.1.328: `claude mcp list` with the variable set wrote `.claude.json` inside that folder, so Claude keeps it there. The app now reads it there too (`ClaudePaths`, reliability batch R2). Still to check: one real session under the variable.
- [ ] **Two windows on one state** (reliability batch R1): the second window says it only watches, under Needs you, and starts, resumes and alerts nothing. Close the first one: the second takes over on a later pass.
- [ ] **A Trust step open for a minute** (reliability batch R3): `claude` is run about 4 times a minute for the passes, not 23 (screenshot fixture: 24 calls in 60 s, against 118 before).
- [ ] **A broken `runs.json`** (reliability batch R9): a "Supervisor problem: runs.json cannot be read ..." row under Needs you and one Problems toast; the row clears when the file is good again.
- [ ] **OpenTelemetry** (gems item 3): one session with telemetry to the console, to see the attribute names.

## Decisions still open (no run needed; answer when convenient)

The list in `session-summary-2026-11-17.md`, "Waiting for the owner, Decisions": Windows toast package, start at usage reset,
deleting old transcripts, waking a `done` coder, a sound for a waiting session that goes stale, the Respawn click waiting for a
running pass, the light-theme tab label contrast.

## After the run

Write what you saw (one line per step is enough) in this file or tell the planner; the planner turns each line into a finding
(F65 onward) and a round plan.

## Automated run on the owner's real data, 2026-11-19 (what could be done without a hand on the mouse)

Method: the built App launched with `--state <scratch>` `--no-pass` (no claude call, nothing written to the real state) and the real `~/.claude` transcripts (195 transcripts, 29,678 messages), with
`--open-tab ...` and `--shot`; pictures in `%TEMP%\bullshark-live\shots`. Not a substitute for the clicking items above.
- Limits page / Usage on real data: the page is drawn within 2 s of opening (history read 195 transcripts about 1.2 s); 10 limit hits in 30 days with plausible reset times; the hit table's token column sums all four
  kinds (cache reads dominate: hundreds of millions). OK. Usage per day / projects table is below the hits (the second screenshot taken at 2 s shows the hits; scrolled content not captured).
- Storage page on real data: 88 + 46 + ... transcripts per project, sizes plausible (460 MB, 363 MB), Archive not pressed. OK.
- **Found and fixed:** with no live session in the rail, every rail refresh (the history read finishing) showed the setup page over the Insights/Settings tab (`RestoreSelection` only protected Search, Transcript and Changes). Away on real data was blank
  3 of 3 runs before the fix; after it, Away and Settings stay. (Pre-existing for the old Away/Limits/Storage/Timeline tabs.)
- **Found and fixed:** the ctx meter's ⚠ showed on nearly every row (contexts of 300k to 900k against the 200k default). A session bigger than the chosen window is now judged against 1M (`ContextMeter.EffectiveWindow`).
- Observed, not fixed: the History folder is shown as the encoded name (`C--Git`); rows wrap to three lines in the default rail width; the white band over the status row appeared in the capture taken at 2 s and not at 9 s (so the band is likely a capture-before-render artefact).
- CLI facts: `claude --version` 2.1.328; `claude --help` lists `--permission-mode` choices acceptEdits, auto, bypassPermissions, manual, dontAsk, plan; `--bg`, `--session-id` and `respawn` exist. Whether `--bg` accepts `--permission-mode auto`
  was NOT tested (it would start a real session).
- Not done (needs real sessions or a hand): everything under Phase 3, the tag/auto-naming steps, the toasts, the viewed ticks, the gates (`respawn --all` would restart the owner's real background sessions: not run).
- Owner decisions recorded 2026-11-19: coders run in `auto`; old transcripts are never deleted by the app (archive only), no further work on deletion.

## Review fixes, 2026-11-19 (docs/plans/review-fixes-2026-11-19.md; a hand is needed)
- [ ] F1 paste: copy text that contains an escape character (for example in PowerShell: `Set-Clipboard "a`e[201~b"`), paste it into a session. The session gets `a[201~b` with no escape: nothing after the text runs as typed input.
- [ ] F2 Changes: stage one changed file (`git add <file>`) and leave a second change unstaged. The Changes tab lists both files, and the staged one shows its hunks and its counts. The deletion label still shows for a file that lost most of its lines.
- [ ] F3 git config: set `core.fsmonitor` in a session folder's repository to a script that writes a marker file, then open the Changes tab. The marker is not written.
- [ ] F4 attach: a session with a normal id still attaches; no `claude attach` line is built from a bad id (the session shows no terminal and the log says "bad session id").
- [ ] F5 close: close a session window while a pass runs; the window closes, and no second window takes the lock until the pass has ended.
- [ ] F6 close: close a session while the rail is busy; it is not resumed after the close.
