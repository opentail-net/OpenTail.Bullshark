# Batch from the desktop/Codex research: context meter, plan progress, toasts by event (2026-11-18)

Source: `ideas-desktop-codex-2026-11-18.md`, ranking items 1-3 (A1, B2, B4). Owner: "save, as a plan and code". Ideas are researcher-reported; what we build is
checked against our own code below. All three are read-only or write only our own files (`library.json`, `ui.json`); nothing is sent to a session.

## Contracts this must satisfy (read in the code 2026-11-18)
- Row text: `ActivityLine(sessionId, state, fallback, running)` in `SessionWindow.Rail.cs` (~line 737) returns the second-line string: cache mark, "N ago", then state. It gets the
  facts from `_runningFacts` / `_historyFacts` (`LastRequest`, `Ttl`, `ContextTokens`). It returns a plain string (no colour runs). It is used for live rows (line ~925) and history rows
  (`LibraryRail.cs` HistoryRow, which appends " · N msgs" and `TagsSuffix`). Do not break the 🔥/🌡️/❄️/🚀 marks (auto magic naming reads the cache state, not the text).
- `CacheRule.Label` already puts the context tokens in each row's tooltip ("a message reads about N cached tokens"). The meter adds the figure to the visible line; it does not replace the tooltip.
- No context-window size is known to us (the desktop research marks it UNCONFIRMED). Do **not** invent one silently: use a setting `contextWindow` in ui.json (tokens, default 200,000,
  labelled "assumed" in Settings; the owner can choose 1,000,000 for a 1M model). Warn mark at >= 80 % of it.
- `LibraryEntry(Name, Pinned, Archived, Tags, Note, Colour, MagicAt, MagicNamedAt)` in `Core/Library/SessionLibrary.cs`: records are loaded with optional fields, an older `library.json` loads with the
  new field null; `IsEmpty` must treat an empty plan path as empty; `SessionLibrary.Update` re-reads under the file lock; `LibraryEditDialog` returns the details (`SetDetails` in `SessionWindow.Library.cs`).
- Toasts: `AnnounceAttention()` (`SessionWindow.Attention.cs`) plays the sound, flashes, and shows `ToastNotifier.Show(gained, due)` (`Core/Ui/ToastText.Build`) only when `notify` is on, the window is not in
  front and `toast` is on. Reasons: `AttentionReason` Blocked, RestartLoop, StartFailed, Stopped, Failed, Overlap, Conflict, LimitReached. Existing `ToastText` tests must keep passing (the all-categories
  default gives the same toasts as today). A toast never carries a path, prompt or detail (`ToastText` rule): new toasts follow it.
- Settings page (`SettingsView.cs`, `SessionWindow.Settings.cs`) writes ui.json through `UiFile.Set` and reports what the file holds afterwards; `Styles/Controls.xaml` has `SwitchToggle`, `PillToggle`. Zoom
  stays in the status bar corner (owner decision; do not touch it).
- Ready for review: `_review` (`SessionWindow.ReadyForReview.cs`, `ReviewItem`) lists finished coders with changes. Limits in force: `LimitStatus.Active(projectsRoot, now)` (status bar A2 reads it
  about once a minute, `SessionWindow.LimitStatus.cs`).
- Privacy: no transcript text, no plan-file text in logs; the plan file is read only to count checkboxes and take the first open item's words (shown in a tooltip, never logged).

## Part 1. Context meter
- [x] 1.1 Core `Cache/ContextMeter.cs`: `ContextMeter.Short(long tokens)` -> "ctx 142k" (thousands, rounded down, "ctx 1.2M" from 1,000,000; empty string when tokens <= 0),
  `ContextMeter.IsHigh(long tokens, long window)` (>= 80 %), `ContextMeter.WindowFrom(JsonNode?)` reading `contextWindow` (default 200,000; clamp 50,000 to 5,000,000; junk -> default).
  Tests: formatting edges (0, 999, 1,000, 142,300, 999,999, 1,000,000, 1,250,000), threshold exactly 80 %, junk settings, clamp.
- [x] 1.2 App: `ActivityLine` appends " · ctx 142k" (and " ⚠" when high) for a session with facts and tokens > 0; the live-row tooltip and history tooltip keep `CacheRule.Label`.
  The window reads the `contextWindow` setting with the other ui.json reads on each rail build (cheap) or caches it and refreshes on change.
- [x] 1.3 Settings > General: "Context window for the ctx meter" pill choices 200k / 1M (writes `contextWindow` 200000 / 1000000), caption "The window of the model is not read from Claude;
  this is your choice. The warning mark shows at 80 %." If ui.json holds another number, show "Custom (N)" without rewriting it (same approach as `EscalationChoices`).

## Part 2. Plan progress on the row
Decisions: the owner attaches a plan file to a session by hand (absolute path) in the Edit dialog; we do not guess a session's plan. Counts `- [ ]`, `- [x]`/`- [X]` and `- [!]` (not done) list items,
any indentation; a line inside a fenced code block does not count. Shown as "3/7" on the row's second line, with the first open item's text (cut to 80 characters) in the tooltip.
- [x] 2.1 Core `Library/PlanProgress.cs`: `PlanProgress.Parse(IEnumerable<string> lines)` -> `(int Done, int Total, string? Next)`; `PlanProgress.ReadFile(path)` -> the same or null when the file is missing/unreadable
  (never throws; caps the read at 512 KB). Tests: counts, nested items, `[!]`, fenced block ignored, empty file, no checkboxes (Total 0 -> shown as nothing), 100 % done, `Next` cut at 80, CRLF.
- [x] 2.2 Core `LibraryEntry` gets `string? PlanFile = null` (last parameter); `IsEmpty` counts it; library load/save round-trips it; an old file loads with null. Tests in the existing library tests style.
- [x] 2.3 App: `LibraryEditDialog` gets a "Plan file (optional, a Markdown file with checkboxes)" box and a Browse… button; `SetDetails` stores it (trimmed; empty -> null). The row second line gets " · 3/7"
  for a session whose mark has a plan file that parses with Total > 0; an in-memory cache keyed by (path, last write time, length) avoids rereading on every rail build. The tooltip gets "Plan: 3 of 7 done. Next: ...".
- [x] 2.4 Palette: nothing new. README: one line.

## Part 3. Toasts by event
Decisions: five categories, each its own switch under Settings > General > Notifications, **toasts only** (sound and flash behave as today for waits): **Blocked** (Blocked), **Problems** (RestartLoop, StartFailed,
Stopped, Failed), **Notices** (Overlap, Conflict, LimitReached), **Ready for review** (a coder newly joins the Ready-for-review list: new toast, silent, no sound), **Limit reset** (a usage limit that was in force is
gone: new toast "Usage limit reset", silent). Stored as one ui.json object `toastEvents` {blocked, problems, notices, ready, limitReset: bool}; a missing key or value = on (same default as `NotificationsOn`).
New toasts need `notify` on, `toast` on, and the window not in front (same gates as today).
- [x] 3.1 Core `Ui/ToastCategories.cs`: enum `ToastCategory { Blocked, Problems, Notices, Ready, LimitReset }`; `ToastCategories.Of(AttentionReason)`; `ToastCategories.Enabled(JsonNode? toastEvents, ToastCategory)`;
  `ToastText.Build(gained, due, Func<AttentionReason,bool>? allow = null)` (default allows all: existing behaviour and tests unchanged); `ToastText.Ready(names)` ("Ready for review", "<name>[, and N more]") and
  `ToastText.LimitReset(labels)` ("Usage limit reset", the label of the limit(s) from `LimitStatus.LabelOf`, e.g. "5-hour"). Same rules: name cut at `MaxNameLength`, no detail. Tests for all of it incl. filtering to nothing -> null.
- [x] 3.2 App: `AnnounceAttention` filters the toast through the category switches (sound and flash unchanged). New hooks: when `_review` gains a session since the last pass (first pass = baseline, like attention),
  and when `LimitStatus.Active` goes from non-empty to empty (compare with the previous read; the status-bar read already runs about once a minute: reuse it, do not add a second scan). Both silent toasts only, and only when
  the window is not in front. A toast is never made for the baseline pass. Failures are logged once through `ToastNotifier.LogFailure`.
- [x] 3.3 Settings > General > Notifications: five switches under "Toasts for:" (disabled, dimmed, when the toast switch is off), each writes `toastEvents` through `UiFile.Set` (read the object, change one key, write it back),
  reports what the file holds afterwards. The palette's "Toggle toasts" stays.
- [x] 3.4 README line; `docs/plans/live-run-checklist.md` items for all three parts (ctx figure on a live and a history row; a plan file attached and ticked while the window is open; each toast category, needs a minimised window).

## Checks
Core tests with counts; App build; the whole suite `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false`. Screenshots with `scripts/Test-NewScreens.ps1 -Exe src/OpenTail.Bullshark.App/bin/Debug/net10.0-windows10.0.19041.0/OpenTail.Bullshark.App.exe`
(check free RAM and that no Bullshark window is open first): rail with a ctx figure and a plan figure (extend the fixture: one transcript with usage so ctx shows, one plan file attached to a session in the
fixture library), Settings > General with the new switches in dark and light. View every picture; fix what is wrong.

## Orchestrator review
Diff against Contracts: ToastText defaults unchanged, sound/flash unchanged, no baseline toast, no text of the plan or transcript logged, old library.json loads, `IsEmpty` right, the window setting not
invented (setting + label "assumed"), zoom untouched, light theme read.

## Open / not decided
- The context window is the owner's setting (200k default), not read from the model.
- Plan file is attached by hand, absolute path only.
- "Finished with changes" toast uses the Ready-for-review list as the definition.

### Coder's assumptions and deviations (2026-11-18, desktop batch coder)
- Context window: a stored number is clamped to 50,000 to 5,000,000; text, a bool, zero or a negative gives 200,000. Settings shows "200k (assumed)" and "1M", and "Custom (N)" for another number (not rewritten).
- The figure below 1,000 tokens is the raw number ("ctx 999"); from 1,000 it is in thousands, from 1,000,000 in tenths of a million, rounded down.
- The ⚠ is added after the figure at 80 % or more of the window (exact integer test).
- The ctx and plan figures are appended after the state, as 1.2 and 2.3 say. The second line of every rail row now wraps instead of being cut with an ellipsis: at the default rail width the figures at its end were lost. The full text stays in the tooltip. Revert to NoWrap if the owner prefers one-line rows.
- Plan items: only the dash bullet counts (`- [ ]`, `- [x]`, `- [X]`, `- [!]`). Numbered and `*` items do not count. A line in a fenced block does not count. A byte order mark is dropped. `Next` is the first not-done item with text, cut at 80 characters.
- `PlanProgress.ReadFile` returns a record `PlanProgressCount(Done, Total, Next)` rather than a bare tuple: the same fields, named.
- The plan figure shows on live rows and history rows only, not on Ready-for-review rows (their tooltip has no plan line either).
- The plan path is trimmed and loses one pair of surrounding double quotes. The Edit dialog refuses a relative path and keeps itself open.
- Limit reset (3.2): the plan says "when LimitStatus.Active goes from non-empty to empty". Implemented per limit type instead: a type in force at the last pass that is no longer in force is announced, so a 5-hour reset while a weekly limit stays in force is announced too. The list is the last status-bar scan, so no second scan.
- Ready for review (3.2): a toast for each coder that joins `_review` between two passes. The first pass only sets the baseline.
- Toast tags: the Needs-you, Ready and Usage-limit toasts each have their own tag, so one kind does not replace another in the action centre. Each is silent. Gates: notify on, toast on, window not in front, and the kind's own switch on.
- The sound and the flash for a wait do not depend on the kind switches (as the plan says).
- Not done: the palette and the ctx figure on Ready-for-review rows are not changed; the README has one bullet for the rail marks, not a separate ctx line.
