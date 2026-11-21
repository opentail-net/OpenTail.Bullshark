# Tag filter, magic tags, and auto magic rename on 🔥 to ❄ (2026-11-18)

Owner's words: "Tag filter is good. Magic rename should just add tags too, when activated. 'Auto Magic Rename' is a possible thing for
sessions that have never been named (no 'again' postfix). It is triggered when a Fire session goes to Snowflake."
Owner's opinion asked first; no separate "Project" concept (a tag covers it).

## Contracts this must satisfy (read in the code 2026-11-18)
- `LibraryEntry(Name, Pinned, Archived, Tags, Note, Colour, MagicAt, MagicNamedAt)`; `SessionLibrary.TagsOf(text)` is the one tag normaliser (lower case,
  split on spaces/commas): reuse it, do not write a second one. `SessionLibrary.Update(entries => ...)` re-reads under the file lock; `StoreMagic` in
  `SessionWindow.MagicRename.cs` writes a magic name and keeps the owner's own name unless `ReplaceManual`.
- `MagicNamer.RunAsync` builds the prompt with `MagicName.PromptFor(usedNames, extract)`, calls `IMagicNameModel.NameAsync`, cleans the reply with
  `MagicName.Clean(reply)`, makes it unique with `MagicName.Distinct`, then `store(new MagicNamed(...))`. Existing tests of Clean/Distinct/PromptFor/RunAsync must
  keep passing (a reply with only a name still works and gives no tags). `HaikuNameModel.SystemPrompt` is the model's instruction (reply with ONLY a name):
  it changes in part B, and `Arguments` includes it.
- `MagicCandidates`: `Eligible` (not subagent, not archived, not live, has a last message, no custom title, AssistantMessages > 0), `KindFor` (Fresh / Again /
  ReplaceManual / None), `IsCold` (2 h idle, >= 4 messages, no name, no MagicAt), `ColdInOrder`, `Cold`. The owner's name is never replaced without a confirm.
- Rail filter: `RailFilter` enum (All, Running, History, Temporary, Archived), `_filter`, `ChooseFilter`, `_filterMenu` (`SessionWindow.Rail.cs` around lines 47-130),
  `IsShownInHistory` (`SessionWindow.Search.cs`, search matches), `InFolders`, `InPinnedGroup`, `ShownHistoryFolders()` (the one list the rail and the magic rename's
  "top of the visible list" order both read), `ShowsLive`/`ShowsHistory`. The "Needs you" group is built apart from the filter.
- Cache mark: `CacheRule.Classify(lastRequest, now, ttl, contextTokens)` gives Warm (🔥) / Cooling (🌡️) / Cold (❄️) / Unknown; the rail row gets it from
  `_historyFacts[sessionId]` (`LastRequest`, `Ttl`, `ContextTokens`) via `CacheTextFor`. 🔥 to ❄ is Warm or Cooling to Cold.
- Automatic naming today: `MaybeAutoMagicRename()` (called when a pass has finished, `SessionWindow.xaml.cs` near line 326), setting key `magicAuto` (off by default,
  palette "Toggle automatic magic naming"), `MagicCandidates.AutoPerHour` = 3 calls an hour (`NamedSince` + `_magicAutoCalls`), `_magicRunning` guards a second run,
  `RunMagicAsync(targets, maxCalls, automatic)`. Nothing is ever sent to a session; the call is Haiku in a scratch folder (`--no-session-persistence`, no tools).
- Privacy: no transcript text in logs; tags are the owner's data too (never logged with text).

## Part A. Tag filter (rail)
Decisions: one tag at a time (click again to clear), held in memory for the window run (assumption: the plain filter is not saved either; check `_filter`: if
it is saved in ui.json, save the tag the same way). The filter hides **rows**, never a session that needs the owner: the "Needs you" group is always complete.
- [x] A1. Core `Library/TagIndex.cs`: `TagIndex.Counts(IEnumerable<LibraryEntry>)` -> tags in use with counts (case-insensitive, sorted by count desc then name) and
  `TagIndex.Has(LibraryEntry? mark, string? tag)` (case-insensitive; null/empty tag = true). Tests: counts, case, empty tags, null mark, tag absent, sorting.
- [x] A2. App: `_tagFilter` (string?). The filter menu gets a separator and then the tags in use as checkable items ("#name (count)"), and "Clear tag filter" when one is set.
  The filter box text shows it ("show: all · #opentail"). Applies to History rows and the Pinned group (inside `IsShownInHistory` or next to it, so
  `ShownHistoryFolders()` and therefore the magic rename's "visible list" respect it) and to live session rows in folders; **never** to "Needs you". A folder with no row
  left is not drawn. If the chosen tag no longer exists after a tag edit, the filter clears itself with a status line. Search + tag filter combine (both must match).
- [x] A3. Palette: entries "Filter by tag #x" for the tags in use and "Clear tag filter" when set (action items; hint = count). Why: keyboard route.
- [x] A4. Tests for anything pure in Core; the App parts are checked by build and a screenshot (see Checks).

## Part B. Magic rename also adds tags
Decisions: the model replies on two lines: line 1 the name, line 2 `tags: a, b` (0 to 2 tags). Tags are **added** to the session's existing tags (merged,
case-insensitive distinct), never removed, up to **8 tags in total**; the owner's tags are kept. The prompt lists up to 30 tags already in use in the library
(most used first) and asks the model to reuse them when they fit and to invent a new one only when none fits. A tag is 2 to 20 characters of `a-z 0-9 -`, lower case;
anything else is dropped; "again" adds only tags not already there. If the tag line is missing or unusable the name is still stored (tags are optional).
- [x] B1. Core `Library/MagicName.cs`: `PromptFor(usedNames, extract, knownTags)` (keep the old 2-argument overload working or update callers and tests), and a parser
  `MagicName.ParseReply(string? reply)` returning `(string? Name, IReadOnlyList<string> Tags)`: the name goes through the existing `Clean`; the tags line is found by
  its `tags:` prefix (case-insensitive) on any line after the first; tags are split on commas/spaces, normalised with `SessionLibrary.TagsOf`, filtered by the rule above,
  at most 2, distinct. `MagicName.Clean` itself keeps its behaviour (a reply of one line is still a name).
- [x] B2. `HaikuNameModel.SystemPrompt`: ask for the two-line format, say tags are lower-case single words, reuse the given tags when they fit, at most 2, none if
  nothing fits. Update the test that pins the arguments/prompt, if any.
- [x] B3. `MagicNamer`: `MagicJob` gets `KnownTags` (IReadOnlyList<string>, default empty); `MagicNamed` gets `Tags` (IReadOnlyList<string>, default empty); RunAsync uses
  `ParseReply`. An unusable name is still a failure; unusable tags are not.
- [x] B4. App `StoreMagic`: merge the tags into the entry in the same `Update` (cap 8 in total; existing first, then new in order; case-insensitive distinct). The batch/auto runs
  build `KnownTags` from `_marks` (top 30 by `TagIndex.Counts`). `JobFor` passes it. Status summary mentions tags added when any ("added 5 tags").
- [x] B5. Tests: ParseReply (name only; name + tags; tags line in capitals; tags with junk characters; more than 2 tags; duplicate tags; tags line first; empty reply; tags line with
  nothing after it), PromptFor lists the known tags and caps at 30, MagicNamer stores tags from a stubbed model (use the existing fake model in the tests), name-only
  reply stores no tags, a failing name with good tags is a failure and stores nothing, the merge function (a pure helper in Core, e.g. `MagicName.MergeTags(existing, added, cap)`):
  keeps existing order, no duplicates, cap 8, existing never dropped even when it already has more than 8.

## Part C. Auto magic rename when a session goes 🔥 to ❄
Decisions: this **replaces** the periodic "name the cold sessions" auto run. The toggle ("Toggle automatic magic naming", key `magicAuto`) stays, **off by default** (it uses
usage), its hint becomes "on: names a never-named session when its cache goes cold ❄". Eligible: `Eligible` and no name and no `MagicAt` and >= `MinMessages` messages
(Kind Fresh only: never 'Again', never the owner's own name). Not the 2-hour rule: a cold cache already means idle (about 1 hour, or 5 minutes for a 5-minute cache).
The trigger is a **change seen while the window is open**: sessions that were already ❄ when the window started are not named (the wand mark and the palette do those, with a confirm).
- [x] C1. Core `Cache/CacheWatch.cs`: `Update(IEnumerable<(string SessionId, CacheState State)> states)` remembers the last state per session id and returns the ids whose
  state moved from Warm or Cooling to Cold since the previous Update. The first Update for an id only records it (returns nothing for it). Unknown states are ignored (no change
  recorded). An id that disappears is forgotten. Tests: first call returns none; Warm->Cold returns it; Cooling->Cold returns it; Cold->Cold none; Cold->Warm->Cold returns it again;
  Unknown ignored; an id that appears already Cold returns none; disappears/reappears.
- [x] C2. Core `MagicCandidates.IsAutoCandidate(digest, mark, live)` = `KindFor == Fresh` && messages >= `MinMessages` (no time rule). Tests.
- [x] C3. App: after each pass (where `MaybeAutoMagicRename` is called now) compute the cache state of every history digest from `_historyFacts` with `CacheRule.Classify`, run
  it through one window-lifetime `CacheWatch`, take the ids it returns, keep those that pass `IsAutoCandidate` and are not in `_magicFailures` at the limit, and **queue** them in a
  `HashSet<string> _magicQueue` (so nothing is lost while a run is going). When `_magicAutoOn`, no run is going (`_magicRunning`), the history is loaded and the library readable, run
  the queue through `RunMagicAsync(..., automatic: true)` limited by the existing per-hour cap (`AutoPerHour`, `NamedSince`, `_magicAutoCalls`); what the cap leaves over stays queued for
  the next pass. When the setting is off, the watch still runs but nothing is queued (do not build a backlog while off). Remove the old periodic cold-batch trigger and the
  5-minute-after-start rule; keep the other guards (not while a pass runs, usage-limit handled by the call itself). A queued id that became ineligible (named by the owner, live again,
  archived) is dropped when the run starts.
- [x] C4. Palette/menu text for the toggle updated; the status line for an automatic run says "Automatic: named N session(s) that went cold".
- [x] C5. README line; `docs/plans/live-run-checklist.md` items: the tag filter and its menu, a magic rename adding tags, auto naming on a 🔥 to ❄ change (needs a session whose
  cache goes cold while the window is open: wait about an hour, or set a short test hook if one is easy and safe, but do not ship a hook that changes behaviour).

## Checks
Core tests with counts; `dotnet build src/OpenTail.Bullshark.App -nodeReuse:false`; the whole suite `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false`. Screenshot of the rail with the
tag filter chosen: `pwsh scripts/Test-NewScreens.ps1 -Exe src/OpenTail.Bullshark.App/bin/Debug/net10.0-windows10.0.19041.0/OpenTail.Bullshark.App.exe` (the default -Exe is a stale
folder). If a screen flag for a starting tag filter is easy (like the existing filter-name test flag), add one and a screen; otherwise say it was not captured.

## Orchestrator review
Diff against the contracts: Needs-you never filtered; `ShownHistoryFolders` is still the single list; owner names and tags never overwritten/removed; the queue cannot grow while the
setting is off; no transcript text in logs; Clean/old callers unchanged; the model prompt change is covered by a test.

## Open / not decided
- Tag filter is one tag at a time, not saved between runs (assumption; checked: `_filter` is not saved either, so the tag is not).
- Auto naming is off by default.
- "Group by tag" rail view (opinion item 2) is not in this plan.
- Built 2026-11-18 (coder): the tag filter hides no Needs-you row; waiting runs and failed starts (no tags) stay visible under a tag filter (decision).
- A folder with no row left under a tag filter is not drawn; without a tag filter the folders are drawn as before.
- The search results tab is not filtered by the tag; the rail's search list is (both must match there).
- The filter label is cut with an ellipsis at 130 units (`FilterBox.MaxWidth`) so the "Sessions" title stays readable at the default rail width; the full tag is in the box's tooltip.
- The tags line may appear first and may start with a bullet mark; the name is the first line that is not a tags line (the plan said "any line after the first"; read this way and tested).
- A tag is kept only if it is literally 2-20 of a-z, 0-9, hyphen (a leading "#" or a trailing "." drops it; no repair).
- `MagicName.MergeTags` also drops blank tags and repeats among the owner's own tags (first kept, compared without case). Nothing else is removed.
- A name refused because the owner's name is there writes nothing, tags included.
- Automatic naming: a session that goes cold while it is still live is seen by the watch but is not queued (it is not a candidate then), and it is not named later when it ends. Follow-up if the owner wants it.
- Automatic naming: the watch covers every past session, not only the rows on screen. A queued session that fails stays queued and is retried until the window-run limit (2 failures).
- Test hook for the trigger: none added (C5 says not to ship a behaviour hook). The live check is in `docs/plans/live-run-checklist.md`.
- Screens: `--rail-tag <tag>` (test only) and `rail-tag-dark` in `scripts/Test-NewScreens.ps1`; the fixture gives the coder session the tag `screenshot`, so other screens show that tag on the row.

## Orchestrator review, 2026-11-18
Read the diff against the contracts: Needs-you untouched; `IsShownInHistory` is the single place of the tag filter; the queue is emptied while the setting is off; no text in logs.
One change by the orchestrator: a session that goes cold while still live now stays queued until it is no longer live (the coder reported this gap; a session is usually closed after its
cache went cold). Suite: 1361 passed. Not run live: the automatic trigger, the tag menu with real data.
