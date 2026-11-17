# Round 7, 2026-11-17: use what the CLI now offers

Status: not started.

Source: research of 2026-11-17 into recent Claude Code CLI features (notes: `research-cli-features-2026-11-17.md`, items C1..C12),
checked by the planner against the installed CLI (2.1.326) on the owner's machine, read-only.

## Facts checked (planner, 2026-11-17, read-only commands)

| Fact | Evidence |
|---|---|
| `claude daemon status` printed `not running`, `control.sock: unreachable (connect ENOENT ...)`, `bg workers: 0 in roster.json (control unreachable)`, `roster.json: updated 5467s ago`, and the daemon log path — while `claude agents --json --all` still listed background sessions as `blocked` and `done`. So the listing can show sessions whose daemon is down. | Run locally |
| `claude daemon --help`: `run`, `status`, `logs`, `uninstall`, `stop` ("Shut down the supervisor and terminate background sessions"). `claude respawn <id>\|--all`: "Restart a background session (or all of them) so it picks up the current Claude binary." | Local help |
| `claude --help` lists `--session-id <uuid>`. Bullshark finding F18 says `--bg` ignored it (2026-11-14); the research says it now works with `--bg`. **Not verified**: needs a real start (gated, owner). | Local help; F18 |
| `agents --json` entries here carry `cwd, id, kind, name, sessionId, startedAt, state`; no `status` or `waitingFor` field was present on 2.1.326 (the docs mention `waitingFor`). `cwd` is the launch folder. | Local listing |
| No `.claude/worktrees` folder exists under the folders of the listed background sessions, so on this machine background sessions edit in place. The docs say `--bg` isolates into `.claude/worktrees/` unless `worktree.bgIsolation` is `none` (not verified locally). | Local folders; research C6 |
| Docs: `claude purge` and `cleanupPeriodDays` delete old transcripts (default not verified). | Research notes |

## Contracts this must satisfy

| Member | Promise |
|---|---|
| `AgentInfo.ParseList` | Unknown fields ignored; missing optional fields are null. |
| `ClaudeCli` | Read-only commands only, except owner-clicked actions (stop, rm, start). Never runs `daemon stop`, `daemon uninstall` or `respawn` on its own. |
| `SupervisorPolicy` | Unchanged by this round, except where an item says so. |
| Changes tab / `FileOverlap` / `MergeCheck` | Work on the folder the session really edits. |

## Items

- [ ] **1. Daemon health.** `ClaudeCli.DaemonStatusAsync()` runs `claude daemon status` (read-only) and parses: running or not, pid, version, uptime, control reachable, worker count, roster age, log path. Unknown output → "unknown", never an error. The window reads it each pass in the background step. When the daemon is not running or control is unreachable: a "Needs you" row "Background daemon is not running — sessions listed as working/blocked are not alive", the status bar says so, and **the supervisor makes no start or resume decisions that pass** (a listing from a dead daemon is not trusted, like an unreadable listing). Tests with captured outputs (running, not running, unreachable, garbage).
- [ ] **2. Daemon version drift.** When `daemon status` shows a version different from `claude --version`, show a notice with a "Respawn all…" button that asks for confirmation and runs `claude respawn --all`. Never automatic. Tests on the version compare.
- [ ] **3. Forward-compatible listing fields.** Parse optional `status` and `waitingFor` (string or object; keep the raw text). When present, the Needs-you row and the tab tooltip say what it waits for (permission, input, dialog…). Tests with samples that have and lack the fields.
- [ ] **4. Work on the folder the session really edits.** Read the latest `cwd` from the session's transcript records (if the transcript lines carry one; check a real transcript read-only, and a test fixture); prefer it over the listing's `cwd` for the Changes tab, file overlap and the merge check. Paths compared normalised. Tests: a transcript whose `cwd` is under `.claude/worktrees/x` makes those features use it.
- [ ] **5. Transcript clean-up warning.** Read `cleanupPeriodDays` from `~/.claude/settings.json` (read-only; absent → say "Claude's default", without a number unless the docs page states one — check code.claude.com/docs settings page and record the URL). The Storage page shows "Claude deletes transcripts older than N days; archive to keep them" and marks sessions within 3 days of that age. Tests on the age rule.
- [ ] **6. `--session-id` with `--bg` (gated).** Do not build. Record the gate for the owner: one throwaway start with `--bg --session-id <uuid>` in a trusted folder; if the session id matches, Bullshark can name the transcript before launch and drop the read-back poll.
- [ ] **7. Ideas recorded, not built** (write them under "Later" in this file with the research references): HTTP hooks per run for live overlap and limits (C3), `/goal` as a run's stop condition (C9), OpenTelemetry cross-check (C11).

## Decisions made while building

(builder writes here)
