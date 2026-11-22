# Concept notes: notifications, approval prompts, minimum tool version (2026-11-22)

Provenance: written by a reader subagent in the clean-room process (decided by the owner, plan entry dated 2026-11-22). The reader read agent-manager (Go) and muxel (Rust) and wrote these notes without copying code. The notes use our own terms and contain no names from those sources. The orchestrator checked them for source names and procedure-level detail before saving. Implementation is ours, and it starts from these notes only.

## A. Alert on change, not on state

**Goal.** Owners running several sessions should be pulled back only when a session newly needs them. Alerts that repeat while a state persists, or that fire while the owner already sees the change, teach them to ignore alerts. Each alert should mark one real change and arrive only when the owner cannot see it.

**Concepts.**
- Alerts are transitions, not levels: staying in a state never re-alerts.
- A state that reverts before holding for a short dwell time never alerts.
- Attending means the app window is focused and the session is selected. Attended sessions get no desktop banner, but the in-app feed still records them.
- After a session alerts for a kind, it stays quiet for that kind until the owner looks at it or a cooldown ends.
- Coincident alerts merge into one cue, the more urgent kind winning, and cues keep a minimum gap.
- A banner carries the session identity. Activating it brings the app forward and selects that session.
- On startup, each session's first observed state is recorded silently.

**Policy options.**
- Default kinds: needs-input only; needs-input plus errored, with finished opt-in; or all three. The middle option keeps routine turn ends quiet.
- Focus gate: window unfocused only, or window unfocused or session unselected. One reference design omits the gate on purpose, so decide it consciously.
- Cooldown: none; a fixed quiet period; or until looked at, which needs no timer and is the strictest.
- Dwell time and cue gap: measure flicker in real sessions, then pick.

**Fit.** The supervisor detects transitions by comparing each session with its last recorded state. The notifier checks window activation and the rail selection. A banner activation passes the session identifier back, which selects that rail row.

**Risks.**
- A visible but unfocused window on a second monitor may suppress a banner the owner needs.
- Long dwell delays real alerts; short dwell admits flicker.
- Click delivery varies by platform, so keep an audible fallback.
- Sampling the screen can miss brief states; prefer the agent's own events where available.
- Forgetting to clear per-session memory on look-back drops alerts.

## B. Showing agent-written approval text faithfully and arming decisions

**Goal.** A permission or trust prompt asks the owner to approve text the agent wrote. Hidden characters can make the displayed text differ from what will run: text that reorders on screen, or extra content hidden behind blank space. Faithful display, decision controls that are hard to hit by accident, and no late approval of stale requests keep the decision meaningful.

**Concepts.**
- Hidden characters (controls, zero-width and direction-override marks, line separators, blank-rendering fillers) become visible markers. Stripping them silently makes the displayed text differ from the text that will execute.
- Long runs of blank space, invisible characters or stacked combining marks collapse to a counted marker, so they cannot push the request out of view.
- Never truncate what is approved. Reject an over-long request at submission, with a reason.
- Agent-declared labels, such as the asker or the project, are shown as declared, not verified.
- Arming: decision controls stay inert until a dwell time has passed since first display, not since arrival. Presses during that time do nothing.
- Press binding: a decision counts only when press and release land on the same armed control for the same request.
- Expiry: each request has a bounded lifetime from arrival. On expiry it is refused with a stated reason, the waiting agent learns the outcome, and the outcome stays queryable for a limited time.
- Pointer-only decisions avoid accidental keyboard approvals, at some cost to accessibility.

**Policy options.** Dwell length; expiry length; queue depth limit; outcome retention; pointer-only or keyboard-capable decisions; reject versus a full-text detail view for over-long requests.

**Fit.** One sanitizer in the supervisor produces the displayed text, so the approval view and any banner share the same handling. Decision controls bind to the request identifier. The arming clock starts when the view reports the card rendered. Expiry runs in the supervisor's queue and answers the waiting permission call with a refusal.

**Risks.**
- Over-escaping mangles legitimate text: emoji joiners, combining marks in some scripts, and mixed-direction text. Test with real samples.
- A dwell that restarts on every re-render never arms.
- An expiry racing a click must be decided atomically.
- Escaped text must still read sensibly to screen readers.
- A press that is not bound to its request can let a click on a neighbouring request decide the wrong one.

## C. Refusing to start a tool that is too old, and offering an update

**Goal.** An agent CLI older than we support fails mid-launch or mid-session, with the cause buried in terminal output. Checking the installed version before starting lets the app explain the problem and offer an update instead of a failed launch.

**Concepts.**
- Minimum supported version per tool, kept as data beside the tool definition rather than spread through code.
- Probe: ask the installed binary for its version. The probe must use the same binary the launch will run, so resolve the path once and reuse it.
- Three outcomes: supported; too old (installed and minimum both known); could not determine (missing, unreadable, timed out, unparseable). A timeout is "could not determine", never "too old".
- Cache a success keyed to the binary's identity (path, size, modification time), so an upgrade is seen on the next check. Do not cache failures, so a fix shows at once.
- Check at session start and when listing tools, so the UI never offers a tool it will refuse.
- The refusal is a typed result, not an error string, so the UI can choose an update card.
- Update offer: show the command when known. When another owner manages the install, say how it was installed rather than offer a command that will fail.
- Probing runs off the UI thread. The launch waits briefly for an answer in flight, then reports that it is checking.

**Policy options.**
- Unparseable version output: refuse, or start with a visible warning.
- Probe time limit: short, since a user is waiting.
- Minimum version: the first release with the behaviour we depend on, never "latest".
- Update action: show the command only, or run it on confirmation.
- Re-check cadence for the tool list.

**Fit.** A version service beside the session launcher returns the typed outcome before any terminal is created. The session list badges a tool that failed the check, and the update dialog offers the command with a copy action. Minimums change only alongside the adapter change that needs them.

**Risks.**
- Probe and launch picking different binaries (path order, or a Windows copy versus a WSL copy) is the most likely real bug.
- Version strings carry suffixes and pre-release tags. Compare by segments, and never treat a parse failure as old.
- Running a binary to probe it carries the same trust as launching it, so bound its time and output.
- A minimum that is too strict locks out valid users; one that is too loose recreates the failure.
