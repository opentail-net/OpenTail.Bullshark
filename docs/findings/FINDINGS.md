# OpenTail.Bullshark findings log

Each finding has an ID (F1, F2, ...), the date, what was tested, the evidence, and what it changes. Newest findings are appended at the bottom. A finding is only marked **verified** when a test or a run shows it; otherwise it is marked **inferred** or **open**.

---

## F1 Background sessions work (verified, 2026-11-14)

- `claude --bg --model haiku --effort low --name ovc-probe-1 "..."` prints `backgrounded · <8-char id> · <name>`. The first call prints `Starting background service…`, so the daemon starts on demand.
- `claude logs <id>` returns the session's terminal output, including escape codes.
- The earlier `claude logs` failure (`connect ENOENT \\.\pipe\cc-daemon-*-control`) happened before any background session had been started. Inferred cause: the service was not running. Re-check in P2.
- **Do not read** files under `~\.claude\daemon\` (they hold key files). The supervisor uses only public `claude` commands.

## F2 Session states (verified, 2026-11-14)

- `claude agents --json` returns an array. Background entries have `id`, `cwd`, `kind: "background"`, `startedAt`, `sessionId`, `name` and `state`. Interactive entries have `pid`, `kind: "interactive"`, `status`, `cwd`, `startedAt`, `sessionId`, `name`.
- `state` values seen: `working`, `blocked`, `done`. Interactive `status` seen: `busy`.
- A finished background session stays listed with `state: "done"`. It is not removed.

## F3 Output leaks out of the embedded terminal when the app inherits a console (verified, fixed)

- **Symptom:** started from a console or with redirected standard handles, the child's output (for example `ping`, `claude attach`) went to that console. The embedded terminal showed only the title sequence, then `Session Terminated`.
- **Cause (inferred):** the child process inherited the app's standard handles or console.
- **Fix:** at startup the app calls `FreeConsole()` and sets `SetStdHandle` for input, output and error to zero (`App.xaml.cs`).
- **Verified:** launched from bash (a console) with `cmd.exe /c ping`, the console got 0 bytes and the terminal captured 5 `Reply from` lines.
- **Rule for the supervisor:** do not start sessions with inherited handles.

## F4 Text capture needs output logging on (verified)

- `ExtendedTerminalControl.LogConPTYOutput` must be `true`. With it `false`, `TermPTY.GetConsoleText` throws `NullReferenceException`.
- With it on, `GetConsoleText(true)` returns the output with escape codes stripped.
- The first line of captured text is the child's executable path, taken from the terminal title sequence. Strip it when parsing.

## F5 `claude attach` runs inside the embedded terminal (verified, P1 core question)

- `claude attach ovc-probe-1` in the embedded terminal shows `Attaching…`, then the full Claude Code screen: colours, the prompt box, the `auto mode on` line, and the session's history.
- Screenshot: `C:\Users\Dmitri\AppData\Local\Temp\bullshark-spike\attach-probe.png`.

## F6 Typing into the embedded tab reaches the session (verified, nudge mechanism)

- `TermPTY.WriteToTerm("Reply with exactly the words TYPED OK and nothing else.\r")` sent into the attached session. The session replied `TYPED OK` (the transcript shows the reply).
- So a nudge sent through the tab works as real input.

## F7 Closing a tab detaches and leaves the session running (verified for one case)

- `OnClosing` calls `DisconnectConPTYTerm()` and disposes the terminal. The library then throws `ArgumentException: Process with an Id of N is not running`. The app catches and logs it (`%LOCALAPPDATA%\OpenTail.Bullshark\logs\app.log`). Inferred: the attach process had already exited when the terminal was disposed.
- Without this handler, closing the window throws `ObjectDisposedException` from `TermPTY.CloseStdinToApp`.
- A background session that was `working` when its tab closed stayed `working` two seconds later (`ovc-long`, a 40-second `ping`). Completion check: see F7b.

## F7b Long session completion after tab close (verified)

- `ovc-long` ran a 45-second Bash tool call. Its tab was closed during the call. The transcript then records `turn_duration` 45225 ms and the session reached `done`, so the session finished on its own after the tab closed.
- Acceptance test T5 in `scripts/Test-Spike.ps1` repeats this: the session is still `working` after the tab closes.

## F8 Sessions show a login-expiry warning (verified)

- Every session shows `⚠ Your login expires in 3 days · run /login to renew`.
- **Impact:** an unattended session will eventually stop working. The supervisor should read this warning and raise an alert before it expires (a P7 item).
- Account in use: `Claude Pro` (from the session banner).

## F9 Session model and effort shown in the banner (verified)

- The banner reads `Haiku 5.5 with low effort · Claude Pro`. The probe sessions used `--model haiku --effort low`, which is the cheapest route for tests.

## F10 Transcript format (verified, one file)

- Location: `~\.claude\projects\<encoded cwd>\<sessionId>.jsonl`. The encoded cwd replaces `:` and `\` with `-` (`C:\Git` becomes `C--Git`, and `C:\Users\Dmitri\AppData\Local\Temp\tmp.4ao6g4mss4` becomes `C--Users-Dmitri-AppData-Local-Temp-tmp-4ao6g4mss4`).
- One JSON object per line. Records with `type` `user` or `assistant` carry `message.content` blocks. Assistant records include `message.model` and `message.usage` with `input_tokens`, `cache_creation_input_tokens`, `cache_read_input_tokens` and `output_tokens`. These fields make a spend estimate possible (P7).

## F11 Screenshots and overlapping windows (verified, to improve)

- `CopyFromScreen` captures whatever is on screen, so other windows showed at the edges of the terminal screenshots. Fix planned: capture the window itself with `PrintWindow`.

## F12 Startup argument quoting (verified, tooling)

- `Start-Process -ArgumentList` with an array joins arguments without quoting, so a value with spaces is split. Use `scripts/Run-Spike.ps1`, which quotes the command.

## F13 WPF-UI.Terminal internals (verified from source, `ded266a`)

- `RestartTerm` runs `TermPTY.Start(cmd, cols, rows, logOutput, null, workingDir)` on a background task.
- `ProcessFactory` creates the child with `CreateProcess` and `EXTENDED_STARTUPINFO_PRESENT`, and attaches the pseudo console with `PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE`. That is the documented ConPTY route.
- `StartupCommandLine` defaults to `powershell.exe`. It is set before the control loads in our app.

## F14 `stop` and `rm` take session IDs, not names (verified)

- `claude stop <name>` and `claude rm <name>` print `No job matching '<name>'`. Use the 8-character ID that `claude --bg` prints, or the ID from `claude agents --json`.
- `claude attach <name>` and `claude logs <name>` accept names (help text). Only `stop` and `rm` need IDs.
- `claude stop` is asynchronous. `rm` right after `stop` can fail, so wait until the state is `done` first (`scripts/Test-Spike.ps1` does this).
- `claude rm <id>` removes a finished session from the list. Its transcript stays on disk.

## F15 Acceptance results for P1 (verified, two full runs)

- `scripts/Test-Spike.ps1` passes T1-T5 on two runs. Cleanup leaves only the owner's sessions.
- T4 counts 22-24 matches of `TYPED OK`. Terminal redraws repeat the text, so the assertion is a lower bound (at least two), not an exact count.
- Cost of one run: four short Haiku sessions at low effort.

## F16 Open items carried into P2

- Window screenshots still capture overlapping windows (F11).
- The library's close-time exception is caught (F7). A cleaner fix may exist in a later library version.
- `claude --bg` printed `Starting background service…` during cleanup. Check whether the daemon stops when idle, and whether that matters for the supervisor's timing.

## F17 The account is a subscription, not pay-per-token (verified from the banner)

- Session banners read `Claude Pro`. Usage is therefore a subscription allowance, not dollars per request.
- `--max-budget-usd` does not apply (print mode only). P7 should count tokens per session from the transcript `usage` fields and alert on the owner's own thresholds, not show dollars.

## F18 `claude --bg` ignores `--session-id` (verified)

- Output: `warning: --bg manages the session id; ignoring --session-id (use --resume <id> to continue an existing session)`.
- Consequence: the supervisor starts a session, reads its real session id back from `claude agents --json` (matching the 8-character id that `--bg` prints), and records that id. `ClaudeCli.ResolveSessionIdAsync` does this.

## F19 A resumed session with no prompt is idle and is listed as `blocked` (verified)

- `claude --bg --resume <sessionId>` with no prompt prints `backgrounded · <id> · <name> (idle — send a prompt to start)`.
- `claude agents --json` then lists it with `state: "blocked"`, and the session never starts working on its own.
- Consequence: `blocked` is ambiguous. It can mean a permission prompt or an idle session. The supervisor never answers either on the owner's behalf; it alerts. A resume now carries a short continuation prompt (`SessionSpec.ResumePrompt`), so the session goes straight to `working` and then `done`.
- Manual replica with the continuation prompt: same session id after resume, state `done`.

## F20 The live supervisor test is still failing after a resume (open)

- `SupervisorLiveTests.ALostSessionIsResumedUnderTheSameIdAgainstTheRealCli` passes the start and the removal steps. It fails at the check after the resume, when the session is not listed under its session id.
- A manual replica (start, wait done, `rm`, resume with a continuation prompt) passes. The difference is still being isolated.

## F21 A session that asks a question also shows as `blocked` (verified)

- Reproduction: resume with the production continuation prompt in a folder with no `docs/bullshark/PLAN.md`. The session replied with a question (where is the project checked out?) and stopped. `agents --json` listed it as `blocked`.
- So `blocked` means "waiting for input" in general: a permission answer, an answer to a question, or an idle session with no prompt.
- Consequence: the continuation prompt must not invite questions. It now says so (`SessionSpec.ResumePrompt`), and the alert text names all three cases.

## F22 Live messages between sessions work (verified, 2026-11-14)

- `ListAgents` lists the background sessions on this machine as peers, by name (for example `ovc-live-r3 [240529]`, `bg`, `idle`).
- `SendMessage` to a background peer returns `success` and queues the message. The idle peer switched to `busy`, read the message, and sent `ROUND TRIP OK` back. The reply arrived in this session as a `cross-session-message`.
- So sessions can talk directly and live, without a file in between.
- Caveats from the tool description: a message drains at the receiver's next tool round, and a session in a different permission mode holds the message for its user's approval. The reply above came from `from-mode=prompting`, so mode mismatch is a real risk for the planner (`auto`) and coder (`acceptEdits`) pairing.

## F23 Decision: direct messages are the handoff; files are the record (2026-11-14)

- Owner direction: sessions should see each other directly. A file-only handoff is a step back.
- New handoff: the planner sends the coder the batch pointer, and the coder reports back with `SendMessage`. `docs/bullshark/PLAN.md` stays as the durable record and the recovery point, because a session that is restarted or compacted has to recover from something.
- Open for the owner (D3 revised): both sessions must run in the same permission mode, or messages wait for approval. The default proposed is `auto` for both.

## F23 (revised) Handoff is live messages only; no file messaging (owner decision, 2026-11-14)

- The owner's direction, in their words: "No file messaging. That is pants!" The earlier F23 (plan file as the record) is withdrawn.
- Handoff: the planner sends each batch to the coder with `SendMessage`, with the batch text in the message. The coder sends its report back the same way. Verified live with `ovc-live-r3` (F22) and with the session the owner started (`git-7b`, reply `LIVE LINK OK`).
- Recovery without files: a restarted session resumes with its own transcript. `SessionSpec.ResumePrompt` tells it to ask the other session to resend the current batch by message.
- `PlanFile` keeps only a parser and `HandoffMessage`, which builds the message text. It does not write or read messages from disk.
- Open: a session that is compacted may lose a batch detail. The fix is to ask the peer to resend it. Measure how often this happens before adding anything else.

## F24 A new workspace needs the owner's one-time trust acceptance (verified, 2026-11-14)

- The end-to-end demo (`scripts/Demo-Handoff.ps1`, folder `samples/demo-handoff`) failed before any session started. `claude --bg --model` exited 1 with: `Workspace not trusted. Run claude in <folder> once and accept the trust prompt, then retry.`
- The supervisor logged the failure and stopped cleanly. It did not retry in a loop.
- In `~/.claude.json`, the earlier temp-folder sessions are recorded with `hasTrustDialogAccepted: false` and still ran. So the exact rule that triggers the prompt is not yet known. The sample folder is not recorded at all.
- Decision: OpenTail.Bullshark does not edit the trust settings to get past this. Trust is the owner's security decision. The owner runs `claude` once in `samples/demo-handoff` and accepts the prompt, then the demo runs again.
- Open: find the rule. Test by starting a session in a folder the owner has already accepted, and in a fresh folder, and compare.

## F25 The launcher opens one window with the two session tabs, and the handoff is visible (verified, 2026-11-14 18:20)

- `scripts\Start-OpenTail.Bullshark.bat` opens `OpenTail.Bullshark.App.exe` with the demo folder and task. The window has a tab per session (`ovc-planner`, `ovc-coder`), each an embedded terminal running `claude attach <id>`.
- Captured from the window: the planner's tab shows `Message from @ovc-coder: Batch 1 done, with one deviation about the location`. So the live handoff happens through `SendMessage` and is visible to the owner.
- Closing the window detaches the tabs, and both sessions stay `working` (observed).

## F26 The coder worked in a git worktree inside the repo, not in the demo folder (verified, open)

- The coder's report reached the planner as a deviation about the location. The planner's transcript shows `od -c` on `C:/Git/split/OpenTail.Bullshark/.claude/worktrees/coder-hello-batch.../hello.txt`.
- So the coder created a worktree under `.claude/worktrees` in this repo. That is a side effect in the owner's repo, which the owner did not ask for.
- Open decision for the owner: whether coders may use worktrees. Until then, the brief should say which folder to work in, and cleanup of the worktree should be checked.

## F27 The bg start did not hit the trust prompt this time (observed, cause unknown)

- F24 recorded `Workspace not trusted` for `samples/demo-handoff` earlier today. The same folder started both sessions at 18:20 without the prompt.
- Cause not known. Not a fix. Keep F24 as the reference for when it reappears.

## F28 Trust is recorded per git repository root, and only git repositories are checked (verified, 2026-11-14)

- `~/.claude.json`, under `projects`, holds `hasTrustDialogAccepted` per path. The owner's repo root `C:/Git/split/OpenTail.Bullshark` is `true` (accepted by hand), and the demo folder inside it has no record of its own. It starts because it is inside that trusted root.
- Test: a plain folder outside any repo started a background session with no prompt. A git repository outside the trusted tree was refused with `Workspace not trusted`. So the rule is about git repositories, not folders.
- OpenTail.Bullshark reads this file only. It never writes `hasTrustDialogAccepted`: accepting trust is the owner's decision. `TrustCheck` (in Core) reports the state, and `--open-for-trust` opens `claude` in the repository root for the owner to accept.
- There is no CLI query for trust in the build checked. The JSON file is the list.

## F29 A session waiting for a peer's reply also shows as `blocked` (observed, 2026-11-14)

- The planner `5bc2852c` sent batch 1 and ended its turn waiting for the coder. `agents --json` listed it as `blocked`. Its last output says the message reached the coder's inbox but that session had not read it yet.
- So `blocked` also covers normal waiting between sessions, not only permission prompts and questions. The supervisor's alert will fire on normal handoffs unless it checks the peer's state first. Open: how to tell a wait for a peer from a stuck session.

## F30 A process error on my side: a check that started sessions (2026-11-14)

- I ran the supervisor against the demo folder with a new state file to test the trust check. The supervisor is not read-only: with no record it starts both roles. Two duplicate sessions with the same names started, and I stopped and removed exactly those two (`8a846fae`, `dbe49848`). The window's sessions were left alone.
- Lesson kept: a check must never use the start path. The trust check runs before the start path, and the test for it is offline.

## F31 The trust choice can be made inside the OpenTail.Bullshark window (verified up to the owner's choice, 2026-11-14)

- When a repository is not trusted, the window shows a Trust tab. The tab runs an interactive `claude` in the repository root in an embedded terminal. The dialog appears in the tab (screenshot `bullshark-spike\trust-tab.png`).
- The owner chooses "Yes, I trust this folder" in the tab (down arrow, then Enter). The window's trust check runs every 3 seconds while the tab is open. When trust is recorded, the tab is removed and the sessions start.
- Not yet verified: the owner's acceptance itself, and that the sessions start straight after it.
- Note: "No, exit" is the default highlighted choice. The status bar says which key to press.

## F32 The demo coder session disappeared after the window was closed (open, 2026-11-14)

- Before the window was restarted, `ovc-coder` `8aed1196` was `done` and listed. It was not listed afterwards.
- The old window was closed with `CloseMainWindow` and then `Stop-Process -Force`. That may be related, but it is not confirmed.
- The demo planner `5bc2852c` is still `blocked`, waiting for a reply from a coder that no longer exists.
- Open: check whether a window exit can end a background session, and whether the supervisor should resume a missing coder.

## F33 Duplicate session names confuse the planner (observed, 2026-11-14)

- The scratch planner reported that "two sessions have that name" (`ovc-coder`), because the demo pair and the scratch pair both use the same names.
- Fix needed: names unique per workspace (for example, a short suffix from the repository), so a message always reaches the right coder.
- Also noted: the scratch planner read the empty project and is waiting for the first real task, as intended.

## F34 Assistant usage repeats across records of the same message: naive sums over-count about 3.6 times (verified, 2026-11-14)

- Current session transcript: 725 assistant records with `usage`, but only 255 distinct `message.id` values. Up to 8 records share one id, one per content block, each carrying the same usage.
- Naive sum: output 1,408,366 tokens; cache reads 288,278,333; cache writes 1,701,971; input 1,462.
- Sum once per message id: output 394,535; cache reads 98,382,094; cache writes 594,252; input 518.
- So `TranscriptReader` (before this fix) over-counted by roughly 3.6x on output and 2.9x on cache reads. Fixed in this phase: usage is counted once per `message.id`; records without an id are counted once each.
- Open check: whether the usage values are identical across the records that share an id (the fix assumes they are; a test asserts it on real data).

## F35 ccusage 15.1.0 token totals match our global de-duplication exactly (verified, 2026-11-14)

- Method: ccusage 15.1.0 (JavaScript source read from the npm package) de-duplicates assistant usage by `message.id:requestId`, first occurrence wins, across the whole projects folder. Its source has `createUniqueHash` and a `processedHashes` set; records missing either key are counted every time.
- Run: `ccusage session --json --offline` on all 119 transcript files, compared with our own sum using the same rule across all files.
- Result (identical to the token): input 52,766; output 28,563,463; cache writes 65,743,222; cache reads 10,540,460,657. Output per model matches for all five models.
- Per-file de-duplication alone is about 2.5% higher (output 29,274,584; cache reads 10,886,650,325). The extra comes from the same message appearing in more than one transcript file. So the index must de-duplicate across files, not within one file.
- Naive sums (no de-duplication) are about 3x too high on output tokens and 2.5x on cache reads. That was our reader's bug (F34).
- ccusage's `sessionId` is the project folder name, not the session UUID (as its own docs warn). The index uses the UUID from the file name.
- Cost: `totalCost` is 0 offline, because pricing needs the network. This project shows tokens only (F17), so cost is not compared.

## F36 The first record of a message can hold a placeholder output count; keep the largest value (verified, 2026-11-14)

- Across all transcripts, 25,580 distinct message keys (`message.id:requestId`). In 23 of them, a later record has different usage from the first. In 19 of those 23 the later record has more output tokens. The first record shows a small placeholder count (for example 8), and the later record shows the final count (for example 435). That looks like streaming.
- Effect: ccusage 15.1.0 counts the first record, so its output total is about 12,559 tokens lower (28,563,463 by ccusage; 28,593,947 by the largest-value rule). Input, cache writes and cache reads are identical under both rules. The difference is 0.04% of output.
- Decision: `UsageLedger` keeps the largest value per field for each message. We will therefore differ from ccusage by this small output amount, and the difference is explained here.
- Also fixed in the same change: usage used to be attributed to the last model seen, not the model of its own record (regression test `EachMessageIsAttributedToItsOwnModel`).

## F37 OpenTail.Bullshark's index agrees with ccusage 15.1.0 on the same transcripts (verified, 2026-11-14)

- `scripts/Check-Usage-Against-ccusage.ps1` runs both on the same 119 files. OpenTail.Bullshark: `OpenTail.Bullshark.Supervisor --usage-report` (read-only, starts no session, reads 774 MB in about 4 seconds).
- Result: input 52,810 = 52,810; cache writes 65,803,891 = 65,803,891; cache reads 10,555,014,301 = 10,555,014,301. Output 28,626,115 vs 28,613,556: a difference of 12,559 (0.04%), which is exactly the placeholder difference in F36.
- So for tokens, ccusage is accurate on this data. The only difference is the placeholder rule, which we keep on purpose.
- Cost: ccusage's cost is 0 offline. We do not compare cost (F17).

## F38 WPF's hardware drawing paints blank on this machine; software rendering fixes it (verified, 2026-11-14)

- Symptom: any WPF window of OpenTail.Bullshark painted white, except the embedded terminals. The tab headers and the status bar never showed. A History tab or window with plain content (even a red block) was white. The terminals still drew, because they use their own DirectX surface.
- Ruled out, by test: the terminal sharing the process (the History app painted white on its own), setting content before or after load, the console detach (F3), and XAML versus code-built windows.
- Fix, verified: `RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly` at startup. With it, the History window paints its content, and the main window shows its tab headers and status bar, while the terminals still draw. Applied to both `OpenTail.Bullshark.App` and `OpenTail.Bullshark.History`.
- Hardware: AMD Ryzen 7 5700G with integrated Radeon graphics, no NVIDIA. The cause is not proven to be the driver; it is what the tests show.
- Decision: the History view runs as its own process (`OpenTail.Bullshark.History.exe`), opened from the History button. This keeps it out of the terminal window, and it is what the owner asked for in a separate panel. A panel inside the tabs is not used.
- The History window shows the index: 119 transcripts, 25,658 messages counted once (F34, F35, F36), with session headings. Resume options are disabled, as planned (D14).

## F39 claude-scrollback 0.1.0: useful viewer, but its server exposes transcripts on all interfaces (verified from the package, 2026-11-14)

- Package: MIT, pure Python, no runtime dependencies. Contents: `generator.py` (HTML), `server.py`, `__main__.py`.
- Usage: shows a token badge per message; no totals, no de-duplication. So it is not a source for the cost figures (F35 to F37).
- Risk: `server.py` binds `HTTPServer(("", port), ...)`, which listens on all interfaces. Running it exposes the owner's transcripts to the local network. Do not run the server as shipped.
- Use later: the standalone `generator.py` could export one session to a static HTML file (see `docs/later/claude-scrollback.md`). Not added now.

## F40 The transcript view on real data: this session's context was about 839k tokens at its last turn (observed, 2026-11-14)

- The History window opened the largest transcript, this session (`c71e8ad0`): 38 turns, 360 messages, 503 tool calls, 55 files written, and a context of 839,222 tokens at its last turn (cache reads and writes included).
- This is the cost the owner asked about: resuming this session in full would load about 839k tokens as context on the next turn. The resume button shows that figure before anything runs (it is disabled in this phase).

## F41 The state file could only hold one coder: records keyed by role would throw on two coders (verified by test, 2026-11-15)

- `StateStore.Load` built its dictionary with `ToDictionary(r => r.Role)`, so a second coder record would throw `ArgumentException`. `SupervisorRunner` also keyed records by role, so two coders would overwrite each other.
- Fixed in S1: records are keyed by session name, and `Role` stays in the record. A duplicate name keeps the record with the later `StartedAt`, and the other is logged.
- Tests: `TwoCodersWithDifferentNamesLoadAndSaveWithoutAnError`, `TwoRecordsWithOneNameKeepTheLaterStartAndLogTheOther`, `TwoCodersOfOneRunStartWithTheirOwnRecords`.
- The file that already exists loads unchanged, under its names: `TheFileBeforeTheNameKeyLoadsUnderItsNames`. No file rewrite is needed.

## F42 `ResolveSessionIdAsync` matches the short id, not the name (verified by reading the code, 2026-11-15)

- `ClaudeCli.ResolveSessionIdAsync` finds the agent with `a.Id == shortId` (ClaudeCli.cs:73). It does not match by name, so duplicate names do not make the supervisor read back another session.
- Duplicate names still matter for `attach`, `logs` and `stop` when given a name (F14), and for peer messages by name (F22, F33).

## F43 The supervisor host has its own spec list (verified by reading the code, 2026-11-15)

- `src/OpenTail.Bullshark.Supervisor/Program.cs:54-58` builds `DefaultPlanner` and `DefaultCoder` itself. The window builds the same pair in `SessionWindow.Specs` (SessionWindow.xaml.cs:49-53). Both are replaced in S5 by the active run from `runs.json`.

## F44 A transcript folder is the working directory with every character outside A-Z, a-z, 0-9 replaced by `-` (verified on real folders, 2026-11-15)

- Checked: 34 folders under `~/.claude/projects`. Four hold no transcript at all, so they are not checked. Of the 30 checkable folders, 29 match the rule: the first `cwd` in the transcript, with each character outside `A-Za-z0-9` replaced by `-`, equals the folder name.
- The one that does not match is the worktree folder `C--Git-split-OpenTail-Bullshark--claude-worktrees-coder-hello-batch1`. Its only transcript is `8aed1196-caaa-4c13-81ea-f2ef607248f5.jsonl` (the demo coder, F32). Its first `cwd` is `C:\Git\split\OpenTail.Bullshark\samples\demo-handoff`, the launch folder. There is no copy under the launch folder. So the folder name follows the worktree the session moved into. Cause unknown: the data does not say why the first `cwd` is the launch folder.
- Not covered: no checkable `cwd` contains `_`, a space or a non-ASCII character, so those are unverified. The longest folder name and the longest `cwd` are both 119 characters, and no truncation was seen. Longer paths are unverified.
- Consequence for OpenTail.Bullshark: `FolderKey` only groups a run's folder with the history groups. It does not find a session's transcript. `TranscriptReader.FindTranscript` searches by session id, so it still finds the transcript after a worktree move.

## F45 A hidden embedded terminal keeps its process, its output and its input (verified with a counter, 2026-11-15)

- Test: `OpenTail.Bullshark.App.exe --selftest <json> --no-pass` runs a PowerShell counter (`TICK n`, and echoes each key as `KEY x`) in an embedded terminal. It hides the terminal at 4 s, shows the terminal again at 14 s, sends `q` at 16 s, and writes the result at 19 s.
- Result: process started and alive; 10 ticks written while hidden (4 at hide, 14 at show); 5 ticks after show; the key `q` came back as `KEY q`.
- The terminal is hidden with `Visibility.Hidden`, not `Collapsed`, so it keeps its size. Nothing is moved between parents.
- Not verified: the same with a real `claude attach` client. The counter is not Claude. A test with a real session is left for the owner or a later test session.

## F46 A screen-copy screenshot captures whatever window is on top, not the window (observed, 2026-11-15)

- The first two window captures used `CopyFromScreen` on the window's area. Both saved another program's window (a browser), because the OpenTail.Bullshark window was behind it. Both files were deleted. I did open both images to check them before I knew what they showed, so both are in this session's transcript file under `~/.claude/projects`. Deleting the PNG files did not remove that copy. The owner decides what happens to it.
- Fix in `SessionWindow.SelfTest.cs`: `PrintWindow` draws this window only, so no other program's pixels can be saved. Hosted terminal content may be blank in such a picture; the self-test is what checks the terminal. A capture from `PrintWindow` gave the full window at 1375 by 875.
- The spike's `MainWindow.SaveScreenshot` still uses `CopyFromScreen` (spike code only, not the product). Do not use it for a review picture.
- Observed once: two captures run back to back in one script; the second produced no picture and no log line. A run alone worked. Cause not found.

## F47 The setup page builds, and its checks write nothing (verified by build and test, 2026-11-15)

- `Microsoft.Win32.OpenFolderDialog` compiles in this WPF build, so Browse is in the page. It was not clicked.
- Tests show that the folder check creates no folder and no file when the folder is missing, and that reading the run file with `Load()` creates no file (`FolderCheckTests`, `RunStoreTests`).
- Start writes `patterns.json` only, beside the state file. The screenshot run did not press Start, and it created no `patterns.json`.
- `scripts/Test-Pane.ps1` still passes 4 of 4 after the setup page took the placeholder's place. The setup page's screenshot was taken with `PrintWindow` and shows only the OpenTail.Bullshark window.

## F48 A failed start aborted the pass and dropped the records of the whole pass (found by review, fixed 2026-11-15)

- In `SupervisorRunner.CheckOnceAsync`, a failed `--bg` or a failed session-id read threw out of the loop, and the records were saved only after the loop. A planner started earlier in the pass lost its record, so the next pass would start it again.
- Fixed in S5: each action is handled on its own. A failed start is recorded with no session id and reported as an alert. Records are saved in a `finally`. A failed resume counts as a restart.
- Tests: `ASecondStartThatFailsKeepsTheFirstRecordAndIsNotRetriedOnTheNextPass`, `FailedResumesCountAsRestartsAndStopAtTheRestartLoop`, `AStartThatNeverShowsUpIsReportedAndRecordedWithoutASession`.
- Not measured: how often `--bg` fails for a real model and effort pair. S9c covers the pairs.

## F49 The warm and cold rule against the real transcripts (measured 2026-11-15 with `OpenTail.Bullshark.Supervisor --cache-report`)

- Scope: 120 transcript files; 25,879 requests, each counted once (message.id:requestId); 25,751 consecutive pairs. Sessions by lifetime of their cache writes: 108 one hour, 10 five minutes, 2 unknown. Read-only; the report took about 3 seconds.
- Method: for each consecutive pair in a session, the gap is put in a bucket with the limits of the lifetime the earlier request wrote with. The later request "rewrote" when its cache writes were at least half its context, and "read" when its cache reads were at least half.
- One hour, under 50 minutes (warm): 25,300 pairs. 0.075% rewrote the context, which is under the 0.5% bar in the plan. 99.8% read it.
- One hour, 50 to 60 minutes (cooling): 17 pairs. None rewrote. This suggests the "may rewrite" wording for this band is too pessimistic. The sample is small, so the limits stay as D-S7 has them.
- One hour, 60 minutes and over (cold): 47 pairs. 81% rewrote, and 19% still read the cache. The sample is small.
- Five minutes, under 4 minutes (warm): 385 pairs, none rewrote. 4 to 5 minutes: 1 pair, read. 5 minutes and over: 1 pair, rewrote. Too few to judge the five-minute cold limit.
- The "77% of turns rewrite after 1 to 6 hours idle, under 0.5% within an hour" figure in the plan had no recorded method (not in FINDINGS, and no script in the history). This measurement replaces it.
- Caveats: a partial rewrite below half the context is not counted as a rewrite, so the rewrite shares may be low. The cold sample is 47 pairs.

## F50 The rail labels are computed off the UI thread, and their wording is tested, not seen (built 2026-11-15)

- Running sessions: the pass reads the last 64 KB of each transcript (doubling up to 16 MB), off the UI thread, every pass. History rows read their transcript tails once when the history loads.
- Both kinds of row use the last request: the last assistant record with usage. The lifetime comes from its cache writes (ephemeral 1 hour or 5 minutes), or from the nearest earlier write.
- Tests: boundaries at 50 and 60 minutes and at 4 and 5 minutes, the label wording (tokens only), the tail reader on a huge last line, a partial last line, a file held open for writing, and the report's dedupe and buckets.
- Not verified on screen: the labels in the rail. The window was not run against a state with records, because that would start `claude attach` processes and migrate the owner's real state.

## F51 The rail splitter moves; a simulated drag reaches it (verified 2026-11-15)

- The window runs at 125% scale. The process reports the window as 1100 by 700 logical pixels, and a screen capture (PrintWindow) is 1375 by 875 physical pixels. A test that mixes the two misses the splitter. With the logical coordinates the drag reached the splitter: the log shows "drag started" and "drag completed", and the rail's width moved from about 253 to 289 logical pixels.
- The splitter's grip is now 6 pixels wide and drawn in the theme's muted colour. The rail's width is proportional (0.3 of the star width, minimum 180), so the splitter sets it.
- Not verified: how it feels to drag by hand. The owner's report that the splitter did not move was not reproduced in the owner's own session; the cause could be a missed grip (5 pixels, dark on dark).

## F52 A reviewer change sent by message is received, but the planner did not use the subagent (observed, 2026-11-15)

- Two live runs. Planner Haiku (low), coder Haiku (low). The reviewer started as self; a resume prompt to the idle planner changed it to a Sonnet subagent and gave the coder a second batch (bye.txt with BYE).
- Both runs: the planner sent batch 2, and the coder wrote bye.txt with BYE. The planner made no Agent call (structured parse of its transcript found none, though the Agent tool was in its tool list).
- Run 1: the planner polled the agent list while it waited, and the test timed out at 12 minutes. Run 2: the planner ended its turn after sending batch 2, its transcript has no reply to the coder's report, and it was still listed as working when the test ended. The test timed out at 8.5 minutes.
- The test's wait condition (every session "done") would never be met on success either: a planner waiting on a peer shows as blocked (F29). The fix is to wait for the coder's report message in the planner's transcript.
- Both leftover planners were stopped and removed by folder. No test sessions remain.
- Not established: whether a resume prompt wakes a waiting planner, and whether a Haiku planner will use the Agent tool when told to.

## F53 Phase 1 of the review: lost writes, adoption, start failures, close and resume (built and tested by code and unit tests, 2026-11-15; live sessions not run)

Source: the review in `docs/plans/review-2026-11-15.md`, Phase 1 only. Phases 2 to 4 wait for the owner.

- **Stores:** every change to `state.json` and `runs.json` reads the file again under a named lock (`FileLock`), changes it, and writes it to a temporary file with its own name, then moves it over. A file that cannot be read throws `StoreUnreadableException` and is not overwritten. Verified by `ACorruptStateFileIsLeftAloneAndNothingIsStarted`.
- **Supervisor pass:** writes only the records it changed for its own sessions, under the lock. A session the owner forgot during the pass stays forgotten. Verified by `ARecordForgottenDuringAPassStaysForgotten`.
- **Adoption:** a listed session with the spec's name is adopted, not started a second time. Verified by `AListedNameIsAdoptedNotStartedTwice`.
- **Failed start:** any exception from a start or resume is a failed start for that session; the other sessions of the pass still run. Verified by `APassSurvivesAStartThatThrowsAnyException`.
- **Listing:** output with no list is an error. Empty output is still no sessions. Verified by `ListAgentsTests`.
- **Trust:** an unreadable `~/.claude.json` counts as untrusted, and nothing starts because of it. Code only.
- **Process runner:** the timeout covers writing stdin; a timeout or a cancel kills the process tree. Code only.
- **Full resume:** refused when the session is listed. The kept-alive run gets the solo restart prompt. Verified by `AFullResumeGetsTheSoloRestartPrompt`.
- **Close:** lists the sessions first and stops only a listed session that is not done. A failed stop puts back the runs and the record. A closed tab stays closed until the owner selects the session in the rail. Code only.
- **Start:** the Start button is disabled while the run is written. The rail reads `runs.json` once per refresh. The log follows `--state`. A `--state` with no path is an error.
- **Supervisor host:** a pass survives any exception. Ctrl+C ends the loop. Its log sits beside the state file.
- **Resume text:** the closing line no longer invites questions the brief forbids. The summary is capped at 6000 characters.

Result: 242 Core tests pass. The app and the supervisor host build.

Not verified: the close and resume flows with real sessions, the Trust fallback and the timeout kill against the real CLI, and the UI on screen. These need the owner present (Phase 3).

## F54 Phase 2 of the review: the cuts that need no owner answer, and the close message (built and tested by unit tests, 2026-11-16; live sessions not run)

Source: the review in `docs/plans/review-2026-11-15.md`, Phase 2. Only the cuts with no open question were made.

- **Phase 1 re-checked:** committed as 51b51e7, which is in HEAD (8ac81ae). I read the code at HEAD, and the Core suite there passes 269 tests (the 51b51e7 commit message said 242). Start-failure, lost-write, adoption, listing, resume-prompt and timeout items are in the code; see the review's checkboxes for the file references.
- **Close:** a session that is idle (`done`) or unlisted is closed without a stop, and the status line now says "Closed ... Nothing was stopped" instead of "Stopped". The plan's rule was "stop unless unlisted"; question 6 of the review decides whether idle sessions should be stopped too.
- **Removed, with their tests:** `NudgePolicy`, `PlanFile`, `TrustCheck.OpenClaudeForTrust`, `SessionWindow.Rail` `HistoryLabel` (no callers), and `AppDataMigration` with its two calls (App and supervisor). The data copy's precondition holds: `%LOCALAPPDATA%\OpenTail.Bullshark` has `state.json` and `runs.json`.
- **Checked on the live data (read only, 2026-11-16):** no record in `state.json` or `runs.json` has the Assistant role. The records are `ovc-planner` and `ovc-coder`, so those names are data the app still reads.
- **Assistant parked by the owner (2026-11-16):** the idea is valid and can be deferred. Not cut; the code stays.
- **Not cut, waiting for the owner:** the legacy run with `ovc-` names and `--workdir`/`--task` (question 1), the spike (question 8), and one supervisor host (question 4).
- **Summary levels kept (owner, 2026-11-16):** all three stay. The 6,000-character cap on the start text cuts Complete short. The planned fix is a Summaries folder, with the path in the start text instead of the summary. It is recorded in the review's Phase 2 and not built; it waits for Phase 3 under the freeze.
- **Status lines:** the README and `docs/plans/setup-and-sessions.md` no longer say "design only". The README points at the review.

Result: 248 Core tests pass (269 minus the 21 tests of the removed code). The supervisor builds. The app build shows five warnings. Two nullable warnings in `RunPlan.cs:139-140` and two CS8601 warnings in `MainWindow.xaml.cs` and `SessionWindow.SelfTest.cs` are not from this pass (those files are unchanged from HEAD). The fifth, an un-awaited `Dispatcher.BeginInvoke` at `TranscriptView.cs:83` (CS4014), is in the owner's uncommitted change.

Not verified: the close flow with a real session, the Assistant and legacy cuts (not made), and the UI on screen. These need the owner present (Phase 3).

## F55 Tabs reorder, and the pane splits in two: a terminal moves between parents and keeps its process (built and tested with counters, 2026-11-16; no Claude session used)

Source: `docs/plans/tabs-and-panes.md`, T1 and T2.

- **The spike (the gate of T2):** `--selftest <json> --selftest-mode move` moves a running terminal (the counter of F45) from the pane to a second grid in the same window and back. Result: process alive; ticks 4 before the move, 10 in the second grid, 16 back; a key sent in each place came back; width 831, then 416, then 831. So a terminal can change parent inside one window. This replaces "nothing is moved between parents" (F45) for moves inside the window.
- **T1 reorder:** a tab is dragged along its strip and changes places with a tab when the pointer passes that tab's middle. The order of the session tabs is saved in `tabs.json` beside the state file. Verified by unit tests (`TabOrderTests`: move, arrange, drop place, save and read). The drag itself was not driven on screen.
- **T2 split:** a session tab moves to a right pane by its right-click menu, or by a drag released over the right half of the pane. The right pane has its own strip. Only session tabs go there; the setup, the transcripts and the search stay at the left. Closing the right pane's last tab closes the split, and the terminal goes back to the left, hidden, first.
- **Verified by `scripts/Test-Split.ps1`** (two runs, a counter in place of each session): the split opens; each terminal is in its pane, visible, 413 wide; each strip has its tab; a key sent to each terminal reaches only that one; both counters keep running; the split survives a rail refresh; closing the right tab closes the split and the terminal lives on at the left; a second start opens in the saved order with the saved session at the right. A screenshot shows both panes drawn.
- **Paste:** WPF's keyboard focus does not follow these terminals (`Focus()` on one did not change `IsKeyboardFocusWithin`), so the paste target is found from the native focus (`GetFocus` against each terminal's hosted window). Seen: the native focus was the right terminal's window after it was shown.
- **Also fixed:** a click on a session's tab now shows its terminal even when its rail row is already selected. Before, the pane kept what it showed (for example the setup).

Not verified:
- The drag on screen (reorder, and the drop onto a pane), and the right-click menu. The move itself is the same code the test calls.
- That a mouse click inside a terminal moves the paste target to it. The script's two clicks were skipped because another window covered the test window.
- The same with a real `claude attach` client: redraw after the move to a narrower pane, and input.
- The light theme, and the splitter between the two panes under a drag.
- The split's width is not saved: it opens half and half.

## F56 Phase 2 of the review: the legacy run is cut (built and tested by unit tests, 2026-11-16; live sessions not run)

Source: the review in `docs/plans/review-2026-11-15.md`, Phase 2.

- **End of the live run (owner, 2026-11-16, "end the run"):** `ovc-planner` was stopped with `claude stop` (its conversation is kept). `state.json` and `runs.json` were set to `[]`. Backups are in the scratchpad (`*.20261116-084158.bak`).
- **Cut:** `RunPlan.Legacy`; `RunStore.MigrateLegacyIfNeeded` and `LegacyRunId`; `SessionSpec.PlannerName`, `CoderName` and the default-spec helpers; the supervisor's `--workdir` and `--task` and its migration call; the window's migration call; `scripts/Demo-Handoff.ps1`, which used the removed options.
- **Kept:** the window's `--workdir` and `--task`, which pre-fill the setup page and are not legacy; the Assistant (parked by the owner); the spike; the supervisor hosts; the planned Summaries folder.
- **Tests:** three migration tests and the legacy-pair test removed. Fixtures moved to `TestSpecs` (names `test-planner` and `test-coder`). The old `ovc-` names stay only where a test reads an old state file (`SupervisorNamesTests`, `StateStoreTests`) and in the title and run-name tests, which read old text.
- **Result:** 259 Core tests pass. That count includes `tests/OpenTail.Bullshark.Core.Tests/Tabs/TabOrderTests.cs` (15 tests), an untracked file in the owner's working tree that this pass did not write. The supervisor builds. The app compiles into a scratch folder: the owner's running `OpenTail.Bullshark.App` holds its DLLs, so the normal build failed on a file lock. The process was not stopped.
- **Checked after the cut (2026-11-16):** the supervisor, with a temporary state file and no run, starts nothing and exits cleanly; `--workdir` is rejected. The pane self-test (`scripts/Test-Pane.ps1`, four checks) passes 4 of 4 on a fresh build of the tree at 09:38, which includes two uncommitted App files.
- **Not verified:** the supervisor and the window with real sessions (Phase 3).

## F57 Session library L1: the facts Claude Code writes into transcripts (built and tested by unit tests, 2026-11-16; not committed)

Source: `docs/plans/session-library.md`, slice L1 only.

- **Built:** `TranscriptSummary` and `SessionDigest` gain optional members: `AiTitle`, `CustomTitle`, `LastRecap`, `LastRecapAt` and `Compactions`. `SessionDigest.Title` is now the Claude Code title, else the first message, else "Session" and the short id. The custom title is shown in the tooltip only (L2), because it is a role name. Files: `Core/Activity/TranscriptReader.cs` and `TranscriptIndex.cs`.
- **Verified by unit tests:** `TranscriptFactsTests.cs`, eight tests, the five the plan names and three on the title order. The Core suite passes 262 with the Live tests filtered out (254 before this slice).
- **Verified on the real folder, read-only:** `scripts/Check-Titles.ps1`. 137 transcript files; 70 with an `ai-title`; 46 with a `custom-title`; 39 with an `away_summary` recap; 23 with a `compact_boundary`. The counts are in the plan.
- **Finding, not changed:** many sessions open with a `<local-command-caveat>` user record from `/model` or `/effort`. A session with no `ai-title` takes that text as its title. Question 6 of the plan.
- **Not verified:** the four record shapes in other CLI versions (this check is CLI 2.1.325); the title, tooltip and recap on screen (L2).
- **Not done:** no commit, no change to the rail or the tabs (L2), no session started or resumed, no model call.

## F58 Session library L2 and L3: the rail's tooltip, and the library store (L2 partly checked on screen; L3 built and tested; 2026-11-16; not committed)

Source: `docs/plans/session-library.md`, slices L2 and L3. L1 is F57. L4 and L5 are not started.

- **L2 built:** a history row's tooltip adds the recap with its age, the custom title when there is one, and "Compacted N times" when N is above 0 (`FactsTip` in `SessionWindow.Rail.cs`). The row title, the transcript heading and the resume heading all read `Title`, which L1 changed.
- **L2 verified on screen:** a scratch state folder with `--no-pass` and `--shot`. The row "OpenTail.Bullshark review..." (session 29b84a3b, with a Claude Code title) and the row "Hello from git-86..." (session f8045ed8, with none; its first message is its title) show different titles, as expected. The session ids were matched from the rows' second lines against the transcripts.
- **L2 not verified:** the tooltip (a hover was not done); the transcript and resume headings (a click was not simulated); a subagent row (none is in the visible part of the list).
- **L3 built:** `SessionLibrary` in `Core/Library/` (`library.json` beside the state file, keyed by session id, `FileLock` and `WriteAtomic`, `StoreUnreadableException` on a bad file, empty entries removed), `TitleFor` (the owner's name first), and `TagsOf`.
- **L3 verified by unit tests:** the six named tests and two more. The Core suite passes 270 with the Live tests filtered out (262 before L3).
- **Stopped before L4:** L4's checks need rename, pin, archive, the Archived filter, colour and reopening, all by clicks, which the handover rules out.

## F59 Session library: L5 core, L4 in the rail, L5 in the rail (built; partly checked on screen by scripts and screenshots; 2026-11-16; not committed)

Source: `docs/plans/session-library.md`, the brief from the planner (git-fb) of 2026-11-16. L1 to L3 are in 72cee28 (F57, F58).

- **Built, Core:** `HistorySearch.Run` takes the owner's extra text (D-L7). `ResumeText.Compose` writes the `Continues session:` line first (D-L8). `TranscriptReader` reads it from the first user message into `ContinuesSessionId`. `SessionTitle.Clean` skips a resumed session's start text. `SessionLibrary.TitleFor` takes the lineage rule: "↪ " and the earlier session's title. The Core suite passes 287 with the Live tests filtered out.
- **Question 6 (caveat):** no change needed. The reader already skips a first message that starts with `<`, so a `/model` caveat never becomes the title. The earlier finding in F57 was wrong (the check script read the raw record). `ACaveatIsNotTheFirstMessage` covers it.
- **Built, App:** the Pinned group (D-L4); the Archived filter (D-L5), muted rows in search; the colour bars on rail rows, and the top edge on tabs (D-L11), with eight shades in `RailPalette`; the row menu and the edit dialog (D-L10), built not clicked; tags and note at the top of the row tooltip (tags are also on the second line); "continues:" in the tooltip; "Continued from…" and "Continued in…" links in the transcript view; "matched its name, tags or note" in search; the notice for an unreadable library (D-L3).
- **Checked, no clicks:** `scripts/Test-Library.ps1` passes 6 of 6 on the scratch build: the seeded library is shown; a run leaves it as it was; the light theme and the Archived filter run; an unreadable library.json is logged and left alone. Screenshots looked at: dark (Pinned group, the blue bar on "Plan for L4", the archived session hidden), light (the same), Archived (only the archived session, muted), unreadable (the notice at the top, the marks gone).
- **Not verified:** the right-click menu, the edit dialog and its typing, pin, archive, rename, tag, note and colour changes made by clicks; the hover tooltips; the tab's colour bar; the transcript's "Continued from…" and "Continued in…" links; the search text; a real resume, which is not permitted and not run. Tags on the second line are cut at the default rail width.
- **Decisions the plan did not settle:** Pin and Archive are on history rows only; a live row's menu offers Edit only. An archived row is muted with opacity 0.55. The start text names the source session on its first line; this changes the text of the prompt passed to the resume's CLI call, not the call. A resume in full keeps its id and writes no line (D-L8).
- **Committed** on 2026-11-16: the L5 core in 1a2a1ff, and L4 with the L5 screens in the commit after it. The Core suite (287 with Live filtered) and the App build pass; the on-screen items are listed above.
- **Review fixes (2026-11-16, from git-fb):** the row title uses `DisplayTitle` like the tab and the heading; a tab's edge is always 2 px; the tab titles read the records once per strip, and a mark change reads the library once; the five files I touched were saved with LF, as in the index, so their diffs show only my lines. The Core suite passes 287 (Live filtered), the App builds, and `scripts/Test-Library.ps1` passes 6 of 6.

## F60 Briefs and templates: a saved brief expands its settings at launch (built and tested by unit tests and screenshots, 2026-11-16; not committed)

Source: the brief-templates batch from the planner (git-fb), with the owner's decision. The two words are used in one sense only: a TEMPLATE is the built-in text for a pattern and a role, with tags, which the app owns and `SetupPlan.DefaultBriefTemplate` returns. A BRIEF is what the owner sees and edits on the Preview tab, and what Save stores for the pattern. A brief starts as the template. Reset discards the saved brief and takes the template again.

- **The bug:** a saved brief froze the sentences that depend on the settings. A coder brief saved with an assistant read "Your assistant is : …" after the assistant was set to none. A planner brief saved with reviewer "opus" still sent reviews to a subagent after the reviewer was set to "self".
- **The fix:** two clause tags. `{assistantclause}` in a coder brief expands to the sentence the generated brief adds for the current settings, or to nothing. `{reviewclause}` in a planner brief expands to `RunPlan.ReviewClause` for the current reviewer, or to nothing for "self". Each sentence has one function (`RunPlan.AssistantClause`, `RunPlan.ReviewClause`), so the generated brief and an expanded brief read the same.
- **The template** is the generated brief with no assistant and reviewer "self", with the names as tags, and the clause tag of its role at its end. So the same text serves every reviewer and assistant choice. A solo planner has no reviewer and gets no clause tag.
- **Renamed:** `PatternSettings` and `SetupRequest`: `PlannerBriefTemplate` and `CoderBriefTemplate` are now `PlannerBrief` and `CoderBrief`. Null means no saved brief, so the template is used. A `patterns.json` with the old names still loads, and the old values are ignored; a test covers that. The owner's live `patterns.json` holds only the solo settings and no brief keys, so nothing is dropped.
- **The page:** "Save briefs" and "Reset to the templates"; the messages "Briefs saved." and "Briefs reset to the templates."; the handlers `SaveBriefs_Click` and `ResetBriefs_Click`. The tags are listed in one line under the Preview heading, and the line wraps.
- **Expansion is one pass** (`Regex.Replace`, which does not rescan what it inserts). A tag typed in the task stays as typed (`ATagTypedInsideTheTaskIsNotExpandedAgain`).
- **Tests:** the Core suite passes 298 (287 before, plus 11). They cover: the default coder template does not depend on the assistant; the default planner template does not depend on the reviewer; a default template expanded for each setting equals the brief `RunPlan` generates (five settings); an assistant clause with no assistant leaves no text and no double space; the one-pass rule; the old names are ignored on load.
- **Checked, not changed:** Start saves the page's text. The editor writes into the brief on every change, and Start persists the request, so an edited brief that was not saved is saved by Start, and the run uses it. The Preview lists the sessions and the two briefs; it does not show the resume prompt.
- **Screenshots:** `preview-dark.png` and `preview-light.png` (the Preview tab, on a scratch state folder with a valid folder and task). The tab is opened with `--setup-detail preview`, a test option like `--rail-filter`; no click is made. Both were looked at.
- **Old extra text (PlannerExtra, CoderExtra):** read from older `patterns.json` files, and not used. The page has no boxes for them. With no saved brief, a launch starts with the built-in template, expanded, and adds no owner text; `WithOwnerText` is removed. A test checks that a request with extra text and no saved brief launches the expanded template.
- **Not verified:** a run started from a saved brief (no session is started); the Save and Reset buttons, which sit below the fold in the screenshot.
- **Not committed.** The files are the six setup files, `RunPlan.cs` (`AssistantClause`), `SessionWindow.xaml.cs` (the option), this finding, and the part-3 files that are still uncommitted (`TranscriptView.cs`, `SessionWindow.SelfTest.cs`, `RunPlan.cs`).

## F61 `claude agents --json` leaves out finished and stopped sessions (checked 2026-11-17, `claude` 2.1.326; bug open)

- **The fact:** without `--all`, `agents --json` prints active sessions only. `claude agents --help`: "--all  With --json: also include completed background sessions". On the owner's machine: 8 sessions without `--all`, 20 with it. Missing from the default list: 10 `background/done`, 2 `background/stopped`. Two `done` sessions were in both lists, so `done` alone does not decide it.
- **Entry fields seen:** `cwd, id, kind, name, sessionId, startedAt, state` (no `pid`).
- **The bug:** the supervisor lists without `--all` and treats a missing session as lost, so it resumes a session that finished or that the owner stopped from the CLI or Agent View (with the "You were restarted" prompt; capped at 3 restarts in a sliding hour). Bullshark's own close-tab stop removes the spec first, so it is not affected.
- **The fix:** planned in `docs/plans/bugs-2026-11-17.md` item 1 (list with `--all` for the pass; `stopped` and `failed` alert, never resume; the window's display list stays the default listing).

## F62 `claude --bg` refuses a folder that has not been trusted (checked 2026-11-17)

- **The fact:** `claude --bg --model haiku --effort low --name gate-plain "<prompt>"` in a new scratch git repo exited 1 with "Workspace not trusted. Run claude in <folder> once and accept the trust prompt, then retry." Nothing started (`agents --json --all` was the same before and after).
- **Effect:** the worktree-per-coder gate (`docs/plans/additions-2026-11-17.md` item 3) could not run `--bg -w`. Trusting a folder takes an interactive `claude` run that writes `~/.claude.json`, which only the owner does. To finish the gate, the owner trusts one throwaway repo, then the gate is rerun.
- **Fits the existing trust step:** Bullshark already skips runs in untrusted folders and shows a Trust step (`TrustGate`), so this is the same rule seen from the CLI side.
