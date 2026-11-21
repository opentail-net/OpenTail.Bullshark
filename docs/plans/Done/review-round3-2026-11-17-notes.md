# Review round 3 (read-only), OpenTail.Bullshark, working tree on top of fefbc35
Paths are under src/. "Confirmed" = proved by the lines read; "plausible" = depends on timing or data I did not see.
Note: App/App.xaml.cs:31 handles DispatcherUnhandledException (logs, Handled = true), so an exception in an async void handler or UI callback does not kill the process. It leaves the UI half-updated and the status line stale.

## 1. Search misses any word containing a backslash or a quote (confirmed)
Core/Search/HistorySearch.cs:87 (decoded-text check at :92)
The cheap pre-filter tests the words against the RAW JSON line (`raw.Contains(word)`), but the real match is on the decoded message text. In the JSONL file `\` is written `\\`, `"` is `\"`. A query such as `src\Core` or `C:\Git` is not a substring of the raw line, so the line is skipped before it is parsed.
Scenario: the owner searches `OpenTail.Bullshark\src`, a path typed in many messages. No result, although the text is there. Windows paths are the common case.
Fix: when a word contains `\` or `"`, also test its JSON-escaped form, or skip the pre-filter for such words.

## 2. TranscriptView.ShowAsync can show session A's lines under session B's header (confirmed)
App/TranscriptView.cs:91-128
`_digest` is set first (:93), then `await Task.Run(TranscriptPager.Open ...)` (:107). Nothing checks after the await that this call is still the latest. Two quick selections (arrow keys down the rail, or a search-result click while the rail selection loads): B finishes first, then A finishes and runs `_pager = A`, clears `_transcript` and fills it with A's lines. `_digest`, the header, the Resume button and the path check in `LoadTurnFactsAsync` (:493) all say B.
Scenario: the pane shows A's text while Resume offers to resume B.
Fix: keep a call counter or token, and return after the await if it changed.

## 3. A bad runs.json (or a held lock) breaks the SetupView constructor, so the window start (confirmed no catch; consequence plausible)
App/SetupView.xaml.cs:65-66, called from App/SessionWindow.xaml.cs:96
`new RunStore(...).Load()` throws StoreUnreadableException (corrupt file) or TimeoutException (mutex held 10 s). `FolderSuggestions.Build` can also throw: `FolderKey.From(run.WorkingDirectory)` calls GetFullPath on any hand-edited path (Core/Folders/FolderCheck.cs:288). The constructor has no catch, and App.OnStartup (App.xaml.cs:41-52) catches only ArgumentException. The window may never show, and the process may stay alive with no window.
Fix: wrap the folder-suggestion read in try/catch and offer an empty list.

## 4. PatternStore: unlocked, and one bad read erases the other patterns' saved briefs (confirmed)
Core/Setup/PatternStore.cs:211-243
- `Load` catches only JsonException (:219). An IOException or UnauthorizedAccessException (antivirus or sync tool holding the file) escapes `For()`, and so SetupView.LoadPattern (:182) and Refresh().
- `Save` is a read-modify-write with no FileLock (RunStore and SessionLibrary use it) and a fixed `.tmp` name (:240). Two windows can interleave, lose one writer's pattern, or write the same `.tmp` file.
- If patterns.json holds invalid JSON, `Load` returns an empty dictionary and `Save` writes only the one pattern. The saved planner and coder briefs of the other patterns are lost. The other stores throw StoreUnreadableException and refuse to overwrite; this one overwrites.
Fix: use FileLock.Hold and FileLock.WriteAtomic, and throw StoreUnreadableException on a bad file.

## 5. Full resume can leave a running, unsupervised session while reporting "Not resumed" (confirmed)
App/SessionWindow.Resume.cs:286-303
`StartBackgroundAsync` and `ResolveSessionIdAsync` (30 s wait) share one try. If the start succeeded and resolving the id times out, the catch says "Not resumed" and returns. The claude session is running, but there is no record and no run, so nothing keeps it alive and the rail may not show it.
Scenario: a slow machine takes more than 30 s to list the new agent. The owner presses Resume again, and the first (full-context, costly) session keeps running beside the second.
Fix: split the two calls; on a resolve failure say the session may be running and give its short id.

## 6. A resume text can exceed the command line limit, and the status line claims success (plausible)
Core/Resume/ResumeText.cs:141-147 (latest turn "never cut"), Compose :165-185 (only the summary is capped); App/SessionWindow.Resume.cs:350-378
`Tail` keeps at least one turn whatever its size, and "you"/"claude" lines are full text. A last turn holding a 40 KB paste makes the Task longer than the launch limit. `RequireCarriable` (Core/Claude/ClaudeCli.cs:97-109) then refuses the start inside the run, after StartFromTextAsync has written the run and set "The new session starts on the next pass." The owner sees success; the failure is in the log.
Fix: cap a rendered turn (cut the middle of an over-long line), or check the composed length against the launch limit before saving the run.

## 7. Resume failures are swallowed and leave "Reading session..." on the status line (confirmed)
App/SessionWindow.Resume.cs:224 and :335-345; callers use `_ = ResumeAsync(digest)` (SessionWindow.Rail.cs:635, SessionWindow.xaml.cs:103)
- `ResumeSource.Load` (:224) is in no try. Any IOException (file deleted, or item 13) escapes into the discarded task.
- The summary step catches only ClaudeCliException (:340). A missing `claude` executable gives Win32Exception from Process.Start (Core/Claude/CliRunner.cs:59), and a cancel gives OperationCanceledException. Both are lost the same way.
- `ResumeAsync` is not guarded against running twice (two clicks on the resume mark before the dialog opens).
Fix: one try/catch around the whole body that sets the status; a re-entrancy flag.

## 8. ui.json (theme, zoom): non-atomic write, no lock, and a bad file blocks all later saves (confirmed)
App/UiFile.cs:32-46, :49-53
`File.WriteAllText` truncates in place. A crash, or a second window writing at once, can leave a truncated file. `ReadRoot` then throws JsonException, and `Set` logs and returns on every later call, so zoom and theme silently stop persisting until the owner deletes the file. Two windows doing read-modify-write also lose each other's key.
Fix: FileLock.Hold plus FileLock.WriteAtomic; on a parse failure, move the bad file aside and start a new one.

## 9. SetupCheck throws on valid JSON of an unexpected shape (confirmed)
Core/Health/SetupCheck.cs:59, 66, 69, 93, 95
`JsonElement.TryGetProperty` throws InvalidOperationException when the element is not an object. `RootElement.TryGetProperty` at :59, :93, :95 fails for a settings.json or .claude.json whose root is `[]` or a string. `group.TryGetProperty("hooks")` (:66) and `hook.TryGetProperty("command")` (:69) fail when a hooks array holds a string or null. `ReadJson` catches only parse errors. The whole check dies instead of listing a Problem item.
Fix: test `ValueKind == JsonValueKind.Object` before each TryGetProperty.

## 10. Library and run writes: UnauthorizedAccessException not caught; null entries crash (confirmed)
App/SessionWindow.Library.cs:44, 109; App/SessionWindow.Resume.cs:312; Core/Library/SessionLibrary.cs:305
- The catch lists are `StoreUnreadableException or TimeoutException or IOException`. `FileLock.WriteAtomic` (Core/Supervisor/FileLock.cs:61) can raise UnauthorizedAccessException from `File.Move(overwrite)` onto a read-only file or one held by antivirus. In ChangeMark the exception goes to the dispatcher handler: a pin or rename silently does nothing, with no status. In ResumeInFull (:309-310) it hits after the session started, so no record or run is written (same orphan as item 5).
- A `library.json` with `"id": null` deserializes to a null entry, and `pair.Value.IsEmpty` (SessionLibrary.cs:305) then throws NullReferenceException on every Update, so no mark can be saved until the file is fixed by hand. Hand-edit only.
Fix: add UnauthorizedAccessException to the catches; drop null values in `Read`.

## 11. Setup page does file I/O on the UI thread on every keystroke (confirmed)
App/SetupView.xaml.cs:235-259, wired at :62, :266, :273, :323, :325
`Refresh()` runs for each character typed in the folder box or either brief editor, and for every combo change. Each run calls `SetupPlan.Validate`, which calls FolderCheck: Directory.Exists plus `TrustCheck.Check` (reads `~/.claude.json`, often several MB, and walks to the git root). It then calls `SetupPlan.Build` and `DefaultBriefTemplate` again. Typing a path stutters when `.claude.json` is large or the folder is on a network drive.
Fix: debounce (as the search box does) and cache the parsed trust list by file timestamp.

## 12. InstructionsDialog: state set before the read; line endings rewritten (read order confirmed; line endings plausible)
App/InstructionsDialog.cs:200-210, :223-253
- `Open` sets `_index`, `_current` and `_stamp` (:202-204) before `InstructionFiles.Read` (:205; plain ReadAllText, Core/Instructions/InstructionFiles.cs:70). If the read throws (IOException, UnauthorizedAccessException) inside the list SelectionChanged handler, the dispatcher handler swallows it, the editor keeps the previous file's text, and `_current` already names the new file. Save then writes the previous file's text over the new file (the stamp matches, so no "changed on disk" warning).
- WPF TextBox.Text reports line breaks as CRLF, so a CLAUDE.md with LF endings is saved back with CRLF on the first save, and `_editor.Text != _loadedText` stays true after undoing an edit, so Close asks to discard when nothing changed.
Fix: read first, assign state after; restore the file's own line endings on save.

## 13. WorkingDirectoryOf opens the transcript with FileShare.Read (plausible)
Core/Resume/ResumeSource.cs:25
`new StreamReader(path)` requests share mode Read, which fails with a sharing violation while another handle has the file open for writing. Every other reader goes through `TranscriptFile.Open` (ReadWrite | Delete), and its doc comment says why. Resuming a session that was written a moment ago can throw IOException here (then silently, see item 7).
Fix: `new StreamReader(TranscriptFile.Open(path), Encoding.UTF8)`.

## 14. SetupCheck reports false Problems (plausible)
Core/Health/SetupCheck.cs:165-188, 283-289, 26-29
- `Expand` handles only `%VAR%` and `~`. A hook command such as `"$CLAUDE_PROJECT_DIR"/hooks/x.sh` or `${CLAUDE_PLUGIN_ROOT}/...` contains `/`, so LooksLikePath is true, `File.Exists` fails, and a "program not found" Problem is shown for a hook that works. A command starting with `VAR=1 cmd` or a shell builtin is judged the same way.
- With CLAUDE_CONFIG_DIR set, `ClaudeConfig.Dir` is used for settings.json, but `DefaultClaudeJsonPath` is always `~/.claude.json`; I did not verify where Claude keeps that file in that case, so treat this half as unverified.
Fix: expand `$VAR`/`${VAR}`, and skip commands whose first word is not a plain path.

## 15. Supervisor project: no loop to review; shared-state doc is stale (confirmed)
OpenTail.Bullshark.Supervisor/Program.cs (47 lines)
It is two read-only reports (`--usage-report`, `--cache-report`) and exits 2 otherwise. It takes no FileLock and writes no state file, so it cannot race the window. FileLock's doc (Core/Supervisor/FileLock.cs:11-13, "the window and the supervisor are two processes that write the same files") is out of date. Small defects: no try/catch, so an unreadable folder gives an unhandled exception trace (CacheReport.ReadRequests catches only IOException, Core/Cache/CacheReport.cs:153, so UnauthorizedAccessException escapes); `--projects` followed by another flag takes that flag as the folder.

## 16. Search bookkeeping (confirmed, low)
App/SessionWindow.Search.cs:85-133; Core/Search/HistorySearch.cs:101; App/SearchView.cs:112
- Each search makes a CancellationTokenSource and never disposes it.
- A cancelled older search that fails with a non-cancel exception reaches `catch (Exception)` (:127) and calls `SetBusy(false)` and `SetStatus("Search failed")` while a newer search runs, so the hourglass vanishes early.
- `MatchSession` catches only IOException and UnauthorizedAccessException, so any other exception from one file aborts the whole search, against its doc comment ("A session whose transcript cannot be read is left out").
- Results show `digest.Title`, not the owner's name or lineage title the rail and tab use, so a renamed session appears under its old title.
Fix: dispose the CTS in a finally; check `cancel.IsCancellationRequested` in the generic catch; catch Exception per file.
