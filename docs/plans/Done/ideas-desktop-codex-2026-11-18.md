# Ideas from the Claude desktop app and OpenAI Codex, 2026-11-18

Source: two Haiku researchers (one per product), each told to use primary sources only, to skip what Bullshark already has or lists in
`ideas-2026-11-18.md`, and to mark what it could not confirm. **Researcher-reported, not re-checked by the orchestrator**: check the URL before
relying on a detail. Rules every idea must keep: nothing is typed into a session for the owner, nothing is merged or accepted on its own, local
only, no new paid service.

## A. Claude desktop app (Claude Code on desktop)
Coverage: primary sources confirm desktop features only to Week 37 (Sept 2026), https://code.claude.com/docs/en/whats-new/index.md. October and
November 2026 are UNCONFIRMED. Skipped as built or already listed: away digest, escalation, palette, review lane, PR/CI card, restore points,
limit auto-continue, finish sound.

| # | Idea | Desktop does (source) | In Bullshark | Size | Needs | Fit |
|---|---|---|---|---|---|---|
| A1 | Context fullness per session | "Check usage" shows context fullness (https://code.claude.com/docs/en/desktop) | A meter per tab from the last transcript usage; warn before a planner compacts | S | usage fields (read today); the model's window size is UNCONFIRMED | read-only |
| A2 | Hook event feed | 33 hook events incl. `Stop`, `StopFailure`, `PermissionRequest`, `Notification`, `SessionEnd` (https://code.claude.com/docs/en/hooks) | Hook scripts append events to a local file: instant state, no 15 s poll | M | hook entries in settings (owner); scoping to `--bg` sessions UNCONFIRMED | observe only; never answer `PermissionRequest` |
| A3 | `auto` permission mode for the coder | A classifier checks actions; "long tasks, reducing prompt fatigue" (https://code.claude.com/docs/en/permission-modes) | Attacks the `blocked` stalls (the README's main failure); revises decision D3 | S | owner decision; `--bg` support UNCONFIRMED; pauses after 3 blocks in a row | our launch flag; adds no permission ourselves |
| A4 | Deny-only guard hooks | `PreToolUse` can block a call (https://code.claude.com/docs/en/hooks) | Block `rm -rf`, `git push`, `git reset --hard` in our sessions only | M | hook script; per-session settings UNCONFIRMED | deny only |
| A5 | Catch-up and skip reasons | One catch-up for the latest missed run within 7 days; history gives skip reasons (https://code.claude.com/docs/en/desktop-scheduled-tasks) | Same rules for our scheduled runs; history says why a run did not start | S | owner sets catch-up (default off) | code only |
| A6 | Keep awake while work is due | "Keep computer awake"; a run is skipped if the machine sleeps through it (same page) | Hold the sleep block while a run is due within the hour or a session works | S | `SetThreadExecutionState` (Win32, no package); the owner's power call | local, no service |
| A7 | Permissions in effect | "Always allowed" panel per task, revocable (same page) | Per run: mode and allow rules, to tune the allowlist after a `blocked` | S | settings read | read-only |

Findings to check (not ideas):
- `claude agents --json` states, per docs: `working`, `blocked`, `done`, `failed`, `stopped`; `idle` is UI-only (https://code.claude.com/docs/en/agent-view). Answers
  the README's open state-values item from docs, not from a run.
- Unattached sessions that finished or wait for input are stopped after about an hour. `Ctrl+T` pins one; no CLI pin is documented (UNCONFIRMED). The supervisor's
  restart path must handle `stopped`.
- Background sessions edit in `.claude/worktrees/` by default (`worktree.bgIsolation`). The Changes tab must read that folder.
- The README contradicts itself on the coder's mode: line 117 says `auto`, line 150 says "open", decision D3 says `acceptEdits` plus allowlist.

Bullshark does better: one away digest across all sessions, with limit history and reset-start; the desktop recap is per session (Week 17).

## B. OpenAI Codex (desktop app, CLI, IDE, cloud)
Sources: developers.openai.com/codex now redirects to learn.chatgpt.com/docs; the researcher read the targets. Key: A https://learn.chatgpt.com/docs/code-review ·
B .../docs/automations?surface=app · C .../docs/agent-configuration/subagents.md · D .../docs/agent-approvals-security · E .../docs/config-file/config-advanced ·
F .../use-cases/follow-goals · G .../docs/environments/git-worktrees · H .../docs/changelog

| # | Idea | Codex does | In Bullshark | Size | Needs | Fit |
|---|---|---|---|---|---|---|
| B1 | Second-opinion review of a diff | `/review`: a dedicated reviewer reads the diff and edits nothing [A] | Read-only review of a finished coder's diff, listed in Changes | M | owner OK on usage; hidden `claude -p`, as magic rename does | sends and accepts nothing |
| B2 | Plan progress as the goal | `/goal`: one objective, a stopping condition, a progress row; runs "many hours" unattended [F] | Rail row: "3 of 7 done, next: ..." from the plan file's checkboxes | S | plan path per session | reads a file |
| B3 | Per-file "viewed" marks | Mark files as viewed in Changes [A] | A viewed flag per file, cleared when the file changes | S | none | no git write |
| B4 | Toast filters by event | Filter by event type; fire `unfocused` or `always` [E] | Toast per event: blocked, finished with changes, limit reset | S | none | good |
| B5 | Blocked-prompt helper | Auto-review screens exfiltration, credential probing, destructive actions [D] | On a blocked row: the prompt, a risk note, an allow rule to copy | M | prompt text (unconfirmed); a model call | partial: advice only, approval stays with the owner |
| B6 | Subagent tree under a session | `/subagents` switches agent threads; Active and Done lists [C] | Subagent runs under the session's row, read-only | M | transcript reader; Claude's `subagents/agent-*.jsonl` (57 files seen locally) | viewing only |
| B7 | Unread mark on finished scheduled runs | Runs with findings get an unread marker [B] | An unread dot until opened; empty runs unmarked | S | a definition of "finding" | good |
| B8 | Side question about a session | CLI side chats in tabs (0.168.0, 0.169.0) [H] | A read-only answer from the transcript, shown in the window; nothing sent | M | the owner's usage per question | good if read-only |

UNCONFIRMED: the desktop "Needs input" task status (search summary only); the `/goal` budget and 4,000-character cap (the official page is silent); whether subagents get
their own worktrees (docs silent); the notification method name (changelog 0.168.0 says `osc777`, the config page lists `auto`, `osc9`, `bel`).
Worktree detail [G]: Codex keeps the newest 15 managed worktrees, snapshots a worktree before cleanup, and starts in detached HEAD.

Bullshark does better: limit history and time-to-hit; Needs-you escalation and snooze. The Codex pages read have focus-based notifications only, and its auto-review decides
eligible approvals itself, which principle 3 rules out.

## C. Orchestrator's ranking (best fit for "walk away, come back, carry on")
1. A1 context meter per session (S, read-only).
2. B2 plan progress on the rail row (S).
3. B4 toasts by event (S; pairs with the toasts and escalation already built).
4. B3 viewed marks in Changes (S).
5. B6 subagent runs under a session (M).
6. A2 hook event feed (M; needs the owner to add hook entries).
Needs an owner decision first: A3 `auto` mode for coders (changes how coders are launched), A6 keep awake, B1 and B8 (they use usage).
Not worth it now: A4, B5 (advice only, partial fit), B7 until "finding" is defined.
