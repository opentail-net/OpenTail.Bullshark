# Bringing in a new CLI provider: the onboarding playbook

Status: **process, from practice.** Written 2026-11-22 from the Codex provider (2026-11-21) and the GitHub Copilot CLI provider (2026-11-22). Use it for any further CLI (for example opencode, aider or Gemini). The owner's working rules are in `~/.claude/CLAUDE.md`; this file says how they apply to a provider. Every gate below is a check that can fail; a step is not done until its gate is met.

## What a provider is

A provider is one AI CLI that the app can list, read, resume and summarise. Each one implements only the interfaces it has (`Core/Providers/Capabilities.cs`: history, transcript, terminal opener; supervision and live list are optional and Claude-only). The app shows a provider in its rail, through the provider dropdown (`ProviderChoice`, `ProviderId`, `ProviderPaths`, `SessionKey`). Claude and Codex must not change when a provider is added.

## Stage 1: research (researcher agents, effort xhigh)

- **Vendor facts** (one question per agent): install and version; where sessions are stored and their format; the list and resume commands; launch flags (folder, model, prompt, non-interactive mode, tool limits); hooks; SDK or protocol servers; licence and terms; sign-in and quota.
- **Local facts** on the owner's PC: `--help` output, the installed package's names and shipped schema, and the session folders' **names, key names and counts only**. No values, no text.
- **Examples** in the reference folder: concept notes only, one licence check per project. No code is carried across.
- Output: a research note in `docs/research/`, each fact marked VERIFIED or ASSUMED, with corrections to earlier notes.
- **Gate:** the orchestrator re-checks the decisive facts on this PC without a model call.

## Stage 2: probes (scratch only)

- **Rule:** no probe writes into the owner's real home folder. Give each probe its own config and log folders, and delete them afterwards. Stop only processes we started.
- **Model calls:** at most one, on the owner's allowance, and only with the owner's go-ahead. Record the premium count and the model. Read the output before deleting scratch.
- **Questions to answer** (adjust to the CLI): does a new session take an id from us; what does an unknown resume id print; can a summary run without the owner's sessions being written; is there a version read that does not download; what the sync and telemetry defaults are; which field holds the user's own words (is there a transformed copy?); how a long prompt reaches the CLI (a Windows command line is about 32,000 characters, and some CLIs take no stdin prompt); whether the shim survives quotes and new lines.
- **Gate:** every question is answered, or marked deferred with the step it blocks. Results go into the research note.

## Stage 3: plan (one plan per provider, checkbox list)

Write `docs/plans/provider-<cli>-<date>.md` with these parts, in order:

1. **Goal**, and the parity target stated plainly (what the template provider does, and what is left out, with a reason).
2. **Decisions** `G-D1…` (defaults, made without the owner, each with its reason) and **owner items** `G-O1…`, each with a default that is safe to run on.
3. **Parity table**: each template feature → the step that builds it → status. Gaps are named, with a reason.
4. **Contracts** against the seam: each member, its file and line, what callers rely on, and what the new provider does. Read the code; do not work from memory.
5. **Existing-behaviour map**: each template file → copy, new, or not copied, at file level. Each copy gets a branch-level map before it is written.
6. **Stages** G0 (probes) to G5 (optional), each with a gate and a commit rule.
7. **Risks**, and a review disposition table at the end, filled in later.

## Stage 4: review of the plan

- A reviewer (Sonnet, allowed for a high-risk plan) reads the plan against the code and the research. Findings are graded must, should or note, and go in a review log. Every finding gets a disposition in the plan.
- **Gate:** no must-fix item is open before any code is written.

## Stage 5: Core (one coder)

- Read-only first: history source, transcript reader, summary, terminal command lines, setup check, start launcher, resume text, provider class.
- Shared seam files get **additive** edits only. Claude and Codex tests must pass unchanged.
- Fixtures are synthetic, written by hand from the key names. Logs carry counts and exit codes only. No session text in any exception message.
- The coder does not commit. The orchestrator reads the diff, reruns the tests on each target, and commits only the task's own files.
- **Gate:** the tests pass on every Core target, with counts recorded before and after.

## Stage 6: App (one coder)

- Copy the template provider's App partials (rail, search, transcript tab, row menu, exit watcher, setup row) and rename them. Shared window files get Copilot-style branches only.
- No blocking call on the UI thread. A check that calls a process is asynchronous (a blocking wait deadlocks when the check's continuation needs the UI thread).
- Start-up cleanup of scratch folders runs only in the window that holds the single-instance lock for that state file, and before any call.
- **Test target names:** use the full names (for example `net10.0-windows10.0.19041.0`). The short name runs zero App tests and still exits 0.
- **Gate:** the App tests pass on each full target, with counts recorded before and after.

## Stage 7: resume screen and summary

- Copy the template's resume screen. Modes: full resume (terminal), last turns (no model call, the default), and summary (one call, at a level). Show the cost line before any call.
- A resume is refused, with a status line, when the id is not in the app's own list or the folder is missing. No silent fallback.
- One run at a time. A window that does not hold the state lock makes no new summary.
- Screen checks: write a script with a fake shim and a fake summary source. Run it **only** with devenv closed and no CLI process of the owner's running, and never against the owner's state folder.

## Stage 8: review of the diff and fixes

- A reviewer reads the whole range. Look for: privacy (text in logs, names, fixtures), scratch isolation, every command line carrying the sync or telemetry switch, nothing typed into a session, deadlocks, failure paths that end in a status line, and any change to Claude or Codex results.
- Fixes go to a coder with exact specifications. The orchestrator reruns the tests, reads the fix diff, and records each disposition in the review log and the plan.

## Stage 9: licence, owner decisions, commits

- The project's licence file gets a preamble statement of independence and of the names used, above any unchanged licence text. Do not edit standard licence terms.
- Owner decisions are recorded with their defaults, and the owner's acceptance is recorded with its date.
- Commit each stage locally with the attribution lines. **Push only on the owner's approval**, and say how many commits a push would send (the branch may hold earlier work).

## Checklist for the next CLI

- [ ] Installed here: version, every shim (exe, cmd, ps1), and which one a shell runs first.
- [ ] Research note with VERIFIED and ASSUMED facts; examples read for ideas only.
- [ ] Probes run in scratch; one model call at most, with the owner's go-ahead.
- [ ] Plan with parity table, contracts, file map, decisions, owner items with defaults.
- [ ] Plan review; no open must-fix.
- [ ] Core built by one coder; Claude and Codex tests unchanged; counts recorded.
- [ ] App built by one coder; shared files only add branches; full-name test targets.
- [ ] Resume screen and summary; refusals; guards; screen script written (run with devenv closed).
- [ ] Diff review and fixes; dispositions recorded.
- [ ] Licence preamble checked; owner decisions recorded; commits local; push waits for approval.

## Gotchas (learnt on this PC)

- **Test targets:** App tests need the full target names. A short name runs zero tests and exits 0.
- **Git Bash and cmd.exe:** Git Bash rewrites the `/d` and `/c` switches. Run `cmd.exe` with `MSYS_NO_PATHCONV=1`, or the command never runs.
- **Prompts:** keep them off the command line and out of stdin where the CLI does not read it. Put the text in a file in the working folder and let the CLI read it with a read-only tool. Keep cmd.exe lines free of quotes, percent signs, ampersands and exclamation marks.
- **PowerShell:** `pwsh.exe` 7 with a `.ps1` shim passes multi-line text intact. Windows PowerShell 5.1, and a `.cmd` shim, drop or cut quotes.
- **Deprecated flags:** a CLI may warn about a flag (Copilot's `--config-dir` warns and names `COPILOT_HOME`). Keep the tested route until the replacement is verified.
- **Single-instance lock:** it is per state file, so scratch folders beside that file are safe to clean at start-up only in the window that holds the lock.
- **Coders' counts:** rerun them. A coder's count can come from a wrong target.
- **Pre-existing flake:** `Trust\ClaudeTrustRetryTests.cs` can abort a net11.0 run with an IOException (file in use). Rerun once; do not change it in a provider task.
