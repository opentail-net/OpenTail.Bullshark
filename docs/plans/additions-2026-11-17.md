# Additions, 2026-11-17

> Split on 2026-11-21: the done items moved to [Done/additions-2026-11-17-done.md](Done/additions-2026-11-17-done.md). This file keeps the open items.

Status (2026-11-17): Phase A items 1 and 2 built and on `main` (7a18376), compiled but not yet seen in a running window. Item 3 skipped at its gate (F62). Phase B items 4 and 5 built; item 6 has its argument but no caller (D6.2); Phase C item 7 built. On `gems-2026-11-17`, not pushed or merged; not seen in a running window.

Source: research of 2026-11-17 into similar tools (Agent View, Claude Code desktop, Claude Squad, ccmanager, ChatML, AgentsRoom) and
current `claude` CLI features. Agent View and the desktop app now cover plain "see all sessions". Bullshark's edge is unattended
supervision, planner/coder runs, history/search/resume and cache facts on Windows. These additions build on that edge.

Build after `review-2026-11-17.md` is done; items 2 and 3 of that plan change the same pass code (`PassAndRefreshAsync`,
`SupervisorRunner`). Same branch rules apply: worktree `C:\Git\split\OpenTail.Bullshark-review`, tests with every item,
`dotnet test OpenTail.Bullshark.slnx -nodeReuse:false`, one commit per item, no push, no new NuGet packages unless an item says so.

## Facts checked (installed CLI 2.1.326, `claude --help`, 2026-11-17)

| Fact | Status |
|---|---|
| `claude agents --json` lists active sessions; `--all` adds completed ones; `--cwd <path>` limits to sessions started under a path | Verified in `claude agents --help` |
| A waiting session shows as state `blocked` in the listing (Bullshark findings F19, F21) | Verified in code (`SupervisorPolicy`, `AlertKind.Blocked`) |
| `-w, --worktree [name]` creates a git worktree for a session | Verified in `claude --help` |
| `claude rm <id>` deletes the session's worktree only if safe; refuses with unpushed commits, then accepts `--discard-unpushed <commit>@<worktree-id>` or `--force-remove-worktree <worktree-id>` from its own report | Verified in `claude rm --help` |
| Whether `--bg` already puts a session in a worktree by default (one blog says Agent View does), whether `-w` combines with `--bg`, and whether `agents --json` reports the worktree path | **Not verified: gate of item 3** |
| `claude --bg --exec <cmd>` (shell jobs) | **Not in 2.1.326 help**: dropped, see end |

## Contracts this must satisfy

| Member | What it promises / what callers rely on |
|---|---|
| `AgentInfo` / `AgentInfo.ParseList` | Fields id, sessionId, name, kind, state, status, cwd, pid, startedAt. `EffectiveState` = state ?? status ?? "unknown". Unknown JSON fields are ignored, so new fields are additive. |
| `SupervisorPolicy.Decide` | Alerts `Blocked` once per entry into `blocked` (uses `SessionRecord.LastState`), never answers on the owner's behalf. `RestartLoop` after `SupervisorRules.MaxRestartsPerHour` (3). |
| `SupervisorRunner.CheckOnceAsync` | Records keyed by session name; saves only changed records under the file lock; failed start/resume becomes an alert, not an exception. |
| `SessionSpec` | Role, Name, WorkingDirectory, Model, Effort, Brief, PermissionMode, ResumePrompt. Serialized inside `RunRecord` in runs.json. |
| `RunRecord` (runs.json) | RunId, PatternKey, WorkingDirectory, PermissionMode, CreatedAt, Specs. Old files must still load: any new field is optional with a default. |
| `SessionRecord` (state.json) | Role, SessionId, Name, StartedAt, Restarts, RestartTimes, LastState, LastAlert. Same rule for new fields. |
| `ClaudeCli.StopAsync` / `RemoveAsync` | Take the short id, not the name (F14). `requireSuccess: false`: callers read the result. |
| `TranscriptIndex` digests | Carry `FilesWritten`; `TokenUsage(Input, Output, CacheCreation, CacheRead)` per message, totals in `IndexTotals`. |
| Window process | The supervisor pass runs inside the window process (15 s timer). With the window closed, nothing is supervised or notified. |

## Phase A: leave it running unattended

### 3. One worktree per coder (gated)
- [ ] **Gate, do first:** start one throwaway background session in a scratch git repo with `--bg` alone, then with `--bg -w <name>`; record in this file: where it runs, what `agents --json` shows (cwd, any worktree field), what `claude rm` does. Then `claude rm` both and delete the scratch repo. If `-w` does not work with `--bg`, stop this item and record why. **Result 2026-11-17: not run; `--bg` refused the scratch folder as an untrusted workspace. Item 3 skipped, see D3.1.**
- [ ] `SessionSpec` gets optional `bool Worktree = false`; the setup page offers "own worktree per coder" for runs with more than one coder (planner stays in the main folder).
- [ ] `BackgroundLaunch` gets `string? Worktree`; `StartBackgroundAsync` adds `-w <name>` (name = session name). Resume never passes `-w` (the session already has its worktree).
- [ ] Ending a run: offer "Remove session" which calls `claude rm`; if it reports unpushed work, show its report and offer the `--discard-unpushed` value it printed. Never pass `--force-remove-worktree` without the owner's click.
- [ ] Item 2 must show the worktree's changes (uses `cwd` from the listing, so this follows if the gate shows `cwd` is the worktree).
- [ ] Tests: argument lists with and without worktree; resume never gets `-w`; `rm` report parsing from captured samples.

## Phase B: control and hindsight

### 6. Narrow listings with `--cwd`
- [ ] Use it only where one folder is wanted (for example `ResolveSessionIdAsync` after a start, with the launch folder) — check first that `--cwd` matches a worktree session started from that folder (gate of item 3), else keep the full list there.

## Not in this plan (and why)

- Shell jobs via `claude --bg --exec`: not in the installed CLI's help (2.1.326); blogs describe it but it cannot be relied on.
- Copying Agent View's dashboard: research preview, keys and layout may change.
- Notifications through Claude Code hooks: would change the owner's Claude settings; the polled `blocked` state gives the same signal.

## Decisions made while building

### Item 3 (one worktree per coder): skipped at the gate

- D3.1 Skipped. The gate was run as far as it could go: `claude --bg --model haiku --effort low --name gate-plain "reply with the word ok and stop"`, in a new scratch repo (one commit, under the session scratchpad, not one of the owner's repos). It exited 1 with `Workspace not trusted. Run claude in <scratch folder> once and accept the trust prompt, then retry.` `claude agents --json --all` listed the same 20 sessions before and after, so nothing was started. The `-w` form was not run, because the first step refuses. The gate is therefore unclear, and the brief says to skip the rest of the item. Nothing of item 3 is built: no `Worktree` on `SessionSpec`, no `-w` in `BackgroundLaunch`, no "Remove session", no tests for them.
- D3.2 Trust was not granted for the scratch folder. The trust record is kept per path in `~/.claude.json` (`projects[path].hasTrustDialogAccepted`; the scratchpad folder of another session there is `false`). Granting it needs an interactive `claude` run in the folder, which writes that file, and that changes the owner's permission settings. I only read `~/.claude.json`.
- D3.3 No session was stopped, removed or started by this run, so there was nothing to clean up with `claude stop` or `claude rm`. The scratch repo was deleted.
- To finish item 3: the owner accepts trust once for a throwaway folder (or names one that is already trusted), then the gate is run again. The same trust gate stops any run in an untrusted repository (finding F24), so the setup page's trust step matters to item 3 as well.
- Item 2 does not depend on item 3. Its folder is the listed `cwd`, else the spec's folder (D2.8), so it works for a session in the main folder and will show a worktree's changes once one exists and is listed with its `cwd`.
