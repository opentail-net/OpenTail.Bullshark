# Live run checklist: the owner's sitting

One sitting with the owner at the screen and the real `claude` CLI. Every line here needs a person to look, click, or watch a real session. Work through it in section order. Section B (the end-to-end run) comes first because the screen checks use real sessions. Probes of the CLI that need a throwaway folder are in [cli-gates.md](cli-gates.md); build work is in the plan docs listed in [00-current-work.md](00-current-work.md). Record one line per step. A check that could touch the owner's data says so in its own line.

Expected time: about 1 to 2 hours (an estimate: it depends on how long the plan > code run takes). What it costs: real `claude` sessions (use Haiku at low effort for the run), and one trust prompt you accept by hand.

## A. Before you start

- [ ] Build: `dotnet build OpenTail.Bullshark.slnx -nodeReuse:false`.

- [ ] Make a scratch folder for the run (a throwaway git repo: `git init`, one commit), and a scratch state folder in `%TEMP%`.

- [ ] In the scratch repo, run `claude` once and accept the trust prompt (this is also the first gate below).

- [ ] Start the window with `--state <scratch state folder>\state.json` so nothing touches the live state. Leave `--claude-home` at
  its default (the real one): the run needs the real CLI.

- [ ] Keep a second terminal open for `claude agents --json` after each step.

## B. End-to-end run

After each step: run `claude agents --json`, count the attach processes (Task Manager, `claude.exe` with `attach`), and write one line.

- [ ] Start a plan > code run in the scratch repo. Watch one batch and one report.

- [ ] Close a tab only; then close a session.

- [ ] `claude stop` and `rm` one session; watch the resume (F20 is still open).

- [ ] Restart the window: the same tabs come back, and no new session starts.

- [ ] Restart the daemon (or the PC): note what is listed (finding 12; F63 says the daemon runs on demand).

- [ ] Resume in full; resume from a summary; look for the summary call in History (finding 15).

- [ ] Open the screens the rounds built and see them with live data: Needs you, Changes tab, merge check, effort panel, run
  timeline, Storage page (do not press Archive), file-overlap notice.

- [ ] Ctrl+Shift+N with a terminal focused: does the chord reach the window (not the terminal), and does it show the next waiting session with the keyboard in its terminal?

- [ ] End run. Alerts in the window. Attach on demand. Hourly login check (17, 18, 20).

Check: with five recorded sessions, one attach process per opened tab; a blocked session shows a badge.

Pass when: no name appears twice at any step, each step has its line, and F20 is answered.

## C. The screens

### C1. Rail, folders, tags, names

- [ ] Plan progress: in a session's Edit dialog, Browse… picks a Markdown file; a relative path is refused with a red line. The row shows
  "3/7" after the cache and state; ticking an item in the file and waiting for the next pass (or 🔄) changes it. Its tooltip gives "Next: ...".
- [ ] Plan file removed or cleared in Edit: the figure goes; the library still loads with the rest of the marks.
- [ ] Context meter: a session with a transcript shows "ctx N" on its live row and its history row, after the state. A window of 200k with
  a last request of about 170k shows "⚠" after it; Settings, General, "1M" removes the mark. The tooltip still gives the cache's tokens.

- [ ] P9 folder names on real data: the History folder header shows the folder's name (for example `Git` or `OpenTail.Bullshark`), not `C--Git-...`; its tooltip shows the full path. The Storage page's project rows show the same names. A group names a folder only from sessions whose working folder encodes to the group's own key: a group whose sessions all moved to a sub-folder keeps its raw key (for example `C--Git-Public`). The groups themselves are unchanged: each folder's sessions are under the same header as before.

- [ ] P9 cache: the first start after this build reads every transcript again (the cache from before is not used). The second start is quick and the names are the same.

- [ ] T1 folder names: search for a word that several sessions say. Each result's folder line shows the folder's name (for example `OpenTail.Bullshark`), and its tooltip shows the whole path. Settings, General is unchanged. Usage (Insights): "Top project" and the project table show the same names.

- [ ] Tag filter: the rail's show menu lists the tags in use with their counts; a tag shows only its rows in History and the folders,
  the filter box reads "show: all · #tag", and a second click on the tag clears it. Needs you still lists every waiting session.

- [ ] Tag filter with a search: a row must match both. A tag that no session has any more clears the filter, with a status line.

- [ ] Palette: "Filter by tag #x" and "Clear tag filter" do the same as the menu.

- [ ] Group by tag: the show menu's "Group by: Tag" lists the History tag groups with their counts; a session with two tags is under both; "No tag"
  is last; "Group by: Folder" brings the folders back. Needs you and the tag filter behave as before.

- [ ] Magic rename on a named session ("Magic rename again"): the owner's tags stay, up to two tags are added, and the status line says so.

- [ ] Magic rename again (row menu, a session with a magic name): the new name is not the old one. When the model repeats it, the name
  comes back with " - 18 Nov" (or " 2") after it. The old name is free again for other sessions.

- [ ] Automatic naming: with "Toggle automatic magic naming" on, leave the window open on a never-named session of at least four messages
  until its cache goes from 🔥 to ❄ (about an hour, or five minutes for a five-minute cache). The status line says "Automatic: named N
  session(s) that went cold". A session that was already ❄ when the window opened is not named.

- [ ] Turn it off again: nothing that was waiting in the queue is named later.

- [ ] Magic rename, not seen in a running window: the wand and its tooltip, the row menu items, the confirm dialogs, the palette entries and toggle, the status lines, the History mark, and the wand's emoji (it relies on font fallback). Source: Done/magic-rename-2026-11-18.md.

### C2. Needs you, notices, toasts, supervisor screens

- [ ] L3 silent notice, real data: start a coder that runs a command longer than the threshold (or set Settings, Naming, "A working session with no output for" to 5 minutes for this check). The rail's Needs you row shows "working · ... no output for 5 min; may be a long command" at the end of its activity line, one sound and one Notices toast when the window is not in front. Let the session write again: the notice goes away. Silent again: one more sound.

- [ ] N4 quiet hours: in Settings, General, pick a window that contains now (for example one that starts an hour ago and ends an hour from now). Let a session start waiting: no sound, no flash, no toast; the rail's Needs you still lists it; the log says "needs you (quiet hours)". Pick Off: the next wait sounds again.

- [ ] N4 quiet hours and escalation: during the window a re-alert that falls in it gives no sound (the log says "again ... quiet hours"); after it ends, the session still waiting sounds again at its next interval.

- [ ] Toasts by event: with the window minimised, each kind can be switched off under Settings, General, and its toast does not come
  (blocked, problems, notices). Sound and flash come as before for a wait whatever the switches say.

- [ ] Ready for review: a coder that finishes with changes gives one silent toast "Ready for review" when it joins the list, and none on
  the first pass after the window opens. Opening its Changes tab (or Mark reviewed) and no new work gives no toast.

- [ ] Usage limit reset: with a limit in force, the first pass after its reset gives one silent toast "Usage limit reset" with its name
  ("5-hour", "weekly"), and nothing when the window is in front.

- [ ] Toasts switched off as a whole (Toast switch off): the five kinds are dimmed and give no toast.

- [ ] Q6 Settings: change a toast switch or the quiet hours in Settings and check that the next pass announces by the new setting (the toast or the log line), and that the Settings page shows it at once.

- [ ] **Two windows on one state** (reliability batch R1): the second window says it only watches, under Needs you, and starts, resumes and alerts nothing. Close the first one: the second takes over on a later pass.

- [ ] **A Trust step open for a minute** (reliability batch R3): `claude` is run about 4 times a minute for the passes, not 23 (screenshot fixture: 24 calls in 60 s, against 118 before).

- [ ] **A broken `runs.json`** (reliability batch R9): a "Supervisor problem: runs.json cannot be read ..." row under Needs you and one Problems toast; the row clears when the file is good again.

- [ ] Escalation, snooze and acknowledge, not seen in a running window: the sound, the flash, the menu, and the tab acknowledgement. A session whose tab is already active when it starts waiting is not acknowledged, so it re-alerts (judged right; the owner may not be at the PC). Source: Done/escalation-2026-11-18.md; Done/backlog-2026-11-18-done.md (D2).

### C3. Settings, palette, first-run settings

- [ ] Settings: each switch and choice (notifications, automatic naming, the re-alert interval, theme) writes ui.json, and the page shows the
  value the file holds afterwards. A file that cannot be written shows "Not saved" and the old value.

- [ ] L3 Settings: Naming shows the row "A working session with no output for" with Off, 30, 45, 60 and 120 min; a value set by hand in `ui.json` shows as "Custom (N min)" and is not rewritten. Off: no notice at all.

- [ ] N4 Settings: the presets write `quietHours` in `ui.json` (an object, or null for Off); a window set by hand in `ui.json` shows as "Custom (HH:mm-HH:mm)" and is not rewritten by a click.

- [ ] Context window in Settings: "200k (assumed)" is selected by default; a value set by hand in ui.json shows as "Custom (N)" and is not rewritten.

- [ ] B6 theme: with no `railTheme` in `ui.json` and Windows in light mode, the rail starts light; in dark mode, dark. After a choice with the rail's switch, the choice wins at the next start, whatever Windows says.

- [ ] B9 attention file: Settings, Claude, switch on: `attention.json` appears beside the state file after a pass, with `at`, `count` and the reason codes. Switch off, or close the window: it is deleted.

- [ ] Palette: Open Insights, Open Settings, and Open Timeline, Storage, Limits and Away open Insights on their sub-tab.

- [ ] Command palette, not seen in a running window: the chord (Ctrl+Shift+P), the "Go to..." button, the palette window's look, the focus return to a terminal after Esc, the actions' effects on the rail, and Ctrl+Shift+P with a terminal focused. Source: Done/palette-2026-11-18.md; Done/keys-and-find-2026-11-18.md; Done/backlog-2026-11-18-done.md (D3).

### C4. Changes and review

- [ ] Viewed marks: in Changes, tick two changed files; restart the window and open Changes again: they are still ticked, and "3 of 7 viewed"
  (or the count you have) is right. Edit a ticked file in the scratch repo: its tick clears and the count drops. "Clear" removes the session's ticks.

- [ ] Big-deletion check: delete a tracked file in the scratch repo, and empty a tracked file of most of its lines: each shows "check: ..." on its row,
  the count line says how many, and no sound or toast comes.

- [ ] F2 Changes: stage one changed file (`git add <file>`) and leave a second change unstaged. The Changes tab lists both files, and the staged one shows its hunks and its counts. The deletion label still shows for a file that lost most of its lines.

- [ ] F3 git config: set `core.fsmonitor` in a session folder's repository to a script that writes a marker file, then open the Changes tab. The marker is not written.

- [ ] Overlap, not seen in a running window: the rail mark, the Needs you row, and the overlapping files at the top of Changes. Source: Done/gems-2026-11-17-done.md (D1.8).

- [ ] Merge check, not seen in a running window: the ⚠ mark, the Needs you row, the Changes button, and the clipboard copy. Source: Done/gems-2026-11-17-done.md (D2.8).

- [ ] Ready for review lane, not seen in a running window: its rail section, its menu, and the mark on Changes. Source: Done/ready-for-review-2026-11-18.md.

### C5. Transcripts, search, Insights, Night

- [ ] T4 find on a long real transcript: open the biggest transcript in the history (read only, `--no-pass`, scratch state). Press Ctrl+F and type a word that is in it: the first hit is highlighted, the count shows "1 of n", and the log's `find:` line gives the time (target under 1 s). Enter and Shift+Enter move through the hits; Escape closes the bar and the highlight goes.

- [ ] T5 jump keys: with the transcript focused (click a line), `]` and `[` move between prompts, `}` and `{` between failed results, `Alt+]` and `Alt+[` between file writes, `Ctrl+]` and `Ctrl+[` between compactions. The log's `jump:` line gives the time of a jump that draws a new page. With the find box or the rail's search box focused, `[` and `]` type text and move nothing.

- [ ] T3 copy: right-click a Bash call line, "Copy command", and paste it into a text file: the full command. Right-click a long tool result, "Show full result": the box shows the text with its length; a result over 200,000 characters shows the cut note. Ctrl+C on a focused message copies it. Nothing is sent to a session (a terminal with the keyboard gets no paste).

- [ ] T2 keyboard: in a search result, Tab moves from session to session and to each matching line; Enter opens it at that line (the transcript then has the keyboard, so `Ctrl+F` works at once). The effort panel's items take Tab and open with Enter or Space.

- [ ] Q3 transcript: open the largest transcript and press "Load earlier" about six times: the lines past 1,000 are released and the one muted line under the header says which lines are shown; a find or a jump to a released line shows it again. Note the `transcript earlier page` lines in the log.

- [ ] Same-call loop: in a real transcript where a command failed three times in a row, the effort panel lists "Same call failed 3 times in a row"
  with its lines, and a click shows the first one. A run of fail, edit, pass is not listed.

- [ ] Where the effort went, not seen in a running window: the panel's layout and the jump to a line. Source: Done/gems-2026-11-17-done.md (D4.6).

- [ ] Export a past session, not seen in a running window: the Export button and the saved file. Source: Done/additions-2026-11-17-done.md (D7.5).

- [ ] Insights: the four sub-tabs (Away, Usage, Timeline, Storage) switch with the mouse and with Left and Right; the window reads the
  page again when it is shown; the sub-tab shown last comes back when the Insights button is pressed again.

- [ ] A return to the window with news opens Insights on Away; Got it closes Insights when the return opened it.

- [ ] Limits page (2026-11-18 usage plan): it shows usage per day and top projects under the hits; the numbers are plausible against the Storage
  page; the page stays responsive on the owner's data (the first open after a restart is the cold read).

- [ ] Limit status, not seen in a running window: the status-bar text and its tooltip, the Limits button, and the page. Source: Done/limit-status-and-history-2026-11-18.md.

- [ ] Away digest, not seen in a running window: the page, the Away button, and the automatic opening on a return. Source: Done/away-digest-2026-11-18.md.

- [ ] Storage page, not seen in a running window: its checkboxes, the archive confirm box, and the History reload. Its Archive button is disabled until the first listing (by code only). Do not press Archive on the owner's data. Source: Done/gems-2026-11-17-done.md (D6.7); Done/round6-2026-11-17.md.

- [ ] N1 heartbeat: with the window open, `heartbeat.json` beside the state file changes about every 15 seconds (its "at" is recent). Close the window, wait two minutes, open it again: the Night page says "Last pass" with the age the file gives, and "Window/pass" is Check while the window was closed.

- [ ] N2/N6 preflight: Insights > Night shows ten rows and a verdict. Refresh updates "Last pass". A check that says "check" is one the owner can act on; "unknown" means the value could not be read (for example the login, before a live session was read).

- [ ] N2 Sleep row on this PC: the row reads "sleeps after N min on AC power" (or "never sleeps"), and N is the "Put the device to sleep" value under Windows Settings > System > Power & battery > Screen and sleep, on AC power. Compare, do not change it: keep-awake is not part of this batch.

- [ ] N2 Runs row: with a run waiting for a start time within 12 hours, the row names it and its start time; with none, "none waiting to start in the next 12 h".

- [ ] N5 report: return to the window after at least 30 minutes away with something to say. `reports\away-yyyy-MM-dd-HHmm.md` appears beside the state file; the Away page says "Saved to reports\..." and "Open reports folder" opens it in Explorer. The file has the digest, a "Plans" section for sessions with a plan file, and a "Tokens in this period" line. A second return within the same minute gets a `-2` name, and nothing is overwritten.

- [ ] N5 cleanup: a file in `reports\` that is not named `away-yyyy-MM-dd-HHmm.md` (or with `-n`) is never deleted, even with more than 60 reports.

- [ ] N3/N6 palette: "Open Night (preflight)" in the command palette opens the Night sub-tab; `--open-tab night` (the screenshot option) does the same.

### C6. Sessions and setup

- [ ] Run limits, not seen in a running window: the setup fields and the notices in the log (`LimitNear`, `LimitReached`). Source: Done/additions-2026-11-17-done.md (D4.7).

- [ ] Run timeline, not seen in a running window: the Timeline tab and its chooser. Source: Done/additions-2026-11-17-done.md (D5.7).

- [ ] Start when the usage limit resets, not checked in a running window: the checkbox, the waiting row, and the run line. No real limit was hit in the test. Source: Done/limit-reset-start-2026-11-18.md.

- [ ] S7 Screenshot review of the whole window with the owner. Decide D-S1 and the open questions.

Session library, the on-screen boxes of L2 to L5 (the steps themselves are in session-library.md):

- [ ] The transcript view's heading and the resume prompt's heading use the same title. By code reading only: both read
      `digest.Title`. Not seen on screen: a transcript opens with a click, and clicks were not simulated.

- [ ] Checked on screen with a temporary state folder: a session with an `ai-title`, one without, and a subagent row. Partly: an
      `ai-title` row and a row without one were seen. No subagent row is in the screenshot.

- [ ] The row menu and the edit dialog (D-L10), on history rows and on live session rows. Not on folders, headings or failed starts.
      Built: `RowMenu` and `EditMark` in `SessionWindow.Library.cs`; the dialog is `LibraryEditDialog.cs`. Not seen: the menu and the
      dialog need clicks, which are for the owner. Decision to confirm: Pin and Archive are on history rows only. A live session is
      not archived from the rail, so the live row's menu offers Edit only.

- [ ] Titles use `TitleFor`; tags show on the second line; the note shows in the tooltip. Seen: "Plan for L4" in the Pinned group, in
      the dark and the light theme. Built, not seen: the tags on the second line are cut at the default rail width. They, and the note,
      are at the top of the row's tooltip, which needs a hover (for the owner).

- [ ] The colour bar on the rail row and on the tab (D-L11), in both themes. Seen: the rail row's bar (Blue) in the dark and the light
      theme. Built, not seen: the tab's top bar (`DrawStrip`), because a tab needs a click. The Red session is not in the visible part
      of the list.

- [ ] Checked on screen with a temporary state folder: rename, pin, unpin, archive, the Archived filter, unarchive, a tag, a note, a
      colour in the dark and the light theme; then close and reopen the window and see them again. Seen: the Archived filter; a colour
      in both themes; a fresh start reads `library.json` and shows the Pinned group, which is the reopen. Not done: rename, pin, unpin,
      archive, unarchive, a tag, a note and a colour changed by clicks (for the owner). Their writes are unit-tested in
      `SessionLibraryTests`.

- [ ] `SearchView` shows "matched its name, tags or note" for a match with no messages. Built (`SearchView.cs`). Not seen: a search needs
      typing, which is for the owner.

- [ ] The rail tooltip says "continues: <title>". The transcript view shows "Continued from…" and "Continued in…" links. Built
      (`ContinuesTip`, `ShowLineage`, `LineageLink`). Not seen: a hover and a transcript click are needed, and no continuation exists yet
      to show the links.

### C7. Window, tabs, zoom, look (both themes)

- [ ] Checked on screen with a temporary state folder: three tabs, reorder, wait for one rail refresh (the order holds), reopen.
      Not done: the drag was not driven on screen. For the owner.

- [ ] Checked on screen with real sessions: planner left, coder right, both live; resize the splitter; collapse the rail;
      paste into each pane after a click in it. For the owner.

- [ ] Hover and drag of the slider with the mouse: not driven by any check.

- [ ] Zoom: the slider, the percentage and the − and + at the bottom right of the status bar still zoom; Ctrl+plus, Ctrl+minus and Ctrl+0 do the same.

- [ ] Zoom keys not checked yet: Ctrl+0, the numeric keypad, and a key in the rail's search box. Source: zoom (Done/zoom.md, Keys).

- [ ] The status bar on a 1366 px window keeps the zoom, Settings, Insights and Go to… at the right, with Retry, Respawn and Trust only when they show.

- [ ] P1 check boxes: on Changes (a session with changed files) the "viewed" box is a small rounded box; ticked, it is filled blue with a white or dark check mark. The Setup box "Start when the usage limit resets" and the Storage transcript boxes look the same. Clicking each still works (the tick stays, the box follows the setting).

- [ ] P2 text boxes, lists and the zoom slider: a text box has a rounded edge that turns blue when it has the keyboard. The zoom slider in the status bar is a thin track with a round thumb; the zoom still steps in the same places.

- [ ] P3 scroll bars and tooltips: a dialog (Edit session, Instructions, Go to) has the same thin scroll bar as the window. A tooltip is a rounded box in the menu colour, wraps past about 420 px, and has no internal id in it.

- [ ] P4 combo boxes: Setup's Pattern, Coders, Folder (type a path: it still takes the typing), Planner, Reviewer and Assistant boxes, and the Timeline's Run and Session boxes open the same rounded list in both themes. The colour box in Edit session follows the theme.

- [ ] P5 keyboard: on the setup page, Tab goes through Task, Folder, Browse, Pattern, Coders and the models, and reaches Start last. On Insights and Settings, Tab reaches only the chosen pill; Left and Right move along the row.

- [ ] P7 contrast: in the light theme the "check:" labels on Changes are a dark amber that reads on the grey panels; in the dark theme the control edges (buttons, boxes, combo boxes) are visible without being bright. Numbers are in the plan.

- [ ] P8 tooltips: hover the Setup Reviewer box and its footnote mark. Each is one or two lines, with no "(S9c)" or "(D-S10)" in the text.

- [ ] Button style, not seen on screen: the context menus (row, filter, tab), the Close and Resume prompts, the Idea, Edit and Instructions dialogs, the default-button accent, disabled and pressed states, and long status-bar labels on a narrow window. Source: Done/button-style-2026-11-18.md.

- [ ] Rail: the Trust row was cut at its edge in the round 4 screenshot. Look at it in the owner's rail. Source: Done/round4-2026-11-17.md.

### C8. Resume (after resume-preview.md is built)

- [ ] P6 Resume: the Resume button in the resume prompt is the same green as Start, not grey (the resume prompt was not in the screenshots: check it by hand).

- [ ] R1 preview, tail: open Resume on a real past session (History, then Resume). The Resume button has a narrow "▾" part on its right; open it and choose "Preview the prompt…". The pane shows "Prompt the new session starts with: about N tokens (C characters)." and the text, read-only, with no model call. The text starts with "Continues session: <that session's id>".

- [ ] R2 preview, summary: choose "From a summary", a level, then ▾ > "Preview the prompt…". The tooltip says it makes the one Haiku call. Press Resume afterwards: the log has one `summary saved: …` line for the preview and no second one, and the new session starts from the same summary text as the preview.

- [ ] R3 copy: ▾ > "Copy the prompt", then paste into a text editor: the same text as the preview. Nothing is typed into any session.

- [ ] R4 full: choose "In full": both menu items are off, and their tooltips say "Resuming in full sends no prompt."

- [ ] R5 stale: after a preview, change the level or the turns: the pane hides. Change the level, then back: the summary is made again (a new call, expected).

- [ ] R6 log: `logs\app.log` has `preview built, N chars` lines and no transcript text.

- [ ] R7 look: the split button matches the other green buttons in both themes, and the ▾ menu opens under the button without leaving the dialog's screen.

### C9. Diagnostics, first run, setup check

- [ ] B5 launcher: double-click `scripts\Start-OpenTail.Bullshark.bat` after a build. The window opens with no demo folder or task. With no build it says so and waits for a key. `scripts\Start-Demo.bat` opens the demo task as before.

- [ ] B5 version: Settings, Claude shows `Version 0.1.0 (<hash>) · .NET 10`. The number 0.1.0 is an assumption until the owner names one.

- [ ] B1 claude on PATH: run the setup check with `claude` off PATH (or on a PATH without it): the first row "Claude Code · claude" is a problem with the install sentence beside it. With it on PATH: "found: <path>".

- [ ] B2 errors: with `claude` off PATH, a pass shows "Claude Code could not be started: …" on the status line and under Needs you, not the exception.

- [ ] B3 crash file: run `OpenTail.Bullshark.App.exe --crash-test --state <scratch>\state.json`. The status line says "The app hit an error and kept going: logs\crash-….txt"; the window stays open; the file is in `<scratch>\logs\`. Read it: no full user path, no token, the exception and the last lines are there, and the file is redacted.

- [ ] B4 diagnostics: Settings, Claude, "Save diagnostics…". The status line names `diagnostics\diagnostics-….zip` and the folder opens. Open the zip: six entries; `settings.txt` has values for booleans and numbers and no note text; no transcript, `library.json`, `runs.json` or `cache` entry. Read the rest for anything private before sharing it.

- [ ] B7 shortcuts: Ctrl+Shift+P, "Keyboard shortcuts": the sheet lists the chords with their scopes, in both themes. A transcript's hint line reads as before.

- [ ] B8 empty states: a scratch state with no transcripts: Storage names the folder and offers "Open Claude's folder". A scratch state with no runs: Timeline says "No runs yet. Start one from the + tab." and "Open the setup tab" opens the setup tab.

- [ ] Seen on screen: the dialog's layout, the colours in both themes, and the button's place in the bar.

### C10. Review fixes and lifecycle

- [ ] F1 paste: copy text that contains an escape character (for example in PowerShell: `Set-Clipboard "a`e[201~b"`), paste it into a session. The session gets `a[201~b` with no escape: nothing after the text runs as typed input.

- [ ] F4 attach: a session with a normal id still attaches; no `claude attach` line is built from a bad id (the session shows no terminal and the log says "bad session id").

- [ ] F5 close: close a session window while a pass runs; the window closes, and no second window takes the lock until the pass has ended.

- [ ] F6 close: close a session while the rail is busy; it is not resumed after the close.

- [ ] L2 backoff: a session the daemon drops and cannot resume (use the fake claude of `scripts/Test-NewScreens.ps1` with a state record whose session is not listed): the first resume is at once; the next one waits one minute, the one after two more; the third restart in the hour is followed by one restart-loop alert and no more resumes until an hour has passed.

- [ ] L1 name clash (cannot be seen without two live sessions of one name, which the window never starts itself): with the fake claude, list one working session under the planner's name (`FAKECLAUDE_AGENTS`) and point the state record at another, unlisted session id. Run one pass (not `--no-pass`): Needs you shows "name taken by another session", the log says "ALERT NameTaken", and `calls.log` has no `--bg`. Stop the listed row (list `[]` or a `done` row): the next pass resumes. The CLI's own behaviour on a name clash is not verified (see the plan).

- [ ] Closing the window (or a tab whose session ended) writes no "detach failed" line to `logs\app.log`. If one appears, note the
  exception and whether the session was still running.

### C11. Performance and soak

- [ ] Q1 12-hour soak: start the app as usual, leave it idle with the owner's real sessions for 12 hours, then close it. Read `logs\app.log` for the `soak (...)` lines (README, "Soak log"). Expected: a steady working set, private bytes, handles and threads over the hours (no climb); `terminals` equals the live sessions; `ui.json reads` shows 0 on the UI thread per pass once the Q2 build runs (background reads one per pass). Note any climb in handles or threads, and the largest `last pass` and `last rail` values.

- [ ] Q5 rail: scroll the rail down, wait two minutes with nothing happening: the rail keeps its scroll position and the selected row. A new session or a changed state redraws it.

- [ ] Limits and Away on the owner's data after a reboot: the first open is a cold read. Its time is not measured yet. Source: Done/limit-perf-2026-11-18.md.

### C12. Limit wake (after limit-wake-2026-11-20.md's phase 1 is built)

- [ ] LW1 shows: a session the owner flags with "Wake after the limit resets" (row menu) that stops at a real limit shows `Limit resets HH:mm · wakes after` on its row and in Needs you. Without the flag the text has no "wakes after".

- [ ] LW2 wakes once: after the reset (plus two minutes) the flagged session that is NOT running is started once; its transcript gets the line "The usage limit has reset. Please continue where you left off." and the log has one `limit wake:` line with the short id, the hit time, the reset time and "resume in full". It is not started again before the reset, nor twice for the same hit.

- [ ] LW3 the owner's stop holds: a session the owner stopped (listed as stopped) is not woken; Needs you says "listed as stopped or failed: not woken" in the row text and the log has one `limit wake of … not started` line.

- [ ] LW5 a second hit of a session that was woken: it is NOT woken again by this feature. A wake creates a record (the session is kept alive in the rail), and the rule then says "kept in the rail: not woken". Check what really happens on a second hit, and whether the supervisor's own restart fires before the reset (see the review notes in `limit-wake-2026-11-20.md`).

- [ ] LW6 running: a flagged session that is still running after its reset gets the row text "running: open it to continue" and one toast "Limit reset … can continue. Open it.", and is not started.

### C13. Codex provider (stage C; not seen on screen yet)

Codex is installed on this machine as a `.cmd` shim, so a Codex tab starts through `cmd.exe /d /c`. None of these has been run on screen.

- [ ] Dropdown: "Claude | Codex" at the top of the rail. Choosing Codex swaps the rail to Codex history grouped by folder, and the Claude rows, Needs you, limits, the filter and search go away with an empty-state line. Choosing Claude again brings them back, and tabs already open stay in place.
- [ ] "New Codex session…": the folder dialog opens; a Codex tab runs `codex -C` in the folder and the Codex screen appears in the tab. Check that the cmd.exe wrapper really starts Codex (the shim) and that the tab shows its own screen.
- [ ] Double-click a Codex history row: a tab runs `codex resume <id>`. A second double-click on the same row activates that tab; no second process starts.
- [ ] The "+" tab under Codex starts a Codex session, not Claude's run setup.
- [ ] Closing a Codex tab: does the Codex process end with the terminal? (open point 1 in the plan.)
- [ ] Setup check under Codex: one item, "codex found" with its path, or a problem if it is missing.
- [ ] Under Codex, a Claude run that is due on an untrusted folder does not open a Claude terminal; the status line says it waits for trust, and switching to Claude answers it.
- [ ] Restart the window: Codex tabs are not restored (expected; plan C-D5).

## D. After the run


Write what you saw (one line per step is enough) in this file or tell the planner; the planner turns each line into a finding
(F65 onward) and a round plan.

## E. Where the checks come from

The batch each group of checks above came from, for the record:

- Tags and automatic naming: [Done/tags-and-auto-magic-2026-11-18.md](Done/tags-and-auto-magic-2026-11-18.md)
- Night batch: [Done/night-batch-2026-11-19.md](Done/night-batch-2026-11-19.md)
- UI polish batch: [Done/ui-polish-batch-2026-11-19.md](Done/ui-polish-batch-2026-11-19.md)
- Insights and Settings: [Done/insights-and-settings-2026-11-18.md](Done/insights-and-settings-2026-11-18.md)
- Desktop batch (context meter, plan progress, toasts): [Done/desktop-codex-batch-2026-11-18.md](Done/desktop-codex-batch-2026-11-18.md)
- Fixes batch: [Done/fixes-batch-2026-11-18.md](Done/fixes-batch-2026-11-18.md)
- Features batch: [Done/features-batch-2026-11-18.md](Done/features-batch-2026-11-18.md)
- Review fixes, 2026-11-19: [Done/review-fixes-2026-11-19.md](Done/review-fixes-2026-11-19.md)
- Lifecycle batch: [Done/lifecycle-batch-2026-11-19.md](Done/lifecycle-batch-2026-11-19.md)
- Transcript and search batch: [Done/transcript-search-batch-2026-11-19.md](Done/transcript-search-batch-2026-11-19.md)
- First-run batch: [Done/firstrun-diagnostics-batch-2026-11-19.md](Done/firstrun-diagnostics-batch-2026-11-19.md)
- Perf and testability batch: [Done/perf-testability-batch-2026-11-19.md](Done/perf-testability-batch-2026-11-19.md)
- Review of 2026-11-15, phase 3 and 4: [Done/review-2026-11-15.md](Done/review-2026-11-15.md)
- Resume preview: [resume-preview.md](resume-preview.md)
- Limit wake: [limit-wake-2026-11-20.md](limit-wake-2026-11-20.md)
- Setup check: [Done/setup-check.md](Done/setup-check.md)
- Session summary of 2026-11-17: [Done/session-summary-2026-11-17.md](Done/session-summary-2026-11-17.md)
