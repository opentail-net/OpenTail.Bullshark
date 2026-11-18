# Session summary, 2026-11-17 (autonomous run while the owner was away)

How the day ran: the planner (Opus) reviewed and researched, wrote a checkbox plan per round with the contracts it had to keep, and
handed each plan to a Haiku builder in its own worktree. Each branch was checked (build, tests, `git ls-remote`, key diffs,
screenshots) before it was merged to `main` and pushed. Independent read-only Sonnet reviews looked at each round's code. The owner's
pop-out work, then uncommitted, (`DetachedSessionWindow.cs`, `SessionWindow.PopOut.cs` and edits in five `SessionWindow.*` partials) was set
aside and put back for every merge; it was committed unchanged on the owner's word (`050e936`), except one line in `SessionWindow.Rail.cs` where both
sides changed it (both kept). Tests: 357 at the start, 1007 at the end.

## What was built (plans in this folder, decisions inside each)

| Plan | In short |
|---|---|
| `review-2026-11-17.md` | Trust file read once; pass off the UI thread; one listing per pass; launch length guard; testable clock; file lock fixes. |
| `additions-2026-11-17.md` | Needs you (rail group, flash, sound); Changes tab; run limits; run timeline; Markdown export; `--cwd` argument. Worktree per coder skipped (gate). |
| `bugs-2026-11-17.md` | Finished/stopped sessions no longer resumed (F61); restart-loop alert once; one bad transcript skipped; one way to open transcripts. |
| `gems-2026-11-17.md` | File-overlap alert; merge-conflict check with a copy-able resolve prompt; effort panel; scheduled runs; storage page with archive/restore (no delete). OpenTelemetry gated. |
| `bugs-round3-2026-11-17.md` | 16 fixes: search with `\`/`"`, startup with bad runs.json, locked pattern/ui/library stores, resume orphans and length, and more. |
| `round4-2026-11-17.md` | Unreadable stores never read as empty; one error filter; setup check for `VAR=1 cmd` and built-ins; first screenshots (`scripts/Test-NewScreens.ps1`). |
| `round5-2026-11-17.md` .. `round9-2026-11-17.md` | Reviews of the day's code: stop guard, event-log failures, limits from real start, archive safety, attention keys, incremental transcript reads, timeline paging, DST-safe start times, daemon health (F63) with stale rows, Respawn all with a hold, limits kept when a session is closed. |
| `round10-performance-2026-11-17.md` | Index and search speed on the owner's transcripts: the index built and the search run on several threads, a search pre-filter for escaped words, batched search, and an index cache kept between starts. Merged to `main` and pushed. Numbers in `docs/findings/perf-2026-11-17.md`. Open: the first index read with no cache, about 1.6 s. |
| `round11-2026-11-17.md` | Review of round 10 (findings R1, R2, R3, R5 and R7): the cache key from a fresh stat, a cache that never fails the history load, a locked atomic cache save, search and index with one thread fewer than the processors and cancellable, and a test that fails when the cache misses a summary or digest field. Merged to `main` and pushed. |
| `round12-2026-11-17.md` | A bad transcript line or file is skipped, not fatal, in every reader and in the window's pass; shared line helper; the merge check and Changes tab use the writes cache; bounded CLI output reads; supervisor reads logs for live sessions only; the log rotates at 5 MB; the index parses UTF-8 bytes (cold 1.69 s to 1.35 s). Merged to `main` and pushed. |

Findings added: F61 (`agents --json` hides finished/stopped sessions without `--all`), F62 (`--bg` refuses an untrusted folder),
F63 (daemon not running while the listing still shows sessions; the daemon runs on demand).

## Waiting for the owner

Decisions:
- Windows toast notifications need a package (CommunityToolkit or Windows App SDK). Today: flash + sound in the window only.
- Start when the limit resets: built 2026-11-18, see `limit-reset-start-2026-11-18.md`. Not checked in a running window.
- Limit in force on the status bar and the Limits history page (backlog A2, A4): built 2026-11-18, see `limit-status-and-history-2026-11-18.md`. Not checked in a running window.
- "While you were away" digest (backlog D1): built 2026-11-18, see `away-digest-2026-11-18.md`. Not checked in a running window.
- Should old transcripts ever be deleted (today: archive only)?
- Should a `done` coder in a live run ever be woken? Today: no.
- A sound when a session that is already waiting goes stale (daemon stopped)? Today: no new sound (round 8 B2.2).
- Respawn click waiting for a pass that is already running (round 9 item 2, optional part, needs a UI-thread change).
- Light theme tab labels look pale (#5A6270, about 5.6:1); left as is (round 4).
- Decided by the owner: a transcript line or file that cannot be read is skipped, and the rest loads (built in round 12).
- The first index read with no cache is about 1.35 s on the owner's data after round 12 (was 1.6 s); still over the 1 s line. No next step planned.

Gates (need one real, owner-run step; nothing was started on the owner's behalf):
- Worktree per coder (additions item 3) and using `--cwd` for listings: trust one throwaway git folder (`claude` there once, accept), then rerun the gate in the plan.
- `--bg --session-id <uuid>`: does the session keep that id now (F18 said no on 2026-11-14)?
- Live OpenTelemetry data (gems item 3): one session with telemetry to the console, to see the attribute names.
- Does `claude --bg` accept a name that a dead listing row still holds (round 8 B1.4)?
- Does `claude respawn --all` restart stopped sessions, and does a blocked session lose its prompt (round 8 B3.2)?
- Where `.claude.json` lives when `CLAUDE_CONFIG_DIR` is set (round 3).

Not verified on screen with real sessions: every screen was checked with `--no-pass`, a scratch state and a stand-in `claude`; none
with live sessions. The end-to-end run of the review of 2026-11-15 (Phase 3) is still open.

Housekeeping: done. The merged worktrees and branches, and the remote branch `review-fixes-2026-11-17`, were removed on the owner's word.
P4 (log-on supervisor) stays not wanted, as decided on 2026-11-15.
