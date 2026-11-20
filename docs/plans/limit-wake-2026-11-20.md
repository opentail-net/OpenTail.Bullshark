# Sessions that stop at the usage limit: show the wake time, and wake them after the reset (2026-11-20)

Owner's problem: waking a session that stopped because the tokens ran out, after the reset: "sometimes it works, sometimes it does not."
Owner's decisions (chat, 2026-11-20): (1) **show the wake time** on the row and in Needs you ("Limit resets 07:20 · wakes after"), yes;
(2) **for a chosen option**, detect that the session stopped because of the token limit, and **auto-resume it after the limit has passed**.

## Evidence (read from the owner's transcripts, last 8 days, times and entry kinds only; script not kept in the repo)
- 37 limit hits in 13 sessions. After a hit: **21 wakes before `resetsAt` -> the limit again, every time**; **11 wakes at or after `resetsAt`
  (earliest 0.6 min after) -> answered, every time**; 5 hits were never woken. Two sessions were woken every 30 minutes all night and failed until the reset.
- So the fix is timing, not the wake text: **never wake before `resetsAt`**; waking about 1-2 minutes after it worked 9 of 9.
- The supervisor does nothing for a session that is listed as `done` (idle): `SupervisorPolicy.DecideSession` returns `[]` for working/done/busy.
  A session that stopped at the limit is, as far as the listing shows, an idle session. Nothing in the code wakes it. `LimitReset.Find`
  (reset time of the limits in force) is used only by "Start when the usage limit resets" on the setup page and by `MagicNamer`.
- Transcript shape (checked on the owner's transcripts, 2026-11-18, `LimitReset.cs` header): an `assistant` entry with `apiError` =
  `usage_limit_reached` and `apiErrorParams.rate_limit_info` = `{status:"rejected", resetsAt:<unix seconds>, rateLimitType:"five_hour"|"seven_day"|...}`.
  After the hit, only metadata kinds follow until a wake: `system`, `cost-state`, `last-prompt`, `ai-title`, `mode`, `session-owner`, `bridge-session`,
  `artifact-*`, `queue-operation`, `file-history-snapshot`. A wake is a `user` entry (not a tool result).

## Vendor facts (checked with `claude --help`, claude 2.1.329)
- `claude --bg --resume <session-id> [prompt]` "continues that session in the background under the same ID, **or starts a copy and says so when
  the session is already running**". So it is safe only when the session is **not** running. This is why the existing `ResumeInFullAsync` refuses a session that is listed.
- There is **no CLI command that sends text to a running session**. Input goes through `claude attach` (a terminal). Not decided here.
- NOT VERIFIED: what `claude agents --json --all` lists for a background session that stopped at the limit (the owner's current listing has none to look at;
  `abd348a2` is `stopped`, but that one was stopped by hand). The code must therefore decide by a table over every state (below), never by a guess.

## Scope
**Phase 1 (this plan, build now):** detect, show, opt-in flag, and the wake for a session that is not running. For a session that is running (listed
alive) the app only notifies. **Phase 2 (NOT in this plan, owner's live check first):** typing the wake prompt into an attached terminal for a flagged,
listed session. Recorded as an open item; do not build.

## Design (decisions, with why)
1. **Detection is a pure function over a transcript's tail** (`Core/Setup` or `Core/Activity`, coder's choice after reading `LimitEntries` and `SessionDigest`):
   `LimitStop.Find(lines)` returns `LimitStop(HitAt, ResetsAt, Type)` when the **last** `usage_limit_reached` / `rejected` entry has **no `user` entry
   (not a tool result) and no other `assistant` entry after it**; metadata kinds above are ignored. Several limit entries in a row (a retry) are one hit: the last one counts.
   `status` other than `rejected` is not a stop (as in `LimitReset.InForce`). Why: a wake is a `user` entry, so "nothing after the hit" means "still stopped";
   it also covers the owner having woken the session by hand.
2. **The digest carries it**, so no extra file reads: add `LimitStop? LimitStop` to `SessionDigest`, computed in the same pass that builds the digest
   (the index cache is keyed by file time and size). Bump the cache version so old entries are rebuilt. Read `IndexCache`/`TurnFacts` first and say what you changed.
3. **Show it** (the owner's wording): the session's row second line and its Needs-you item say `Limit resets 07:20` (local time, 24 h, with the date when it is not today:
   `Limit resets Thu 07:20`), plus ` · wakes after` when the session's AutoWake flag is on. Once `resetsAt` has passed and it was not woken: `Limit has reset`
   (plus ` · waking now` while a wake is being made). Needs you gets a **notice-only** reason `AttentionReason.UsageLimit`: it never re-alerts
   (`AttentionEscalation` already lists notices that never re-alert; add this one), it clears when a `user` entry follows (the digest no longer has a stop).
   The existing silent toast "a usage limit that reset" stays as it is.
2b. Only sessions the window shows (live rows, and history rows within the 8-day window) get this; a session with a stop older than 8 days shows nothing.
3b. **The flag** `AutoWake` (bool) in `LibraryEntry` (library.json, keyed by session id, like tags): `IsEmpty` must count it; reads of old files default to false.
   Toggle in the row menus (history and live): a checkable item **"Wake after the limit resets"**, tooltip: "When this session has stopped at the usage limit and
   the limit has reset, Bullshark starts it again with the line: 'The usage limit has reset. Please continue where you left off.' Once per limit. Never for a session you stopped."
4. **The wake rule** is a pure Core function `LimitWake.Decide(...)` with a table, run once per pass for flagged sessions only:

   | Condition (all must hold) | Result |
   |---|---|
   | AutoWake is off, or no stop detected | nothing |
   | `now < ResetsAt + 2 min` (`LimitReset.StartFor`), or `LimitReset.Find(projectsRoot, now)` is not null (any limit still in force, e.g. the weekly one) | wait (row says "wakes after") |
   | this hit was already woken (the library entry holds `AutoWokenHitAt == HitAt`) | nothing |
   | 3 wakes of this session in the last 24 h | one notice "woken 3 times in a day; not waking again", then nothing |
   | listed `stopped` or `failed` (the owner stopped it, or it failed) | **no wake**, notice only |
   | listed alive (`working`, `blocked`, `busy`, `done`, stale) | **no wake**; one toast/notice "Limit reset: <name> can continue. Open it." (phase 2 may type into it) |
   | not listed at all, and the folder is trusted and exists | **wake**: the same start as `ResumeInFullAsync` (`StartBackgroundAsync` with `ResumeSessionId` and the fixed prompt as `Prompt`) |
   | not listed, folder missing or not trusted | notice with the reason, no wake |

   After a wake the library entry records `AutoWokenHitAt = HitAt` and a wake time. A **new** limit hit (a different `HitAt`) makes the session eligible again.
   Why this table: `--bg --resume` on a running session starts a **copy** (vendor fact above); a session the owner stopped must stay stopped; the
   21/21 early-wake failures say never before the reset; and the unknown listing state is handled by "do nothing, tell the owner" rather than a guess.
5. **The wake reuses `ResumeInFullAsync`'s guards** (fresh listing, "already open in the rail", `FolderCheck`, name from `RunNames`, record plus kept-alive run) as one
   shared method; do not copy the code. It must not run while `RespawnHold` is on, and not while a resume is already in progress (`_resuming`).
6. **Quiet hours do not stop a wake** (nights are when it is wanted); they still silence toasts as they do now. The wake is logged as one line
   (session short id, hit time, reset time, method), never any transcript text.
7. **Principle 3 (record):** the owner's per-session opt-in is their standing instruction for that session, and the app sends only the fixed line above, once per limit hit.
   It never sends to a session the owner stopped, never to a running session in this phase, and never before the reset.

## Contracts this must satisfy
- `SessionDigest` / `IndexCache`: existing callers and tests keep working; the new field defaults to null; the cache version bump rebuilds old entries.
- `LibraryEntry` stays a record with defaults; `SessionLibrary` reads old library.json files (missing keys = defaults); `IsEmpty` counts the new fields so an entry with only AutoWake is not dropped.
- `AttentionList`: a new reason must not change the order or text of existing reasons; notices never re-alert; the item clears on its own.
- `LimitReset.Find` is the only source of "a limit is in force" (it reads the 8-day tail and accounts for both the 5-hour and the weekly limit).
- Existing behaviour that must not change: a supervised run's restart rules (`SupervisorPolicy`), budget limits (`RunBudget`), "Start when the usage limit resets", the magic-name limit guard.

## Steps
- [ ] Read first: `Core/Setup/LimitReset.cs`, `LimitEntries.cs`, `Core/Library/SessionLibrary.cs` (+ the digest builder: grep `SessionDigest`, `IndexCache`, `TurnFacts`), `Core/Supervisor/AttentionList.cs` and `AttentionEscalation.cs`, `App/SessionWindow.Resume.cs` (`ResumeInFullAsync`), `App/SessionWindow.Attention.cs` (the limit-reset toast), the row text code in `App/SessionWindow.Rail.cs`, the row menus (grep `Magic rename` for where the history/live menus are built).
- [ ] Core: `LimitStop` record + `Find(lines)`; tests with **synthetic** JSON lines for: stop with only metadata after; woken (user entry) -> none; tool-result `user` entry -> still stopped; non-limit assistant after -> none; two limit entries in a row -> last; `status` not rejected -> none; garbage lines skipped; `seven_day` type kept.
- [ ] Core: add `LimitStop` to `SessionDigest`, computed in the digest pass; bump the index cache version; test that a digest built from a transcript with a stop carries it.
- [ ] Core: `LibraryEntry.AutoWake`, `AutoWokenHitAt`, `AutoWakeTimes` (the wake times of the last 24 h, for the 3-a-day cap); library tests (old file reads; IsEmpty; round trip).
- [ ] Core: `LimitWake.Decide` with the table above, pure (inputs: stop, flag, entry, listing state, `LimitReset.Find` result, folder status, now). One test per table row, plus: a new `HitAt` re-arms; clock earlier than HitAt does nothing; exactly `ResetsAt + 2 min` wakes, one second before waits.
- [ ] Core: `LimitWakeText`: the row text and the Needs-you detail (`Limit resets 07:20`, `... · wakes after`, `Limit has reset`, with the day when not today); tests for today / tomorrow / past.
- [ ] Core: `AttentionReason.UsageLimit` (notice, never re-alerts, clears with the stop). Tests.
- [ ] App: show the text on the row and in Needs you; the "Wake after the limit resets" checkable menu item (history and live rows) writing `AutoWake` through `SessionLibrary.Update`.
- [ ] App: once per pass (after the listing is read, not while `RespawnHold` or `_resuming`): run `LimitWake.Decide` for flagged sessions and carry out the result: wake via the shared start method; notices via the existing notice path. Record `AutoWokenHitAt` and the wake time only **after** the start has returned an id (recording earlier would hide a retry after a failed start); on failure, log it, hold retries for 5 minutes, and do not mark the hit as woken.
- [ ] App.Tests: the menu item toggles the flag; a row with a stop shows the text (headless, existing helpers). **No test may start a claude session, press anything on the real clipboard, or touch the owner's library.json: use scratch state folders under a unique `%TEMP%\bullshark-limit-wake-<guid>`.**
- [ ] Build App (use `-p:OutDir=<scratch>\` if the owner's running app locks the normal bin: do not stop it) and run the full Core.Tests and App.Tests; report counts and exit codes.
- [ ] Add live items to `docs/plans/live-run-checklist.md` ("Limit wake"): LW1 a flagged session that stops at a real limit shows `Limit resets HH:mm · wakes after`; LW2 after the reset it is started once with the line; LW3 a session the owner stopped is not woken; LW4 **what `claude agents --json --all` lists for a session stopped at the limit (record it here: this decides phase 2)**; LW5 a second hit re-arms it.

## Rules for the coder (carry these)
- DELETION RULE: delete only what you created in this task, with a unique self-created name (include a guid). List it and read any note file in a SEPARATE step before deleting; never chain a listing and a delete; never `rm -rf` a fixed short name in a shared folder. Do not touch `%TEMP%\bullshark-*` folders you did not create this run.
- Never start a claude session, never run `claude respawn`, never kill a process you did not start. Tests must not resume the owner's sessions (the app refuses supervision under a test host; do not weaken that). Do not read the owner's real transcripts in tests: synthetic fixtures only.
- Do NOT commit or push. Work in the working tree on `main`; the orchestrator reviews the diff.
- Never log transcript text; log ids, times and the method only.
- Report: changed files; tests with counts and exit codes; what is not done; anything you decided differently from the plan (say why); every place you were unsure.

## Open items (owner)
- Phase 2: typing the wake line into an attached terminal for a flagged session that is listed alive. Needs LW4 first, and the owner's word on principle 3 for that case.
- Interactive sessions (like a plain `claude` in a terminal) cannot be woken by the app; they only show the text and notify.
- The wake line is fixed; making it editable is not planned.

## Review notes (orchestrator, 2026-11-20, after the coder's phase 1)
Verified: diff read (Core `LimitStop`, `LimitWake`, the App wake file, the shared `StartFullResumeAsync` refactor); Core 2175 and App.Tests 29 pass (built to a scratch OutDir); claude.exe pids unchanged before and after.
- **What phase 1 really covers.** The wake starts only a session that is **not listed at all** (the listing is taken with `--all`). A session listed as `done` cannot be told
  apart from one that is idle but alive, so it gets the notice "running: open it to continue" and is **not** started (a `--resume` on a running session makes a copy).
  If a background session that stopped at the limit is listed as `done` (likely, but NOT verified: LW4), the auto-wake will not fire for it. That case is what phase 2 is for.
- **Coder decision 2 (kept in the rail), accepted for now:** a wake makes a record, so a later hit of the same session is "kept in the rail: not woken" (LW5 corrected).
  Open: should a kept session whose process is gone be woken by reusing its record? Not decided; do not build without the owner.
- **Open and important (not changed, outside this plan):** `SupervisorPolicy.DecideLost` resumes a lost kept session after at most a 1-2 minute gap, with no
  knowledge of the limit. If a kept session's process exits at the limit, the supervisor would resume it **before the reset** (three tries an hour, then the restart-loop alert),
  which is the 21-of-21 failure pattern. A fix would hold `DecideLost` while the session has a stop in force. Needs the owner's word because it changes supervisor behaviour.
- Coder decisions accepted: stop is read from the 256 KB tail on each supervisor pass for flagged and supervised sessions (the digest alone is stale between history reads);
  the model and effort for a wake are the saved solo planner's; a limit entry with no readable time is not a stop; quiet hours only log the "running" toast; the first-run cache
  format bump rebuilds every transcript index once.
- Gaps: no headless test of the row text composition (the text itself is tested in Core); the live items LW1-LW6 are the owner's.
