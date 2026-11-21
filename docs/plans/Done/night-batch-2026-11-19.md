# Night batch: bedtime preflight, supervisor heartbeat, night report file, quiet hours (2026-11-19, written AFK)

Source: brainstorm round 1 (overnight angle, researcher-reported, code facts re-read by the orchestrator): the window is the only supervisor (the headless host is report-only), so a closed or sleeping
PC means no 09:00 start, no restarts and no record. Chosen: ideas 1, 3, 5 (all local, read-only or our own files, no owner decision needed). Left for the owner/other rounds: **keep awake** (power is the
owner's call; the preflight only *reports* the sleep timeout), stalled state (false alarms), park until morning (hides blocks), night usage comparison.
Decisions made AFK (assumptions, change if wrong): quiet hours are off by default; when on they silence sound, flash and toast for **everything** (the rail, the log and the digest still show it); the
night report is written automatically only when the return digest has news.

## Contracts (read in the code 2026-11-19)
- The pass runs on a 15 s `DispatcherTimer` in `SessionWindow.xaml.cs` (`PassAndRefreshAsync`); `Supervisor/Program.cs` is report-only. Nothing records when the last pass ran.
- `RunStore.IsWaiting(run, now)` / `DueSpecsOf` / `RunRecord.StartAt`: a run with a future start waits. `LoginExpiry.IsExpiringSoon(remaining)` (Core/Health): the login-days check; `DaemonStatus` (Core/Health or Claude);
  the Setup check (`SessionWindow.SetupCheck.cs`, `SetupCheck.Run`/report: find the Core type); `LimitStatus.Active(projectsRoot, now)`; `AttentionList` (Needs-you).
- `AnnounceAttention()` (`SessionWindow.Attention.cs`): sound, flash and toast only when `notify` is on; re-alerts counted by `AttentionEscalation` (spells, `MaxReAlerts`). A quiet period must still update the
  escalation (so a re-alert that falls inside it is used up silently, as with `notify:false`) and must never change what is *listed*.
- Away: `AwayDigest.Build`, `AwayExport.Markdown(digest, since, now)`, `RefreshAwayAsync(onReturn)`, `_insightsForDigest`; the Away page has "Copy as Markdown". A report file must not change those.
- Insights: `InsightsView` (pill tabs: Away, Usage, Timeline, Storage), `InsightsTab` enum, `OpenInsights`, `RefreshInsightsPage`; palette deep links ("Open Away/Usage/Timeline/Storage"); `--open-tab` test names in
  `Core/Ui/TestScreens.cs` and `SessionWindow.SelfTest.cs`; `scripts/Test-NewScreens.ps1`.
- Settings: `SettingsView`, `SessionWindow.Settings.cs`; ui.json through `UiFile.Read/Set` (the write reports what the file holds); `EscalationChoices` is the pattern for a choice with a "Custom" value.
- Files go beside the state file (`Path.GetDirectoryName(Path.GetFullPath(_options.StatePath))`): a new `heartbeat.json` and a `reports\` folder. Atomic writes via the existing `FileLock.WriteAtomic`. Never in a repo.
- Privacy: the report holds the digest (names, counts, the last reply cut to 300 chars): local, in the state folder, never logged beyond its path.
- Principle 3: nothing is sent to a session; `powercfg` is read-only (`/query`), run with a timeout, "could not read" on any failure.

## Steps (Core first with tests, then App)
- [x] N1. Core `Health/Heartbeat.cs`: `Heartbeat.Write(path, now, passNumber?)` and `Heartbeat.Read(path)` -> `DateTimeOffset?` (atomic, tolerant of a missing or damaged file: null, never throws); `Heartbeat.Age(last, now)`; `Heartbeat.Describe(age)` ("12 s ago", "3 min ago",
  "2 h ago"). Tests. App: write it at the end of every pass (cheap: one tiny file; skip the write when the previous write was under 10 s ago).
- [x] N2. Core `Health/Preflight.cs`: pure `Preflight.Build(PreflightInput)` -> `PreflightReport` { rows, verdict }. Each row: `Name`, `Status` (Ok / Check / Unknown), one short `Text` (no paths beyond a folder name, no prompt text). Rows: **Window/pass** (last pass age; Check when
  older than 2 min), **Daemon** (running / idle / down: Check when down while runs exist), **Login** (days left from `LoginExpiry`; Check when expiring soon; Unknown when unreadable), **Setup check** (count of problems from the existing check; Unknown if not run),
  **Usage limit** (in force: type and reset time; Check when a limit is in force and a run is due before it resets), **Runs** (runs waiting with a start time in the next 12 h: count and the earliest; Check when none is scheduled but sessions are live is NOT a problem: Ok),
  **Trust** (sessions waiting at a trust step: Check), **Sleep** (the AC sleep timeout in minutes: Check when it is set and a run is due or live; Ok when "never"; Unknown when unreadable), **Needs you** (count now: Check when > 0), **Quiet hours** (on/off and the window).
  `Verdict`: "Ready for the night" when no row is Check, else "N things to check". `PreflightInput` is a plain record the App fills (so the logic is testable without a window): last pass, daemon state, login remaining, setup problem count, active limits, runs, trust count,
  sleep minutes (int?), needs-you count, quiet hours. Tests for every row's three states and the verdict.
- [x] N3. Core `Health/PowerSettings.cs`: `ParseSleepMinutes(string powercfgOutput)` for `powercfg /query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE` (the "Current AC Power Setting Index: 0x...." line, seconds; 0 = never) -> `int?` (minutes; null when not found). App side runs it through the existing `ProcessCliRunner`
  (timeout 5 s, no window) off the UI thread, read when the Night page is shown. Tests with sample outputs (English; a missing line; hex values; zero).
- [x] N4. Core `Ui/QuietHours.cs`: `QuietHours.Parse(JsonNode?)` ({"from":"22:00","to":"07:00"} or null/off), `IsQuiet(now local time, from, to)` handling windows that cross midnight and from == to (= off), junk -> off, `Label(...)`. Tests incl. 23:59, 00:00, crossing midnight, boundaries (from inclusive, to exclusive).
  App: `AnnounceAttention` skips sound, flash and toast while quiet (escalation still updated; the log line says "quiet hours"); Settings > Naming (rename that sub-tab "Alerts"? NO: keep names; put it in Settings > General under Notifications): pill choices Off / 22:00-07:00 / 23:00-08:00 / 00:00-09:00 (+ "Custom (..)" shown without rewriting), writes `quietHours` through `UiFile.Set`.
- [x] N5. Core `Ui/NightReport.cs`: `NightReport.Markdown(AwayDigest, since, now, IReadOnlyList<(string Session, int Done, int Total, string? Next)> plans, UsageReport? usage)` = `AwayExport.Markdown` plus a "Plans" section (boxes done/total and the next open item) and a "Tokens in this period" line (New and Cache read, from the usage report days inside the period).
  `NightReport.FileName(now)` -> `away-2026-11-19-0905.md` (local time, sortable); `NightReport.UniquePath(folder, name)` never overwrites (adds -2, -3). Tests.
  App: when a return opens Insights on Away (`RefreshAwayAsync(onReturn: true)` with news), write the report once to `<state folder>\reports\` on a pool thread; log only the file name; the Away page gets a muted line "Saved to reports\away-....md" and a button "Open reports folder" (shell open). A failed write is said on the page, never thrown.
  Keep only the newest 60 reports (delete older files **that this feature created**: names matching `away-*.md`; nothing else in the folder).
- [x] N6. App: Insights gets a fifth pill **Night** (after Away): the preflight rows as a calm list (status as a coloured dot or a text word "ok / check / unknown" using the palette warning colour, no emoji), the verdict line on top, a Refresh button, "Last pass: 12 s ago", and below it the quiet-hours state with a link-style button to Settings. `InsightsTab.Night`, palette entries "Open Night (preflight)", `--open-tab night`, a
  screen `insights-night-dark` (and light) in `scripts/Test-NewScreens.ps1` (fixture: a waiting run and a heartbeat file). Refreshes when shown and every 30 s while visible (a `DispatcherTimer` that stops when hidden).
- [x] N7. Docs: README bullets (Night page, reports folder, quiet hours); `docs/plans/live-run-checklist.md` items for N1-N6 (quiet hours: with a window containing now, a new wait gives no sound/flash/toast, and the rail still lists it; the report file appears after a return with news; the preflight Sleep row on this PC).

## Checks
Core tests with counts; App build; `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false`. Screenshots (check free RAM and no Bullshark window): `scripts\Test-NewScreens.ps1 -Exe src\OpenTail.Bullshark.App\bin\Debug\net10.0-windows10.0.19041.0\OpenTail.Bullshark.App.exe`; view every new picture (Night page dark and light, Settings General with quiet hours); fix what is wrong.

## Orchestrator review
Diff against Contracts: quiet hours never hide what is listed, escalation still updates; no writes outside the state folder; the report cleanup only touches `away-*.md`; preflight rows never show prompt text; heartbeat writes are rate-limited and failure-tolerant; powercfg read-only with a timeout; light theme read.

## Open / not decided
- Keep awake (needs the owner), stalled state, park until morning, night usage comparison.
- Quiet hours silence everything including a Blocked wait: owner may want Blocked exempt.

### Assumptions made while building (2026-11-19, coder; change if wrong)
- **Daemon row "down"** = the daemon is not running while listed sessions still claim working or blocked (`StaleSessions`). A plain "not running" is idle and Ok. Control-socket reachability is not used.
- **Trust row** counts folders with a trust step (`_trustSteps`), not sessions.
- **Login row** comes from the supervisor pass: `SupervisorLoop.LastLogin` records what the pass already read from live sessions (no extra `claude logs` call). Unknown until a pass has read a live session.
- **Setup check row** runs the existing read-only `SetupCheck.Run` when the Night page is shown and on Refresh (not on each 30 s tick). Unknown until then.
- **Sleep row** reads `powercfg` on show and Refresh only (not each 30 s). "Due or live" = a run with sessions that is live now or starts within 12 h. The minutes are rounded up.
- **Usage limit row** is Check when a run with sessions is live, or starts before the governing (latest) reset.
- **Verdict** wording: "1 thing to check" (singular) and "N things to check". Unknown rows do not count.
- **Quiet hours row** is always Ok: it states the window and whether it is quiet now.
- **Heartbeat** is written only after a pass that really ran (not with `--no-pass`, not while trust is unknown), at most every 10 s. Its pass number counts those passes.
- **Report cleanup** matches only `away-yyyy-MM-dd-HHmm.md` with an optional `-n` before `.md`: a stricter reading of "away-*.md", so a file the owner names `away-notes.md` is never deleted. Newest = by the time in the name, then the `-n` number.
- **Report content**: saved only on a return with news; plans are the library's plan files of the sessions that have one (boxes done of all, next open item); tokens are the usage-report days that fall in the period, counted whole.
- **Quiet hours on Off** write JSON `null` under `quietHours` (`UiFile.Set` now takes a nullable value). A hand-set window shows as "Custom (...)" and is not rewritten.
- **Quiet gating** is placed after the "notify" and "toast" switches, so the "(quiet hours)" log lines appear only where a sound or toast would otherwise have shown. The silent ready and usage-limit toasts are silenced too.
- **Screenshot fixture** (`scripts/Test-NewScreens.ps1`) now writes a second run, waiting seven hours ahead, into `runs.json` for every screen, and a fresh `heartbeat.json` before each screen. The rail screens show that waiting run.
- **Not built**: keep-awake (owner's call), stalled state, park until morning, night usage comparison.
