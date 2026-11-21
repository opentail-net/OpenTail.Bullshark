# Keyboard speed: next waiting session, and a palette that finds more (2026-11-18, written AFK)

## How it was chosen
Three Haiku researchers brainstormed on 2026-11-18 (cost/usage, daily workflow, safety/quality; 18 ideas, researcher-reported, not re-checked
beyond the code reads below). Ranked by fit to "walk away, come back, carry on", size and risk of false alarms. Chosen: workflow ideas 1 and 2
together (both S, no transcript reads, nothing typed into a session, no new package). Why these: the owner runs 5-20 sessions; the one thing
they do on return is go to what waits, and the palette only matches titles, so "waiting" or a folder name finds nothing.

Kept for later (open, in order of my ranking): safety 4 big-deletion check (S), safety 6 same-call loop (S), cost 1 tokens by day/project (M),
workflow 3 rail tag filter (S), cost 4 long-idle large-context list (S), workflow 4 transcript bookmarks (M), workflow 5 compare two sessions (M),
safety 1 risky command flags (S, noisy), safety 5 edited-without-test (S-M, noisy), cost 3 pace to limit (needs owner OK), cost 2 model mix,
workflow 6 file lookup (M, index over ~860 MB), safety 2 secret-shaped text (M, high false alarms), safety 3 edits outside folder (noisy),
cost 5, cost 6.

## Contracts this must satisfy
- `CommandPalette.Filter(entries, query, max)` (Core/Ui/CommandPalette.cs): pure; empty query lists by `Order`; every word must match or the
  entry is left out; entry score = lowest word score; ties by `Order` then input index; never throws. Existing tests in
  `CommandPaletteTests` must pass unchanged (a title match keeps its score 3/2/1).
- `PaletteEntry(Id, Title, Hint, Order)` is built positionally in App; a new member must be an optional last parameter.
- `AttentionList.From` returns items oldest first; one session can have several items (overlap per file). Nothing here answers a prompt.
- `GoToSession(sessionId, title)` shows the tab and rail row; `SetActive` acknowledges the wait (escalation D3). That is the only
  acknowledgement path; the new key uses it, so it acknowledges like a click does. Nothing is sent to any session (principle 3).
- `OnPanePreviewKeyDown` (SessionWindow.xaml.cs) sees chords even with a terminal focused (Ctrl+Shift+P, zoom). Do not change paste or zoom.

## Existing behaviour mapped (palette scoring)
| Case | Today | After |
|---|---|---|
| Title starts with word | 3 | 3 |
| A title word starts with it | 2 | 2 |
| Letters in order in title | 1 | 1 |
| Title does not match | entry out | matches if a word of `Keywords` starts with it: score 1 (never above a title match; no subsequence in keywords) |
| `Keywords` null/empty | n/a | behaves exactly as today |

## Steps
- [x] 1. Core: `PaletteEntry` gets `string? Keywords = null` (last, optional). `ScoreOf` takes keywords: per word, title score first; if 0,
  keyword word-start (or prefix) match gives 1; else 0. Why: tags/folder/state become findable without outranking titles.
- [x] 2. Core tests in `CommandPaletteTests`: keyword finds an entry the title does not; title match outranks keyword-only match for the same
  word; keyword never matches as subsequence; null keywords unchanged; all-words rule holds across title+keywords (one word in title, one in keywords).
- [x] 3. Core: `AttentionList.NextWaiting(IReadOnlyList<AttentionItem> items, string? currentSessionId)` returns the SessionId of the next
  waiting session or null. Items with null SessionId are skipped; distinct sessions in list order (oldest first); if the current session is in
  that list, return the one after it, wrapping (a single waiting session that is current returns null: nothing else to go to); if current is
  not in the list or null, return the first. Why: the first press goes to the longest waiting, repeats walk the list.
- [x] 4. Core tests (`AttentionListTests`): empty -> null; current null -> first; current is first -> second; current last -> wraps to first;
  only one and it is current -> null; current not waiting -> first; duplicate items for one session count once; null SessionId skipped (StartFailed).
- [x] 5. App, palette: session entries pass `Keywords` = project folder leaf name + tags (use what the rail row already uses: read
  `TagsSuffix`/library mark code; folder from the record/digest) + the hint text. Add action entry "Go to next waiting session" with hint
  "Ctrl+Shift+N", hidden when `NextWaiting` is null, placed first among the actions.
- [x] 6. App, key: Ctrl+Shift+N in `OnPanePreviewKeyDown`, handled like Ctrl+Shift+P (Handled = true, run queued at Input priority): find the
  active session id (the active tab of the pane that has focus when it is a session tab, else left active tab), call `NextWaiting(_attention, id)`,
  then `GoToSession` with `LiveTitle` of its record; when null, `SetStatus("No other session is waiting.")`. A session in a window of its own
  is skipped (`IsInOwnWindow`), as the palette does.
- [x] 7. README: one line about the chord and the palette matching tags/folder. Add the item to the live-run checklist (chord with a terminal
  focused; does Ctrl+Shift+N reach us from inside the terminal).
- [x] 8. Build the App (`dotnet build src/OpenTail.Bullshark.App -nodeReuse:false`), run `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false`.

## Checks
Tests pass with counts; read the diff against this plan. Not seen in a running window: the chord, the palette rows, the focus return.

## Open / decisions (AFK)
- Chord chosen: Ctrl+Shift+N (assumption: free in this window; the app handles no other Ctrl+Shift chord but P). Change in one place if wrong.
- Keywords match is word-start only, to keep noise low (assumption).
