# Code-review fixes (2026-11-19, written AFK)

Source: independent Sonnet review of HEAD (2db2af1), read-only, 15 findings; it found no data-loss path in Storage and no transcript text in logs, and rated the supervisor policy, start/adopt/resume recovery, FileLock/SingleInstance affinity, archive containment and
argument escaping as sound. The orchestrator re-read the code for #1, #3 and #5 (below: **checked**); the coder re-checks every other item first and records false findings.
Grades are the orchestrator's (must = wrong behaviour/security, should = real but small, note = taste or documentation). **Queued** behind the UI polish batch (running), **before** `lifecycle-batch-2026-11-19.md`.

## Items
| # | Grade | Where | Defect | Checked | Fix |
|---|---|---|---|---|---|
| F1 | **must** | App/SessionWindow.xaml.cs ~702-705 (paste) | Clipboard text goes into `ESC[200~ ... ESC[201~` with its own ESC characters intact | **checked** (`GetText().Replace(...)` then wrapped, no strip) | Remove ESC (0x1B) and other C0 controls except tab and the line breaks that become CR; also drop a lone CR run > some sane count? (no: keep), keep Unicode text; Core pure helper `PasteText.Clean(string)` + tests (embedded `ESC[201~`, NUL, BEL, DEL 0x7F, CRLF, a very long string, surrogate pairs, an empty string) |
| F2 | should | Core/Git/GitChanges.cs ~108-116 | `git diff` and `git diff --numstat` show unstaged changes only; `status` lists staged files too, so the Changes tab can list a file whose staged hunks are missing | **checked** | Use `git diff HEAD` (and `--numstat HEAD`) so staged + unstaged show together; an unborn HEAD (no commits) falls back to the old pair plus `git diff --cached`; tests with a real temp repo for: staged only, unstaged only, both, renamed, untracked (listed, no hunk, as today), unborn HEAD, binary; the deletion check and viewed ticks must still work |
| F3 | should | Core/Git/GitRunner.cs ~51-62 | git runs in session folders with the repo's own config honoured (`core.fsmonitor`, textconv, hooks) | **checked** | Pass `-c core.fsmonitor=false -c core.hooksPath=NUL` (Windows null device; verify it works with git 2.53 here) and keep `--no-ext-diff`; test that a repo with `core.fsmonitor` set to a script that writes a marker file does not run it for status/diff/merge-tree |
| F4 | should | App/SessionWindow.xaml.cs ~543; DetachedSessionWindow.cs ~28; Core/Claude/ClaudeCli.cs ~260 | The terminal command line is built from `ShortIdOf(sessionId)` (first 8 chars of an unvalidated id); `logs` takes `idOrName` with no `-` guard | verify | Require 8 hex chars (`[0-9a-fA-F]{8}`) before building the attach line, else show "bad session id" and do nothing; `logs`/`attach` etc. refuse an argument starting with `-`; tests for ShortIdOf validation and the guards |
| F5 | should | App/SessionWindow.xaml.cs ~197-201 | Closing the window releases the supervisor lock while a pass still runs and the pass has no CancellationToken | verify | Cancel the pass on `Closing` (a `CancellationTokenSource` passed to `CheckOnceAsync`), then release the lock; a cancelled pass writes no partial records (the runner already saves in a `finally`: keep it) |
| F6 | should | App/SessionWindow.Tabs.cs ~225-300 and Core/Supervisor/SupervisorRunner.cs ~239 | Closing a session during a pass: the pass holds specs and a listing from before the close and may `--resume` the session the owner just closed; the merge then drops the record | verify | Before a `Resume`, re-read the run's current specs (a closed session is no longer in them), or have the close wait for `_passRunning`; choose the smaller; test with a fake CLI that blocks `--bg --resume` while the spec is removed: no resume is issued for a removed spec |
| F7 | note | Core/Ui/UiSettingsFile.cs ~141 | A damaged `ui.json` is moved to `ui.json.bad` with overwrite: a second corruption replaces the first evidence | verify | Timestamped name (`ui.json.bad-yyyyMMdd-HHmmss`), keep at most 5 |
| F8 | note | Core/Supervisor/SupervisorRunner.cs ~186; Core/Claude/ClaudeCli.cs ~186, 301 | CLI stdout/stderr are put into exception messages, which go to `app.log`/`events.jsonl`; a failing `claude --bg` could echo the brief | verify | Cut to the first line and 200 characters in those messages (privacy rule: no task text in logs); tests |
| F9 | note | Core/Supervisor/SessionRecord.cs `Read` | A record with `"Name": null` throws `ArgumentNullException` in the dedupe loop, not covered by `StoreErrors.IsExpected` | verify | Treat a null/empty name as a damaged record: skip it with a log line (not a crash); test |
| F10 | note | Core/Trust/ClaudeTrust.cs ~44-49 | A torn read of `~/.claude.json` is a hard problem for that pass | verify | One retry after 100 ms before reporting a problem (PassHealth already shows it once); test the retry helper |
| F11 | note | Core/Git/MergeCheck.cs ~73 | `merge-tree --write-tree` writes objects: the reliability batch already corrected the comment | already done | check the comment says so; nothing else |
| F12 | note | App SessionWindow.Attention.cs | `_announced` advances during quiet hours, so a wait that began in quiet hours is never announced later (only re-alerts and the morning digest remind) | design | **Not changed.** By design of quiet hours; record in the plan and README |
| F13 | note | Core/Activity/IndexCache.cs (`LastText`, `FirstUserText`) | The index cache keeps transcript text on disk beyond Claude's retention | design | README privacy note only (state folder `cache`); owner may want it off |
| F14 | note | TranscriptReader.cs ~57-59, 409; FindTranscript; CreationTimeUtc ordering | `NotebookEdit` paths and shell-made writes are not seen; `FindTranscript` walks the tree; token attribution follows file creation time | verify | Add `notebook_path` to the write-tool reader if the tool field name is confirmed in a real transcript (grep the owner's transcripts read-only); the rest recorded only |

Design note from the review (not a finding): the supervisor passes the resume/brief prompt on `claude --bg --resume`, i.e. it does inject text when it starts/resumes a session. That is the documented F23 behaviour of starting a run (the owner started it); principle 3 is about not typing into the owner's sessions or answering prompts. No change; recorded here so it is a conscious decision.

## Contracts
- Principle 3 stays: nothing answers a prompt, merges or accepts. No real `claude` sessions in tests; git tests use temp repos only (clean them up; never touch the owner's repos).
- Existing tests for `GitChanges`, `MergeCheck`, `DeletionCheck`, viewed marks, `ClaudeCli`, `SupervisorRunner` keep passing; `git diff HEAD` output must still feed `DeletionCheck` and the viewed-file stamps.
- A paste into a session must still be a bracketed paste of the clipboard text (only control characters change); Ctrl+V into a text box is untouched.

## Steps
- [ ] F1 first and alone (Core helper + tests, then the one-line use in the handler), then F2-F6 in order, then F7-F10, then docs for F11-F14. For each: verify, fix with tests, or record "false/already handled".
- [ ] Docs: README (quiet hours note F12, privacy note F13), `live-run-checklist.md` (paste of text containing ESC; a staged file shows its hunks in Changes; an unusual git config does not run), plan ticks with each finding's outcome.

## Checks
Core tests with counts; App build; whole suite `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false`. For F2 a screenshot of Changes with a staged file (extend the fixture: `git add` one file), dark; view it.

## Orchestrator review
F1 strips ESC and controls (try the hostile string in a test); F2 shows staged and unstaged and the deletion check still flags; F3 proven by the marker-file test; F4-F6 do not change normal behaviour (attach works for real ids); no task text in any log line.
