# Review notes: provider seam stage A (50e1031..71d33f9), reviewer Sonnet

Findings, most severe first. No Must fix found.

1. Should fix (before C) - App SessionWindow.xaml.cs KeyOf (line ~42) and every KeyOf(tab.Key) call (Tabs.cs, Split.cs, Rail.cs, PopOut.cs):
   KeyOf builds the key from the window's active provider, not from the tab's own provider. OpenTab.Provider (A4) exists so "an open tab stays with its own provider",
   but nothing reads it. Identical for Claude today; once C adds a provider switch, a Claude tab looked up after a switch to Codex gets `codex:<id>` and misses its terminal,
   hidden-set and right-pane entries. Suggest: add KeyOf(OpenTab tab) => new(tab.Provider, tab.Key) and use it wherever a tab is at hand; keep KeyOf(string) only where no tab exists.
2. Should fix - Core/Library/SessionLibrary.cs Read, migration branch (lines ~205-210): the read-only path Load/LoadAll now writes (backup + Write). If the folder is read-only or the disk is full,
   File.Copy/WriteAtomic throw IOException/UnauthorizedAccessException from a "read". The App catches StoreErrors.IsExpected in SessionWindow.Library.cs:50 so the rail shows without marks (acceptable),
   but then the migration is retried (and a backup attempted) on every load. Failure is safe (see 3e). Suggest: a test that a read-only library does not lose entries, and optionally log once.
3. Note - SessionKey.Parse/HasProviderPrefix (SessionKey.cs): a bare id that contains a colon and starts with lower-case letters/digits/hyphens before it (for example `abc:def`) is taken for provider `abc`.
   Claude ids are UUIDs (no colon), so no real input today; empty string parses as bare Claude id "" (same as before); `claude:` gives an empty id key (harmless). UUIDs are never mistaken (hex plus hyphens, no colon).
4. Note - SessionLibrary.Read: if a file holds both `X` and `claude:X`, the later pair in file order silently wins after migration. Only reachable by hand edit; the backup holds both.
5. Note - ClaudeProvider.ReadPageAsync: wildcards and separators refused; `..` as id only searches for `...jsonl`, harmless. FindTranscript sweeps all subfolders (existing behaviour). Line bounds clamp to 0. No finding.

Area results:
- 1 Pure refactor for Claude: no finding beyond item 1. Rail build uses ActiveClaude.BuildIndex with the same root and cache; ProviderPaths uses options.ProjectsRoot/ArchiveRoot, same values as before. Tabs, hidden set, terminals, right pane keyed with the same ids under `claude`.
- 2 SessionKey: see item 3.
- 3 Library migration: (a) migration is in Read, called only from LoadAll/UpdateAll, both under FileLock.Hold: ok. (b) backup uses File.Copy(overwrite:false) to a name checked free (-2, -3): never overwrites or deletes; a race is covered by the lock plus the throw. (c) second load: no bare keys, so no backup and no write (tested). (d) new-format file not rewritten (tested). (e) backup first, then WriteAtomic (temp file + move): if Write throws, the original is intact; if the copy throws nothing is migrated. (f) null entries skipped and logged once, dropped at next write; but a null-only bare key does not trigger migration (fine). (g) Update replaces only Claude keys and keeps other providers' (tested). (h) empty-entry removal is in UpdateAll over all keys, unchanged for Claude.
- 4 ClaudeProvider: ResumeCommandLine uses ClaudeCli.AttachCommandLineOf: only the first 8 characters, which must be 8 hex digits, reach the command line, so no injection (a longer id with a shell tail is cut to the short id). NewCommandLine ignores the folder; the window sets the working directory (as the trust step does). No finding.
- 5 App keys: every _terminals/_hiddenSessions/_rightSessions lookup uses KeyOf (grep for the remaining uses shows only Clear/Count/Values, which need no key). Persisted tab layout (TabLayout in Split.cs:210, TabDrag.cs:36, Tabs.cs:412) still stores bare ids both when written (t.Key) and when read (_savedRight.Contains(record.SessionId)): symmetrical. It will need a provider decision in C.
- 6 Tests: git diff 50e1031..71d33f9 -- tests shows only added lines (461 insertions, 0 deletions): no existing assertion removed or loosened. LibraryMigrationTests really write an old-format file, check the keys, the backup name pattern and that the backup bytes equal the old bytes, a second load adds no backup and leaves bytes unchanged. Not tested: write failure leaving the original intact, and the read-only folder (item 2).

Verdict: stage A is fine to build C on
