# Continue: pick up the last thing worked on, in the resume screen the owner prefers (plan, 2026-11-23)

Status: **C0 to C6 and C8 built and committed (2026-11-23); C7, the screen check, is open.** Owner's request (2026-11-23): a "continue" that finds the last session worked on, and opens the correct resume screen for Claude, Codex or Copilot. The owner may prefer resuming from a summary; that preference is a setting, and it can be set per session. Sequencing: after the provider kit refactor's resume stages (R5 and R6 in `provider-kit-refactor-2026-11-23.md`), because the same resume screens change here. Owner also said: plan it, then add it.

## Goal

One action, **Continue**, that: (1) picks the most recently active session of the current provider (or of all providers, if the owner chooses that scope); (2) if that session's terminal is already open and running, shows the tab; (3) otherwise opens its resume screen with the owner's preferred mode already selected. Nothing starts on open. The owner presses Start, as on every resume screen today.

## What "last thing worked on" means

- The session with the latest **last activity** among the listed sessions of the provider, excluding subagents and archived sessions. Last activity is the session's last message time, from each provider's own data, never the file's write time alone (a watcher or a summary can touch a file).
- Verified per provider in stage C0 (not from memory): Claude from its digest's last activity; Codex from the last rollout record's timestamp (C0 correction 1: the session index lags the rollouts, so it is not used); Copilot from `workspace.yaml`'s `updated_at`. Each maps to the shared `SessionSummary.LastActivity`. A null last activity sorts last; it is never treated as recent.
- A running Claude session counts as last worked on like any other. If its tab is open, Continue shows the tab rather than resuming a second copy (the same rule as a resume today).

## Decisions taken (defaults; the owner may change them)

- **CN-D1 Scope.** Default: the active provider only. A setting `continue.scope` = `"provider"` (default) or `"all"` widens it to the most recent session of any provider. The scope never mixes providers in one list.
- **CN-D2 Default mode.** Default: **none (null), so the screen decides** (set 2026-11-23 while building C2). Claude's resume screen keeps its own recommendation (Full or Summary, C0 correction 2); Codex and Copilot start on last turns, which costs nothing. Opening a screen never makes a summary. (The earlier wording "Default: last turns" is superseded by this.) The setting `continue.defaultMode` may be `"full"`, `"lastTurns"` or `"summary"`.
- **CN-D3 Summary is never run by opening the screen.** A preferred **summary** mode only selects the mode. The call is made on the screen's own action ("Make summary"), as every summary today (Codex R-D3, Copilot G3). The cost line is shown before the call.
- **CN-D4 Summary level.** Setting `continue.summaryLevel`: `"minimal"`, `"normal"` (default) or `"complete"`.
- **CN-D5 Precedence.** The mode is resolved in this order: the session's own remembered mode (a library mark, CN-D6), then the global setting, then the default (last turns). A remembered mode is set only from the resume screen's "Remember for this session" tick.
- **CN-D6 Per-session memory.** A new optional library field `ResumeMode` (`full`, `lastTurns`, `summary` or absent). Absent means no memory. The existing library entries and their migration are unchanged.
- **CN-D7 Nothing typed, nothing auto-started.** Continue only opens a screen or activates a tab. The principle 3 rule holds: the app never types into a session.
- **CN-D8 Logs.** Counts and mode names only: no session id, title, folder or text in the log.
- **Owner decision, 2026-11-23: Claude's screen gets the "Remember for this session" tick too.** That supersedes the AFK default recorded in C4 (no tick on Claude's screen). Its implementation is a separate step, after the C5 coder's build, and it touches Claude's dialog and Claude's choice record (an additive trailing `Remember`), plus C6's write of the mark under the `claude:` key.
- **CN-D9 Claude's screen.** Claude's resume screen is Claude's code. Adding the initial mode to it is a change to a Claude file. Decision: it is done as an additive optional parameter with a characterisation test that the default screen is unchanged. This is the only Claude file the feature touches, and it is flagged for the owner's review.

## Contracts this must satisfy (verify each in C0)

- `SessionSummary` (Core/Providers/SessionSummary.cs): `LastActivity`, `IsSubagent`, `Folder`, `Key`, `Title`.
- The library: `LibraryEntry` and `SessionLibrary` (Core/Library): where a new optional field goes, and its round-trip tests (`LibraryMigrationTests`, `LibraryReadOnlyMigrationTests`).
- Behaviour settings: `BehaviourSettings` and `BehaviourStore` (Core/Behaviour): how a new key is read, its default, and what an unknown value does (must fall back to the default, as the others do).
- Resume screens: `ResumePrompt` (Claude), `CodexResumePrompt`, `CopilotResumePrompt`: their starting mode (today hard-coded to the last-turns choice), and the "Remember" tick's place.
- The palette and rail: where a command is added (`SessionWindow.Palette.cs` `PaletteItems` for Claude; the Codex and Copilot palette branches), and the place for a button beside the provider dropdown.

## Stages

C0 to C6 and C8 are built and committed. Their text is in [Done/continue-last-session-2026-11-23-done.md](Done/continue-last-session-2026-11-23-done.md). The open stage and items are below.

## Checklist (open)

- [ ] **C7. Screen check** (scratch state; devenv closed): the Continue button, the palette item, the three screens opened on each mode, light and dark. Owner check: a real Continue with a real session, and a summary chosen on purpose.
- [ ] Owner decides S4 (from C8): keep a remembered mode when a Start is refused. Today it is kept, because the write follows the Start press.
- [ ] Owner decides deviation 1 (from C5): the status line Continue sets is replaced at once by each resume screen's "Reading session..." line. Keeping it needs a status hook on the screens.
- [ ] Owner decides deviation 3 (from C5): Claude's screen refuses a missing folder only at Start, while Continue refuses at open with the Codex screen's text. Accept, or align.
- [ ] Owner decides (from C4): on Claude's screen a Summary start leaves "Recommended: in full" beside a checked Summary radio. The owner may want the text to follow the start.
- [ ] Test defect (from C4): `RunGuardTests.ClaudeProcessesDidNotGrowUpToThisPoint` counts every claude.exe on the machine, so another Claude session starting during a run fails it. It should count only the processes this run starts.
- [ ] Small Core change (from C5, deviation 2): make Core's `ClaudeProvider.SummaryOf` non-private and remove the copy `ContinueSummaryOf`.
- [ ] Log paths (from C8, not changed): the rail's library banner and one log line show the library path when the library cannot be read (`Rail.cs:631`, `Library.cs:73`, `:76`). Owner decides.

## Risks

- **Claude's screen (CN-D9).** The only Claude change. It is additive and characterised; the owner reviews it.
- **"Last" is only as good as the provider's data.** If a provider's last activity is missing or stale, Continue can pick the wrong session. The status line names the session it picked (its title), so the owner can see it; C0 checks this on this PC.
- **Summary cost.** Mitigated by CN-D2 and CN-D3: the default never calls a model, and the call is an explicit press.
- **Order with the refactor.** Starts after R5 and R6, so the resume screens are built once from the shared rules.

## Owner decisions (defaults stand unless the owner changes them)

- CN-D1 to CN-D9 as above. In particular: the default is last turns (no model call), and Continue looks only at the active provider.
- CN-D9 is the only Claude change; the owner should look at it. **Update 2026-11-23:** the owner wants the Remember tick on Claude's screen as well (see the decision above), so the tick is added there in a later step.
