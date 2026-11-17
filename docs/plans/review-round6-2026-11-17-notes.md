# Review round 6: af884a3..682e883 (read-only)

## Findings (most severe first)

1. SupervisorPolicy.cs:172 (WasStoppedOrFailed, lines 132-135) + SupervisorRunner.cs:117-125. The stop guard also reads LastAlert. A "Stopped: ..."/"Failed: ..." LastAlert is only replaced by another alert or cleared by Resume; a listed state of working does not clear it.
   Scenario: session stopped (alert Stopped, LastState stopped); owner revives it by hand, listing shows working, LastState becomes working, LastAlert still "Stopped: ...". Later the daemon loses it: the gone branch returns [] because of LastAlert, so a session that should be resumed never is, and AttentionList (ReasonFor, not listed + Stopped prefix) shows it as Stopped. Confidence: confirmed.
   Fix: clear a Stopped/Failed LastAlert when the observed state is neither (runner, where LastState is updated), or drop the LastAlert test now that LastState is kept (keep it only for records with no LastState).

2. RunBudget.cs ElapsedOf (about lines 70-78). Duration counts from StartAt as soon as it has passed, whether or not any session started.
   Scenario: run scheduled 02:00 with a 4 h limit; window opened 07:00. Pass 1 starts the sessions (runner.Budgets still empty, SessionWindow.xaml.cs:256 sets it after the pass), pass 2 sees 5 h elapsed, so Reached: lost sessions are not resumed and a LimitReached item appears at once. Confidence: confirmed for the arithmetic, plausible for the scenario.
   Fix: origin = earliest StartedAt of the run's session records (else max(CreatedAt, StartAt) while nothing has started), so downtime before the first start is not spent.

3. AttentionList.cs:202-206 (LimitItem) + SupervisorPolicy.cs:97-112. LimitReached item is built from LimitLevel alone: no check that the lead is live, and LimitLevel is only lowered when a budget exists for the session (budget is null when the run has no limit or is no longer in runs.json, so the `else if (wanted < told)` never runs).
   Scenario: run hits its limit and finishes (all done), or the owner removes the limit or deletes the run: the lead keeps LimitLevel=reached, so "LimitReached" stays under Needs you for as long as the record exists, and a limit added later gives no new reached notice. Same permanent-item problem item 7 removed for overlaps. Confidence: confirmed by reading.
   Fix: show LimitItem only while the lead is listed and not done/stopped/failed, and reset LimitLevel to null when the run has no budget.

4. RunBudget.cs ForSessions (lead choice). Lead = first spec whose record has a session id. If an earlier spec gets its id later (failed start retried, adopt), the lead moves to a record with LimitLevel null and the same level is announced again (sound and flash twice for one run). Confidence: plausible.
   Fix: take the max LimitLevel over the run's records as "told".

5. SupervisorPolicy.cs:97-112 with Carry (SupervisorRunner.cs:244-253). A limit alert replaces LastAlert on the lead. If the lead's LastAlert was the RestartLoop alert, it is erased: Needs you loses the RestartLoop row, and once the limit is raised the policy (about line 181, no RestartLoop prefix) emits the loop alert a second time. A Stopped alert on a session that is no longer listed also disappears from Needs you. Confidence: confirmed by reading, impact small.
   Fix: keep limit notices out of LastAlert (LimitLevel already records them).

6. FileOverlap.cs SessionWritesCache.Read, the `replaced` test (about line 195). A transcript rewritten in place with a larger length (compaction or rotation by another tool) has length >= known.Offset and a different length from known.Length, so it is read as an append from the old byte offset: garbage lines, old files stay. Only shorter, or same length with a new stamp, are detected. Confidence: plausible (Claude Code transcripts are append-only, so low).
   Fix: keep a hash of the first bytes (or the first line) and compare before trusting the offset.

7. FileOverlap.cs CompleteLines (about lines 229-259). A final record with no trailing newline is never taken (documented in D9). For a finished session whose last line (a Write tool_use, the last usage) lacks "\n", that record is lost for good; before, ReadFile read it. Confidence: plausible (depends on Claude Code always ending lines with "\n").
   Fix: at EOF, if the pending bytes parse as JSON, take the line but do not advance Offset.

8. SessionWindow.Overlap.cs:38 + FileOverlap.LiveRecords. A session missing from one active listing is "not live", so its overlap notice vanishes for a pass and returns on the next as a new Overlap (overlaps are not remembered like conflicts), sounding again. Confidence: plausible.
   Fix: remember announced overlap keys like _announcedConflicts, or hold the previous clash set when the listing lacks a recorded session.

9. SessionWindow.xaml.cs:256 (known, plan D4.3). Runner.Budgets is empty during the first pass after the window opens, so a run already at its limit gets lost sessions resumed (and unstarted specs started) once before the limit applies. Not changed by round 5. Confidence: confirmed, acknowledged.
   Fix: compute budgets before the first pass from the transcripts.

10. StorageView.cs / SessionWindow.Storage.cs: the Archive button is enabled until the first pass ends (SetArchiveAvailable is only called in the pass's finally). The click handler still refuses (about line 100), so no data risk; the "waits" note is not shown at start. Confidence: confirmed, cosmetic.
    Fix: call SetArchiveAvailable(false) in InitStorage.

## Fixes judged correct (read, no defect found)
- Item 2: Emit moved out of the try, Write catches StoreErrors.IsExpected (TimeoutException included); a failed Start still continues after its own Emit.
- Item 5: EventLog.Write ends a cut-off last line under the same lock; reader skips the garbage line (JsonException) only.
- Item 18: failed rotation keeps appending, logs once; rotated file not deleted.
- Item 12: EscapeArgument matches .NET PasteArguments (empty arg, whitespace-or-quote wrap, backslash doubling before a quote and at the end); Fit loop terminates.
- Item 13: StartTime builds wall clock in the zone with that day's offset; Describe correct (a time in a spring-forward gap lands an hour later, harmless).
- Item 4 core rule: null set refuses all, 24 h window by last write, state-file failure refuses.
- Item 9 offsets: bytes counted at newline only, so UTF-8 split across reads is safe; BOM handled once; CR stripped; shrink and same-length-new-stamp reset; a failed read does not advance Offset and retry is idempotent; firstLine keeps unkeyed usage keys stable.
- Items 6/7: Gained ignores notices for "already waiting"; conflict key = session + sorted files; announced set seeded on the first pass.
- Item 1 main path: LastState stopped/failed kept while not listed (only the stale LastAlert part is wrong, finding 1).
- Item 3 for a future StartAt and StartingNow (moves StartAt to now, only when waiting).
- Item 10: ForRun/Newest/Line fine; events without RunId match by name; old lines read with RunId null.
- Items 11, 15, 16, 17: ClaudeTrust/InstructionFiles/FileStamp distinguish missing from unreadable; Save refuses when the stamp cannot be taken; Changes/Storage catches use StoreErrors.IsExpected.
- Item 14: TextToSave returns the file text when only line breaks differ; lone CR no longer converted (WPF TextBox handling of a lone CR was not checked).
