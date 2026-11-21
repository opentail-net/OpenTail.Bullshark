# Rail feature inventory: Claude side vs Codex side

Read-only inventory of the App (`src\OpenTail.Bullshark.App`) on 2026-11-21, for the Codex parity section of `docs/plans/provider-codex-2026-11-21.md`. File names are short forms: Rail = SessionWindow.Rail.cs, Lib = .Library.cs, LRail = .LibraryRail.cs, Att = .Attention.cs, Tabs = .Tabs.cs, Cdx = .Codex.cs, Srch = .Search.cs, Split = .Split.cs, Pal = .Palette.cs, xaml = SessionWindow.xaml. Line numbers are as of the research run.

Codex column: YES (works for Codex now), PART, NO (absent or hidden). Class: **Shared** (history only, could be the same control on both sides), **Claude-only** (needs supervision or live state), **Codex only**.

## Rail rows

| Feature | Does | Where | Codex | Class |
|---|---|---|---|---|
| Live row | Title, state, shared-file flag | Rail:1093-1111 | NO (no live rows, Cdx:124) | Claude-only |
| Activity line | Idle time, cache mark, context %, plan figure | Rail:900-924 | NO | Claude-only |
| History row | Title, model, time, tags, colour bar, note tooltip | LRail:78-101; Cdx:203-220 | YES | Shared |
| Plan figure | "3/7" plan progress | Lib:217-233 | NO (Lib:218 reads Claude marks only) | Shared |
| Resume mark | Resume prompt: model, effort, summary | Rail:934-953; Resume:12-17 | NO (Codex: double-click, Cdx:213-218) | Claude-only |
| Match badge | Count of matching messages | Rail:959-971 | NO | Shared |

## Row menu

| Feature | Does | Where | Codex | Class |
|---|---|---|---|---|
| Pin / Unpin | Pins a past session to the Pinned group | Lib:306 | NO (Cdx:235-244) | Shared |
| Archive | Moves a session to Archived | Lib:307; Cdx:241 | YES | Shared |
| Edit… | Name, tags, note, colour, plan file | Lib:313, 318-334; Cdx:242 | YES (plan saved, not shown) | Shared |
| Resume in terminal | Runs `codex resume` in a tab | Cdx:240, 283-306 | YES | Codex only |
| Changes…, Wake after limit | Live folder's git changes; restart after limit | Lib:310, 312 | NO | Claude-only |
| Magic rename | Haiku names a past session, after confirm | MagicRename:93-107 | NO | Claude-only |
| Acknowledge, Snooze 1h/4h | Silences re-alerts for a waiting session | Att:162-183 | NO | Claude-only |
| Mark reviewed, Open Changes | Ready-for-review actions | ReadyForReview:102-109 | NO | Claude-only |

## Groups

| Feature | Does | Where | Codex | Class |
|---|---|---|---|---|
| Needs you | Waiting sessions, daemon and problem notes | Att:73-150; Rail:475 | NO (Rail:409-413 returns early) | Claude-only |
| Ready for review | Finished coders not yet reviewed | ReadyForReview:71-109 | NO | Claude-only |
| Pinned | Pinned past sessions, top of History | LRail:14, 30-47 | NO | Shared |
| Folder groups | Past sessions by folder, newest first | Rail:590-664; Cdx:159-197 | YES | Shared |
| Archived group | Archived sessions, collapsed | Cdx:178-185 | YES | Shared |
| Group by tag | History grouped by #tag | Rail:184-207 | NO | Shared |
| Reload mark | Re-reads history, no model | Rail:536-540; Cdx:112-117 | YES | Shared |
| New Codex session | Folder pick, then `codex -C` tab | Cdx:122-123, 309-325 | YES | Codex only |

## Filters

| Feature | Does | Where | Codex | Class |
|---|---|---|---|---|
| View dropdown | All, Running, History, Temporary, Archived | Rail:55-62, 137-146, 253-260; Cdx:87 | NO (hidden) | Running: Claude-only; the rest shared |
| Tag filter | Rows holding the chosen #tag | Rail:152-182, 214-221 | NO | Shared |

## Search

| Feature | Does | Where | Codex | Class |
|---|---|---|---|---|
| Search box | Past-message search, 3+ letters a word; also name, tags, note | Srch:43-69; Lib:236-242 | NO (disabled, Cdx:88-89) | Shared |
| Results tab | Matches newest first; opens the transcript at the line | Srch:111-157, 183-194 | NO | Shared |

## Tabs

| Feature | Does | Where | Codex | Class |
|---|---|---|---|---|
| Session tab | One per live session; one per opened Codex session | Tabs:439-476; Cdx:331-346 | PART | Live: Claude-only |
| "+" | New session; under Codex starts a Codex session | Tabs:97-102 | YES | Shared |
| State glyph | Busy, idle, waiting, closed | Tabs:594-605 | NO (Codex tabs have no state) | Claude-only |
| Close (x) | Closes the tab; Claude asks hide or stop | Tabs:219-246; Cdx:375-381 | YES (Codex closes the terminal only) | Shared |
| Stop session | Stops the process, closes the tab | Tabs:253-260 | NO | Claude-only |
| Title and colour | Owner's name; colour bar on the tab | Lib:105-121; Tabs:543 | YES | Shared |
| Move to right pane | Session tab to the right pane | Split:235-246 | YES (menu not gated; not run) | Shared |
| Own window | Pops the session out | PopOut:20-23 | NO (by design) | Claude-only |
| Transcript tab | A past session's conversation | Tabs:156-159; Rail:831-834 | NO (Cdx:14-15) | Shared |

## Status bar

| Feature | Does | Where | Codex | Class |
|---|---|---|---|---|
| Status line | Last action, pass summary | xaml:67-68; xaml.cs:739 | YES | Shared |
| Needs-you count, limit status, Trust/Retry/Respawn | Claude waits, usage limit, start fixes | xaml:25-59; Att:101-107; LimitStatus:79-82 | NO (hidden, Cdx:92-96) | Claude-only |
| Zoom | Terminal text size | xaml:28-40; Zoom:38-42 | YES (Cdx:341) | Shared |
| Theme | Dark or light rail | xaml:85-86; Rail:367 | YES | Shared |
| Go to… | Palette: sessions, pages, actions | xaml:49-51; Pal:84-158 | PART (not gated) | Shared shell |
| Insights | Away, Usage, Timeline, Storage | xaml:46-48; Away:134 | NO (not gated; reads Claude marks) | Claude-only |
| Settings | Preferences | xaml:43-45 | YES (not gated) | Shared |

## Findings

- **Gaps (bugs):** the palette (Pal:84-158) and Insights (Away:134) have no provider gate. Under Codex they still list and open Claude sessions, actions and data.
- **Most useful missing shared features for a Codex user:** (1) search of past sessions; (2) a transcript view of a past session; (3) Pin and the Pinned group (the library already keeps `codex:` entries, Lib:27-28); (4) tag filter and group by tag (Edit already writes tags, Cdx:242); (5) the plan figure on Codex rows (Edit already writes PlanFile, Lib:210, but the row reads only Claude's marks, Lib:218).
- **Already working for Codex:** resume, folder groups, reload, archive, edit, colour, zoom, theme.
- **Claude-only by nature:** Needs you with acknowledge and snooze (Att:113-183), which needs a supervisor; live rows, state glyphs, stop and respawn; limit wake and limit status (Lib:312; LimitStatus:79-82).
