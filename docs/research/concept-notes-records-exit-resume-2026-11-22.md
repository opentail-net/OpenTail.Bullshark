# Concept notes: session records, ending from process exit, resume fallbacks (2026-11-22)

Provenance: written by a reader subagent in the clean-room process (decided by the owner, plan entry dated 2026-11-22). The reader read four outside sources and wrote these notes without copying code. The notes use our own terms and contain no names from those sources. The orchestrator checked them for source names and procedure-level detail before saving. Implementation is ours, and it starts from these notes only.

## A. One versioned record per session, with state and attention kept apart

**Goal.** Each session row is fed by several sources (agent event notifications, the exit watcher, owner actions, restart replay), and any of them can arrive late, twice or out of order. A version that only moves forward makes duplicates and reordering harmless, so the rail converges on one truth. Keeping the agent's state apart from whether the owner has looked stops a focus change or a restart from rewriting facts the rail must keep.

- One record per session, keyed by our own stable session id, never by the tool's id or a tab position.
- Fields: state (starting, idle, working, waiting for owner, ended), version, state-changed-at, last-activity-at, last-seen-at, and the identity of the current process.
- Each applied change increments the version. An update applies only if its version is higher than the stored one.
- Events carry a monotonic counter and a boot identifier from their origin; same-boot events compare by counter. Wall-clock time is for display only. Unstamped events fall back to arrival order and are flagged.
- Session start, session end and owner-input-needed events apply in any order. An error report never replaces a newer state.
- Unread is derived: completed, and completion time later than last-seen. Viewing updates last-seen only. No stored flag can disagree with the timestamps.
- Ended records are kept. Ended disables input; the row stays.

**Policy options.**
- Whether working with no new evidence decays, and after how long.
- Whether an unseen completion stays until new work starts (suggested) or expires.
- Negative ages after a clock change: clamp to zero (suggested).

**Fit.** Store the record in the existing per-session persistence, with version as a column. Write updates conditionally, only when the stored version is lower. The rail renders records only and never infers state from terminal text.

**Risks.** Two writers bumping one version collide, so one component owns the bump. A version gap signals a lost update: re-read the full record. Whether a monotonic counter advances during sleep varies by platform; test resume-from-sleep. Restart replay must not revive working or idle from history (see B and C).

## B. Ending a session from the process exit, not from polling

**Goal.** The rail should show an agent as ended promptly and reliably, even when the agent's own session-ended notification never arrives. Our app starts each agent inside a terminal it owns, so it can wait on that process directly. A blocking wait is more exact than a periodic liveness check, which can miss a process that starts and ends between checks.

- Exit is an operating-system event. Use a wait that blocks until the process ends: a waiter per child, or a process handle registered with the event loop. One liveness check at registration covers a process that already exited.
- Identify the process by id plus start time, or by the open handle. Ids are reused, so an old exit must never end a newer process.
- An exit ends the session only if that process is still the session's current one. If a replacement agent has taken the terminal (resume or restart), rebind the record instead.
- The terminal reader drains output produced before the exit, and only then is the exit published, so final lines survive.
- Record both the exit code and any terminating signal; a killed process can report a generic code.
- An exit caused by the owner closing the tab, or by app shutdown, is recorded as expected, not as a failure.

**Policy options.**
- One waiter per process, or one shared watcher.
- If the replacement check cannot be answered: retry count, interval, and what follows the last retry (mark ended, or keep the last state).
- Time bound for collecting the exit code after output ends.
- Whether an exit soon after launch counts as a failed launch (see C) or a plain end.

**Fit.** The launcher holds the child handle and publishes an exit event with process identity into the session store, which applies it under concept A's version rule. On shutdown, cancel every waiter so none outlives its session.

**Risks.** Reused ids: verify identity before acting. The process we start may be a wrapper with the agent as a descendant; decide which process represents the session, since a wrapper exiting while the agent runs is not an end. On Windows, terminal teardown can cut off final output; test that output drains first. Ended stays retained and sticky (see A).

## C. Resuming a past session with a labelled fallback and one tab per conversation

**Goal.** When the owner reopens a past session, return to the same conversation when possible, say so when not, and never let two tabs resume one conversation. Shared folders make "newest in folder" often wrong, so it is a labelled last resort, never a silent default.

- Keep three ids apart: our session id, the tool's conversation id (what resume needs), and a parent id for forks only. Display names are never identity.
- Capture the conversation id by choosing it at launch where the tool allows, or by reading it back from the tool's own store after launch. Bind only to a single unambiguous candidate.
- Resume ladder, in the owner's order: the saved id; then the tool's own picker, shown to the owner; then the newest conversation in the folder, only as the single candidate. The tab and record say which was used.
- A resume counts as failed when the tool reports the conversation missing, exits early, or shows no sign of life in time. Failures from our app, terminal or transport never clear the saved id.
- One open tab per conversation id. A duplicated tab clears the copied id.
- A restart into a fresh conversation retires the old id, so the new run cannot bind back to it.
- Rebuild the resume command from a sanitised launch record: keep model, configuration and folder; drop prompts, credentials, old session selectors and non-interactive modes.

**Policy options.**
- Time bound for showing life, and retry count.
- Whether the newest-in-folder step is allowed at all (on with a note, or off).
- On a missing-conversation error: drop the saved id and start fresh, or ask the owner (suggested: ask).
- Ambiguous candidates: refuse (suggested) or take the earliest.

**Fit.** The session record stores the conversation id, any retired id, and the fallback used. The rail shows a banner with Retry, Show the command, and Dismiss. The early-exit signal from B feeds the failure test.

**Risks.** Newest-in-folder silently resumes the wrong conversation when folders are shared, so always disclose it. Confirm a picker's choice on a second look before binding. Parse tool stores behind a version check and fail closed. Enforce single-tab ownership in the store's write, not only in the UI.
