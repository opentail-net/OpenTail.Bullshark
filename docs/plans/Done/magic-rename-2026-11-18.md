# Magic rename (wand), 2026-11-18

Owner request (2026-11-18): "magic renames": Haiku reads a past session and gives it a better, distinct, short name, so nobody has to rename by hand.
It reads the whole transcript, or the top and the tail of a large one. Names cannot be too long. The wand emoji marks a magic name. **Live sessions
do not get this** (agreed). It covers "scenario 1" (the library name of a past session in History); "scenario 2" (run sessions) maybe later. It is
done **once, when a session has gone cold**, and not repeated ("snowflake").

## What exists (read first)

- `Core/Library/SessionLibrary.cs`: `LibraryEntry(Name, Pinned, Archived, Tags, Note, Colour)` in `library.json`, keyed by session id; `Update(change)` under a file lock. `LibraryEditDialog` (App) edits name, tags, note, colour; `DisplayTitle(digest)` (Rail.cs) shows library Name first, else the custom title / AI title / first user text.
- `Core/Activity/TranscriptIndex.cs` `SessionDigest` (`IsSubagent`, `LastActivity`, `CustomTitle`, `AiTitle`, `FirstUserText`, `FilePath`); `TranscriptReader` / `TranscriptJson` / `TranscriptFile` for reading transcripts.
- `Core/Claude/ClaudeCli.cs` (and its process helpers): how Bullshark runs `claude`. `LimitStatus.Active(projectsRoot, now)`: limits in force.
- Live = a session in the state records or in the `agents --json` listing (`openIds` in `SessionWindow.Rail.cs`).
- Local CLI facts (claude 2.1.327, checked 2026-11-18 with `claude --help`): `-p/--print`, `--model <alias>` (use `haiku`), `--no-session-persistence` (do not save the session to disk), `--tools ""` (no tools), `--system-prompt`, `--strict-mcp-config`, `--disable-slash-commands`, `--bare` (skips hooks etc.; may need API-key auth, so **do not rely on it**: check), `--max-budget-usd`, `--output-format text`.

## Contracts this must satisfy

| Member | Promise |
|---|---|
| Owner's names win | A session that already has a library `Name`, or a Claude custom title (`CustomTitle`, set by the owner with /rename), is never renamed by the wand. Only "Magic rename again" on its menu (an explicit act) may replace a magic name; it never replaces a manual name without a confirm. |
| Live sessions | Never. A session in the state records, in the listing, or with `LastActivity` newer than the cold age is not a candidate. Subagent transcripts are not candidates. Archived sessions are not candidates. |
| No pollution | The helper call must not create a session in History: `--no-session-persistence`. If that does not work, the helper's working folder is excluded from History (and from search, Away, Limits) by its folder key; say which. The helper runs in its own scratch folder beside the state file (`magic-rename/`), never in a project. |
| No tools, no side effects | `--tools ""`; the prompt asks only for a name. Nothing is written except the library entry. Nothing is ever sent to a session (principle 3). |
| Limits | No call while a usage limit is in force (`LimitStatus.Active` non-empty): say so and skip. A batch stops at the first limit error. |
| Privacy | Transcript text goes only to the owner's own `claude` under their account, as in normal use. Never logged: the log gets counts, session ids and outcomes, not text. The extract holds user and assistant TEXT only (no tool calls or tool results) and is capped. |
| `library.json` | Unreadable file = nothing is written (existing rule, D-L3); new fields are optional so old files load. |
| UI thread | The call and the reading run off the UI thread; the rail refreshes when done. |

## Design decisions (AFK; change if you disagree)

- D1 **Candidates ("gone cold")**: in the loaded history, not subagent, not archived, not live, no library Name, no custom title, `LastActivity` older than **2 hours**, at least 4 messages, and not already magic-named. A session that comes back to life is simply not a candidate until cold again.
- D2 **The snowflake rule**: a magic name is stored with `MagicAt` = the session's `LastActivity` when it was named. The session is never auto-renamed again, even if it grows. "Magic rename again" (row menu) is the only way, and it is offered when `LastActivity` is newer than `MagicAt` ("it has changed since").
- D3 **Name rules** (pure Core, tested): at most **40 characters**, no line breaks, no quotes or trailing punctuation, no emoji from the model (the wand is added by the UI), first letter upper-case, cut at a word boundary with "..." only if it must be cut. If the result is empty, or equals (case-insensitive) another library name or another magic candidate's name in the same batch, the code appends a short distinguishing part (" - 18 Nov", then " 2", " 3"), still within 40 characters. The prompt shows the model the names already used in the same project folder, so it avoids them.
- D4 **Extract** (pure Core `NameExtract.Build(lines, maxChars = 16000)`): user and assistant text blocks in order, each prefixed "You:" / "Claude:", each capped at 1200 characters, tool results and thinking dropped. If the whole is within the cap it is sent whole; otherwise the first 40% and the last 60% of the cap, joined by "[... middle of the session left out ...]". Command/skill boilerplate (e.g. `<command-name>` blocks, system reminders) is dropped.
- D5 **Prompt**: one short system prompt: "You name past coding sessions. Reply with ONLY a name of at most 5 words (max 40 characters) that says what the session was about and tells it apart from similar sessions. No quotes, no emoji, no punctuation at the end. Do not describe yourself." plus the used-names list, plus the extract. Model `haiku`.
- D6 **Display**: the row shows the wand then the name; the tooltip says "Named by Haiku on <date>. Rename it yourself to keep your own name." A manual edit in `LibraryEditDialog` clears `MagicAt` (the name is now the owner's).
- D7 **Triggers**: (a) row menu "Magic rename" on one past session (works on any non-live session, ignoring the 2 h rule, still never replacing an owner's name without a confirm); (b) palette and Library-rail action "Name all cold sessions (N)" with a confirm dialog that says how many, that it uses Haiku, and that it is capped at **10 per run**; (c) **automatic** naming of cold sessions is a setting `magicAuto` in ui.json, **default OFF** (it spends the owner's usage); when ON: at most 3 per hour, only when no usage limit is in force and the window is idle (no pass running), never at window start. The owner turns it on from the palette ("Toggle automatic magic naming").
- D8 Failures (timeout 90 s, non-zero exit, empty or unusable reply) leave the session unnamed and are counted in the status line; a session that failed twice in this window run is skipped until the next run of the window.

## Gate (step 1, run 2026-11-18)

- Command (run by hand in a scratch folder, stdin `hello`, once): `claude -p --model haiku --no-session-persistence --tools "" --output-format text --system-prompt "Reply with one word."`
- Result: replied `Hello`, exit 0. No new `.jsonl` under `~/.claude/projects` (185 before, 185 after), and no project folder was created for the scratch folder. So `--no-session-persistence` works and the folder-exclusion fallback is NOT needed.
- Auth and trust: `ANTHROPIC_API_KEY` is not set in this shell, so the call used the local Claude login. No trust prompt in `-p` mode (scratch folder never used before).
- The build sends the same arguments through `ICliRunner` (`HaikuNameModel`): user content on stdin, the system prompt as `--system-prompt`, working folder `magic-rename/` beside the state file.
- One model call was made for the gate. A second `claude -p` line in a diagnostic command was refused by the permission check before it ran.

## Decisions (built 2026-11-18; the coder's choices where the plan left room)

Built: steps 1 to 7. Core: `Library/MagicName.cs`, `Library/NameExtract.cs`, `Library/MagicCandidates.cs`, `Claude/MagicNamer.cs` (with `IMagicNameModel`, `HaikuNameModel`); `LibraryEntry` gained two optional fields. App: `SessionWindow.MagicRename.cs`, and small hooks in `SessionWindow.LibraryRail.cs`, `SessionWindow.Library.cs`, `SessionWindow.Rail.cs`, `SessionWindow.Palette.cs`, `SessionWindow.xaml.cs`.

- **Two timestamps, not one.** D2 says `MagicAt` = the session's last message when it was named, which cannot also give the naming date (D6) or count the hour (D7c). So `LibraryEntry` has `MagicAt` (the last message, for the snowflake rule) and `MagicNamedAt` (the naming time, for the tooltip date and the hourly cap). Both optional; an old file loads with both null.
- **An empty reply after cleaning is a failure** (D8), so D3's "if the result is empty" means nothing is named; no empty name is ever stored.
- **Cleaning (D3):** the first non-empty line only; emoji and other symbols, surrogates, joiners and variation selectors removed; double quotes removed, apostrophes removed only at a word's edge; leading markdown marks (`#`, `*`, `-`, `>`, bullets) removed; trailing punctuation removed; first letter upper-case; a longer name is cut at a word boundary within 37 characters and ends with "...".
- **"Distinct" suffixes:** " - 18 Nov" (invariant date), then " 2", " 3"; a cut name loses its "..." before the suffix is added. The comparison ignores case.
- **"At least four messages"** (D1) counts user and assistant messages together.
- **Extract (D4):** text blocks only; records with `isMeta` dropped; text that starts with `<` or `/`, or contains `<system-reminder>`, or starts with "You were restarted", "Continues session:" or "This session continues an earlier Claude Code session", is dropped. The extract keeps no more than the cap in memory (a rolling tail).
- **Used names in the prompt:** the library names of sessions in the same project folder, most recent first, at most 30. The batch's own names (`Distinct`) are the rule that enforces uniqueness; the prompt only helps.
- **Session names taken into account:** every library name except those of the sessions being named (so "again" does not clash with its own old name).
- **Batch order:** the most recent cold session first. A batch takes at most 10 model calls (`MagicNamer.MaxPerRun`).
- **Sessions with no text** count as a failure for D8 (so one with nothing to name is not picked again on every pass). The palette count and the batch leave out sessions that failed twice in the window run.
- **Automatic naming (D7c):** off by default; the palette toggle writes `magicAuto` in ui.json. It starts only after the window has been open **five minutes** (the plan says "never at window start" without a number; five minutes is the coder's choice), after a pass has finished, when no limit is in force (as the status bar last read it), and when fewer than **3 names or calls** were made in the last hour. The count is the larger of: the library's names given in the last hour (`MagicNamedAt`), and the automatic calls this window run made in the last hour (so failed calls count too). The namer checks the limit again before each call.
- **Limits:** no call while a limit is in force, checked before each call; a limit error stops the batch. A limit is recognised by the words "usage limit", "limit reached" or "rate limit" in the output of a failed call; the text is not logged.
- **Single rename (D7a):** "Magic rename" for a session with no name; "Magic rename again" once it has changed since it was named; "Magic rename… (replaces your name)" for an owner's name, after a yes/no confirm. A session with a Claude custom title gets none. Live sessions, subagents and archived sessions get none.
- **Owner's name in the batch:** a batch never replaces an owner's name; the store reads the entry again under the lock, so a name the owner set during the run is kept.
- **Manual edit clears the magic marks** only when the name text changes (`WithName`, in `SessionWindow.Library.cs`). Saving the dialog with the same name keeps the wand. The plan said "a manual edit clears MagicAt"; this is the narrower reading.
- **The wand (D6)** is drawn in front of the name in the History row's title and in the row's tooltip ("Named by Haiku on <date>. Rename it yourself to keep your own name."). Tabs, the palette and the live rows do not show it.
- **The library-rail action** is a 🪄 mark in the History header, beside 🔄 (this window has no Library page). The palette has "Name cold sessions with Haiku (N)" and "Toggle automatic magic naming".
- **The helper's folder** is `magic-rename/` beside the state file; the call is made with `--no-session-persistence` as the gate showed, so no folder is excluded from History.
- **Logs:** session ids, counts and outcomes only. Names, reply text and error text from the CLI are not logged. An unreadable transcript is logged by exception type and counted as having no text.
- **Reply cap:** 4,096 characters are read from the reply; the call times out after 90 seconds (through `ICliRunner`).
- **Concurrency:** one run at a time (`_magicRunning`); the failure counts are a `ConcurrentDictionary` because the palette reads them on the UI thread while a run writes them.

## Not done or not verified

- **No real Haiku call** was made in tests (rule: all tests use the fake `IMagicNameModel`). The gate was the only model call. The quality of real names is unknown.
- **Not seen in a running window:** the wand and its tooltip, the row menu items, the confirm dialogs, the palette entries and toggle, the status lines, the History mark, and the way the wand's emoji renders in the row title (it relies on font fallback).
- **The automatic path** (the five-minute start, the hourly cap, the idle check) is App code and is not unit-tested; it was checked by reading the code only.
- **Existing code noticed, not changed:** `ClaudeCli.SummariseAsync` runs `claude -p` without `--no-session-persistence` (its working folder is the App's current folder). Whether that writes a transcript into History was not checked here.

## Steps

- [x] 1. **Gate (no code)**: run once by hand in the scratchpad folder: `claude -p --model haiku --no-session-persistence --tools "" --output-format text --system-prompt "Reply with one word."` with stdin "hello"; see that it replies, that no new `.jsonl` appears under `~/.claude/projects` for that folder, and what auth/trust behaviour it shows. Record the result and the exact command line in this file under "Gate". If it fails, find the smallest working variant (for example `--append-system-prompt`) and record it; if no variant works without making a session file, use the folder-exclusion fallback from the contracts table.
- [x] 2. Core `Library/MagicName.cs` (pure): name cleaning, cap, dedupe (D3). Tests: too long, quotes, emoji, newlines, empty, duplicate of an existing name, duplicate within a batch, word-boundary cut, unicode, 40-char exact.
- [x] 3. Core `Library/NameExtract.cs` (pure): D4. Tests: short session whole; long session top and tail with the marker; tool results dropped; per-message cap; boilerplate dropped; empty transcript = null; bad lines skipped; a transcript with only tool calls = null.
- [x] 4. Core `Library/MagicCandidates.cs` (pure): D1 over digests, library, live ids, now. Tests for each rule (cold age, live, subagent, archived, named, custom title, already magic, too few messages; a limit in force is handled by the caller).
- [x] 5. Core `LibraryEntry` gets optional `MagicAt` (DateTimeOffset?); `IsEmpty` semantics unchanged (a magic name is a name); a library file without it loads. Tests for load/save round trip and an old file.
- [x] 6. Core `Claude/MagicNamer.cs`: an `IMagicNameModel` seam (`Task<string?> NameAsync(string prompt, CancellationToken)`) with the real implementation (Haiku through the `claude` CLI) running the command from the Gate; the seam is there so a local model can be added later (see Nice to have), and nothing else in the feature knows which model answers (read `ClaudeCli` for how processes are started; 90 s timeout; output cap 4 KB; kill on cancel) and a fake for tests. Tests with the fake: success, empty reply, timeout, exception, batch cap 10, stop at a limit error, per-session failure counted, no call when a limit is in force (inject the limit check).
- [x] 7. App: the wand rendering and tooltip (D6), the row menu (D7a), the batch action and confirm (D7b), the `magicAuto` setting and palette toggle (D7c), status lines ("Named 7 sessions, 1 failed"), `LibraryEditDialog` clears `MagicAt` on a manual name change. All work off the UI thread, then refresh the rail. Reuse how the library is written (`SessionLibrary.Update`).
- [x] 8. Docs: tick this file with a "Decisions" section for what you chose; README only if it lists library features (checked: it lists none, so no change); backlog line (`backlog-2026-11-18.md`, section F); one line in `session-summary-2026-11-17.md`.
- [x] 9. Commits: one for the Core pieces + tests (`afa39d5`), one for the App and these docs (the commit after it), only the task's files, `git status` first. Not pushed.

## How to check

`dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` (1167+ passing now); if the full build fails only on locked App output files, build the App to a scratch OutDir and say so. State plainly that the real Haiku call and everything on screen were not seen by tests (only the gate run, if it worked).

Result (2026-11-18, coder): `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` gave **1273 passed, 0 failed, exit 0** (1178 before; 95 new). The solution build, the App included, succeeded with no new warnings (the one warning in `MainWindow.xaml.cs` was there before). The App was not locked, so it built to its normal output folder.

## Nice to have, later (not part of this build)

Owner (2026-11-18): "get Haiku to do this initially, and later a local model. Not for immediate addition." Nothing below is built now; the
`IMagicNameModel` seam in step 6 is the only preparation, so a second provider is one new class and a setting.

- **Local model provider.** No runtime is assumed (the owner never named one; the earlier Ollama idea was the planner's guess and is withdrawn). Design sketch, to be confirmed with the owner when it is wanted:
  - **A local model behind the `IMagicNameModel` seam (owner, 2026-11-18: "rope in a local model", cost nothing). No runtime is assumed.** Two providers: `local` and `haiku` (the `claude` CLI call above). The local provider speaks the **OpenAI-compatible chat API** (`POST {endpoint}/chat/completions`, `stream:false`, temperature 0.2), which llama.cpp's server, LM Studio, Ollama and most other local servers offer; the owner chooses the runtime and the model, Bullshark does not. Settings in ui.json: `magicModel` = `haiku` (default until the owner configures a local one) or `local`; `magicEndpoint` (for example `http://127.0.0.1:PORT/v1`, no default guessed); `magicLocalModel` (the model name the server expects; may be empty if the server has one loaded). **Nothing is auto-detected, nothing is started, nothing is installed.** If `local` is chosen and the endpoint does not answer, the rename stops with a clear message (no silent fallback to Haiku, because the owner chose local for privacy and cost; a "fall back to Haiku" tick box is a later option). **No model and no new package is bundled**: one `HttpClient` call, so the app does not grow. The context size is the server's setting: the prompt tells the owner in the status line if the reply says the context was exceeded, and the extract is capped at 16,000 characters (about 4-5k tokens) so a server with an 8k context works. The local provider is not subject to the usage-limit rule, and transcript text never leaves the machine. Local replies go through the same `MagicName` cleaning (D3), so a chatty small model cannot produce a long or odd name. Timeout 180 s (a first call may load the model); one session at a time. A small settings dialog (or the palette: "Magic rename settings...") sets the three values; the "Test connection" button sends a one-line prompt and shows the reply and the time.
- **Preview before saving** the first few batches (old title, proposed name, tick to accept): suggested by the planner, not yet agreed.
- **Scenario 2** (names for run sessions): later, and only with a safe rename of the state keys (see the answer on renaming live sessions).
