# Continue: pick up the last thing worked on, in the resume screen the owner prefers (plan, 2026-11-23)

Status: **plan, nothing built.** Owner's request (2026-11-23): a "continue" that finds the last session worked on, and opens the correct resume screen for Claude, Codex or Copilot. The owner may prefer resuming from a summary; that preference is a setting, and it can be set per session. Sequencing: after the provider kit refactor's resume stages (R5 and R6 in `provider-kit-refactor-2026-11-23.md`), because the same resume screens change here. Owner also said: plan it, then add it.

## Goal

One action, **Continue**, that: (1) picks the most recently active session of the current provider (or of all providers, if the owner chooses that scope); (2) if that session's terminal is already open and running, shows the tab; (3) otherwise opens its resume screen with the owner's preferred mode already selected. Nothing starts on open. The owner presses Start, as on every resume screen today.

## What "last thing worked on" means

- The session with the latest **last activity** among the listed sessions of the provider, excluding subagents and archived sessions. Last activity is the session's last message time, from each provider's own data, never the file's write time alone (a watcher or a summary can touch a file).
- Verified per provider in stage C0 (not from memory): Claude from its digest's last activity; Codex from the session index's update time; Copilot from `workspace.yaml`'s `updated_at`. Each maps to the shared `SessionSummary.LastActivity`. A null last activity sorts last; it is never treated as recent.
- A running Claude session counts as last worked on like any other. If its tab is open, Continue shows the tab rather than resuming a second copy (the same rule as a resume today).

## Decisions taken (defaults; the owner may change them)

- **CN-D1 Scope.** Default: the active provider only. A setting `continue.scope` = `"provider"` (default) or `"all"` widens it to the most recent session of any provider. The scope never mixes providers in one list.
- **CN-D2 Default mode.** Default: **last turns** (no model call, nothing spent). The setting `continue.defaultMode` may be `"full"`, `"lastTurns"` or `"summary"`.
- **CN-D3 Summary is never run by opening the screen.** A preferred **summary** mode only selects the mode. The call is made on the screen's own action ("Make summary"), as every summary today (Codex R-D3, Copilot G3). The cost line is shown before the call.
- **CN-D4 Summary level.** Setting `continue.summaryLevel`: `"minimal"`, `"normal"` (default) or `"complete"`.
- **CN-D5 Precedence.** The mode is resolved in this order: the session's own remembered mode (a library mark, CN-D6), then the global setting, then the default (last turns). A remembered mode is set only from the resume screen's "Remember for this session" tick.
- **CN-D6 Per-session memory.** A new optional library field `ResumeMode` (`full`, `lastTurns`, `summary` or absent). Absent means no memory. The existing library entries and their migration are unchanged.
- **CN-D7 Nothing typed, nothing auto-started.** Continue only opens a screen or activates a tab. The principle 3 rule holds: the app never types into a session.
- **CN-D8 Logs.** Counts and mode names only: no session id, title, folder or text in the log.
- **CN-D9 Claude's screen.** Claude's resume screen is Claude's code. Adding the initial mode to it is a change to a Claude file. Decision: it is done as an additive optional parameter with a characterisation test that the default screen is unchanged. This is the only Claude file the feature touches, and it is flagged for the owner's review.

## Contracts this must satisfy (verify each in C0)

- `SessionSummary` (Core/Providers/SessionSummary.cs): `LastActivity`, `IsSubagent`, `Folder`, `Key`, `Title`.
- The library: `LibraryEntry` and `SessionLibrary` (Core/Library): where a new optional field goes, and its round-trip tests (`LibraryMigrationTests`, `LibraryReadOnlyMigrationTests`).
- Behaviour settings: `BehaviourSettings` and `BehaviourStore` (Core/Behaviour): how a new key is read, its default, and what an unknown value does (must fall back to the default, as the others do).
- Resume screens: `ResumePrompt` (Claude), `CodexResumePrompt`, `CopilotResumePrompt`: their starting mode (today hard-coded to the last-turns choice), and the "Remember" tick's place.
- The palette and rail: where a command is added (`SessionWindow.Palette.cs` `PaletteItems` for Claude; the Codex and Copilot palette branches), and the place for a button beside the provider dropdown.

## C0 results (2026-11-23, read-only; the full table is in the job folder, `continue-c0-facts.md`)

Corrections to the plan above, from reading the code and this PC's data (names and counts only):

1. **Codex last activity comes from the rollouts, not the index.** Codex's `LastActivity` is the last rollout record's timestamp (`Core/Codex/CodexHistorySource.cs:142, :241-244`); the index is not read for it. On this PC the index's `updated_at` is more than 60 seconds earlier than the rollout's last record for 53 of 54 entries, so the index must not be used to pick the last session.
2. **Claude's resume screen does not start on last turns.** It starts on Full or Summary by recommendation, and its Tail radio is unchecked (`App/ResumePrompt.xaml.cs:56-57, :78, :85`). CN-D9 therefore has to keep Claude's recommendation as the default, and the setting applies only where the owner has chosen a mode.
3. **There is no "Remember for this session" control on any screen.** It is new on all three screens (C4 and C6 build it).
4. **The palette has no Codex or Copilot branch.** `PaletteItems()` is one list, and the Claude-only items are gated by `claude` (`App/SessionWindow.Palette.cs:81-168`, gate at `:86`). "Continue" is added ungated.
5. **`IsSubagent` is null, not false, for Codex (when not marked) and Copilot (always).** Continue skips a session only when `IsSubagent == true`. Copilot subagents therefore cannot be excluded in v1; this is a known limit, not a bug.
6. **`SessionSummary` has no archived flag.** Archived is a library mark, so `ContinueChooser` takes the marks as input and excludes archived sessions there.
7. **`LibraryEntry.IsEmpty` must list any new field** (`Core/Library/SessionLibrary.cs:38-41`); `ResumeMode` is added there (C3).
8. **Open tab checks are per provider and not in the Claude resume path.** Claude's resume does not check for an open tab (`App/SessionWindow.Resume.cs:43`), and the Codex and Copilot screens do not either (`CodexResume.cs:49`, `CopilotResume.cs:29`). Continue checks for an open tab itself, before it opens any screen: `ShowOpenCodexTab` (`SessionWindow.Codex.cs:571`), `ShowOpenCopilotTab` (`SessionWindow.Copilot.cs:360`), and the Claude tab lookup (`SessionWindow.Tabs.cs:150`).

Sanity figures for this PC (names and counts only): Claude has 291 of 291 checked transcripts with a timestamp in their last 256 KB, and 158 of 292 transcripts are under `subagents` folders. Codex has 64 rollouts, 2 marked subagent. Copilot has 52 folders, 5 without `events.jsonl`; of 47 compared, `updated_at` is within 60 seconds of the newest event for 45.

Settings and library, confirmed: `behaviour.json` sits beside the state file; a missing or bad value gives the default and counts as ignored (`Core/Behaviour/BehaviourStore.cs:23, :95-117, :217-224`); a new `continue` section follows the `resume.ambiguous` pattern (`BehaviourSettings.cs:143, :180-208`; `BehaviourStore.cs:120-181`). Library keys are `provider:id`, and a new optional field is a trailing defaulted parameter read and written by the generic code (`SessionLibrary.cs:184-227, :302-308`).

## Stages

**C0. Facts (read-only, gate for everything after).** Check each contract above and write the answers into this plan under "C0 results": the field names, the file and line for each, and whether each provider's last activity is present and trustworthy on this PC (names, counts and types only, no session text).

**C1. Pure choice rules (Core, new files).** `Core/Continue/ContinueChooser.cs`: given the listed summaries of one scope, the library marks and the excluded sets, return the session to continue (or null). `Core/Continue/ResumeModePolicy.cs`: resolve the mode from the session's mark, the global setting and the default (CN-D5). Tests with synthetic summaries: the latest wins; subagents and archived sessions are skipped; a null last activity sorts last; ties are broken by id (deterministic); the precedence in CN-D5; an unknown setting falls back to the default.

**C2. Settings (Core).** Keys `continue.scope`, `continue.defaultMode`, `continue.summaryLevel` in the behaviour settings, with the defaults above and the same fallback behaviour as the existing keys. Tests: defaults; each unknown value falls back; round trip.

**C3. Library field (Core).** `LibraryEntry.ResumeMode` (optional). Round-trip; an entry without it loads exactly as before; the existing migration tests pass unchanged.

**C4. Resume screens take a starting mode.** Codex and Copilot screens: an optional initial mode parameter (default: last turns, so current callers are unchanged), and a "Remember for this session" tick that returns the choice to the caller. Claude's screen: the same additive parameter (CN-D9), with a characterisation test that the default screen is unchanged. Tests: each screen opens on the given mode; the default is unchanged; the tick is returned.

**C5. The Continue action (App).** One entry: a **Continue** button beside the provider dropdown, and a palette item "Continue last session". It runs `ContinueChooser`; if the chosen session's tab is open, it shows the tab; otherwise it opens that provider's resume screen at the resolved mode (CN-D5). If there is no session in scope, the status line says so and nothing opens. A missing folder is refused, as resume is today. Tests: the action's pure decisions (which session, which mode, which refusal) are in Core; the App part is a thin call and a status line.

**C6. Remember the choice.** The "Remember for this session" tick on each screen writes `ResumeMode` to the session's library entry. Tests: the write goes to the right key (the provider's own key, per the existing key rules).

**C7. Screen check (scratch state; devenv closed; no CLI of the owner's running).** The Continue button, the palette item, the three screens opened on each mode, light and dark. Owner check: a real Continue with a real session, and a summary chosen on purpose.

**C8. Review.** A Sonnet review of C1 to C6 (privacy of logs; the library write; no typing into a session; that Claude's screen default is unchanged).

## Risks

- **Claude's screen (CN-D9).** The only Claude change. It is additive and characterised; the owner reviews it.
- **"Last" is only as good as the provider's data.** If a provider's last activity is missing or stale, Continue can pick the wrong session. The status line names the session it picked (its title), so the owner can see it; C0 checks this on this PC.
- **Summary cost.** Mitigated by CN-D2 and CN-D3: the default never calls a model, and the call is an explicit press.
- **Order with the refactor.** Starts after R5 and R6, so the resume screens are built once from the shared rules.

## Owner decisions (defaults stand unless the owner changes them)

- CN-D1 to CN-D9 as above. In particular: the default is last turns (no model call), and Continue looks only at the active provider.
- CN-D9 is the only Claude change; the owner should look at it.
