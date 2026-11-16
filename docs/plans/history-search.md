# History search (owner, 2026-11-16)

Find past sessions by what was said in them, the way ChatGPT and Claude.ai search their chats. A title filter is not built: session
names are not good enough to find anything by (owner, 2026-11-16).

## Decisions

- Each word must have at least three letters or digits. "ui" or "db" alone are refused. Two-letter words are a known loss.
- Searched: the owner's messages and Claude's text replies. Not searched: tool calls and tool results, which are most of a
  long transcript and match everything.
- A session matches when every word appears somewhere in it. The words need not be on the same line. Matching ignores case.
- Subagent transcripts are left out unless asked for.
- Newest session first. At most 100 sessions. Each shows its count of matching messages and the newest three of them.
- Each result line opens the transcript at its line number. The matching line is marked and scrolled into view.
- The search runs 400 ms after typing stops, or at once on Enter. Clearing the box closes the results tab.
- No index in the first version: each search reads the transcripts that it needs. Measure before adding an index.
- Known limit: opening a result does not highlight its row in the rail, because the rail may not show that row.

## Contracts this must satisfy

- `TranscriptLine.From(number, json)`: returns null for records it does not show. Kinds: "you" (typed text), "claude" (text
  reply), "tool", "result", "turn". Only "you" and "claude" are searched.
- `TranscriptPager`: `Open(path)`, `LineCount`, `ReadPage(start, count)`, which takes a zero-based start and numbers lines from
  one (`n + 1`). `LastPageStart(size)`. Line numbers shown in the transcript are these numbers.
- `SessionDigest`: `SessionId`, `FilePath`, `ProjectFolder`, `IsSubagent`, `LastActivity`, `Title`, `AssistantMessages`.
- `SessionWindow`: `_history` (the loaded digests, null until read); `ShowOnly(element)` shows exactly one pane item and hides
  the rest; tabs are `TabKind.Session`, `Setup`, `Transcript`; `TabFor`, `AddTab`, `SetActive`, `RefreshTabStrip`, `ActivateTab`,
  `CloseTab`, `CloseOrAsk`; `ShowRailItemAsync` opens a history row's transcript.
- `TranscriptView.ShowAsync(digest)`: loads the last page, or (new) a page around a focus line.
- `RailPalette` and `ApplyTheme`: the rail's search box follows the theme.

## Behaviour map

- Rail category filter (All, Running, History, Temporary): unchanged.
- Subagent rows: still listed in the rail with "· subagent"; left out of search results by default.
- Open a result: the transcript tab for that session is shown, as `ShowRailItemAsync` does, with the focus line. The rail
  selection does not move (the known limit above).

## Steps

- [x] Core: `HistorySearch` with `IsUsable`, `WordsOf`, `Run` (newest first, cap, snippets, cancellation).
- [x] Core tests: every word needed, words on different lines, tool results ignored, subagents hidden by default, newest
      first, case, short words refused, snippet context, cap, newest hits kept, line numbers the transcript view shows.
- [x] Build and run the Core tests (269 pass). Commit and push.
- [x] App: search box at the top of the rail, debounced, Enter runs at once.
- [x] App: `SearchView` results tab (title, folder, age, count, newest three snippets); a click opens the transcript at the line.
- [x] App: `TranscriptView.ShowAsync(digest, focusLine)` pages around the line, marks it and scrolls it into view.
- [x] App: theme colours for the search box; `TabKind.Search` in the tab strip; `ShowOnly` shows the results view.
- [x] Build the app. Checked on screen against a scratch copy with a temporary state folder: "bullshark" typed and Enter gives the
      results tab; a click on a snippet opens "Session c71e8ad0" at line 14088, marked and in view.

Found by those checks, and fixed in the same commit:
- The rail's refresh (every 15 s, and at start) showed the setup page over a search, or over a transcript opened from one, when
  no live session was selected. Such views keep the pane now (`RestoreSelection`).
- [x] Measure one search over all transcripts (time, memory). Measured 2026-11-16 on the 137 transcripts (830 MB), in a script
      that only reads them: loading the index takes 3.6 s (the rail already does this at start). One search takes 1.2 s to 2.3 s:
      "bullshark" 1.2 s (1 session), "transcript" 1.6 s (36 sessions), "claude code" 2.3 s (87 sessions), a word that matches
      nothing 1.2 s (scans all). "the and" stops at the 100-session cap after 1.4 s. No index is needed yet.

## Not verified yet

- Speed inside the app: measured in a script (see Steps). The first search after a restart may be slower while the disk cache is cold.
- Not checked yet: a results list with many sessions, a light theme, and the rail's selection after a result is opened.
- Live: no model call is made by search, so it spends no tokens.
