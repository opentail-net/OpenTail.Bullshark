# Provider seam: design notes (2026-11-19, a design only; nothing built)

Status: **partly superseded by `provider-codex-2026-11-21.md` (2026-11-21).** The plan replaces the single `ISessionProvider` below with separate small interfaces (history, transcript reader, terminal opener, supervised launcher, live source) and takes precedence wherever the two differ. Tier 4 (Ollama naming) is **not part of the Codex plan: deferred, unscheduled.** Tier 0 (plain terminal tab) is folded into the plan's stage C. Status of this file: **proposal for the owner.** No code change is planned from this file without their word. It exists so the decision can be taken with the facts in one place. Research: `../research/other-ai-tools-research-2026-11-19.md`.

## Goal
Let the window show, search and watch sessions of other AI terminals (and use local models for its own small jobs) without weakening the Claude Code path, and without ever typing into a session for the owner or accepting/merging on their behalf (principle 3).

## Tiers (smallest first; each is useful alone)
- **Tier 4: a local model for naming and tagging (S).** A second `IMagicNameModel` (`OllamaNameModel`) next to `HaikuNameModel`; a Settings choice "Naming model: Claude Haiku / Ollama (model name from `GET /api/tags`)"; HTTP only to `localhost` (the one network call the app would make, opt-in, never a remote host; the host/port is a setting, default `http://localhost:11434`, and a non-loopback host is refused). Failure counts like any failed naming call. Privacy gain: no usage spent, nothing leaves the machine. Test with a fake HTTP handler; no real Ollama needed.
- **Tier 0: a generic terminal tab (S-M).** "New terminal" starts any command line the owner types in an embedded terminal with the Changes tab, tags, notes, idle/silent detection by output activity. No transcript, no supervision, no history. Label it clearly as "plain terminal".
- **Tier 1: the provider seam, read-only (M-L).** An `ISessionProvider` the App uses: `Discover()` (sessions with a provider id, title, folder, last activity, state if known), `OpenTranscript(session)` -> a normalised `TranscriptModel` (messages, tool calls, file writes, usage, times; unknown fields null), `Capabilities` (has limits, has cache, can resume, can list, has hooks), `AttachCommand(session)` (a command line for the embedded terminal; never executed by us without the owner opening the tab). Claude moves behind it first (pure refactor, tests must stay green); then **one** read-only second provider (Codex CLI is the researcher's first pick) as the proof. Library keys become `provider:sessionId` (migration: existing keys are `claude:`).
- **Tier 2: supervised start/resume (L, per tool).** Only for a tool with a stable list/state interface (opencode's HTTP API is the best fit; Codex `resume` is interactive). Otherwise stays tier 0/1. A guessed state machine is worse than none.

## What must not change
Claude Code behaviour, its tests (2000+), the privacy rules (no transcript text in logs/crash files/zips), the principle-3 rules, the single-supervisor guard, quiet hours/toasts semantics. A new provider's files are read-only inputs; a "format not recognised" state degrades to the plain terminal view and never crashes.

## Risks and open questions for the owner
1. Product scope and name: README and window title say Claude Code supervision; "OpenTail.Bullshark" is a project name, but the pitch changes if it supervises "AI terminals".
2. Maintenance: other tools' formats drift (opencode changed its DB schema in v2). Each adapter needs fixture files in tests and a version/format check.
3. Licence: Codex, Goose, aider, opencode, Cline, Qwen are Apache-2.0/MIT (formats readable from source); Copilot is proprietary (read what it writes, copy no code); Amp stores threads server-side (not local).
4. Privacy: every provider's transcripts hold the same kind of private text; the redactor, diagnostics allowlist and logs rules apply unchanged.
5. Effort: tier 4 about a day of coder time; tier 0 about a day; tier 1 about a week with the refactor of every `Claude*` assumption (grep count to be taken first); tier 2 per tool.

## Suggested order if the owner agrees
Tier 4 (Ollama naming), then tier 0 (plain terminal), then a **spike** of tier 1 with Claude behind the interface and Codex read-only; stop after the spike and look at the diff size before deciding on more.
