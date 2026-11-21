# CLI gates: throwaway probes that decide what gets built

Each gate is one short probe of the installed `claude` CLI. Run it in a throwaway git folder, never in one of the owner's repos, and with the owner present where a real session starts. Write the answer under the gate's heading. The features listed under a gate wait for its answer. G1 comes first: G2, G3 and G9 need its trust step.

Probe status: G2 was attempted on 2026-11-17 and refused by the trust step (no session was started). No other gate is recorded as run.

## G1. Trust one throwaway folder (prerequisite for G2, G3, G9)

Run `claude` once in a new throwaway git repo and accept the trust prompt. `--bg` refuses an untrusted folder. The trust record is per path in `~/.claude.json`, and granting it writes that file, which changes the owner's permission settings. So this step needs the owner's word. The evidence is under G2.

## G2. `--bg -w <name>`: a worktree per coder, and `--cwd` for listings

- [ ] **Gate, do first:** start one throwaway background session in a scratch git repo with `--bg` alone, then with `--bg -w <name>`; record in this file: where it runs, what `agents --json` shows (cwd, any worktree field), what `claude rm` does. Then `claude rm` both and delete the scratch repo. If `-w` does not work with `--bg`, stop this item and record why. **Result 2026-11-17: not run; `--bg` refused the scratch folder as an untrusted workspace. Item 3 skipped, see D3.1.**

- D3.1 Skipped. The gate was run as far as it could go: `claude --bg --model haiku --effort low --name gate-plain "reply with the word ok and stop"`, in a new scratch repo (one commit, under the session scratchpad, not one of the owner's repos). It exited 1 with `Workspace not trusted. Run claude in <scratch folder> once and accept the trust prompt, then retry.` `claude agents --json --all` listed the same 20 sessions before and after, so nothing was started. The `-w` form was not run, because the first step refuses. The gate is therefore unclear, and the brief says to skip the rest of the item. Nothing of item 3 is built: no `Worktree` on `SessionSpec`, no `-w` in `BackgroundLaunch`, no "Remove session", no tests for them.

- D3.2 Trust was not granted for the scratch folder. The trust record is kept per path in `~/.claude.json` (`projects[path].hasTrustDialogAccepted`; the scratchpad folder of another session there is `false`). Granting it needs an interactive `claude` run in the folder, which writes that file, and that changes the owner's permission settings. I only read `~/.claude.json`.

- To finish item 3: the owner accepts trust once for a throwaway folder (or names one that is already trusted), then the gate is run again. The same trust gate stops any run in an untrusted repository (finding F24), so the setup page's trust step matters to item 3 as well.

Features that wait for this gate: the build items for per-coder worktrees in [supervisor.md](supervisor.md) section 3, including the `--cwd` check of additions item 6; the rule that skips sessions in their own worktree (gems item 1, in the same section); the Changes tab reading `.claude/worktrees/` (same section); and idea 11 in [ideas.md](../later/ideas.md).

## G3. `--bg --session-id <uuid>`: does the session keep the id?

Round 7 calls this Gate 1.

- **Gate 1: `--session-id` with `--bg` (item 6).** Owner runs one throwaway start, in a folder the owner trusts, and nothing else:
  `claude --bg --session-id <a fresh uuid> --name gate-probe "Reply with the word ok and stop."`, then `claude agents --json --all` and
  the session's transcript file. Question: does the session id match the uuid? F18 (2026-11-14) said `--bg` ignored it; the 2.1.326 help lists
  `--session-id <uuid>`; the facts table above says the research found it working with `--bg`, but research section 8 only lists the flag and is not a test. If it matches, Bullshark can name the transcript before
  launch and drop the read-back poll (`ResolveSessionIdAsync`). Stop and remove the probe afterwards, with the owner's own `claude stop`/`rm`.
- **Gate 1 also records D1.6, now informational only.** Run `claude daemon status` before the start and after it, and note whether the daemon is
  up after `--bg`. The item 1 rule no longer depends on the answer (D1.9): a daemon that is not running stops nothing, so no rule has to change.
- Nothing in this round runs that start. The builder did not start any session (the brief forbids it).

## G4. Dead row name (round 8 B1.4)

- [ ] **A dead row's name**: does `claude --bg` accept a name that a dead listing row still holds? (round 8 B1.4)

## G5. `claude respawn --all` (round 8 B3.2)

- [ ] **`claude respawn --all`**: does it restart stopped sessions, and does a blocked session lose its prompt? (round 8 B3.2)

## G6. `claude --bg` and stdin: does it take the prompt on stdin, or after `--`?

- Does `claude --bg` accept the prompt on stdin or after `--`? If yes, item 4's limit can go.

If yes, the launch-length guard of review 2026-11-17 item 4 can go (record: Done/review-2026-11-17.md).

## G7. `agents --json --all`: how a session stopped at the usage limit is listed

This decides phase 2 of limit wake, and it is the gate of backlog A3 (the paused-on-limit reason; see [limits-usage.md](limits-usage.md)).

- [ ] LW4 listing: record what `claude agents --json --all` lists for a session that stopped at the limit (its `state`, and whether it appears in the active list). This decides phase 2 (typing into an attached session); write the answer here.

## G8. `--bg` with `--permission-mode auto`

Coders run in `auto` (owner's decision of 2026-11-19). Not tested: whether `--bg` accepts it. The line below is the CLI fact as it stood then.

- CLI facts: `claude --version` 2.1.328; `claude --help` lists `--permission-mode` choices acceptEdits, auto, bypassPermissions, manual, dontAsk, plan; `--bg`, `--session-id` and `respawn` exist. Whether `--bg` accepts `--permission-mode auto`
  was NOT tested (it would start a real session).

## G9. Live cost over OpenTelemetry (gems item 3)

- [ ] **Gate, do first, no code:** with the owner, start one throwaway interactive session in a trusted folder with `CLAUDE_CODE_ENABLE_TELEMETRY=1`, `OTEL_LOGS_EXPORTER=console`, `OTEL_METRICS_EXPORTER=console`; record the event and metric names and the attributes that carry the session id, model, cost, tokens and tool name. Then check whether a `--bg` launch from a process with those variables exports too. Write both answers in this file. If session ids are not carried, stop the item.

Build items, waiting for G9 (not started):

- [ ] Core: `OtlpReceiver` on `127.0.0.1` only, OTLP/HTTP JSON (`/v1/logs`, `/v1/metrics`); parse into per-session live totals and a ring buffer of recent tool events. No protobuf (no package): set `OTEL_EXPORTER_OTLP_PROTOCOL=http/json`.

- [ ] Enabling: only for sessions Bullshark starts, via the launch environment if the gate shows it works. Never edit `~/.claude/settings.json`; if only that works, record it as an owner decision.

- [ ] Window: live cost and last tool on the session row; transcript totals stay the record of truth.

- [ ] Tests: parsing captured sample payloads from the gate; receiver binds loopback only; unknown attributes ignored.

- Item 3 (OpenTelemetry) waits for the owner's gate. Not built and its gate not run: the gate needs the owner to start a session with telemetry on, and the owner is away (2026-11-17).

## G10. Two sessions of one name

  - Not verified: whether the CLI itself allows two sessions of one name (the guard does not rely on it).

The guard in the lifecycle batch does not rely on the answer, and the name-clash check (checklist C10) tests the guard, so it does not need the answer either.

## G11. CLI switches not yet in use (setup-and-sessions S9)

Each switch is tested on its own in a scratch folder. S9a (`--worktree`) is the same probe as G2: run it once.

- [ ] S9 Evaluate CLI switches not yet in use (added 2026-11-15). Each switch is tested on its own, in a scratch folder, with Haiku and small prompts. Every test writes a finding (F41 onward). Each switch ends with one decision: adopt, park, or drop.
  - [ ] S9a `--worktree <name>`: two sessions write the same file in separate worktrees without clashing; `git worktree list` shows them; removal through `claude rm` leaves no stray worktree. Works if all three hold.
  - [ ] S9b `--fork-session` with `--resume`: the new session has a new id; the original transcript's size and last line are unchanged; the fork continues from the old context. Works if all three hold.
  - [ ] S9c `--effort <level>`: the same prompt at `low` and `high` gives different output token counts, read from the transcript. Also check that haiku accepts each level.
  - [ ] S9d `--add-dir` and `--restricted`: a session in one folder cannot read a file in another folder without `--add-dir`; with `--restricted`, record which tools disappear (Bash is expected to). Decide whether coders can use it.
  - [ ] S9e `--fallback-model`: the flag is accepted, and the behaviour when the first model is unavailable is recorded, if it can be forced without cost. Otherwise park it, with the reason.
  - [ ] S9f `--autocompact`: optional, because it needs a long session. Park it unless a cheap test exists.
  - [ ] S9g `--output-format stream-json --include-hook-events` in print mode: one headless prompt; the events parse as JSON lines. Relevant to the rail's live status.
  - [ ] S9h `--from-pr`: only if a pull request exists to test against. Otherwise park it.
  - Cost limit: at most five short test sessions in total, on Haiku at low effort. Stop and record when the limit is reached.
  - Owner's sessions are never touched. Test sessions are named `ovc-test-*` and removed afterwards.
