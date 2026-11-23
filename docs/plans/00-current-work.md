# Current work: the path through the plans

Status (2026-11-23): finished plans are in [Done/](Done/). This file lists only open work.

## Where the open work lives

| File | Still open |
|---|---|
| [live-run-checklist.md](live-run-checklist.md) | the owner's sitting: every check that needs a person at the screen or a real session |
| [cli-gates.md](cli-gates.md) | throwaway CLI probes; each one decides a feature |
| [supervisor.md](supervisor.md) | review leftovers, per-coder worktrees, performance, known issues |
| [window.md](window.md) | tabs, panes and zoom: open points |
| [session-library.md](session-library.md) | open steps and the owner's questions |
| [resume-preview.md](resume-preview.md) | live checks R1 to R7; summaries step 2 |
| [dedup-resume-dialogs-2026-11-23.md](dedup-resume-dialogs-2026-11-23.md) | the owner's live check D-S10 |
| [provider-parity-2026-11-23.md](provider-parity-2026-11-23.md) | doc edits in the other plan files; the owner's PC checks |
| [codex-net10-zstd-2026-11-23.md](codex-net10-zstd-2026-11-23.md) | review (Z4) and owner decisions |
| [continue-last-session-2026-11-23.md](continue-last-session-2026-11-23.md) | the screen check C7 |
| [provider-kit-refactor-2026-11-23.md](provider-kit-refactor-2026-11-23.md) | R8 and R10; R9 (Claude) needs its own plan |
| [resume-source-target-2026-11-22.md](resume-source-target-2026-11-22.md) | the owner's screen check S5 |
| [r4d-claude-summary-launch-order-2026-11-22.md](r4d-claude-summary-launch-order-2026-11-22.md) | a resume without a preview still makes the summary before the launch refusal |
| [provider-copilot-2026-11-22.md](provider-copilot-2026-11-22.md) | owner decisions G-O1 to G-O5; optional stage G5 |
| [provider-codex-2026-11-21.md](provider-codex-2026-11-21.md) | the provider seam and the Codex rail (5 of 13 steps done) |
| [provider-opencode-2026-11-22.md](provider-opencode-2026-11-22.md) | the owner's checks at the PC |
| [provider-seam-design-2026-11-19.md](provider-seam-design-2026-11-19.md) | design notes behind the Codex plan |
| [limit-wake-2026-11-20.md](limit-wake-2026-11-20.md) | sessions stopped at the usage limit; phase 1 in progress elsewhere |
| [limits-usage.md](limits-usage.md) | the paused-on-limit reason, the pre-limit warning, the usage report's points |
| [../later/ideas.md](../later/ideas.md) | parked ideas |
| [../research/](../research/) | research reports behind the ideas and the provider track |
| [Done/](Done/) | finished plans, and the finished items of plans that are still open (`<plan>-done.md`) |

## The path

### Phase 1: the owner's sitting ([live-run-checklist.md](live-run-checklist.md))

Section B first: the end-to-end run is the one check nothing else replaces, and its result (the F20 resume, and the summary path) decides how much of phase 3 and phase 4 is worth doing as written. Then sections C1 to C12, one sitting. The checks in C8 and C12 need the features of phase 4 to be built first.

### Phase 2: CLI gates ([cli-gates.md](cli-gates.md))

G1 (trust a throwaway folder) comes first; the owner's word is needed. Then G2 (worktrees), G3 (session id), G4 to G6, G7 (the state of a session stopped at the limit), G8 (auto mode), G9 (OpenTelemetry), G10 and G11. Their answers decide the phase 4 items (worktrees, OpenTelemetry, limit-wake phase 2).

### Phase 3: finish what is partly built (no new design)

- **Supervisor** ([supervisor.md](supervisor.md)): section 1 (the solo restart prompt and the listed-session refusal); section 4 (window start timing and the cold index read: measure, then accept or fix); section 6 (known issues).
- **Window** ([window.md](window.md)): the T1 and T2 on-screen checks; the zoom points (the palette, and the 256 colours).
- **Library** ([session-library.md](session-library.md)): the on-screen checks of L2 to L5. L6 (the index cache) only if the measurement says so.
- **Limits** ([limits-usage.md](limits-usage.md)): the API-key gap and the usage report's points.

### Phase 4: not started, in this order

1. **Resume preview** ([resume-preview.md](resume-preview.md)): its live checks R1 to R7 in the sitting. Then summaries step 2, only after the sitting's "resume from a summary" check passes.
2. **Limit wake** ([limit-wake-2026-11-20.md](limit-wake-2026-11-20.md)). Phase 2 waits for G7.
3. **Per-coder worktrees** ([supervisor.md](supervisor.md) section 3). Only if G2 passes.
4. **Live cost over OpenTelemetry** (G9). Only if its gate passes.
5. Ideas from [ideas.md](../later/ideas.md) that the owner picks.

### Phase 5: the provider track ([provider-codex-2026-11-21.md](provider-codex-2026-11-21.md))

Start after the phase 1 sitting passes, because the seam changes the rail that the sitting checks. Follow that plan's own stage order. Its design notes are in [provider-seam-design-2026-11-19.md](provider-seam-design-2026-11-19.md).

**opencode** ([provider-opencode-2026-11-22.md](provider-opencode-2026-11-22.md)) is built. Its checks at the PC are owner-only: the read while opencode is writing, the summary, and the resume screen. Its open owner items are listed as owner item 23 below.

### Phase 6: research backlog (clean-room ideas from [docs/research/](../research/), 2026-11-22)

Each row gets its own plan only when the owner picks it. Built rows with nothing left open are not listed here.

| Row | Still open |
|---|---|
| R2 | re-alert policy: the note says never; the code re-alerts up to three times, every 15 minutes (owner item 24) |
| R3 | not built. The window never answers for the owner. Revisit only if an approval view is added |
| R4 | `SingleInstanceTests.AnAbandonedNameIsTaken` failed once in a full Core run (it races on an abandoned named mutex), not investigated. `claude attach` is not guarded (not verified) |
| R5 | "working" decay not built (its knob is read by nothing; default is no decay). Ended records are still dropped on tab close (owner's decision). Risk: if the installed build and a dev build share one state file, an older build resets versions to 0 |
| R6 | the exit code is not exposed by the package. Closing a tab after its process ended can throw in the library's close step |
| R7 | not run live (no Codex session was started). Owner item: allow newest-in-folder? |
| R8 | not measured on the owner's history. Attribution time and which duplicate wins are unresolved |
| R9 | a running session's activity time rebuilds the rail (scroll returns to top); scroll restore not built; Codex history not covered; not run on screen |
| R10 | other lookup callers still search each time |

## Owner decisions still open

1. **Gate G2.** Trust one throwaway git folder so the gate can run, or name a folder that is already trusted. (Source: [additions record](Done/additions-2026-11-17.md), decision D3.2.)
2. **Should a `done` coder in a live run ever be woken?** Today: no. (Source: [bugs record](Done/bugs-2026-11-17.md), D1.5.)
3. **A sound when a session that is already waiting goes stale** (the daemon stopped)? Today: no new sound. (Source: [session-summary record](Done/session-summary-2026-11-17.md); round 8 B2.2.)
4. **The Respawn click waiting for a pass that is already running.** Needs a UI-thread change; optional. (Source: [round 9 record](Done/round9-2026-11-17.md), item 2.)
5. **Light theme tab labels** (#5A6270, about 5.6:1): accept, or change. (Source: [round 4 record](Done/round4-2026-11-17.md).)
6. **The first index read with no cache**, about 1.35 s on the owner's data, over the 1 s line. Accept, or plan the next step. (Source: [backlog record](Done/backlog-2026-11-18.md), C2.)
7. **Assistant.** Cut it, or narrow the check that still finds it. (Source: [supervisor.md](supervisor.md) section 1.)
8. **The spike window and `--smoke`.** Delete them? (Source: [supervisor.md](supervisor.md) section 2; review question 8.)
9. **When does a run end?** Only when the owner ends it, or after N idle days? (Source: [review record](Done/review-2026-11-15.md), section 7, question 5.)
10. **Should closing an idle (`done`) session stop it too?** (Same source, question 6.)
11. **Should a full resume start idle for the owner to type, or with a prompt?** (Same source, question 7.)
12. **Coders on the same files.** Separate worktrees are allowed, and who cleans them up? (Source: [setup record](Done/setup-and-sessions.md), section 6, question 3; depends on G2.)
13. **The alert channel when away.** Is the Teams path in OpenTail.Orca still wanted? (Source: [setup record](Done/setup-and-sessions.md), section 6, question 5.)
14. **Layout D-S1** (rail on the left, pane on the right): confirm on screen, in the sitting (S7).
15. **Session library:** confirm the recommendations used so far (one row per pinned session under Pinned only; search lists archived sessions?), and may L5 run one real resume in a scratch folder as its check? (Source: [session-library.md](session-library.md), questions 2 to 4.)
16. **Tabs:** is T1 then T2 the order, with T3 only if still wanted after using the split? Are two panes enough? (Source: [window.md](window.md).)
17. **Zoom, palette:** read the owner's own scheme, or accept Windows Terminal's default? (Source: [window.md](window.md), zoom open point 1.)
18. **Usage report:** dollars need a dated rate table from the owner (not built); the 14-day toggle; the per-model split; and whether cache reads count toward the limit (unknown). (Source: [limits-usage.md](limits-usage.md).)
19. **A budget figure for a pre-limit warning** (backlog B3). No number is invented: accept no warning, or give a figure.
20. **Whether the README needs a line for the Limits page.** The record leaves it to the orchestrator.
21. **Ideas that need the owner:** the PR/CI card (needs `gh` and the owner's OK), restore points, the second-opinion review and side questions, keep-awake for due runs, and the hook event feed. (Source: [docs/later/ideas.md](../later/ideas.md).)
22. **Provider scope:** the product name and the pitch, if it supervises other terminals. (Source: [provider-seam-design-2026-11-19.md](provider-seam-design-2026-11-19.md), risks, question 1.)
23. **opencode** ([provider-opencode-2026-11-22.md](provider-opencode-2026-11-22.md), sections 3 and 11): whether the rail rows each summary adds are kept or hidden; whether a double-click on a session with a missing folder should refuse or open the resume screen; whether an archived opencode session can be unarchived from the app (it cannot now).
24. **R2 re-alert policy:** keep the code's re-alerts (up to three times, every 15 minutes), or the note's "never".

## Doc fix still to make

- **limit-wake-2026-11-20.md**: its step "add live items to docs/plans/live-run-checklist.md" should point at the checklist now. The file is in progress elsewhere, so it has not been edited here.
