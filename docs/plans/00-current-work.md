# Current work: the path through the plans

Status (2026-11-21): the plans were restructured. Finished plans are in [Done/](Done/). What is still open is in the files below. Counts from the files as they stand: 136 owner checks, 11 CLI gates, 34 open build items, 40 parked ideas.

## Where the open work lives

| File | What it holds |
|---|---|
| [live-run-checklist.md](live-run-checklist.md) | the owner's sitting: every check that needs a person at the screen or a real session |
| [cli-gates.md](cli-gates.md) | throwaway probes of the CLI; each one decides a feature |
| [supervisor.md](supervisor.md) | supervisor work: review leftovers, per-coder worktrees, performance, known issues |
| [window.md](window.md) | tabs, panes and zoom: open points |
| [session-library.md](session-library.md) | the session library: open steps and the owner's questions |
| [resume-preview.md](resume-preview.md) | the resume prompt's split button (not started) and summaries step 2 |
| [limit-wake-2026-11-20.md](limit-wake-2026-11-20.md) | sessions stopped at the usage limit (not started; in progress elsewhere, so not moved) |
| [limits-usage.md](limits-usage.md) | the paused-on-limit reason, the pre-limit warning, and the usage report's open points |
| [provider-codex-2026-11-21.md](provider-codex-2026-11-21.md) | the provider seam and the Codex rail (plan; not edited in this pass) |
| [provider-seam-design-2026-11-19.md](provider-seam-design-2026-11-19.md) | design notes behind it (not edited in this pass) |
| [../later/ideas.md](../later/ideas.md) | parked ideas |
| [../research/](../research/) | research reports behind the ideas and the provider track |
| [Done/](Done/) | finished plans, and the records of plans that were split |

In `Done/`, a file named `<plan>.md` is the record of a closed plan. It keeps the plan's items as they stood at the restructure, so some open items are also listed in the live files; the live files are the working copies. A file named `<plan>-done.md` holds the finished items of a plan that is still open.

## The path

### Phase 1: the owner's sitting ([live-run-checklist.md](live-run-checklist.md))

Section B first: the end-to-end run is the one check nothing else replaces, and its result (the F20 resume, and the summary path) decides how much of phase 3 and phase 4 is worth doing as written. Then sections C1 to C12, one sitting. The checks in C8 and C12 need the features of phase 4 to be built first, so run them when those are done.

### Phase 2: CLI gates ([cli-gates.md](cli-gates.md))

G1 (trust a throwaway folder) comes first; the owner's word is needed. Then G2 (worktrees), G3 (session id), G4 to G6, G7 (the state of a session stopped at the limit), G8 (auto mode), G9 (OpenTelemetry), G10 and G11. Each gate is a few minutes of probing. Their answers decide the build items in phase 4 (worktrees, OpenTelemetry, limit-wake phase 2).

### Phase 3: finish what is partly built (no new design)

- **Supervisor** ([supervisor.md](supervisor.md)): section 1 (the solo restart prompt and the listed-session refusal); section 4 (window start timing and the cold index read: measure, then accept or fix); section 6 (known issues, most are one-line checks).
- **Window** ([window.md](window.md)): the T1 and T2 on-screen checks (sitting); the zoom points (decide on the palette and check the 256 colours).
- **Library** ([session-library.md](session-library.md)): the on-screen checks of L2 to L5 (sitting). L6 (the index cache) is built only when the measurement says so.
- **Limits** ([limits-usage.md](limits-usage.md)): the API-key gap and the usage report's points.

### Phase 4: not started, in this order

1. **Resume preview** ([resume-preview.md](resume-preview.md)). Self-contained, no gate. Then summaries step 2, but only after the sitting's "resume from a summary" check passes.
2. **Limit wake** ([limit-wake-2026-11-20.md](limit-wake-2026-11-20.md)). Its phase 1 is in progress elsewhere. Phase 2 waits for G7.
3. **Per-coder worktrees** ([supervisor.md](supervisor.md) section 3). Only if G2 passes.
4. **Live cost over OpenTelemetry** (G9). Only if its gate passes.
5. Ideas from [ideas.md](../later/ideas.md) that the owner picks.

### Phase 5: the provider track ([provider-codex-2026-11-21.md](provider-codex-2026-11-21.md))

Start after the phase 1 sitting passes, because the seam changes the rail that the sitting checks. Follow that plan's own stage order, and its own decisions of 2026-11-21. Its design notes are in [provider-seam-design-2026-11-19.md](provider-seam-design-2026-11-19.md).

### Phase 6: research backlog (clean-room ideas from [docs/research/](../research/), 2026-11-22)

Each row is one idea from the concept notes in `docs/research/concept-notes-*-2026-11-22.md`. Status comes from a read-only gap audit, so nothing already built is rebuilt. Each row gets its own plan only when the owner picks it. We implement from the concept note only: no names, no code, nothing carried across from the sources. Medium and large rows wait for their own decisions.

| # | Idea | Note | Status | Effort | Owner decision needed |
|---|---|---|---|---|---|
| R1 | Rollout lookup by start time, with a full-search fallback | index-usage A | built, commit 2de5071 | small | no |
| R2 | Alert on change, not on state; focus gate; cooldown; a click selects the session | notify-prompts A | built, pending push: alerts on a change only, a focus gate, the kinds, a banner click selects its row; configurable in behaviour.json. Open as R2b: Snooze and Acknowledge do nothing while repeats are off (the default) | small | focus gate; re-alert policy (the note says never; the code does it by design); default alert kinds |
| R3 | Approval and trust text: show hidden characters; collapse runs; arm buttons after a delay; expire old requests | notify-prompts B | not at all: agent text is never shown; paste drops control characters silently | small | arming delay; expiry length |
| R4 | Minimum CLI version before launch, with an update offer | notify-prompts C | built, pending commit: a launch below the tested version (Claude 2.1.330, Codex 0.171.0) is refused before anything is created, with an update instruction and no command; an unreadable version warns (`versions.unreadable`). Follow-ups: R4b, throttle the supervisor pass's version probe (it runs about every 15 s while a launch is pending and the version is bad); R4c, the Codex resume preview still makes its summary call before a refusal; `claude attach` is not guarded (it may not start a session; not verified) | small | minimum version per tool; unparseable output: refuse or warn |
| R5 | One versioned record per session; state kept apart from "seen" | records-exit-resume A | partly: locked updates and newer-start-wins exist; no version field; records are dropped on close | medium | whether "working" decays; clock-change policy |
| R6 | End a session from the process exit, not from polling | records-exit-resume B | not at all: ending comes only from the agents listing. **Scope decided 2026-11-22:** Codex terminal tabs only, where our terminal runs `codex` itself. A Claude tab runs `claude attach`, a client, so its exit does not end the session; Claude keeps the agents listing. No retries. The tab shows as ended and stays. The exit code is recorded; a tab the owner closed is recorded as expected | small to medium | none (decided); the retry knob is unused |
| R7 | Resume fallback chain; one tab per conversation | records-exit-resume C | partly: one-tab guards exist; no picker, no newest-in-folder; failed resumes are only logged | medium | allow newest-in-folder?; ambiguous candidates |
| R8 | Usage totals without double counting | index-usage C | partly: Claude is covered; Codex usage is not read at all | medium | attribution time; which duplicate wins |
| R9 | Folder stamps and scoped rescans for the session list; atomic index write | index-usage B | partly: per-file cache and atomic write exist; no folder stamps; no file watcher | medium | refresh trigger; stamp strength |
| R2b | Snooze and Acknowledge silence a session's banners and sound even when re-alerts are off (decided by the orchestrator, 2026-11-22): Snooze silences that session until the snooze time; Acknowledge silences it until the session changes state. The list keeps every change. | R2 follow-up | built, pending push (Core 2531 net11.0 and 2518 net10.0; App 145 on each target; 0 failed). Opening a session's tab acknowledges it, so its banners stay silent until its state changes. Follow-up R2c: remove session names from the existing log lines in SessionWindow.Attention.cs (acknowledge, snooze, waits, ready). | small | none (decided) |
| R10 | Negative memory for failed lookups | index-usage A | built, commit 4f2398a: a miss is kept for 60 s (`lookups.missMemorySeconds`), never for a found file; cleared when the shown session changes. Other lookup callers still search each time (noted) | small | memory window |

**Reuse first, build last** (from the gap audit, read-only, 2026-11-22):
- Reuse: R9 (the transcript index cache, the atomic write under the file lock); R2 (the attention list, the escalation rules, the toast text); R8 (the usage ledger, which already keeps one record per message across files).
- Build from nothing: R3 (approval-text safety and arming), R6 (an exit waiter with identity checks), R10 (miss memory).
- Owner decision found by the audit: R2's note says an alert never repeats while a state persists, but the code re-alerts by design (up to three times, every 15 minutes). Choose which to keep. The default is the code's behaviour until you say otherwise.

**Decisions, 2026-11-22.** The owner said: "you decide, and make things configurable". Every choice below has a default in code, and a knob in one file, `behaviour.json`, beside the state file. Changing a default is a config change, not a code change. The Settings window does not show these yet.

| Row | Decision (default) | Knobs in `behaviour.json` | Build order |
|---|---|---|---|
| R2 | Alert on a state change only; never re-alert while the state holds. Alert only when the window is unfocused. Default kinds: needs-input and errored; finished is off. | `alerts.repeatMinutes` (0 = never), `alerts.focusGate` (`unfocused` or `unfocusedOrUnselected`), `alerts.kinds` | 1 |
| R10 | Remember a missing session id for 60 s, and clear the memory when the pane's session changes. | `lookups.missMemorySeconds` (0 = off) | 2 |
| R4 | The minimum for each tool is the version we tested against: Claude 2.1.330, Codex 0.171.0. A version that cannot be read is a warning, not a refusal. A version below the minimum refuses the launch and offers an update. | `versions.minimum.claude`, `versions.minimum.codex`, `versions.unreadable` (`warn` or `refuse`) | 3 |
| R6 | Mark a tab's session ended when its process exits. No retries. The ended row stays visible. | `exit.endOnProcessExit` (true) | 4 |
| R7 | No newest-in-folder fallback by default, and it is always labelled when used. An ambiguous match is refused. The tool's picker is the fallback, and the owner sees it. | `resume.allowNewestInFolder` (false), `resume.ambiguous` (`refuse` or `earliest`) | 5 |
| R5 | No decay of "working" by default. A clock that goes backwards is clamped to zero. Session records carry a version. | `state.workingDecayMinutes` (0 = never), `state.clampClockBackwards` (true) | 6 |
| R9 | Refresh the session list when the window opens, and every 60 s. A folder's stamp is its file count and newest change time. | `index.refreshSeconds` (60; 0 = only on open), `index.stamp` (`countAndNewest`) | 7 |
| R8 | Usage is attributed to a session's last update. A repeated message counts once, keeping its final copy. Codex usage comes from its last running total. Reasoning counts as output. | `usage.attribution` (`lastUpdate`, `start` or `event`), `usage.reasoningAsOutput` (true) | 8 |
| R3 | **Not built.** The window never answers for the owner (principle 3), and there is no approval view to make safe. Revisit only if you decide to add one. | none | none |

R1 is built and needs no setting. Each row gets a plan only when it is built, and each build runs on its own, one at a time, with its tests.

## Owner decisions still open

1. **Gate G2.** Trust one throwaway git folder so the gate can run, or name a folder that is already trusted. (Source: [additions record](Done/additions-2026-11-17.md), decision D3.2.)
2. **Should a `done` coder in a live run ever be woken?** Today: no. (Source: [bugs record](Done/bugs-2026-11-17.md), D1.5.)
3. **A sound when a session that is already waiting goes stale** (the daemon stopped)? Today: no new sound. (Source: [session-summary record](Done/session-summary-2026-11-17.md); round 8 B2.2.)
4. **The Respawn click waiting for a pass that is already running.** Needs a UI-thread change; optional. (Source: [round 9 record](Done/round9-2026-11-17.md), item 2.)
5. **Light theme tab labels** (#5A6270, about 5.6:1): accept, or change. (Source: [round 4 record](Done/round4-2026-11-17.md).)
6. **The first index read with no cache**, about 1.35 s on the owner's data, over the 1 s line. Accept, or plan the next step. (Source: [backlog record](Done/backlog-2026-11-18.md), C2.)
7. **Assistant.** Cut it, or narrow the check that still finds it. The owner parked it on 2026-11-16; the check still finds it. (Source: [supervisor.md](supervisor.md) section 1.)
8. **The spike window and `--smoke`.** Delete them? (Source: [supervisor.md](supervisor.md) section 2; review question 8.)
9. **When does a run end?** Only when the owner ends it, or after N idle days? (Source: [review record](Done/review-2026-11-15.md), section 7, question 5.)
10. **Should closing an idle (`done`) session stop it too?** (Same source, question 6.)
11. **Should a full resume start idle for the owner to type, or with a prompt?** (Same source, question 7.)
12. **Coders on the same files.** Separate worktrees are allowed, and who cleans them up? (Source: [setup record](Done/setup-and-sessions.md), section 6, question 3; depends on G2.)
13. **The alert channel when away.** The Windows toast is built. Is the Teams path in OpenTail.Orca still wanted? (Source: [setup record](Done/setup-and-sessions.md), section 6, question 5.)
14. **Layout D-S1** (rail on the left, pane on the right): confirm on screen, in the sitting (S7).
15. **Session library, owner's questions**: confirm the recommendations used so far (one row per pinned session under Pinned only; search lists archived sessions?), and may L5 run one real resume in a scratch folder as its check? (Source: [session-library.md](session-library.md), questions 2 to 4.)
16. **Tabs**: is T1 then T2 the order, with T3 only if still wanted after using the split? Are two panes enough? (Source: [window.md](window.md), questions.)
17. **Zoom, palette**: the palette is Windows Terminal's default, written in. Read the owner's own scheme, or accept the default? (Source: [window.md](window.md), zoom open point 1.)
18. **Usage report**: dollars need a dated rate table from the owner (not built); the 14-day toggle; the per-model split; and whether cache reads count toward the limit (unknown). (Source: [limits-usage.md](limits-usage.md).)
19. **A budget figure for a pre-limit warning** (backlog B3). No number is invented: accept no warning, or give a figure.
20. **Whether the README needs a line for the Limits page** (usage report, step 5). The record leaves it to the orchestrator.
21. **Ideas that need the owner:** the PR/CI card (needs `gh` and the owner's OK), restore points (writes to the repo; not a real undo), the second-opinion review and side questions (use usage), keep-awake for due runs, and the hook event feed (the owner adds the hook entries). (Source: [docs/later/ideas.md](../later/ideas.md).)
22. **Provider scope**: the product name and the pitch, if it supervises other terminals. (Source: [provider-seam-design-2026-11-19.md](provider-seam-design-2026-11-19.md), risks, question 1.)

Decided, not open (for the record): toasts are built (the Windows package question is answered; backlog B1); the app never deletes old transcripts (the owner, 2026-11-19); coders run in `auto` (the owner, 2026-11-19); all three summary levels stay (2026-11-16); the log-on supervisor is not wanted (2026-11-15); where `.claude.json` lives under `CLAUDE_CONFIG_DIR` is answered (2026-11-19, [record](Done/live-run-checklist-done.md)); one supervisor host (2026-11-16).

## What was merged, resolved or dropped

| Item | Where it is now | Why |
|---|---|---|
| Review phase 3 steps (six) | checklist section B | the same steps as the checklist's end-to-end run, written more exactly |
| Review phase 4 'End run' | checklist section B | kept as written |
| `--bg --session-id` gate (checklist), round 7 gate 1 | cli-gates G3 | one probe |
| Worktree-per-coder gate (checklist), additions item 3 gate, `--cwd` check (item 6) | cli-gates G2 | one probe |
| OpenTelemetry gate (checklist), gems item 3 gate, backlog B4 (OpenTelemetry) | cli-gates G9 | one probe |
| Backlog B4 (gated live checks, end-to-end run) | cli-gates G1, G3, G9 and checklist section B | its parts already live there |
| Backlog A3 gate (state of a session at the limit), limit-wake LW4 | cli-gates G7 | one probe |
| Backlog B2 (wake sessions paused on the limit) | [limit-wake-2026-11-20.md](limit-wake-2026-11-20.md) | the same feature |
| Limits page check (checklist) and the usage report's layout check | checklist section C5 | one screen |
| Round 9 R1, R3, R9 gates (checklist) | checklist section C2 | window checks, not CLI probes |
| `CLAUDE_CONFIG_DIR` location (round 3; session-summary) | resolved | answered 2026-11-19 ([record](Done/live-run-checklist-done.md)) |
| Toast package question (session-summary) | resolved | toasts built 2026-11-18 (backlog B1) |
| Old transcripts deleted? (session-summary) | resolved | the owner, 2026-11-19: never deleted by the app |
| Summary levels (review question 3) | resolved | all three stay, 2026-11-16 |
| P4 log-on supervisor (round 6) | resolved | not wanted, 2026-11-15 |
| Review questions 1 to 4 | resolved | answered in the record |
| Setup and sessions S7 | checklist section C6 | on-screen review |
| Setup and sessions S8 | [ideas.md](../later/ideas.md) | a later idea |
| Setup and sessions S9 | cli-gates G11 | CLI probes |
| Setup-check on-screen check | checklist section C9 | on-screen |
| Zoom checks (drag, keys) | checklist section C7 | on-screen |
| Zoom open points | [window.md](window.md) | open |
| Review phase 2: summaries, handover, measure, tail, finding 15 | [resume-preview.md](resume-preview.md) | one feature |
| Review phase 2: the spike | [supervisor.md](supervisor.md) section 2 | one item |
| Ideas (desktop, Codex, research) not built | [ideas.md](../later/ideas.md) | parked |
| Ideas built or adopted (desktop A1, B2 to B4; desktop A3 decided and built per README D3; shortlist 1 to 4, 6, 7) | the record in Done | built |
| Gated and parked items of round 7 | [ideas.md](../later/ideas.md) and cli-gates G3 | as written |
| Backlog review logs, batch notes, round notes | the records in Done | history |
| Setup and sessions risks | the record in Done | stale: all built |

## Doc fixes still to make

- **limit-wake-2026-11-20.md**: its live items LW1 to LW6 are copied into checklist section C12 (LW4 is in cli-gates G7). Its step "add live items to docs/plans/live-run-checklist.md" should point at the checklist now. The file is in progress elsewhere, so it has not been edited here.
- Done in this pass: the README Plans section (points at this file and the Done folder), the README coder-mode lines (they agree: `auto`), the code-comment pointers (commit 2a72328), and the provider seam design's research path.
