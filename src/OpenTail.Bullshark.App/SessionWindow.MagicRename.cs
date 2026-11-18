using System.Collections.Concurrent;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Cache;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The magic rename (docs/plans/magic-rename-2026-11-18.md): Haiku names a past session that has gone cold, and the library name gets a
/// wand. One row menu item names one session; the History mark and the palette name the cold sessions (at most 10 a run, after a confirm).
/// Automatic naming is a setting, off by default: a never-named session is named when its cache goes from warm to cold while the window is
/// open (docs/plans/tags-and-auto-magic-2026-11-18.md, Part C). A name also carries up to two tags, which are added to the session's own
/// (Part B). The model calls and the transcript reads run on a pool thread, the library is written through <see cref="SessionLibrary.Update"/>
/// only, and nothing is sent to a session.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The key of the automatic naming in ui.json (plan D7c).</summary>
    private const string MagicAutoKey = "magicAuto";

    /// <summary>The mark of a magic name, in front of the name and in the palette.</summary>
    private const string Wand = "🪄";

    /// <summary>The cache state each past session was last seen in, for this window (tags plan, Part C1). Only the UI thread touches it.</summary>
    private readonly CacheWatch _cacheWatch = new();

    /// <summary>
    /// The sessions that went cold while the window was open and may be named automatically, waiting for the hourly limit (tags plan, Part C3).
    /// Only the UI thread touches it. It is emptied while the setting is off, so no backlog builds.
    /// </summary>
    private readonly HashSet<string> _magicQueue = new(StringComparer.Ordinal);

    /// <summary>Whether automatic naming is on, as ui.json holds it. Off unless the owner turned it on.</summary>
    private bool _magicAutoOn;

    /// <summary>True while a naming run is going. A second request is refused until it ends.</summary>
    private bool _magicRunning;

    /// <summary>The failed calls per session in this window run (plan D8). Only the naming run touches it, and only while it runs.</summary>
    /// <remarks>Concurrent: the run writes it on a pool thread while the palette and the History mark read it on the UI thread.</remarks>
    private readonly ConcurrentDictionary<string, int> _magicFailures = new(StringComparer.Ordinal);

    /// <summary>When each automatic call was made in this window run, so the hourly limit counts the calls that failed too.</summary>
    private readonly List<DateTimeOffset> _magicAutoCalls = [];

    /// <summary>One session in a run, and whether the owner's own name may be replaced (only a confirmed single rename).</summary>
    private sealed record MagicTarget(SessionDigest Digest, bool ReplaceManual);

    /// <summary>Reads the automatic setting once, at start. A file that cannot be read keeps it off.</summary>
    private void LoadMagicAuto() =>
        _magicAutoOn = UiFile.Read(_options.StatePath, MagicAutoKey) is JsonValue value && value.TryGetValue<bool>(out var on) && on;

    /// <summary>The folder the model call runs in: beside the state file, never in a project (plan Contracts, "No pollution").</summary>
    private string MagicFolder() => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_options.StatePath))!, "magic-rename");

    /// <summary>The sessions that are running now: the agents' listing and the state records, as the History mark counts them.</summary>
    private ISet<string> LiveSessionIds() =>
        _agents.Select(agent => agent.SessionId)
            .Concat(_railRecords.Values.Select(record => record.SessionId))
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The cold sessions that the History section lists now, in the order it lists them (the Pinned group first, then the folders, each newest
    /// first), so a run names the top of the list and not an arbitrary ten. Read from memory only.
    /// </summary>
    /// <remarks>A session that failed twice in this window run is left out, so it does not take a place in the batch (D8).</remarks>
    private IReadOnlyList<SessionDigest> ColdSessions(DateTimeOffset now)
    {
        if (_history is null) return [];
        var listed = new List<SessionDigest>();
        if (PinnedGroupShown) listed.AddRange(HistoryOrder.Sorted(_history.Where(IsShownInHistory).Where(InPinnedGroup)));
        foreach (var folder in ShownHistoryFolders()) listed.AddRange(HistoryOrder.Sorted(folder).Take(MaxHistoryPerFolder));
        return MagicCandidates.ColdInOrder(listed, _marks, LiveSessionIds(), now)
            .Where(digest => !_magicFailures.TryGetValue(digest.SessionId, out var count) || count < MagicNamer.MaxFailuresPerWindowRun)
            .ToList();
    }

    /// <summary>
    /// Adds the row menu's magic rename for one past session, if one is offered (D7a and D2). A live session, a subagent and an archived
    /// session get none; a session with the owner's name gets it after a confirm.
    /// </summary>
    private void AddMagicItems(ContextMenu menu, SessionDigest digest, LibraryEntry? mark)
    {
        var kind = MagicCandidates.KindFor(digest, mark, LiveSessionIds());
        if (kind == MagicCandidates.RenameKind.None) return;
        menu.Items.Add(new Separator());
        var header = kind switch
        {
            MagicCandidates.RenameKind.Again => "Magic rename again",
            MagicCandidates.RenameKind.ReplaceManual => "Magic rename… (replaces your name)",
            _ => "Magic rename",
        };
        var item = MenuItemFor(_magicRunning ? header + " (running…)" : header, () => _ = MagicRenameOneAsync(digest, kind));
        item.IsEnabled = !_magicRunning;
        menu.Items.Add(item);
    }

    /// <summary>Names one past session. The owner's own name is replaced only after a confirm.</summary>
    private async Task MagicRenameOneAsync(SessionDigest digest, MagicCandidates.RenameKind kind)
    {
        if (MagicBusy()) return;
        var replace = kind == MagicCandidates.RenameKind.ReplaceManual;
        if (replace)
        {
            var answer = MessageBox.Show(
                this,
                $"Replace the name \"{MarkOf(digest.SessionId)?.Name}\" with a name from Haiku? Your name is lost. " +
                "You can rename the session yourself afterwards.",
                "Magic rename",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
        }
        await RunMagicAsync([new MagicTarget(digest, replace)], maxCalls: 1, automatic: false);
    }

    /// <summary>The History mark and the palette: names the cold sessions, at most ten a run, after a confirm that says how many and that it uses Haiku.</summary>
    private async Task NameColdSessionsAsync()
    {
        if (MagicBusy()) return;
        var cold = ColdSessions(DateTimeOffset.Now);
        if (cold.Count == 0)
        {
            SetStatus("No session in the list has gone cold yet: two hours without a message, at least four messages, and no name.");
            return;
        }
        var take = Math.Min(cold.Count, MagicNamer.MaxPerRun);
        var answer = MessageBox.Show(
            this,
            $"Name {take} of {cold.Count} cold sessions with Haiku? Each one is a model call that uses your usage. " +
            $"At most {MagicNamer.MaxPerRun} are named in one run, and a usage limit stops the run. Your own names are kept.",
            "Name cold sessions",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        await RunMagicAsync(cold.Take(take).Select(digest => new MagicTarget(digest, false)).ToList(), take, automatic: false);
    }

    /// <summary>
    /// Automatic naming (tags plan, Part C), called when a pass has finished, so the window is idle. The cache watch reads the state of every
    /// past session from the facts read with the history; a session that went from warm or cooling to cold since the last pass is queued when
    /// it may be named (<see cref="MagicCandidates.IsAutoCandidate"/>). The queue is named only when the setting is on, no pass or run is going,
    /// the history and the library are readable, and the hourly limit (<see cref="MagicCandidates.AutoPerHour"/>) leaves room; what the limit
    /// leaves stays queued for a later pass. With the setting off, nothing is queued and the queue is emptied, so no backlog builds. The watch
    /// runs either way. No pre-check of the usage limits: a limit that is in force may belong to another model, and the call itself reports a
    /// limit that stops it.
    /// </summary>
    private void MaybeAutoMagicRename()
    {
        // Nothing to compare until the history is read. The watch is not updated before then, so it does not forget what it has seen.
        if (_history is null) return;
        var now = DateTimeOffset.Now;
        var wentCold = _cacheWatch.Update(_history.Select(digest => (digest.SessionId, CacheStateOf(digest.SessionId, now))));
        if (!_magicAutoOn)
        {
            _magicQueue.Clear();
            return;
        }

        // A session that goes cold while it is still open is queued too, and waits in the queue until it is no longer live: a session is usually
        // closed after its cache has gone cold, and it would otherwise never be named.
        var live = LiveSessionIds();
        var noneLive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sessionId in wentCold)
        {
            var digest = _history.FirstOrDefault(d => SameId(d.SessionId, sessionId));
            if (digest is not null && AutoEligible(digest, noneLive)) _magicQueue.Add(sessionId);
        }
        if (_magicQueue.Count == 0 || _magicRunning || _passRunning || _historyLoading || _libraryError is not null) return;

        // A queued session that is no longer a candidate (named by the owner, archived, failed too often) leaves the queue first.
        // A live one stays queued and is skipped for now.
        var candidates = new List<SessionDigest>();
        foreach (var sessionId in _magicQueue.ToList())
        {
            var digest = _history.FirstOrDefault(d => SameId(d.SessionId, sessionId));
            if (digest is null || !AutoEligible(digest, noneLive)) _magicQueue.Remove(sessionId);
            else if (!live.Contains(sessionId)) candidates.Add(digest);
        }

        var hour = now.AddHours(-1);
        var used = Math.Max(MagicCandidates.NamedSince(_marks, hour), _magicAutoCalls.Count(at => at >= hour));
        var allowed = MagicCandidates.AutoPerHour - used;
        if (allowed <= 0 || candidates.Count == 0) return;

        // The most recent first. The sessions beyond the hour's room stay queued; the ones named leave it at the next pass (not a candidate then).
        var targets = candidates
            .OrderByDescending(digest => digest.LastActivity)
            .Take(allowed)
            .Select(digest => new MagicTarget(digest, false))
            .ToList();
        _ = RunMagicAsync(targets, allowed, automatic: true);
    }

    /// <summary>A session the automatic rename may name now: a candidate, and not at the failure limit of this window run (D8).</summary>
    private bool AutoEligible(SessionDigest digest, ISet<string> live) =>
        MagicCandidates.IsAutoCandidate(digest, MarkOf(digest.SessionId), live)
        && !(_magicFailures.TryGetValue(digest.SessionId, out var failures) && failures >= MagicNamer.MaxFailuresPerWindowRun);

    /// <summary>A past session's cache state at <paramref name="now"/>, from the facts read with the history. Unknown when there are none.</summary>
    private CacheState CacheStateOf(string sessionId, DateTimeOffset now) =>
        _historyFacts.TryGetValue(sessionId, out var facts)
            ? CacheRule.Classify(facts.LastRequest, now, facts.Ttl, facts.ContextTokens).State
            : CacheState.Unknown;

    /// <summary>True, with a status line, when a run is going: a second one is not started, and no confirm is shown for it.</summary>
    private bool MagicBusy()
    {
        if (!_magicRunning) return false;
        SetStatus("A magic rename is already running. Wait for it to finish.");
        return true;
    }

    /// <summary>
    /// Names the targets in one background run, then redraws the rail and the tab titles. A run that is already going is refused. A failed
    /// write or an unexpected error is logged and shown on the status line; nothing is left half-drawn.
    /// </summary>
    private async Task RunMagicAsync(IReadOnlyList<MagicTarget> targets, int maxCalls, bool automatic)
    {
        if (_magicRunning)
        {
            SetStatus("A magic rename is already running. Wait for it to finish.");
            return;
        }
        if (_libraryError is not null)
        {
            SetStatus($"Not named: the library cannot be read ({_libraryError}). Nothing is written until it can be.");
            return;
        }
        if (targets.Count == 0) return;

        _magicRunning = true;
        // Background work: the window stays usable (no wait cursor); the status line shows progress.
        SetStatus(automatic ? "Automatic magic naming with Haiku…" : $"Naming {targets.Count} session(s) with Haiku…");
        var marks = _marks;
        var history = _history ?? [];
        var taken = MagicCandidates.TakenNames(marks, targets.Select(target => target.Digest.SessionId));
        try
        {
            var (result, tagsAdded) = await Task.Run(() => NameTargetsAsync(targets, marks, history, taken, maxCalls));
            if (automatic)
            {
                var now = DateTimeOffset.Now;
                for (var i = 0; i < result.Calls; i++) _magicAutoCalls.Add(now);
                _magicAutoCalls.RemoveAll(at => at < now.AddHours(-1));
            }
            var summary = MagicSummary(result, tagsAdded, automatic);
            AppLog.Write($"magic rename run: {summary}");
            SetStatus(automatic ? "Automatic: " + summary : summary);
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"magic rename not saved: {ex.Message}");
            SetStatus($"Not named: {ex.Message}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"magic rename failed: {ex.GetType().Name}");
            SetStatus("Not named: an unexpected error. The log has the details.");
        }
        finally
        {
            _magicRunning = false;
            LoadLibrary();
            RefreshRail();
            RefreshTabTitles();
        }
    }

    /// <summary>
    /// The run itself, on a pool thread: the transcripts are read here, and each name is written as it is chosen. Returns the batch's result and
    /// the number of tags the run added to the library.
    /// </summary>
    private async Task<(MagicBatchResult Result, int TagsAdded)> NameTargetsAsync(
        IReadOnlyList<MagicTarget> targets,
        IReadOnlyDictionary<string, LibraryEntry> marks,
        IReadOnlyList<SessionDigest> history,
        ISet<string> taken,
        int maxCalls)
    {
        var folder = MagicFolder();
        Directory.CreateDirectory(folder);
        var root = _options.ProjectsRoot;
        var namer = new MagicNamer(
            new HaikuNameModel(new ProcessCliRunner(), folder),
            () => false, // see MaybeAutoMagicRename: the call's own limit error stops the batch, not a limit of another model
            TimeProvider.System,
            AppLog.Write);
        // The tags the prompt shows: the most used in the library (tags plan, Part B). Read from the marks the run was given.
        var knownTags = TagIndex.Counts(marks.Values).Take(MagicName.MaxKnownTags).Select(tag => tag.Tag).ToList();
        var jobs = targets.Select(target => JobFor(target, marks, history, knownTags)).ToList();
        var tagsAdded = 0;
        bool Store(MagicNamed named)
        {
            var written = StoreMagic(named, out var added);
            tagsAdded += added;
            return written;
        }
        var result = await namer.RunAsync(jobs, taken, _magicFailures, Store, maxCalls, onCall: (call, total) =>
            Dispatcher.BeginInvoke(() => SetStatus(total <= 1
                ? "Naming with Haiku… this takes 5 to 20 seconds."
                : $"Naming {call} of {total} with Haiku… about 5 to 20 seconds each.")));
        return (result, tagsAdded);
    }

    /// <summary>
    /// One session as the namer needs it: its extract (null when it has no text or cannot be read), the names used in its folder, and the tags
    /// in use that the prompt shows.
    /// </summary>
    private static MagicJob JobFor(
        MagicTarget target,
        IReadOnlyDictionary<string, LibraryEntry> marks,
        IReadOnlyList<SessionDigest> history,
        IReadOnlyList<string> knownTags)
    {
        var digest = target.Digest;
        string? extract;
        try
        {
            extract = NameExtract.Build(TranscriptFile.ReadLines(digest.FilePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"magic rename: session {digest.SessionId} transcript not read: {ex.GetType().Name}");
            extract = null;
        }
        var used = MagicCandidates.NamesInFolder(history, marks, digest.ProjectFolder);
        return new MagicJob(digest.SessionId, digest.LastActivity ?? DateTimeOffset.MinValue, extract, used, target.ReplaceManual, knownTags);
    }

    /// <summary>
    /// Writes one magic name through <see cref="SessionLibrary.Update"/>, on a pool thread. The entry is read again under the lock: a name the
    /// owner set meanwhile is kept, unless the owner confirmed the replacement. Returns false when it was kept, and then nothing is written,
    /// tags included. The tags the reply offered are added to the session's own tags in the same write (<see cref="MagicName.MergeTags"/>):
    /// the owner's tags are never removed, and the total is capped at <see cref="MagicName.MaxTags"/>. <paramref name="tagsAdded"/> counts
    /// the tags that were new to the session (0 when nothing was written).
    /// </summary>
    private bool StoreMagic(MagicNamed named, out int tagsAdded)
    {
        var written = false;
        var added = 0;
        _library.Update(entries =>
        {
            var current = entries.TryGetValue(named.SessionId, out var found) ? found : new LibraryEntry();
            var ownersName = !string.IsNullOrWhiteSpace(current.Name) && current.MagicAt is null;
            if (ownersName && !named.ReplaceManual) return;
            var tags = MagicName.MergeTags(current.Tags, named.Tags ?? [], MagicName.MaxTags);
            added = tags.Count - MagicName.MergeTags(current.Tags, []).Count;
            entries[named.SessionId] = current with
            {
                Name = named.Name,
                MagicAt = named.MagicAt,
                MagicNamedAt = named.MagicNamedAt,
                Tags = tags,
            };
            written = true;
        });
        tagsAdded = written ? added : 0;
        return written;
    }

    /// <summary>
    /// The status line after a run: what was named, and what was not and why, in short. An automatic run says it named the sessions that went
    /// cold (tags plan, Part C).
    /// </summary>
    private static string MagicSummary(MagicBatchResult result, int tagsAdded, bool automatic)
    {
        if (result.StoppedAtLimit && result.Calls == 0 && result.Named == 0)
            return "A usage limit is in force, so no session was named.";

        var parts = new List<string>
        {
            automatic ? $"named {result.Named} session(s) that went cold"
                : result.Named == 1 ? "Named 1 session" : $"Named {result.Named} sessions",
        };
        if (tagsAdded > 0) parts.Add(tagsAdded == 1 ? "added 1 tag" : $"added {tagsAdded} tags");
        if (result.Failed > 0) parts.Add($"{result.Failed} failed");
        if (result.Refused > 0) parts.Add($"{result.Refused} kept their own name");
        if (result.NoText > 0) parts.Add(result.NoText == 1
            ? "1 has nothing to read (only commands such as /clear, no conversation), so no model call was made"
            : $"{result.NoText} have nothing to read (only commands such as /clear), so no model call was made");
        if (result.SkippedAfterFailures > 0) parts.Add($"{result.SkippedAfterFailures} skipped after two failures");
        var text = string.Join(", ", parts) + ".";
        if (result.StoppedAtLimit) text += " A usage limit stopped the run; the rest were not named.";
        return text;
    }

    /// <summary>The History mark that names the cold sessions. It sits beside the reload mark, and asks first.</summary>
    private TextBlock MagicHeaderMark()
    {
        var mark = Label(Wand, muted: true, emoji: true);
        mark.Margin = new Thickness(8, 0, 0, 0);
        mark.Cursor = Cursors.Hand;
        mark.ToolTip = "Name the cold sessions with Haiku (asks first; at most 10 a run; uses your usage)";
        mark.MouseLeftButtonUp += async (_, _) => await NameColdSessionsAsync();
        return mark;
    }

    /// <summary>True for a session whose name the magic rename gave (D6).</summary>
    private static bool IsMagicNamed(LibraryEntry? mark) => mark?.MagicAt is not null && !string.IsNullOrWhiteSpace(mark.Name);

    /// <summary>The wand and a space in front of a magic name, or nothing.</summary>
    private static string MagicPrefix(LibraryEntry? mark) => IsMagicNamed(mark) ? Wand + " " : string.Empty;

    /// <summary>The line at the top of a magic name's tooltip (D6): who named it, and when.</summary>
    private static string MagicTip(LibraryEntry? mark)
    {
        if (!IsMagicNamed(mark)) return string.Empty;
        var when = mark!.MagicNamedAt is { } at ? $" on {at.ToLocalTime():d MMM yyyy}" : string.Empty;
        return $"Named by Haiku{when}. Rename it yourself to keep your own name.\n";
    }

    /// <summary>The palette's entries for the magic rename: the batch with its count, and the automatic setting. Read from memory only.</summary>
    private List<PaletteItem> MagicPaletteItems()
    {
        var cold = ColdSessions(DateTimeOffset.Now).Count;
        return
        [
            ActionItem("magic-cold", $"Name cold sessions with Haiku ({cold})",
                _magicRunning ? "a run is already going" : cold == 0 ? "none has gone cold" : "asks first; at most 10 a run", () => _ = NameColdSessionsAsync()),
            ActionItem("magic-auto", "Toggle automatic magic naming",
                _magicAutoOn ? "on: names a never-named session when its cache goes cold ❄" : "off (the default); it uses your usage when on",
                ToggleMagicAuto),
        ];
    }

    /// <summary>Turns automatic naming on or off in ui.json: the palette's toggle and the Settings switch both come here.</summary>
    private void ToggleMagicAuto() => SetMagicAuto(!_magicAutoOn);

    /// <summary>
    /// Writes the automatic naming key, reads it back, and shows what the file holds: a file that cannot be written leaves the setting as it was, and
    /// the status line says so.
    /// </summary>
    private void SetMagicAuto(bool on)
    {
        UiFile.Set(_options.StatePath, MagicAutoKey, JsonValue.Create(on)!);
        LoadMagicAuto();
        // Off: whatever was queued while it was on is dropped, so it does not run later (no backlog while off).
        if (!_magicAutoOn) _magicQueue.Clear();
        AppLog.Write($"automatic magic naming: {(_magicAutoOn ? "on" : "off")} (the owner's choice)");
        SetStatus(_magicAutoOn != on ? "Not saved: ui.json cannot be written. See the log."
            : _magicAutoOn
            ? "Automatic magic naming is on: a never-named session is named when its cache goes cold ❄, at most 3 an hour, only when the window is idle."
            : "Automatic magic naming is off.");
        RefreshSettingsPage();
    }
}
