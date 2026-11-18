# Needs-you escalation, snooze and acknowledge, 2026-11-18 (ideas D2)

Goal: an item in the Needs-you list that nobody answers makes a noise again later, so a session that blocks while the owner is away from the
PC (but the window is open) is not missed for hours. The owner can acknowledge or snooze an item. Idea from PagerDuty (escalation after a
timeout; acknowledge silences, does not resolve), Datadog downtimes (quiet for a window, notify again at the end if still alerting) and Sentry
(snooze). See `ideas-2026-11-18.md` (researcher-reported). Nothing is ever sent to a session (principle 3).

## What exists (read first)

- `SessionWindow.Attention.cs`: `AnnounceAttention()` runs after every pass (about every 15 s). It compares `_announced` with `_attention` through `AttentionList.Gained(previous, current, _announcedNotices)`, writes `needs you: ...` to the log, plays `SystemSounds.Asterisk` and flashes the taskbar (`TaskbarFlash.UntilFocused`), unless ui.json `notify` is false (`AttentionList.NotificationsOn(UiFile.Read(..., "notify"))`).
- `AttentionList` (Core/Supervisor): `AttentionItem(Name, SessionId, Reason, Detail, StartedAt, FilePath, Files)`; reasons Blocked, RestartLoop, StartFailed, Stopped, Failed (the owner must act), and notices Overlap, Conflict, LimitReached.
- Rail rows of Needs-you: `SessionWindow.Attention.cs` builds them (`LiveRow`); rows have `RailTag`s and `ContextMenu`s elsewhere (see `SessionWindow.Schedule.cs` WaitingRow for a menu example).
- `UiFile.Read/Set` for ui.json settings; `SetActive`/tab selection in `SessionWindow.Tabs.cs`.

## Contracts this must satisfy

| Member | Promise |
|---|---|
| `AttentionList.From/Gained/NoticeKeys` | Unchanged behaviour and tests. Escalation is a separate pure class; it never changes the list. |
| `AnnounceAttention` | First sets the baseline; the first announcement sound for a new item is unchanged; `notify:false` silences everything including re-alerts. |
| Principle 3 | Never type into or answer a session; no auto-resume; only sound, flash and log. |
| State | Escalation state (last alert time, repeat count, snoozed-until, acked) lives in memory for the life of the window. Not persisted (a restart of the window is itself a fresh start). |
| UI thread | Pure, cheap; no disk reads on the tick except the `notify` and `escalateMinutes` ui.json reads that `AnnounceAttention` already does. |

## Design decisions (AFK; change if you disagree)

- D1 Only items the owner must act on escalate: Blocked, RestartLoop, StartFailed, Stopped, Failed. Notices (Overlap, Conflict, LimitReached) never re-alert.
- D2 Interval: `escalateMinutes` in ui.json, default 15, `0` = off. A still-waiting, not-acked, not-snoozed item re-alerts when `now - lastAlert >= interval`; at most 3 re-alerts per waiting spell (so 15, 30, 45 min after the first announcement), then silent. The spell ends when the item leaves the list; if it returns it is a new spell (the normal announcement applies).
- D3 Acknowledge: silences re-alerts for that item for the rest of the spell. The item stays in the list. Opening the session's tab (it becomes the active tab) acknowledges it implicitly. Right-click on its Needs-you row: "Acknowledge".
- D4 Snooze: right-click "Snooze 1 hour" (and 4 hours): no re-alert until then; at the end, if still waiting, it re-alerts once and the repeat count restarts (Datadog: notifies again at expiry if still alerting).
- D5 A re-alert plays the same sound and flash as a new item (no new sound asset), and the log line says `needs you (again, N): name`.
- D6 Re-alerts are keyed by item Name (the row's key), not by reason; a reason change within a spell does not restart the spell (same as `Gained`).

## Steps

- [x] 1. Core `Supervisor/AttentionEscalation.cs`: a small class holding the per-name state with `Update(IReadOnlyList<AttentionItem> current, DateTimeOffset now, TimeSpan interval)` returning the items due for a re-alert (and updating state), plus `Acknowledge(name)`, `Snooze(name, until)`, and a way to mark first announcements (`Announced(names, now)`) so spells start when the normal announcement fires. Only reasons of D1 count. Interval `TimeSpan.Zero` returns nothing. Why a class with an injected `now`: testable without a clock or UI.
- [x] 2. Tests (`tests/OpenTail.Bullshark.Core.Tests/Supervisor/AttentionEscalationTests.cs`, 24 cases with the Theory rows): no re-alert before the interval; re-alert at the interval, then at 2x and 3x, then never; ack stops it; snooze defers, then one re-alert at expiry and the count restarts; item leaves and returns = new spell; notices never re-alert; interval 0 = off; two items independent; a reason change inside a spell keeps the spell.
- [x] 3. App: in `AnnounceAttention`, after the gained handling, call the escalation with the current list and the interval from ui.json `escalateMinutes` (default 15; helper beside `NotificationsOn`, with tests for parsing: missing, non-number, negative, 0, large -> clamp to 1..1440 except 0). Play sound and flash when something is due and notifications are on. Spells must start for the names announced as gained (call `Announced`) and for the baseline items of the first pass (so a session that was already blocked when the window opened still escalates; it is counted from the first pass).
- [x] 4. App: context menu on the Needs-you rows ("Acknowledge", "Snooze 1 hour", "Snooze 4 hours"); opening the session's tab acknowledges. Find where the tab becomes active and hook there; do not poll.
- [x] 5. Docs: tick this file, tick backlog D2, one line in `session-summary-2026-11-17.md`, a "Decisions" section here for anything you chose.
- [x] 6. One commit, only these files, `git status` first. Do not push. (Committed with this file; not pushed.)

## How to check

`dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` (1053 passing now). Build the whole solution including the App (`dotnet test` alone does not build it: run `dotnet build OpenTail.Bullshark.slnx -nodeReuse:false`). Say plainly that the sound, the flash, the menu and the tab-acknowledge were not seen in a running window.

Result (2026-11-18): `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` exit 0, 1077 passed, 0 failed (1053 before, plus 24). `dotnet build OpenTail.Bullshark.slnx -nodeReuse:false` 0 errors; one existing warning CS8601 in `MainWindow.xaml.cs`, a file not touched. Not checked: the sound, the flash, the Needs-you menu and the tab acknowledgement in a running window.

## Decisions

- Step 1 `Announced(names, now)` was not added. `Update` starts a spell for every escalating item it has not seen, which covers the gained items and the first-pass baseline in the same pass. One entry point, and no ordering to get wrong.
- The `escalateMinutes` helper is `AttentionEscalation.IntervalOf`, not beside `NotificationsOn` in `AttentionList`, because `AttentionList` and its tests stay unchanged.
- `escalateMinutes`: missing, not a number (a string, a bool, an array) or negative gives 15 minutes, the default, as `notify` does with a non-boolean. Negative is not clamped to 1. 0 is off. Other numbers are clamped to 1 to 1440 minutes. The number is read from its JSON text, because `TryGetValue<double>` does not convert an int made in code (found by the test for 0).
- The re-alert at the end of a snooze is the first of a new count (`Again` 1). So after a snooze the owner gets at most three alerts in all: the expiry, then two at the interval. A count holds at most three re-alerts, as a spell does.
- Acknowledge clears a snooze, and snooze clears an acknowledgement. Otherwise the snooze's own re-alert would be swallowed.
- With `escalateMinutes` 0 a snooze does not re-alert at its end either: off means off.
- With `notify` false, the re-alerts that fall due are used up silently; they are not held until `notify` is back on.
- The menu choices appear only on a Needs-you row whose item is an escalating reason (D1). A notice-only row (overlap, conflict, limit) has no choices. They sit at the top of the row's menu, above "Changes…" and "Edit…". A failed-start row (no session, so no menu before) gets a menu with the choices too, or its re-alerts could not be answered.
- Opening a session's tab acknowledges it. The hook is `SetActive` for a session tab. That covers a rail selection, a tab click, a search result that selects the session, and the neighbour tab that takes over after a close (the neighbour is shown, so it counts as opened).
- Open question: a session whose tab is already the active one when it starts waiting is not acknowledged, since the tab is not opened again. The owner acknowledges it from the menu, or by selecting the tab again. The plan does not say which is wanted.
- The backlog had no D2 checkbox; a D2 line was added in the D1 format.
