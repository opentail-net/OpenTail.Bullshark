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
- [ ] Ctrl+Shift+N with a terminal focused: does the chord reach the window (not the terminal), and does it show the next waiting session with the keyboard in its terminal?

Pass when: no name appears twice at any step, each step has its line, and F20 is answered.

## Gates (each needs one real step)

- [ ] **Worktree per coder** (additions item 3) and `--cwd` for listings: with the scratch repo trusted, rerun the gate in
  `additions-2026-11-17.md`.
- [ ] **`--bg --session-id <uuid>`**: does the session keep that id now? (F18 said no on 2026-11-14.)
- [ ] **A dead row's name**: does `claude --bg` accept a name that a dead listing row still holds? (round 8 B1.4)
- [ ] **`claude respawn --all`**: does it restart stopped sessions, and does a blocked session lose its prompt? (round 8 B3.2)
- [ ] **`CLAUDE_CONFIG_DIR`**: where does `.claude.json` live when it is set? (round 3)
- [ ] **OpenTelemetry** (gems item 3): one session with telemetry to the console, to see the attribute names.

## Decisions still open (no run needed; answer when convenient)

The list in `session-summary-2026-11-17.md`, "Waiting for the owner, Decisions": Windows toast package, start at usage reset,
deleting old transcripts, waking a `done` coder, a sound for a waiting session that goes stale, the Respawn click waiting for a
running pass, the light-theme tab label contrast.

## After the run

Write what you saw (one line per step is enough) in this file or tell the planner; the planner turns each line into a finding
(F65 onward) and a round plan.
