# Concept notes: session lookup, index freshness, usage totals (2026-11-22)

Provenance: written by a reader subagent in the clean-room process (decided by the owner, plan entry dated 2026-11-22). The reader read two outside sources and wrote these notes without copying code. The notes use our own terms and contain no names from those sources. The orchestrator checked them for source names and procedure-level detail before saving. Implementation is ours, and it starts from these notes only.

## A. Finding one session's file quickly

**Goal and why.** Resolve a session identifier to its file in a large archive of dated folders without a full walk per lookup, and without repeating failed searches. Narrowing by time turns a scan of thousands of files into a few folder listings.

- **Time-bearing identifier.** Read a start time from an identifier only where real files show it embeds one.
- **Candidate folders.** Map that time to its dated folder under the writer's convention. Add the neighbouring day near midnight, or where local and archive days may differ. Identifiers without time use a bounded, background-only walk.
- **Three outcomes:** found, proven missing, unknown. Only a complete walk proves absence. An unreadable folder, an expired deadline or a skipped link gives unknown, never a verdict that the session is gone.
- **Saved path as the cheapest check.** Try it before any search. Reject it if it lies outside the archive root, contains parent-directory steps, or its name does not match the identifier.
- **Identity check.** A file counts only if its metadata names the same identifier and working folder. A short prefix must match exactly one session.
- **Negative memory.** Keep the failed identifier, working folder and a per-folder change stamp taken before listing. Reuse it only while stamps are unchanged and the entry is recent. Clear it when the pane's session assignment changes.
- **Backoff** for pending lookups: short first delay, a cap, reset on success. Watcher events can wake a lookup early, but they can be dropped, so keep a slow fallback poll.

**Options.** Memory window (seconds to minutes); backoff start and cap; fallback walk on or off, and its time budget; folder depth limit.

**Fit.** Codex session files are the main target, so the time shortcut helps if our Codex identifiers carry time. Claude Code keeps one archive folder per project, so a saved-path check followed by a per-project lookup fits. Run all lookups off the UI thread; the UI shows a cached result.

**Risks.** Folder days may follow UTC or local time, so verify before relying on neighbours. A relocated archive (settings override the home location) makes a miss mean the file is elsewhere. Some filesystems do not update folder times on file creation, so stamps can miss new files. Link loops need a guard.

## B. Keeping a large session list fresh cheaply

**Goal and why.** Keep an in-memory list of many sessions current by re-reading only what changed. The check must cost metadata reads only, and a saved index lets the list show at once on launch.

- **Folder change stamp.** A cheap summary per folder: file count and newest modified time, from metadata alone. A folder is stale when its stamp differs from the saved one.
- **Granularity.** Stamp per agent, per project or per dated folder. Finer stamps mean fewer re-reads but more metadata calls; match the archive's own partitions.
- **Blind spots.** Count and newest time together miss a file replaced by an older-dated copy, or an edit that sets an older time. Add size or per-file stamps where that matters.
- **Scoped rescan.** Replace only entries from stale folders, and carry user-owned flags (hidden, renamed) across.
- **Growing text files.** Keep length and modified time per file. If a file only grew, read from the saved offset; ignore a last line that lacks its line ending.
- **Coalescing.** One refresh at a time; requests during a run queue one follow-up.
- **Atomic saved index.** Write a uniquely named temp file, flush it, then rename over the target. On Windows, retry the rename briefly while another program holds the file. On failure, keep the old index and remove the temp file.
- **Commit order.** Save a stamp only together with the data it describes. Take stamps before reading, so a write during the read is caught next time.
- **Failure policy.** Keep the last good list on read errors; report a failed save rather than dropping it silently.

**Options.** Refresh trigger (on open, timer, file watcher, or a mix); debounce interval; stamp strength; scan depth; keep or drop entries of vanished folders.

**Fit.** One in-memory list for Codex and Claude Code sessions, with its saved index under the app data folder. Dated folders suit Codex; project folders suit Claude Code. The UI reads the list synchronously; refresh runs in the background.

**Risks.** Coarse modified-time resolution on some filesystems; clock changes; network drives; antivirus or editor locks; two app instances writing one index (needs a single-writer rule or a lock); stat cost grows with folder count in very large trees.

## C. Usage totals without double counting

**Goal and why.** Build correct token and request totals per agent, model and day from session files. Some agents record running totals and others per-message amounts. Totals are right only if each session counts once and repeated scans do not inflate them.

- **Running total.** A cumulative figure repeated through a session. The last valid one is the session total; summing them all multiplies the count. Skip events that carry no figure.
- **Per-message amounts.** Sum them, but deduplicate by message identity, since a message can be written twice, first partial and then final. Keep the copy with final numbers, not simply the first one seen.
- **Cross-file repeats.** Resumed or branched sessions may repeat earlier messages in a new file, so deduplicate across files, not only within one.
- **Replace, do not append.** Rebuild an agent's records on each refresh. Appending deltas on every refresh double counts.
- **One source per agent.** If an agent also reports through a proxy or telemetry feed, exclude it from one of the two.
- **Field meaning.** Check whether cached input sits inside the input count, whether a provided total already includes its parts, and which buckets (reasoning, tool) count. Never add a total to its parts.
- **Attribution.** Choose session start, last update, or event time. Deduplicate before the time-window filter, so an out-of-window copy cannot hide an in-window one.
- **Model changes.** Decide between one model per session and splitting by model.

**Options.** Attribution time; which duplicate wins; include reasoning and tool buckets; session or message granularity; cost for unknown models as blank or zero.

**Fit.** Codex gets one record per session file, taken from its last running total. Claude Code and other per-message agents get deduplicated messages. The usage cache is keyed per agent and replaced whole when that agent's files change.

**Risks.** A running total that restarts on resume (verify with real files); a partly written last line during an active session; dropping zero-usage records can hide activity; if cached tokens sit inside input, totals and hit rates double count; a zero-count first copy can block the real one; unknown prices make totals unknown.
