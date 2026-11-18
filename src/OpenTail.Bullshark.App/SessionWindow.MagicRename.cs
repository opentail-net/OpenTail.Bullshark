using System.Collections.Concurrent;
using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The magic rename (docs/plans/magic-rename-2026-11-18.md): Haiku names a past session that has gone cold, and the library name gets a
/// wand. One row menu item names one session; the History mark and the palette name the cold sessions (at most 10 a run, after a confirm);
/// automatic naming is a setting, off by default. The model calls and the transcript reads run on a pool thread, the library is written
/// through <see cref="SessionLibrary.Update"/> only, and nothing is sent to a session.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The key of the automatic naming in ui.json (plan D7c).</summary>
    private const string MagicAutoKey = "magicAuto";

    /// <summary>The mark of a magic name, in front of the name and in the palette.</summary>
    private const string Wand = "🪄";

    /// <summary>The window waits this long after it opens before any automatic naming (plan D7c: never at window start).</summary>
    private static readonly TimeSpan MagicAutoAfterStart = TimeSpan.FromMinutes(5);

    private readonly DateTimeOffset _magicStartedAt = DateTimeOffset.Now;

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

    /// <summary>The cold sessions in the loaded history, the most recent first. Read from memory only.</summary>
    /// <remarks>A session that failed twice in this window run is left out, so it does not take a place in the batch (D8).</remarks>
    private IReadOnlyList<SessionDigest> ColdSessions(DateTimeOffset now) =>
        _history is null
            ? []
            : MagicCandidates.Cold(_history, _marks, LiveSessionIds(), now)
                .Where(digest => !_magicFailures.TryGetValue(digest.SessionId, out var count) || count < MagicNamer.MaxFailuresPerWindowRun)
                .ToList();

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
        menu.Items.Add(MenuItemFor(header, () => _ = MagicRenameOneAsync(digest, kind)));
    }

    /// <summary>Names one past session. The owner's own name is replaced only after a confirm.</summary>
    private async Task MagicRenameOneAsync(SessionDigest digest, MagicCandidates.RenameKind kind)
    {
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
        var cold = ColdSessions(DateTimeOffset.Now);
        if (cold.Count == 0)
        {
            SetStatus("No session has gone cold yet: two hours without a message, at least four messages, and no name.");
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
    /// Automatic naming (plan D7c), called when a pass has finished so the window is idle. It runs only when the setting is on, no pass or
    /// run is going, the window has been open for five minutes, no usage limit is in force (as the status bar last read it), and fewer than
    /// <see cref="MagicCandidates.AutoPerHour"/> calls were made in the last hour. The limit is checked again before each call.
    /// </summary>
    private void MaybeAutoMagicRename()
    {
        if (!_magicAutoOn || _magicRunning || _passRunning || _history is null || _historyLoading || _libraryError is not null) return;
        var now = DateTimeOffset.Now;
        if (now - _magicStartedAt < MagicAutoAfterStart) return;
        // No pre-check of the limits here: a limit that is in force may belong to another model (a weekly Fable limit does not stop Haiku, and
        // blocked the first real tries on 2026-11-18). The call itself reports a limit that stops it, and the batch stops there.

        var hour = now.AddHours(-1);
        var used = Math.Max(MagicCandidates.NamedSince(_marks, hour), _magicAutoCalls.Count(at => at >= hour));
        var allowed = MagicCandidates.AutoPerHour - used;
        if (allowed <= 0) return;

        var cold = ColdSessions(now);
        if (cold.Count == 0) return;
        var targets = cold.Take(allowed).Select(digest => new MagicTarget(digest, false)).ToList();
        _ = RunMagicAsync(targets, allowed, automatic: true);
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
        SetStatus(automatic ? "Automatic magic naming with Haiku…" : $"Naming {targets.Count} session(s) with Haiku…");
        var marks = _marks;
        var history = _history ?? [];
        var taken = MagicCandidates.TakenNames(marks, targets.Select(target => target.Digest.SessionId));
        try
        {
            var result = await Task.Run(() => NameTargetsAsync(targets, marks, history, taken, maxCalls));
            if (automatic)
            {
                var now = DateTimeOffset.Now;
                for (var i = 0; i < result.Calls; i++) _magicAutoCalls.Add(now);
                _magicAutoCalls.RemoveAll(at => at < now.AddHours(-1));
            }
            var summary = MagicSummary(result);
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

    /// <summary>The run itself, on a pool thread: the transcripts are read here, and each name is written as it is chosen.</summary>
    private async Task<MagicBatchResult> NameTargetsAsync(
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
        var jobs = targets.Select(target => JobFor(target, marks, history)).ToList();
        return await namer.RunAsync(jobs, taken, _magicFailures, StoreMagic, maxCalls);
    }

    /// <summary>One session as the namer needs it: its extract (null when it has no text or cannot be read), and the names used in its folder.</summary>
    private static MagicJob JobFor(MagicTarget target, IReadOnlyDictionary<string, LibraryEntry> marks, IReadOnlyList<SessionDigest> history)
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
        return new MagicJob(digest.SessionId, digest.LastActivity ?? DateTimeOffset.MinValue, extract, used, target.ReplaceManual);
    }

    /// <summary>
    /// Writes one magic name through <see cref="SessionLibrary.Update"/>, on a pool thread. The entry is read again under the lock: a name the
    /// owner set meanwhile is kept, unless the owner confirmed the replacement. Returns false when it was kept.
    /// </summary>
    private bool StoreMagic(MagicNamed named)
    {
        var written = false;
        _library.Update(entries =>
        {
            var current = entries.TryGetValue(named.SessionId, out var found) ? found : new LibraryEntry();
            var ownersName = !string.IsNullOrWhiteSpace(current.Name) && current.MagicAt is null;
            if (ownersName && !named.ReplaceManual) return;
            entries[named.SessionId] = current with
            {
                Name = named.Name,
                MagicAt = named.MagicAt,
                MagicNamedAt = named.MagicNamedAt,
            };
            written = true;
        });
        return written;
    }

    /// <summary>The status line after a run: what was named, and what was not and why, in short.</summary>
    private static string MagicSummary(MagicBatchResult result)
    {
        if (result.StoppedAtLimit && result.Calls == 0 && result.Named == 0)
            return "A usage limit is in force, so no session was named.";

        var parts = new List<string> { result.Named == 1 ? "Named 1 session" : $"Named {result.Named} sessions" };
        if (result.Failed > 0) parts.Add($"{result.Failed} failed");
        if (result.Refused > 0) parts.Add($"{result.Refused} kept their own name");
        if (result.NoText > 0) parts.Add($"{result.NoText} had no text");
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
                cold == 0 ? "none has gone cold" : "asks first; at most 10 a run", () => _ = NameColdSessionsAsync()),
            ActionItem("magic-auto", "Toggle automatic magic naming",
                _magicAutoOn ? "on: at most 3 an hour, only when idle" : "off (the default); it uses your usage when on",
                ToggleMagicAuto),
        ];
    }

    /// <summary>Turns automatic naming on or off in ui.json. The status line says what the file holds after the write.</summary>
    private void ToggleMagicAuto()
    {
        UiFile.Set(_options.StatePath, MagicAutoKey, JsonValue.Create(!_magicAutoOn)!);
        LoadMagicAuto();
        AppLog.Write($"automatic magic naming: {(_magicAutoOn ? "on" : "off")} (from the command palette)");
        SetStatus(_magicAutoOn
            ? "Automatic magic naming is on: at most 3 an hour, only when the window is idle and no usage limit is in force."
            : "Automatic magic naming is off.");
    }
}
