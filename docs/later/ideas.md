# Parked ideas

Ideas that are not built. Each line says what it would take and who decides. Nothing here is scheduled; [00-current-work.md](../plans/00-current-work.md) says when one comes up. Built ideas are in the record under [Done](../plans/Done/). Older parked notes: [claude-scrollback.md](claude-scrollback.md) and [dynamic-configuration.md](dynamic-configuration.md).

## Desktop app ideas (open)

| # | Idea | Desktop does (source) | In Bullshark | Size | Needs | Fit |
|---|---|---|---|---|---|---|
| A2 | Hook event feed | 33 hook events incl. `Stop`, `StopFailure`, `PermissionRequest`, `Notification`, `SessionEnd` (https://code.claude.com/docs/en/hooks) | Hook scripts append events to a local file: instant state, no 15 s poll | M | hook entries in settings (owner); scoping to `--bg` sessions UNCONFIRMED | observe only; never answer `PermissionRequest` |
| A4 | Deny-only guard hooks | `PreToolUse` can block a call (https://code.claude.com/docs/en/hooks) | Block `rm -rf`, `git push`, `git reset --hard` in our sessions only | M | hook script; per-session settings UNCONFIRMED | deny only |
| A5 | Catch-up and skip reasons | One catch-up for the latest missed run within 7 days; history gives skip reasons (https://code.claude.com/docs/en/desktop-scheduled-tasks) | Same rules for our scheduled runs; history says why a run did not start | S | owner sets catch-up (default off) | code only |
| A6 | Keep awake while work is due | "Keep computer awake"; a run is skipped if the machine sleeps through it (same page) | Hold the sleep block while a run is due within the hour or a session works | S | `SetThreadExecutionState` (Win32, no package); the owner's power call | local, no service |
| A7 | Permissions in effect | "Always allowed" panel per task, revocable (same page) | Per run: mode and allow rules, to tune the allowlist after a `blocked` | S | settings read | read-only |

## Codex ideas (open)

| # | Idea | Codex does | In Bullshark | Size | Needs | Fit |
|---|---|---|---|---|---|---|
| B1 | Second-opinion review of a diff | `/review`: a dedicated reviewer reads the diff and edits nothing [A] | Read-only review of a finished coder's diff, listed in Changes | M | owner OK on usage; hidden `claude -p`, as magic rename does | sends and accepts nothing |
| B5 | Blocked-prompt helper | Auto-review screens exfiltration, credential probing, destructive actions [D] | On a blocked row: the prompt, a risk note, an allow rule to copy | M | prompt text (unconfirmed); a model call | partial: advice only, approval stays with the owner |
| B6 | Subagent tree under a session | `/subagents` switches agent threads; Active and Done lists [C] | Subagent runs under the session's row, read-only | M | transcript reader; Claude's `subagents/agent-*.jsonl` (57 files seen locally) | viewing only |
| B7 | Unread mark on finished scheduled runs | Runs with findings get an unread marker [B] | An unread dot until opened; empty runs unmarked | S | a definition of "finding" | good |
| B8 | Side question about a session | CLI side chats in tabs (0.168.0, 0.169.0) [H] | A read-only answer from the transcript, shown in the window; nothing sent | M | the owner's usage per question | good if read-only |

## Shortlist ideas not built (from the 2026-11-18 research)

| # | Idea | From | What it would be here | Needs |
|---|---|---|---|---|
| 5 | **Health list apart from Needs-you** | Home Assistant Repairs | Problems of the setup itself (CLI missing, folder untrusted, daemon not running, config unreadable) in one list with a one-click fix or the manual step. | Look at `Core/Health` first |
| 8 | **Run templates (recipes)** | Goose recipes; Gemini CLI role agents | Setup choices saved as named templates with a parameter (task text); export to a repo file. Partly there: session library and patterns. | Look at `PatternStore` |
| 9 | **PR/CI card** | Claude Code desktop | CI state next to the merge check. | `gh` installed; owner |
| 10 | **Restore points** | Cline, Claude Code, Gemini CLI checkpoints | Snapshot before a coder run so work can be compared/restored. **Not a real undo** (Bash edits and other sessions' edits are not covered): git stays the rollback. | Owner: writes to the repo/shadow repo |
| 12 | **Rules with traces** ("why did X not fire") | Home Assistant automations | Only worth it after 2 and 3 exist as rules; large. | Later |

Idea 11 (worktree per session) is gate G2 in [cli-gates.md](../plans/cli-gates.md). The coder's `auto` mode (desktop A3) was decided on 2026-11-19 (owner's decision, recorded in the checklist's automated-run notes).

Health list, as decided when it was skipped:

- D5 (health list): **not built, deliberately.** The daemon row (Needs you), the setup check dialog (`SetupCheck`) and the trust steps already cover the cases the research named; a combined list would mostly move them. Revisit after the owner has used the window and says which problems get missed.

## Magic rename

Suggested by the planner, not agreed:

- **Preview before saving** the first few batches (old title, proposed name, tick to accept): suggested by the planner, not yet agreed.

## Round 7 (CLI features): recorded, not built


- **HTTP hooks per run for live overlap and limits (research C3, `research-cli-features-2026-11-17.md` section 3).** A Bullshark-owned
  settings file, passed per run with `--settings`, could send hook events (PostToolUse, Stop, PermissionRequest) to a localhost listener,
  keyed by `session_id`, instead of tailing transcripts. Caveats from the research: hooks do not run under `--bare`; `PreToolUse` block
  semantics were tightened in 2.1.326; `allowedHttpHookUrls` is an admin control. Not built: it needs a listener and a verified per-run hook path.
- **`/goal` as a run's stop condition (research C9, section 9).** The first prompt of a run-limited session could be
  `/goal <condition> or stop after N turns`, a native run limit; the transcript records goal entries that could show a "goal met / paused" chip.
  Caveats: goals need hooks enabled (not under `--bare` or `disableAllHooks`), and the exact JSONL shape of goal entries was not read; inspect a
  real transcript before parsing.
- **OpenTelemetry cross-check (research C11, section 11).** Metrics such as `claude_code.cost.usage` and `token.usage`, and the `api_request`
  event, could cross-check the transcript-derived cost and tokens. Caveats: the OTEL variables must be set where the supervisor starts the
  session, and whether the daemon passes arbitrary variables through is unverified. The transcript stays the offline source of cost.

## Transcript activity: not built


- Context meter on live rows: parked (D-A7). The vendor's page gives 1M tokens for the current models; the CLI cap is unchecked.
- HTML export of one session: parked (D-A7).
- Per-tool duration and the timeline with cache-cold gaps: later, as the Research table says.
- Which sessions changed a file, and usage by day, folder and model: later.

## Session library: other ideas, each for its own plan

Not part of this plan. The order is the assistant's recommendation.

| Idea | Note |
|---|---|
| Attention board and alerts: cards for running, waiting, blocked; a Windows notification | the review's Phase 4 and D10. Waits for the review's Phase 3. |
| Hooks in place of polling | a new Claude Code surface (hooks in settings), not checked yet. Decide it with S9, after the review's freeze. |
| Activity panel (README P6): per prompt, the tools called, which failed, how long the turn took | reads transcripts only. `turn_duration` is already read. Per-tool duration and the failure flag are not checked in the format yet. |
| Context meter on each live row | the context size is already known per session. The window size per model must be verified before a percentage is shown. |
| Reorder tabs, split the pane, a second window | see `docs/plans/window.md`. |
| Launch profiles: a folder, a pattern and its models as a one-click start | touches run start, so it waits for the review's Phase 3. Setup already remembers models per pattern. |
| Session timeline with the compaction marks and the cache going cold | reads transcripts only. `compact_boundary` gives the marks. |
| Files touched, and "which sessions changed this file" | `FilesWritten` is already in the digest. |
| Search inside tool calls | `HistorySearch` leaves them out on purpose; it would be a switch on the search. |
| Usage view by day, folder and model | in tokens, not money (F17). The ledger has the data. |
| History as a tool for the sessions themselves (MCP) | new surface; decide after the freeze. |
| Export one session to HTML | see `docs/later/claude-scrollback.md`. |
| Fork from a point; a worktree per coder with diff review | under the review's freeze. Fork is not verified against the CLI. |
| Message queue; brief library | needs the nudge rule (D5), which the review lists as unused code. |

## Setup and sessions: later

- [ ] S8 Later, not in this plan: decision agent proposes a pattern from a goal ([dynamic-configuration.md](dynamic-configuration.md)).
