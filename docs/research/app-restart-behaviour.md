# App restart behaviour (from the code, 2026-11-21)

Read-only research by a researcher, against the code at `557402b`. Nothing was run. The later text fix in `27e1d84` kept the line count of the two files it touched (`SessionWindow.Codex.cs`, `Capabilities.cs`), so the line numbers still hold. Paths are shortened: `Core/` is `src/OpenTail.Bullshark.Core/` and `App/` is `src/OpenTail.Bullshark.App/`. Re-check a line before relying on it. Items that need the live CLI are marked **UNKNOWN** and listed at the end as owner tests.

## 1. Window restart (the App closed, reopened, or crashed)

**Saved (state files):**
- `state.json`: supervisor records. A pass writes only what it changed (`Core/Supervisor/SupervisorRunner.cs:236-255`).
- `runs.json`: runs (`App/SessionWindow.xaml.cs:192`).
- `tabs.json`: tab order and right pane (`App/SessionWindow.Split.cs:209-215`).
- `library.json`: owner marks and wake flags (`Core/Library/SessionLibrary.cs:72`).
- `ui.json`: settings, including the chosen provider.
- `attention.json`: only while its switch is on; deleted at a clean close (`App/SessionWindow.xaml.cs:558-564`).
- `heartbeat.json` (`App/SessionWindow.Night.cs:76-89`).

**Restored on start:**
- One tab per recorded session (`App/SessionWindow.Tabs.cs:439-454`).
- Tab order and the right pane from `tabs.json` (`App/SessionWindow.TabDrag.cs:32-37`; the right pane is applied on first open, `Tabs.cs:451-452`).

**Not restored:**
- Codex tabs (`App/ProviderSupport.cs:24-27`). The provider choice is restored (`ProviderSupport.cs:11-22`; `xaml.cs:184-185`, only if `CodexProvider.Available`).
- The selected row: the first session is chosen (`App/SessionWindow.Rail.cs:727-765`).
- Folder expansion and the rail filter: memory only (`Rail.cs:96, 104`).
- Escalation (`App/SessionWindow.Attention.cs:40`).
- Wake holds (`App/SessionWindow.LimitWake.cs:57, 63`).
- A plain tab close is memory only (`Tabs.cs:199`), so the tab comes back after a restart.

**First pass:**
- Runs at load (`SessionWindow.xaml.cs:243`) and every 15 seconds (`:99`, `:252`).
- It takes the single-instance name, or only watches (`:194-199`). An abandoned mutex after a crash is taken (`Core/Supervisor/SingleInstance.cs:48-52`).
- It reads daemon status, two listings, state, runs and trust (`:604-644`).
- Untrusted roots get a Trust step, and their runs are not run (`:434`; `Core/Supervisor/PassSnapshot.cs:26-34`). An unreadable trust file runs nothing (`:446`).
- Lost sessions in a due run are resumed (see section 2).

**Differs:** a second window only watches (`:196-197`) until the first one closes (`:535-544`).

## 2. Supervised restart policy

Source: `Core/Supervisor/SupervisorPolicy.cs` (pure). Carried out by `SupervisorRunner.cs:90-180`.

- **Lost** = not in the listing, or listed as stale working (`:192, :199-205`). A lost session resumes under the same id, at most **3 restarts per hour** (`:77, :268-278`), with a 1-minute gap after the first and 2 minutes after the second (`:84, :300-306`). A failed resume counts as a restart (`SupervisorRunner.cs:352-357`).
- **A failed start** keeps a record with no id (`SupervisorRunner.cs:348`). It is not retried until the owner presses Retry (`App/SessionWindow.xaml.cs:974-992`).
- **Stopped or failed:** alert once, never resumed, even when unlisted (`:151-156, :219-229`).
- **Blocked:** alert once, never answered (`:209-215`).
- **Stale-blocked:** alert, and never resumed while it is listed as stale (`:199-204`). The 1-hour hold (`:327`) applies only once it is unlisted.
- **A live session with the same name** gives NameTaken (`:285-291`).
- **Other states** are left alone (`:231`).
- **UNKNOWN:** a `done` session that drops out of `claude agents --all` would be resumed, because `DecideLost` has no `done` guard (`:245-293`). Whether the CLI prunes done sessions is UNKNOWN. This is the most important open point in this document (live test 3).

## 3. Respawn and RespawnHold

- Only a confirmed click runs `claude respawn --all`, with a 5-minute timeout (`App/SessionWindow.xaml.cs:940-971`; `Core/Claude/ClaudeCli.cs:122-123`).
- The hold runs from before the command until 20 seconds after it (`xaml.cs:950, :967`; `Core/Supervisor/RespawnHold.cs:14, 24-54`).
- While held, starts, resumes and the stale-blocked alert are dropped (`SupervisorRunner.cs:122, :186-188`), and the limit wake waits (`App/SessionWindow.LimitWake.cs:292`). Other alerts still fire.
- The button shows only in a supervising window with version drift (`xaml.cs:425-427`). The hold is memory only.
- **UNKNOWN:** whether respawn restarts a stopped session. `RespawnHold.cs:58-59` says it is not checked.

## 4. Daemon

- The App never starts or stops the daemon. It runs read-only `claude daemon status` twice per pass (`xaml.cs:609, :612`; `ClaudeCli.cs:88-100`).
- The only session stop is `claude stop` on an explicit Close Session (`App/SessionWindow.Tabs.cs:321-323`). `--bg` launches may also bring the daemon up (`ClaudeCli.cs:179-189`; comment at `SupervisorRunner.cs:43`).
- When the daemon is not running on both reads, listed working or blocked rows become stale (`Core/Claude/DaemonStatus.cs:111-117, :73-77`). A timeout gives Unknown, and the listing is used as it is (`ClaudeCli.cs:93`).
- Stale rows show "not running (daemon idle)" (`Tabs.cs:66`), a status note (`DaemonStatus.cs:96-99`), and the Needs-you "daemon" row, only when such rows exist (`App/SessionWindow.Attention.cs:76`; `DaemonStatus.cs:90-93`).
- No pass is stopped by a daemon that is down (`SupervisorRunner.cs:42-46`).
- **UNKNOWN:** whether `agents` or `daemon status` starts the daemon, and whether it exits when idle. `DaemonStatus.cs:24-25` quotes help text only. The Claude docs say it idle-exits on macOS and Linux, and Windows is not stated (see `claude-docs-index.md`, section 2).

## 5. Limit wake and holds

- Holds are rebuilt on every pass from transcripts and are not saved. A stop up to 8 days old (`Core/Setup/LimitStop.cs:58`; `LimitEntries.cs:22`) with its reset still ahead holds lost resumes until the reset (`App/SessionWindow.LimitWake.cs:414-430`; `SupervisorPolicy.cs:263`). An unreadable transcript holds for 15 minutes (`LimitWake.cs:47, :426-427`). Holds are read before decisions (`xaml.cs:450-451`), so a restart keeps them.
- After the reset, a lost supervised session can resume at once, with no margin (`SupervisorPolicy.cs:263`).
- A flagged session wakes at reset + 2 minutes (`Core/Setup/LimitReset.cs:13, :36`; `Core/LimitWake.cs:94`), at most 3 a day (`:74`). A session the rail keeps is only noted, not woken (`Core/LimitWake.cs:104-105`; `App/LimitWake.cs:146`).
- **Risk:** woken hits persist in `library.json` (`Core/Library/SessionLibrary.cs:16-17`). The retry hold and the in-memory list do not (`App/LimitWake.cs:25, :57, :63`). So a restart after a failed library write could repeat a wake.
- **UNKNOWN:** whether `--resume` on a listed session makes a copy. The code comment (`Core/LimitWake.cs:57`) cites help text that was not checked. The Claude docs say `--resume <id> --bg` "continues that session under its own ID, or starts a copy under a new ID" (v2.1.257), which does not settle it.

## 6. Codex

- No supervisor. `ClaudeSupervision` is false (`App/SessionWindow.Codex.cs:45`).
- Codex tabs are window-owned terminals running `codex resume` or `codex -C` (`Codex.cs:283-306, :331`). They have no record, no restart and no alert.
- Restored: the provider choice, history from `CODEX_HOME` or `~\.codex` on start (`Core/Providers/ProviderPaths.cs:26-27`; `Codex.cs:242, :253`), and Codex marks in `library.json` (`App/SessionWindow.Library.cs:27-28`).
- Not restored: Codex tabs (`ProviderSupport.cs:24-27`; `App/SessionWindow.Split.cs:209-214`).
- **Important:** the Claude pass still runs while Codex is shown. The pass has no provider check (`xaml.cs:446`). Only the Claude rows are hidden. So Claude supervision can act while the user is looking at Codex. Recorded as an open point for the owner.
- **UNKNOWN:** whether a Codex process survives a window close (`xaml.cs:733-737` `DetachAll`; the ConPTY disposal behaviour was not read), and whether `codex resume` restores the chat (only the command line is built).

## Owner live tests (cannot be run by the research)

1. Close the window during a working run, then run `claude agents --json`. The session should still show working.
2. With no background sessions, run `claude daemon status`, wait for the idle exit, and run it again. Note whether the daemon is up or down. (Windows idle-exit is not documented.)
3. Let a finished session age. Check whether it leaves `claude agents --all`, and whether the App resumes it (`events.jsonl`). This settles the `done` question in section 2.
4. Kill the window process during a working run and restart it. Confirm there is no duplicate session, and that `attention.json` stays until the next pass.
5. Run `claude --bg --resume <id>` on a listed idle session. Count the listed sessions before and after.
6. Open a Codex tab, close the window, and check whether the Codex process ends. Reopen it from the rail.
7. Run a respawn with a stopped session listed, and check whether that session restarts.

## Related

- `docs/research/claude-docs-index.md`: the Claude documentation behind these claims.
- `docs/research/rail-feature-inventory.md`: the rail feature inventory (Claude and Codex).
- `docs/research/codex-evidence/README.md`: the Codex and Claude probe results.
