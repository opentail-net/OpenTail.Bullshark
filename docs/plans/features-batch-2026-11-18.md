# Features batch (2026-11-18): viewed marks, big-deletion check, same-call loop warning, group by tag

Owner: list 3 "in order" (the small features first). From `ideas-desktop-codex-2026-11-18.md` (B3) and `ideas-2026-11-18.md`-style safety ideas (researcher-reported;
the code facts below were read by the orchestrator). **Queued** after `fixes-batch-2026-11-18.md` is reviewed and committed (shared files: ChangesView, rail).
Left out on purpose and why: **subagent runs under a session** (the History already lists subagent transcripts as rows marked "· subagent", last in each folder, so most of it exists);
**hook event feed** (needs the owner to add hook entries); **second-opinion review** (uses the owner's usage; needs their OK); **`auto` mode / keep awake / worktree gates** (owner decisions).

## Contracts (read in the code 2026-11-18)
- Changes tab: `ChangesView` (App), `Show(GitChangesReport report, writtenFiles, writtenNote, shared)`; `GitChanges` (Core/Git) gives `ChangedFile` records with `Path`, `Added`, `Deleted`, `Binary`,
  status; `ParseNumstat` returns `NumstatCount(Added?, Deleted?, Binary)`. It reads **uncommitted** work only (a commit clears it). Git calls are read-only and time-limited; no new git call is needed.
  The "Copy review prompt" button (B1) names the changed files; do not change its text rules.
- UI state files: `ui.json` (`UiFile.Read/Set`, locked, damaged file moved aside), `library.json` (`SessionLibrary.Update`). A "viewed" mark is per session and file and must survive a restart:
  keep it in `ui.json` under one key (e.g. `viewedFiles`: { sessionId: { path: "<length>:<last-write-ticks>" } }) or a small file beside the state file; never in the repo being reviewed.
- Effort panel: `EffortReport.From(lines)` (Core/Activity) already counts failed calls per tool (`FailedByTool`), re-read files, repeated Bash commands, turns with no output; shown in the transcript view's effort panel.
  `EffortReport` is a record built from a transcript; its tests exist. New fields must default so old callers and the IndexCache (round-11 rule: a new cached field must be carried by the cache or not cached) stay valid.
- Rail: `RailFilter`, `_filter`, `_tagFilter` (`ActiveTagFilter`), `ShownHistoryFolders()` (the single list of folders the History section draws and the magic rename reads), `HistoryFolderItem`, `HistoryOrder.Sorted`,
  `MaxHistoryPerFolder`, `InFolders`, `InPinnedGroup`, `IsShownInHistory`, `TagIndex.Counts/Has` (Core/Library). Needs-you is never filtered.
- Principle 3: nothing is sent to a session. Privacy: the file mark stores paths and numbers only; no file text.

## Steps (in order; Core first with tests, then App)
- [x] G1. **Viewed marks in Changes.** Core `Library/ViewedFiles.cs` (pure): `Stamp(length, lastWriteTicks)`, `IsViewed(marks, sessionId, path, currentStamp)` (true only when the stored stamp equals the current one, so a file that changed
  is unviewed again), `Mark`, `Unmark`, `MarkAll`, `Prune` (drop sessions' entries older than 90 days or for files no longer changed, cap 500 files per session). Tests for each incl. the changed-file case and the cap.
  App: a check box or a small "viewed" toggle per file row in `ChangesView` (checked rows muted, count "3 of 7 viewed" next to "Changed: 7"), "Mark all viewed" and "Clear" buttons (GhostButton style), state read when the report is shown
  and written when toggled through `UiFile.Set` (read-modify-write the one key; report a write failure on the status line). The file stamp is the file's size and write time from the report's folder (a deleted file has stamp "deleted").
  Nothing is written to git. The Ready-for-review row's tooltip may say "3 of 7 viewed" when that is cheap to read (optional; skip if it needs a git call).
- [x] G2. **Big-deletion check.** Core `Git/DeletionCheck.cs` (pure): `DeletionCheck.Flag(ChangedFile)` returns a reason or null: a tracked file **deleted** ("deleted"), or `Deleted >= 50 && Deleted >= 3 * Added` ("mostly deleted: -N +M"). Thresholds are constants in one
  place, documented as the orchestrator's assumption (generated files and deliberate removals will trigger it: the label says "check", not "wrong"). Binary and unknown counts never flag. Tests: boundaries (49/50 lines, 3x exactly), deleted file, binary, null counts, renamed file.
  App: flagged rows in `ChangesView` get a ⚠-free text label "check: mostly deleted (-N +M)" in the warning colour of the palette (no emoji, minimalism), a count line "2 files lost most of their lines" under the header, tooltip with the rule. No sound, no toast, not in Needs-you.
- [x] G3. **Same-call loop warning.** Core `Activity/EffortReport.cs`: new list `FailingLoops` (default empty): the same tool name with the same input (compare a stable hash of the input JSON, never stored or logged) failing 3 or more times **in a row for that
  call signature without a success in between** (a fail, edit, pass sequence is not a loop). Each item: tool, count, first and last line number (reuse the existing item type if it fits). Tests: 3 consecutive fails flagged, fail-fail-success-fail not, different inputs not, interleaved other tools still flagged
  (consecutive for that signature), non-error results ignored, a very large input hashed without keeping it, garbage lines skipped. If `EffortReport` is cached in `IndexCache`, carry the new field (and bump nothing else) or keep it out of the cache and compute on view; say which.
  App: the transcript view's effort panel shows "Same call failed N times in a row: Bash (lines 120-188)" with the existing line-jump behaviour for other effort items. Optionally the rail tooltip of a live row; skip if it needs a new read per row.
- [x] G4. **Group by tag.** Core `Library/TagGrouping.cs` (pure): `Groups(sessions, marksLookup)` -> ordered groups: each tag in use (by count desc, then name) with its sessions newest first, then "No tag" last; a session with several tags appears in each of its groups
  (stated in the tooltip); archived sessions follow the existing rules (not listed unless the Archived filter). Tests: ordering, multi-tag session, untagged, empty. App: the rail's show menu gets a second section "Group by: Folder / Tag" (checkable pair, in memory like the tag filter; Settings is not needed) that changes how `ShownHistoryFolders()` /
  the History section draw: tag groups instead of folders. The Pinned group, search filter, tag filter and Needs-you behave as now; the magic rename's "visible list" reads the same single list (update `ColdSessions` if `ShownHistoryFolders()` shape changes: keep one source of truth). Group headers show "#tag (N)". Collapsed state per group is remembered like folders.
- [x] G5. Docs: README one bullet each; `docs/plans/live-run-checklist.md` items for G1-G4 (viewed marks survive a restart and clear on change; a deleted file flagged; a loop flagged in a real transcript; group-by-tag with a multi-tag session); tick this plan.

## Checks
Core tests with counts; App build; whole suite `dotnet test OpenTail.Bullshark.slnx -nodeReuse:false`. Screenshots with `scripts/Test-NewScreens.ps1 -Exe src/OpenTail.Bullshark.App/bin/Debug/net10.0-windows10.0.19041.0/OpenTail.Bullshark.App.exe`
(check free RAM and no Bullshark window first): extend the fixture so Changes shows a deleted-heavy file and some viewed marks, the effort screen shows a loop (add repeated failing calls to the fixture transcript), and the rail shows Group by tag with a multi-tag session; dark, and light for Changes. View every picture.

## Orchestrator review
Diff against Contracts: the viewed mark clears when a file changes; nothing written to git or the reviewed repo; flags are labels not alarms; the loop rule is consecutive-for-that-signature; the cache rule for new fields; ShownHistoryFolders still the single list; Needs-you never filtered; no text logged.

## Open / not decided
- Deletion thresholds (50 lines, 3x) are assumptions (in `DeletionCheck`, one place).
- A session with several tags appears under each tag in Group by tag.
- Group by tag is in memory only.
- Assumption: a tracked file that is deleted is flagged even when it is binary (the count rule skips binary files; a deletion has no counts to skip).
- Assumption: a renamed file is judged by its counts like any other file (a rename with most lines removed is flagged).
- Assumption: a deleted file is one that is not on disk (`File.Exists` false); its tick stamp is `deleted`.
- Assumption: the warning colour is the setup check's warning colour (`RailPalette.Warn`); the palette had none.
- Assumption: the loop rule counts a failure only when the result carries is_error; a success of the same call ends the run; a second record of the same result is ignored.
- Assumption: the effort report is not cached in IndexCache (only the transcript summary and messages are), so the loop list is computed on view and the cache rule does not apply.
- Open: the viewed ticks are one key in ui.json. Two windows ticking at the same moment can lose one window's ticks (each save replaces the whole key); the file lock covers one write, not the read-then-write pair.
- Open: the Ready-for-review tooltip "3 of 7 viewed" is not done (optional in G1; it needs a git read per row).
- Open: `--rail-group tag` is a test flag only (for the screenshot), like `--rail-tag`.
