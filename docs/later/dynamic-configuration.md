# Later: dynamic configuration of sessions

Status: idea only, recorded 2026-11-14. Not built, not scheduled. Do not start this until the fixed pair has run on real jobs.

## The idea

Today the owner starts a fixed pair: `ovc-planner` (Opus) and `ovc-coder` (Haiku). Later, a front-facing supervisor takes a goal or request and decides which sessions to launch for it:

- one coder only, for a small fix;
- a planner and a coder, for a feature (the current pair);
- a planner and two or more coders working in parallel, for a large change.

The owner gives the goal. The supervisor chooses the shape, launches it, and keeps it alive, using the same supervisor and messaging that already exist.

## Why it could be better

- One fixed pair is the wrong shape for many jobs. A small fix is cheaper and quicker with one coder alone.
- The shape follows the goal, so the owner doesn't have to pick the configuration every time.

## Why not yet

- The chooser is a decision about spend and about how many sessions run. It needs hard limits before it is trusted (see Guardrails).
- We don't yet know which shapes work well. Until the fixed pair has run on real jobs, a choice cannot be shown to be good or bad.

## Suggested order

1. **Prove the fixed pair** on real jobs: several batches, a killed session, and a long run (see the tests in the README, phase P5 onwards).
2. **Add presets**, a short fixed list: solo coder, planner plus coder, planner plus two coders.
3. **Let the front-facing supervisor pick one preset** from the goal. The owner can override the choice.
4. **Only then consider free-form configuration.** Do not start here.

## Guardrails (required before step 3)

- A maximum number of sessions at once, and a maximum number of sessions per hour.
- A cost estimate shown before launch, from the usage counted in the transcripts (see F17: the account is a subscription, so the estimate is in tokens and the owner's own threshold, not dollars).
- Approval from the owner before any configuration above a set size is launched.
- The chooser never raises a limit or changes a permission mode on its own.
- All sessions in one configuration run in the same permission mode, or messages wait for approval (F22).

## Links to the rest of the design

- The front-facing supervisor is the later, optional orchestrator in README phase P8. Its authority limits (a fixed list of actions, alerts for anything else, a kill switch) apply here too.
- Handoff between sessions stays live messages only (F23). A new configuration uses the same briefs and the same `SendMessage` handoff.
- Each preset is a `SessionSpec` set in `src/OpenTail.Bullshark.Core/Supervisor/SessionSpec.cs`. A new preset is a new list of specs, not new machinery.
