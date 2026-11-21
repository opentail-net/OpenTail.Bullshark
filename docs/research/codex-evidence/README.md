# Codex evidence library

What we have checked by running Codex on this machine, not what the docs say. Each probe is a script in `probes/`, and every run writes a sanitised log to `results/`. When a new question about Codex comes up, look here first; if it is not covered, add a probe.

## Version under test

- Codex CLI: **0.171.0** (`codex --version`). Installed by npm (`@openai/codex`); on PATH as `codex.ps1` and `codex.cmd`.
- Machine: Windows 11 (10.0.26200), PowerShell 7.
- Account: ChatGPT login, basic subscription. Models offered to this account are listed by `model/list` (see P04). Sol is not offered on this plan.
- Repo commit at the last run is written into each run log.

If the Codex version changes, do not overwrite old results: run the probes again and keep both runs, with their version.

## Rules for every probe

1. **Privacy.** Never print or log the content of the owner's Codex files (`~\.codex`: rollouts, databases, config values, session titles, previews). Log names, counts and types only. Ids are redacted to `<uuid>`; user paths to `<home>`; anything that looks like a secret to `<secret>`.
2. **Model calls are expensive for this account.** Only one kind is allowed: `say hi` with `gpt-6-luna`, behind the `-IncludeModelCall` switch. No long prompts, no tool use, no approvals.
3. **Process hygiene.** A probe may start a process only if it stops that process again. It never stops a process it did not start (for example a daemon or a session the owner runs). Probes that would start or stop the daemon are written up as owner tests, not run.
4. **Trust.** Model calls run from the OpenTail repo, which the owner already trusts for Codex. No probe passes `--skip-git-repo-check` (it saves trust for the folder).
5. **Evidence labels.** Each finding is one of: **OBSERVED** (seen in a run, cite the run log), **DOCUMENTED** (official docs, with the quote), **SCHEMA ONLY** (in the generated app-server schema, not run), **UNCONFIRMED**.

## Index

| Probe | Question | Latest run | Result (version 0.171.0) |
|---|---|---|---|
| P01 | Which Codex is installed and where | 2026-11-21 16:16 | OBSERVED: npm `@openai/codex@0.171.0`, on PATH as `codex.ps1`. A separate Codex desktop app (WindowsApps package, version 26.1117.4957.0) is also installed, with its own app server. |
| P02 | Help text for the commands we rely on | 2026-11-21 16:16 | OBSERVED: saved verbatim in `results/help/` (root, exec, resume, queue, agents, app-server, app-server daemon, remote-control). |
| P03 | Is a shared daemon running now, and which Codex processes exist | 2026-11-21 16:16 | OBSERVED: `app-server daemon version` reports `status: running`, backend `pid`, managed version 0.171.0, with a control socket under `~/.codex`. Several other `app-server` processes on fixed ports belong to other tools; not touched. |
| P04 | Read-only app-server calls | 2026-11-21 16:16 | OBSERVED: `model/list` gives gpt-6-luna (default), gpt-5.6-terra, gpt-5.6-luna (not hidden), plus two hidden entries. `thread/list` returns 59 threads, all `notLoaded`. `thread/loaded/list` returns 0. `account/rateLimits/read`: primary window usedPercent 73, window 43200 minutes (30 days), reset time present. |
| P05 | `say hi` with gpt-6-luna: event order, and when the thread id appears | 2026-11-21 16:17 | OBSERVED (`codex exec --json` only, not the interactive terminal): exit 0, 3.8 s, 4 events. Event 1 (`type`, `thread_id`) comes before any model output. The others are a message item and a usage event. |
| P06 | Claude daemon: running, what keeps it up | 2026-11-21 16:17 | OBSERVED (Claude 2.1.330): `claude daemon status` reports a transient daemon, started on demand by a `claude` process whose folder is the App's net10.0 build. It stays up while background workers run or a `claude agents` is open ("daemon waits for them to settle"). The help says it idle-exits once nothing is connected. |
| P07 | Claude background sessions: state values | 2026-11-21 16:17 | OBSERVED (`claude agents --json --all`): 20 items; states done 14, stopped 3, working 1, blocked 1, and one with no state key. Without `--all`: 4 items. The `failed` state did not appear in this run. |

Owner tests (not run by probes, because they change processes or need the owner's terminal): daemon start/stop with and without `--no-daemon`; an approval wait in a plain terminal session; a Codex tab opened through our own server with `--remote`.

## Files

- `probes/run-claude-probes.ps1`: read-only Claude probes (P06, P07). No model calls.
- `probes/run-probes.ps1`: the Codex probes. Run `pwsh -File probes\run-probes.ps1` (no model calls), or add `-IncludeModelCall` for P05.
- `results/<date>-run.md`: the sanitised log of each run.
- `results/help/`: saved `--help` output for the commands we use.
- `results/claude-help/`: saved `claude --help` output (Claude 2.1.330).

## Related research (one folder up)

- `docs/research/claude-docs-index.md`: the Claude Code documentation for agents, the daemon, states, restarts, hooks and limits, with URLs, quotes and status labels.
- `docs/research/app-restart-behaviour.md`: how the App behaves across restarts, from the code, with live tests for the owner.
- `docs/research/rail-feature-inventory.md`: the rail feature inventory, Claude and Codex side by side.
