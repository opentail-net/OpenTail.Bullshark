# Start a run when the usage limit resets, 2026-11-18

Goal: on the setup page the owner ticks "Start when the usage limit resets"; the run waits (existing `StartAt` mechanism) until
the reset time of the limit Claude Code last reported, plus a margin. Closes the open item in `gems-2026-11-17.md` item 5 / D5.6.

## Gate result (done 2026-11-18, evidence from the owner's own transcripts under `~/.claude/projects`)

The reset time IS available as structured data, so no text parsing of "resets 1:40am" is needed. When a limit is hit, Claude Code
writes an assistant entry to the session transcript:

    {"type":"assistant","timestamp":"2026-11-16T10:05:07.429Z","apiError":"usage_limit_reached",
     "apiErrorParams":{"rate_limit_info":{"status":"rejected","resetsAt":1794834600,"rateLimitType":"five_hour",...}},
     "message":{"model":"<synthetic>","content":[{"type":"text","text":"You've hit your 5-hour limit · resets 1:10pm (Europe/London)"}]}, ...}

- `resetsAt` is Unix **seconds** (1794834600 = 2026-11-16 13:10:00 UTC = 1:10pm London, matches the text).
- Seen `rateLimitType` values: `five_hour`, weekly, session, a model limit (text "Fable limit · resets Nov 19, 9am"). Do not
  hard-code the list; use `resetsAt` only.
- Seen `status`: `rejected`. Other statuses may exist: only `rejected` counts.
- A later entry "Usage limit reset · Continuing automatically" / "Your claude.ai usage limit has reset" appears after reset (informational).
- Not verified: whether `agents --json` carries it (it does not in the sample); behaviour for non-claude.ai (API key) accounts.

## Contracts this must satisfy

| Member | Promise / what callers rely on |
|---|---|
| `RunRecord.StartAt` (RunStore.cs) | Run has no sessions in a pass until `StartAt`; null = start at once; old runs.json loads. **No new field is added.** |
| `SetupRequest.StartAt` (SetupPlan.cs:49) | Becomes `RunRecord.StartAt` (SetupPlan.cs:237). |
| `StartTime.Parse/Describe` (Setup/StartTime.cs) | Typed HH:mm; invalid text blocks Start (SetupView.xaml.cs:391). Unchanged. |
| `TranscriptFile.Open/ReadLines` (Activity/TranscriptFile.cs) | The only way to read a transcript (shared read/write/delete). A bad line or unreadable file is skipped, never fatal (round 12). |
| `StoreErrors.IsExpected` | Expected IO errors are reported in the status line, not thrown. |
| Pass/UI rule | No disk scan on the UI thread: run the scan in `Task.Run`. |

## Design decisions (made AFK, change if you disagree)

- D1 Resolve the time once, at Start click, into `StartAt` (no new store field, no new pass logic). Margin: 2 minutes after `resetsAt`.
- D2 Which limit: among rejected entries with `resetsAt` in the future, take the **latest** `resetsAt` (if the weekly and the 5-hour limits are both active, the weekly one governs).
- D3 No active limit found (none, or all in the past): the run starts **at once** and the status line says "No usage limit is active, starting now".
- D4 Scan: `.jsonl` files directly in each project folder under `~/.claude/projects` AND subagent files, modified in the last 8 days (weekly limits), newest first; read only the **last 256 KB** of each file (limit entries are recent). Skip a partial first line. Cheap pre-filter: line contains `usage_limit_reached` before parsing JSON.
- D5 Ticking the box disables and clears `StartAtBox` (two start times make no sense). Only while the window runs: the existing note stays.

## Steps

- [x] 1. Core `Setup/LimitReset.cs`: `public static class LimitReset { public static DateTimeOffset? Find(string projectsRoot, DateTimeOffset now); internal static DateTimeOffset? FromLine(string line, DateTimeOffset now); public static DateTimeOffset StartFor(DateTimeOffset resetsAt) => resetsAt + Margin; }`. Why: one testable unit; `FromLine` parses one line (`apiError == "usage_limit_reached"`, `rate_limit_info.status == "rejected"`, `resetsAt` number, seconds → `DateTimeOffset.FromUnixTimeSeconds`, ignore if not after `now`). Use `JsonDocument` with try/catch per line (see `TranscriptJson.cs` for the repo's helpers; use them if they fit). Tail read: open with `TranscriptFile.Open`, seek to `max(0, length-256KB)`, skip to first newline if seek > 0. Done in `src/OpenTail.Bullshark.Core/Setup/LimitReset.cs`. `FromLine` is internal, and Core has no InternalsVisibleTo, so the tests reach it through `Find` on temp files. `Find` reads every candidate and keeps the latest reset, with no early stop (D2 needs the maximum).
- [x] 2. Core: a pure helper in `LimitReset` or `SetupPlan` : `ResolveStartAt(bool waitForLimit, string? typedStartAt..., ...)` is NOT needed — keep it in the App: on Start with the box ticked, `await Task.Run(() => LimitReset.Find(root, now))`, set request `StartAt` to `StartFor(found)` or null (D3), tell the owner in the status line (`SetStartStatus`). Done in `SetupView.Start_Click` (now async). The Start button is off during the scan. The time is set on the request with `with`, after validation.
- [x] 3. App `SetupView.xaml`: CheckBox `WaitForLimitBox`, content "Start when the usage limit resets", tooltip: "The run waits until the reset time of the last usage limit Claude Code reported on this computer (plus 2 minutes). If no limit is active, it starts at once." placed beside `StartAtBox` (SetupView.xaml:268-275). `SetupView.xaml.cs`: Checked/Unchecked handler (disable + clear `StartAtBox`, call `Refresh()`); `CurrentRequest()` must stay synchronous and cheap, so resolve the limit time in `Start_Click` only and pass it via a field or by building the request with `with { StartAt = ... }`. Read how `Start_Click` builds and uses the request before editing. Preview text (if the setup page shows the start time anywhere) should say "when the limit resets" without scanning. Done. Deviations: the `SetupView` constructor takes `projectsRoot` (one call site, `SessionWindow.xaml.cs`, so tests and `--claude-home` scan the right folder). The checkbox sits under the Start-at row, so the footer is one line taller. The D3 text "No usage limit is active" is not shown: the run-written line replaces the status in the same call, so the owner sees only the run line.
- [x] 4. Rail: `SessionWindow.Schedule.cs` already shows "Run xxxx starts at {when}". Leave it, but if cheap, mark limit-based runs? NO: skip (no new field). Keep the row as is. Rail row left as it is. One change outside the listed files: the run-written line in `SessionWindow.StartRunAsync` says "They start at HH:mm" when the run waits. It said "within about 15 seconds", which was wrong for a waiting run (also for a typed start time).
- [x] 5. Tests in `tests/OpenTail.Bullshark.Core.Tests/Setup/LimitResetTests.cs` (look at `DelayedRunTests.cs` and a Search/Activity test for temp-folder style; use the repo's temp-dir helper if one exists). Cases: (a) the real line shape above → correct instant; (b) `status` not `rejected` → null; (c) `resetsAt` in the past → null; (d) not-JSON, empty and truncated lines → null, no throw; (e) `Find` over a temp tree with two files, 5-hour (earlier) and weekly (later) → later wins; (f) only past limits → null; (g) file older than 8 days is ignored; (h) a >256 KB file with the limit entry in the last 256 KB is found and a cut first line does not throw; (i) an unreadable/locked file is skipped and the rest are read; (j) `StartFor` adds exactly 2 minutes; (k) unix-seconds (not ms) check: 1794834600 → 2026-11-16T13:10:00Z. Done: 13 tests in `tests/OpenTail.Bullshark.Core.Tests/Setup/LimitResetTests.cs`. Case (k) is covered by the seconds value plus the refusal of the same number in milliseconds; (j) and (a) cover the 2-minute margin and the 13:10 UTC instant. (i) takes an exclusive lock, so it runs on Windows only and returns early elsewhere.
- [x] 6. Docs: tick/annotate item 5 in `gems-2026-11-17.md` (replace the D5.6 line with a pointer to this plan), add a short line in `docs/plans/session-summary-2026-11-17.md`'s decisions list ("Start when the limit resets: built 2026-11-18, see limit-reset-start-2026-11-18.md"), and note it in README only if the README lists setup options (check first). Done: `gems-2026-11-17.md` item 5 ticked and D5.6 pointed here; the open question in `session-summary-2026-11-17.md` points here. README lists no setup options, so it is unchanged.

## How to check

`dotnet test OpenTail.Bullshark.slnx -nodeReuse:false` from `C:\Git\split\OpenTail.Bullshark` (was 1007 passing before this work; new tests add to it; report exact counts and exit code). The App must build (it is in the solution). Not verifiable here: the checkbox in a running window (say so, do not claim it).

## Open items / not done

- Waking sessions already paused on the limit (native `quota_auto_resume_*`): not in this plan.
- API-key accounts (no `rate_limit_info`): not verified.

## Result (2026-11-18)

- `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false`: passed 1020, failed 0, skipped 0 (1007 before, 13 new). Exit code 0.
- `dotnet build OpenTail.Bullshark.slnx -nodeReuse:false`: 0 errors, 0 warnings. The App alone shows one warning in `MainWindow.xaml.cs` (CS8601, line 23), which is not from this work.
- Not checked: the checkbox, the waiting row and the run line in a running window (D5.7 still open). No real limit was hit in the test, so the instant is checked against the sample line from the owner's transcript only.
