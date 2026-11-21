# Claude Code documentation index (for background sessions, daemon, restarts, limits)

Logged 2026-11-21 against Claude Code **2.1.330** (installed; `claude --version`). Compiled by a researcher from official pages and local `--help`. Each entry: URL, title, version or date shown on the page, a quote, and a status.

**Status:** DOCUMENTED = on an official page (quote as extracted by the fetch tool, not checked against raw HTML). UNCONFIRMED = search snippet or third-party only. CLI help = from the installed binary's `--help`.

**Caveat:** the `cli-reference` and `interactive-mode` pages were too large to read in full. Anything from them is search-snippet only and marked UNCONFIRMED.

## 1. Background agents and sessions

| URL | Title | Version or date | Quote | Status |
|---|---|---|---|---|
| https://code.claude.com/docs/en/agent-view | Manage multiple agents with agent view | research preview, no date | "`claude attach <id|name>`: Attach to a session in this terminal"; "`claude respawn <id>`: Restart a session, running or stopped" | DOCUMENTED |
| same | same | same | "`claude logs <id|name>`: Print the session's recent output"; "`claude stop <id>`: Stop a session. Also accepts `claude kill`" | DOCUMENTED |
| local `claude stop --help` | CLI help (2.1.330) | n/a | "Its conversation is kept; resume it later with `claude attach <id>`." | CLI help |

## 2. Daemon (the docs call it the supervisor)

| URL | Title | Version or date | Quote | Status |
|---|---|---|---|---|
| agent-view URL, "The supervisor (daemon)" | Manage multiple agents with agent view | no date | "a background service that runs your background sessions so they keep working after you close agent view or your terminal." | DOCUMENTED |
| same, Start | same | no date | "Claude Code starts it the first time you background a session or open agent view" | DOCUMENTED |
| same, Exit | same | no date | "on macOS and Linux, your sessions stop unless a new supervisor connects within about a minute." Windows is not stated. | DOCUMENTED (macOS and Linux only) |
| same, Process stop | same | no date | "Finished or waiting for your next message, and unattached for about an hour: the supervisor stops the process" | DOCUMENTED |
| local `claude daemon --help` | CLI help (2.1.330) | n/a | "the daemon runs on demand and exits once no client is connected and no background session is running." | CLI help |
| agent-view URL, version table | same | v2.1.257 | "On Windows, a stale `daemon.lock` ... is replaced." | DOCUMENTED |

Observed on this machine (probe P06, `docs/research/codex-evidence/results/`): the daemon was running, transient, started by a `claude` process, and it stayed up while background workers ran or a `claude agents` was open.

## 3. Session states

| URL | Title | Version or date | Quote | Status |
|---|---|---|---|---|
| agent-view URL, "Session states" | Manage multiple agents with agent view | no date | UI words: "Working"; "Needs input" ("waiting on something only you can provide"); "Idle" ("ready for your next prompt"); "Completed"; "Failed"; "Stopped" ("You stopped the session with `Ctrl+X` or `claude stop`") | DOCUMENTED |
| same | same | same | JSON `state` field: `working`; `blocked` ("The session is waiting on you"); `done` ("ready for your next prompt"); `failed`; `stopped`. Idle is not a state: it is the `status` field (busy, waiting, idle). | DOCUMENTED |

Observed (probe P07): states `done`, `stopped`, `working`, `blocked`. `failed` did not appear in that run.

## 4. Restart and resume

| URL | Title | Version or date | Quote | Status |
|---|---|---|---|---|
| https://code.claude.com/docs/en/sessions | Manage sessions | no date | "`claude --continue`: Reopens the most recent session in the current directory" | DOCUMENTED |
| same | same | same | "Claude Code exits with `Your most recent conversation is running in the background`" | DOCUMENTED |
| same | same | same | Not restored: MCP, plugins, `--add-dir`, `--settings`, tool access: "pass flags such as these again, because Claude Code doesn't restore them" | DOCUMENTED |
| agent-view URL, "What persists across restarts" | Manage multiple agents with agent view | no date | "The permission mode, model, and effort you chose 'all persist when the supervisor later stops and restarts its process.'" Also `/rename` names and `Ctrl+S` stash. | DOCUMENTED |
| same | same | same | Stopped session: "Attach or reply to it and it restarts from its saved conversation." | DOCUMENTED |
| same, version table | same | v2.1.257 | "`claude --resume <session-id> --bg`... continues that session under its own ID, or starts a copy under a new ID" | DOCUMENTED |

## 5. Hooks that signal waiting or completion

| URL | Title | Version or date | Quote | Status |
|---|---|---|---|---|
| https://code.claude.com/docs/en/hooks | Hooks reference | no date or version | Stop: "When Claude finishes responding."; "no matcher support | always fires on every occurrence." Exit code 2 "Prevents Claude from stopping" | DOCUMENTED |
| same | same | same | Notification: "When Claude Code sends a notification."; matchers "`permission_prompt`, `idle_prompt`, `auth_success`..."; "Exit code and stderr are ignored" | DOCUMENTED. When `idle_prompt` and `permission_prompt` fire: NOT FOUND |
| same | same | same | PermissionRequest: "When a tool call needs a permission decision."; "Exit code 2 isn't honored for this event"; deny via `decision.behavior` | DOCUMENTED |
| same | same | same | Config shape: `hooks` > `<EventName>` > `[{ "matcher"?, "hooks": [{ "type": "command", "command": ... }] }]` | DOCUMENTED |
| agent-view URL, version table | same | v2.1.198 | "fires the `Notification` hook with the `agent_needs_input` or `agent_completed` type." | DOCUMENTED |

## 6. Usage limits

| URL | Title | Version or date | Quote | Status |
|---|---|---|---|---|
| https://support.claude.com/en/articles/11647753-how-do-usage-and-length-limits-work | How do usage and length limits work? | "Updated over 2 weeks ago" | "your limit resets in full five hours later"; "Weekly limits reset at a fixed time each week" | DOCUMENTED (Help Center) |
| https://code.claude.com/docs/en/errors | Error reference | no date; cites v2.1.324 | Messages: "You've hit your session limit"; "You've hit your weekly limit"; "Claude stopped here · send a message to be asked again". The reset wording was not seen on this page. | DOCUMENTED |
| https://support.claude.com/en/articles/14552983-models-usage-and-limits-in-claude-code | Models, usage, and limits in Claude Code | "Updated over 3 weeks ago" | Enterprise seats: "limit reached, resets at _time_" | DOCUMENTED |
| https://code.claude.com/docs/en/whats-new/2026-w34 | What's new, week 34 (Aug 17 to 21, 2026; v2.1.234 to v2.1.239) | week 34 | "Claude Code now continues your session automatically when a claude.ai usage limit resets; turn it off from the **Continue automatically at usage limit** row in `/config`" | DOCUMENTED |
| https://code.claude.com/docs/en/changelog | Changelog | v2.1.326, Nov 16 2026 | "Changed 'Continue automatically at usage limit' to stop with a notice, sending nothing, when your login changes or ends during the wait" | DOCUMENTED |
| errors page, `CLAUDE_CODE_RETRY_WATCHDOG_MAX_USAGE_LIMIT_WAIT_MS` | Error reference | v2.1.301 and later | "the longest time ... one request waits for your plan's usage limit ... to reset." Default "no cap." | DOCUMENTED |
| interactive-mode page (search snippet only) | Interactive mode | no date | "Usage limit reached · limit resets 3:45pm"; setting `autoContinueAtUsageLimit` (default true, v2.1.234+); at most three automatic continuations; no wait if the reset is more than 24 hours away; Esc or Ctrl+C cancels; background sessions and `-p` runs do not offer the wait | UNCONFIRMED |

## 7. Permission modes and blocked sessions

| URL | Title | Version or date | Quote | Status |
|---|---|---|---|---|
| https://code.claude.com/docs/en/permission-modes | Choose a permission mode | no date; cites v2.1.283 | "Auto mode lets Claude execute without routine permission prompts."; "A separate classifier model reviews actions before they run"; "does not guarantee safety." Auto became the built-in starting mode for interactive terminal and VS Code at v2.1.283. | DOCUMENTED |
| same | same | same | Repeated blocks: auto mode pauses and Claude Code resumes prompting (summary wording) | DOCUMENTED |
| same | same | same | Non-interactive: "Calls needing approval are denied unless you approve them with an allow rule, hook, `canUseTool` callback, or `--permission-prompt-tool`." | DOCUMENTED |
| agent-view URL | Manage multiple agents with agent view | no date | Blocked session: "paused on a permission prompt or other dialog" (Needs input) | DOCUMENTED |

## 8. Desktop app scheduled tasks

| URL | Title | Version or date | Quote | Status |
|---|---|---|---|---|
| https://code.claude.com/docs/en/desktop-scheduled-tasks | Schedule recurring tasks in Claude Code Desktop | no date; "Local scheduled tasks require Claude Desktop 1.1.5368 or later." | "Tasks only run while the desktop app is running and your computer is awake. If your computer sleeps through a scheduled time, the run is skipped." | DOCUMENTED |
| same | same | same | Missed runs: "Desktop starts exactly one catch-up run for the most recently missed time and discards anything older." (last seven days) | DOCUMENTED |
| same | same | same | Cloud tasks: "runs even when your computer is off" | DOCUMENTED |
| desktop "background tasks pane" (search snippet only) | n/a | n/a | not verified on a page | UNCONFIRMED |

## NOT FOUND or partial (official pages)

- Windows supervisor-exit behaviour: the docs cover macOS and Linux only.
- Trigger conditions for `idle_prompt` and `permission_prompt` in the Notification hook.
- Reset wording for the session and weekly limit messages on the official errors page. Only the Help Center gives the five-hour and weekly rules. The exact "limit resets" line is a snippet only.
- The desktop "background tasks pane" on an official page.

## Open questions this index does not settle (need a live test)

- Does closing the App window end a `claude attach` client, or the background session? (Restart research, live test 1.)
- Does a `done` session that drops out of `claude agents --all` get resumed by the App? The App's policy has no `done` guard. (Live test 3.)
- Does `claude --bg --resume <id>` on a listed session make a copy? The code comment cites help text that was not checked.
- Does `claude respawn` restart a stopped session? (Live test 7.)
- What happens on Windows when the supervisor exits.
