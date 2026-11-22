# What four external projects can teach us (2026-11-22)

Four research subagents (Haiku) read four cloned projects in C:\Git\split\examples. They were read-only: nothing from those projects was run or built. The licences were checked against each repository's LICENSE file, and the orchestrator spot-checked each one. The feature claims are the readings of the subagents, with file references; most of those references were not checked.

## Licences: what we may copy

| Project | Licence | Copy code? |
|---|---|---|
| agent-manager | Apache-2.0, with a NOTICE file | Yes, with attribution kept |
| muxel | GPL-3.0 | No (copyleft, incompatible with our PolyForm Strict LICENSE.md) |
| tday | "SAY NO TO SUICIDE PUBLIC LICENSE" v2.0 at the root; Business Source License 1.1 in crates/tday-nativecore | No |
| cmux | GPL-3.0-or-later for most of the repo; BUSL-1.1 for some directories | No |

Default: reimplement the ideas in our own code. Copying agent-manager code would change our licence position, so that is the owner's decision.

## Ideas, by effort

**Small**
1. **Bound the Codex rollout lookup by time** (muxel). The thread ID carries its creation time, so the lookup can search only that day's folder, and cache failed lookups. Our rollout lookup scans every file on each call.
2. **A stat-only change check per session folder** (tday): rescan only the folders whose file count or newest modification time changed. This keeps the rail quick with many rollouts.
3. **Codex usage totals** (tday): the last cumulative token count in a rollout is the session total, not a sum. Verify against real rollouts before using it.
4. **Resume by the saved session ID for each tab**, with labelled fallbacks (agent-manager, tday).
5. **Notify on state change only**, and only when the window is unfocused, with a per-session cooldown; a click focuses the session (agent-manager, tday).
6. **Keep a session's lifecycle state apart from whether it has been seen**, with timestamps (muxel), so the rail cannot contradict itself after a restart.
7. **Strip control and hidden characters from agent-written prompt text**, and arm approval buttons only after a short delay (muxel).
8. **A minimum CLI version check before launch**, with an update prompt rather than a failed launch (agent-manager).
9. **A versioned session record**: a late or duplicate update applies only if it is newer (cmux).
10. **Detect exit with a process-exit watcher, not polling** (cmux). We own our own child processes, so this fits.

**Medium or large**
11. **Bind a session by a token set at launch and echoed in hook events** (cmux). This links to decision O5 (hooks).
12. **Hold a permission request until the owner answers, with a timeout** (cmux). Large; tied to O5.
13. **Budgeted, incremental transcript reads for usage** (cmux). Relates to the optional extras SD1 and SD2.
14. **A worktree per session** (agent-manager, muxel). An owner decision.

## Do not copy

- Scraping status from screen text (muxel, agent-manager): it breaks whenever the UI changes.
- Typing "continue" into agents automatically (muxel): this conflicts with our rule that the window never answers for the owner.
- Turning off Codex's approvals, or allowing a whole tool server without prompts (tday).
- Editing the owner's global Claude settings while a session runs (tday): a crash can leave them blanked.
- A trust-bypass flag on launch (cmux). We refused the same kind of flag earlier.
- Falling back silently when tracking fails (cmux). Show "not tracked" instead.

## Caveats

- Treat READMEs as claims. tday's README says its terminal uses WebGL; the code does not load it.
- Codex details (usage fields, approval events, rollout formats) must be checked against our own rollouts, which is an owner check.
- Each subagent read only part of its project. Their own "not verified" lists are in their reports.
