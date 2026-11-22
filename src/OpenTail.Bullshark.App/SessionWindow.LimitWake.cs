using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Folders;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Resume;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Trust;
using OpenTail.Bullshark.Core.Ui;

namespace OpenTail.Bullshark.App;

/// <summary>
/// Wake after the usage limit resets (limit wake, 2026-11-20). A session stopped at the limit shows when it resets on its row and in Needs you. A session
/// the owner flagged ("Wake after the limit resets") is started again once, after the reset, with the fixed line, when the rule (<see cref="LimitWake"/>)
/// says so. The rule is pure; this file reads the stops, the listing and the flags, runs the rule, and carries out what it says. Only the supervising
/// window does, once per pass, and never while a resume runs, the respawn hold is on, or the trust record cannot be read.
/// </summary>
public partial class SessionWindow
{
    /// <summary>How long a session's wake waits after a start that failed or was refused. The hit is not marked as woken, so the retry is allowed then.</summary>
    private static readonly TimeSpan WakeRetryHold = TimeSpan.FromMinutes(5);

    /// <summary>How long a read of the limit in force is kept. It reads the tails of the transcripts of the last eight days, so it is not read on each pass.</summary>
    private static readonly TimeSpan InForceEvery = TimeSpan.FromSeconds(60);

    /// <summary>
    /// The stops the last pass read at the end of its candidates' transcripts (the sessions it supervises and the ones the owner flagged), by session id.
    /// A null value is a candidate probed with no stop, or one whose state is unknown (see <see cref="_limitUnknown"/>). A session that was not probed
    /// takes its digest's stop, as of the history's read.
    /// </summary>
    private Dictionary<string, LimitStop?> _limitStops = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The sessions whose limit state the last probe could not establish: their transcript could not be read and no stop was known for them. Each is held
    /// for <see cref="UnknownLimitHold"/> from the pass that reads it, so the hold lasts while the read keeps failing and ends on the first good read.
    /// </summary>
    private IReadOnlySet<string> _limitUnknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The hold of a session whose limit state is unknown (<see cref="_limitUnknown"/>): 15 minutes from each pass that still cannot read it. Fail closed:
    /// a lost session is not resumed while its transcript cannot be read. Only the supervisor's lost-session rule reads it.
    /// </summary>
    private static readonly TimeSpan UnknownLimitHold = TimeSpan.FromMinutes(15);

    /// <summary>The reset of the limit still in force (LimitReset.Find), as the last read gave it, and when it was read.</summary>
    private DateTimeOffset? _limitInForce;
    private DateTimeOffset? _limitInForceRead;

    /// <summary>The words each session with a stop shows on its row and in Needs you, as the last refresh drew them.</summary>
    private IReadOnlyDictionary<string, string> _limitTexts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sessions whose wake is held after a failed start, until the time given (retries are not made before it).</summary>
    private readonly Dictionary<string, DateTimeOffset> _wakeHeld = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The hits this window has started a wake for, by session and hit time. Kept in memory too, so a library that cannot be written after a start does not
    /// let the next pass start the same hit again.
    /// </summary>
    private readonly HashSet<string> _wokenHits = new(StringComparer.Ordinal);

    /// <summary>The notices already said for a hit (log and toast), by session, hit and note: each is said once.</summary>
    private readonly HashSet<string> _wakeSaid = new(StringComparer.Ordinal);

    /// <summary>The words of a session's stop for its row and for Needs you, or null when there are none.</summary>
    private string? LimitTextOf(string? sessionId) =>
        sessionId is { Length: > 0 } id && _limitTexts.TryGetValue(id, out var text) ? text : null;

    /// <summary>The words of a stop as the end of a row's line: " · " and the words, or nothing.</summary>
    private static string LimitSuffix(string? text) => string.IsNullOrEmpty(text) ? string.Empty : " · " + text;

    /// <summary>The stop of a session: the last pass's probe when it probed the session, else the digest's (as of the history's read).</summary>
    private LimitStop? StopOf(string sessionId, SessionDigest? digest) =>
        _limitStops.TryGetValue(sessionId, out var probed) ? probed : digest?.LimitStop;

    /// <summary>The past sessions by id (the history's first digest of each id). Built per call: the history is read rarely, the rail often.</summary>
    private Dictionary<string, SessionDigest> DigestsById()
    {
        var byId = new Dictionary<string, SessionDigest>(StringComparer.OrdinalIgnoreCase);
        foreach (var digest in _history ?? new List<SessionDigest>()) byId.TryAdd(digest.SessionId, digest);
        return byId;
    }

    /// <summary>The ids the owner flagged for a wake, as the marks were last read.</summary>
    private List<string> FlaggedIds() => _marks.Where(pair => pair.Value.AutoWake).Select(pair => pair.Key).ToList();

    /// <summary>
    /// The words each session with a stop shows, drawn from the stops and the owner's flags as they are now. Called by the rail's refresh, so no
    /// transcript is read here (the folder check is made only for a flagged session that is due, and the limit in force is the last one read).
    /// </summary>
    private IReadOnlyDictionary<string, string> LimitTextsFor(IReadOnlyDictionary<string, SessionRecord> records, DateTimeOffset now)
    {
        var digests = DigestsById();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records.Values)
        {
            if (record.SessionId.Length > 0) ids.Add(record.SessionId);
        }
        foreach (var digest in digests.Values)
        {
            if (digest.LimitStop is not null) ids.Add(digest.SessionId);
        }
        foreach (var id in _limitStops.Keys) ids.Add(id);

        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            digests.TryGetValue(id, out var digest);
            var stop = StopOf(id, digest);
            if (stop is null) continue;
            try
            {
                if (LimitWakeText.Line(stop, DecisionFor(id, stop, now), now) is { } text) texts[id] = text;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A session whose text cannot be made shows no text; the rail is still drawn. Logged once per session and cause.
                if (_wakeSaid.Add("text|" + id + "|" + ex.GetType().Name)) AppLog.Write($"limit text not made: {ex.GetType().Name}");
            }
        }
        return texts;
    }

    /// <summary>
    /// The rule's decision for a session with a stop. A session the owner did not flag has none (null). Until the listing has been read, a flagged
    /// session waits: nothing is decided on a listing that is not there.
    /// </summary>
    private LimitWakeDecision? DecisionFor(string sessionId, LimitStop stop, DateTimeOffset now)
    {
        var mark = MarkOf(sessionId);
        if (mark?.AutoWake != true) return null;
        if (!_listingKnown) return new LimitWakeDecision(LimitWakeAction.Wait);

        return LimitWake.Decide(new LimitWakeInput(
            stop,
            AutoWake: true,
            Now: now,
            Listing: ListingOf(sessionId),
            AutoWokenHitAt: mark.AutoWokenHitAt,
            AutoWakeTimes: mark.AutoWakeTimes,
            LimitInForce: () => _limitInForce,
            Folder: () => FolderFor(sessionId),
            KeptInRail: _railRecords.Values.Any(record => SameId(record.SessionId, sessionId))));
    }

    /// <summary>How the pass's listing (with the finished sessions) shows a session: not listed, listed as stopped or failed, or listed alive.</summary>
    private WakeListing ListingOf(string sessionId)
    {
        var listed = _allAgents.FirstOrDefault(a => SameId(a.SessionId, sessionId));
        if (listed is null) return WakeListing.NotListed;
        var state = listed.EffectiveState;
        return string.Equals(state, "stopped", StringComparison.OrdinalIgnoreCase) || string.Equals(state, "failed", StringComparison.OrdinalIgnoreCase)
            ? WakeListing.Stopped
            : WakeListing.Alive;
    }

    /// <summary>
    /// The folder a wake starts the session in: the first folder its transcript names, as the owner's resume takes it. Checked as a start checks it, so a
    /// missing or untrusted folder gives the reason. The transcript's head is read, not the whole file.
    /// </summary>
    private WakeFolder FolderFor(string sessionId)
    {
        var folder = FolderOfTranscript(sessionId);
        if (folder is null) return new WakeFolder(false, "the session's folder is not in its transcript");
        var check = FolderCheck.Check(folder, TrustCheck.DefaultClaudeJsonPath);
        return check.Status is FolderStatus.Missing or FolderStatus.NotTrusted ? new WakeFolder(false, check.Message) : new WakeFolder(true, null);
    }

    /// <summary>The first folder named in the session's transcript, or null when the transcript is not known or names none.</summary>
    private string? FolderOfTranscript(string sessionId)
    {
        var path = _transcriptPaths.TryGetValue(sessionId, out var known) ? known : DigestsById().TryGetValue(sessionId, out var digest) ? digest.FilePath : null;
        if (path is null) return null;
        try
        {
            return ResumeSource.WorkingDirectoryOf(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// What the pass read of the limits on a pool thread: the stops of its candidates, and the limit in force when it was read. Unknown holds the ids
    /// whose transcript could not be read while no stop was known for them (see <see cref="UnknownLimitHold"/>).
    /// </summary>
    internal sealed record LimitProbe(Dictionary<string, LimitStop?> Stops, bool InForceRead, DateTimeOffset? InForce, IReadOnlySet<string> Unknown);

    /// <summary>
    /// Reads the stop at the end of each candidate's transcript, and, when a flagged session with a stop is due and the last read is old, the reset of the
    /// limit in force. Touches no window object. The three outcomes of a read are kept apart: a stop found; a read with no stop, which clears a kept
    /// stop; and a read that could not be made, which keeps the id's stop from <paramref name="previous"/> when there is one, and otherwise marks the id
    /// unknown (held, fail closed). A candidate whose transcript is not known (a new session) and that had no stop has no stop and no hold.
    /// </summary>
    internal static LimitProbe ProbeLimits(
        IReadOnlyList<string> candidates,
        IReadOnlyDictionary<string, string> pathOf,
        ISet<string> flagged,
        string projectsRoot,
        DateTimeOffset now,
        bool readInForce,
        IReadOnlyDictionary<string, LimitStop?> previous)
    {
        var stops = new Dictionary<string, LimitStop?>(StringComparer.OrdinalIgnoreCase);
        var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in candidates)
        {
            var kept = previous.TryGetValue(id, out var before) ? before : null;
            var path = pathOf.TryGetValue(id, out var known) ? known : TranscriptReader.FindTranscript(projectsRoot, id);
            if (path is null || !File.Exists(path))
            {
                // No transcript is known for the id now, or its file is not there (deleted or moved; an unreadable file still exists, so it is not this case): a stop known before stays. A session with none is new, so it has no stop and no hold.
                if (kept is not null) stops[id] = kept;
                continue;
            }

            if (LimitStop.TryReadTail(path, out var stop))
            {
                stops[id] = stop;
            }
            else if (kept is not null)
            {
                stops[id] = kept;
            }
            else
            {
                // The state cannot be established and no stop was known: the id is held (fail closed) until a read succeeds.
                stops[id] = null;
                unknown.Add(id);
            }
        }

        var due = stops.Any(pair => pair.Value is { } stop && flagged.Contains(pair.Key) && stop.IsRecent(now)
                                    && now >= LimitReset.StartFor(stop.ResetsAt));
        if (!readInForce || !due) return new LimitProbe(stops, false, null, unknown);
        return new LimitProbe(stops, true, LimitReset.Find(projectsRoot, now), unknown);
    }

    /// <summary>
    /// The pass's probe: the stops of its supervised sessions and the flagged ones, read on a pool thread, and the limit in force when one is due. The
    /// rail and Needs you are drawn from it right after.
    /// </summary>
    private async Task ProbeLimitsAsync(IReadOnlyDictionary<string, SessionRecord> records, DateTimeOffset now)
    {
        var flagged = FlaggedIds();
        var candidates = records.Values.Select(record => record.SessionId).Where(id => id.Length > 0).Concat(flagged)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var pathOf = new Dictionary<string, string>(_transcriptPaths, StringComparer.Ordinal);
        var digests = DigestsById();
        foreach (var id in flagged)
        {
            if (!pathOf.ContainsKey(id) && digests.TryGetValue(id, out var digest)) pathOf[id] = digest.FilePath;
        }
        var readInForce = _limitInForceRead is not { } last || now - last >= InForceEvery;
        var root = _options.ProjectsRoot;
        var flaggedSet = flagged.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previous = _limitStops;

        // The feature must not fail the pass: a read that throws is logged and the last values are kept.
        try
        {
            var probe = await Task.Run(() => ProbeLimits(candidates, pathOf, flaggedSet, root, now, readInForce, previous));
            _limitStops = probe.Stops;
            _limitUnknown = probe.Unknown;
            foreach (var id in probe.Unknown)
            {
                if (_wakeSaid.Add("unknown|" + id)) AppLog.Write("limit state unknown: transcript not readable; the session is held");
            }
            if (probe.InForceRead)
            {
                _limitInForce = probe.InForce;
                _limitInForceRead = now;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"limit stops not read: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// The wake step of a pass: each session the owner flagged that has a stop gets the rule's decision, and the decision is carried out. A start happens
    /// only here, once per hit; a notice is said once. Returns true when a session was started, so the caller draws the rail again.
    /// </summary>
    private async Task<bool> WakeDueSessionsAsync(DateTimeOffset now, bool trustKnown)
    {
        // A window that only watches never starts a session; nor does one whose trust is unknown, or that is in a resume, a respawn or a close.
        if (_supervisor is null || _watching || _windowClosing || _sessionsClosing > 0 || _resuming || _respawnHold.IsHeld || !_listingKnown || !trustKnown)
            return false;

        var started = false;
        foreach (var id in FlaggedIds())
        {
            try
            {
                if (_wakeHeld.TryGetValue(id, out var until) && now < until) continue;
                var stop = StopOf(id, DigestsById().GetValueOrDefault(id));
                if (stop is null || _wokenHits.Contains(HitKey(id, stop))) continue;

                var decision = DecisionFor(id, stop, now);
                if (decision is null) continue;
                switch (decision.Action)
                {
                    case LimitWakeAction.WakeNow:
                        started |= await WakeOneAsync(id, stop, now);
                        break;
                    case LimitWakeAction.Notify:
                        SayOnce(id, stop, decision, now);
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                AppLog.Write($"limit wake not run: {ex.GetType().Name}");
            }
        }
        return started;
    }

    /// <summary>The key of one hit of one session: the session and the time its limit was hit.</summary>
    private static string HitKey(string sessionId, LimitStop stop) =>
        sessionId + "|" + stop.HitAt.UtcTicks.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Starts a session again, once, with the fixed line: the same guards and start as the owner's resume, with the saved solo model and effort (the
    /// resume dialog's default). A refusal or a failed start holds the session for <see cref="WakeRetryHold"/> and does not mark the hit. A start that
    /// returned an id marks the hit as woken, in memory and in the library. Returns true when the session was started.
    /// </summary>
    private async Task<bool> WakeOneAsync(string sessionId, LimitStop stop, DateTimeOffset now)
    {
        var folder = FolderOfTranscript(sessionId);
        if (folder is null) return false;

        var saved = SavedSolo();
        FullResume start;
        _resuming = true;
        try
        {
            start = await StartFullResumeAsync(sessionId, folder, saved.PlannerModel, saved.PlannerEffort, LimitWake.Line, includeCompleted: true, progress: null);
        }
        finally
        {
            _resuming = false;
        }

        if (start.Refusal is not null)
        {
            _wakeHeld[sessionId] = now + WakeRetryHold;
            AppLog.Write($"limit wake not started, retry in {WakeRetryHold.TotalMinutes:0} min");
            return false;
        }

        _wokenHits.Add(HitKey(sessionId, stop));
        _wakeHeld.Remove(sessionId);
        RecordWake(sessionId, stop.HitAt, now);
        AppLog.Write($"limit wake: hit {stop.HitAt:O}, reset {stop.ResetsAt:O}, started by resume in full"
                     + (start.Recorded ? string.Empty : "; not recorded as kept alive"));
        SetStatus(start.Recorded
            ? $"Woke {ShortId(sessionId)} after its limit reset, as {start.Name}."
            : $"Woke {ShortId(sessionId)} after its limit reset as {start.Name}, but it is not recorded. Check the log.");
        return true;
    }

    /// <summary>The owner's marks say the hit was woken, and when: the library entry of the session records the hit and the wake time.</summary>
    private void RecordWake(string sessionId, DateTimeOffset hitAt, DateTimeOffset now)
    {
        try
        {
            _library.Update(entries =>
            {
                var current = entries.TryGetValue(sessionId, out var found) ? found : new LibraryEntry();
                entries[sessionId] = current with
                {
                    AutoWokenHitAt = hitAt,
                    AutoWakeTimes = LimitWake.WithWake(current.AutoWakeTimes, now),
                };
            });
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"limit wake not recorded in the library: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// The notice of a decision that does not start the session: logged once for the hit, and, for a session that is still running after its reset, a
    /// silent toast once when the owner's switches allow it. Quiet hours only log it.
    /// </summary>
    private void SayOnce(string sessionId, LimitStop stop, LimitWakeDecision decision, DateTimeOffset now)
    {
        var key = HitKey(sessionId, stop) + "|" + decision.Note;
        if (!_wakeSaid.Add(key)) return;

        AppLog.Write(decision.Note?.StartsWith("not woken: ", StringComparison.Ordinal) == true ? "limit wake not started: the session's folder cannot be used" : $"limit wake not started: {decision.Note}");
        if (decision.Note != LimitWake.RunningNote) return;
        // The toast is the one that says the session can continue, and it goes only when it is due and the owner's switches allow it.

        var quiet = _passSettings.IsQuietAt(now);
        if (IsActive || quiet || !_passSettings.Notify || !_passSettings.Toast) return;
        if (!AnnouncePolicy.SilentToastShown(_passSettings, ToastCategory.LimitReset, IsActive, quiet)) return;
        // The focus gate also holds it back for the selected session (behaviour.json, R2); the toast names that session for a click.
        if (!AlertRules.Shows(AlertGroup.Notice, AlertTrigger.Change, AlertFocusNow(), sessionId, _behaviour.Alerts)) return;
        ToastNotifier.ShowLimitWake(LiveTitleOf(sessionId), sessionId);
    }

    /// <summary>
    /// The usage-limit holds the supervisor's next pass is held to (review item 1, 2026-11-21): each supervised session whose stop the last probe read,
    /// when that stop is recent and its reset is still ahead, by the session's name. The supervisor does not resume such a session before the reset.
    /// A session with no stop, an old stop, or a reset already passed has no hold, unless its limit state is unknown (<paramref name="unknown"/>, third
    /// review, 2026-11-21): then it is held for <see cref="UnknownLimitHold"/> from this pass, so an unreadable transcript never frees a session early.
    /// </summary>
    internal static IReadOnlyDictionary<string, DateTimeOffset> LimitHoldsOf(
        IReadOnlyDictionary<string, SessionRecord> records,
        IReadOnlyDictionary<string, LimitStop?> stops,
        DateTimeOffset now,
        IReadOnlySet<string>? unknown = null)
    {
        var holds = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var record in records.Values)
        {
            if (record.SessionId.Length == 0) continue;
            if (stops.TryGetValue(record.SessionId, out var stop) && stop is { } found && found.IsRecent(now) && found.ResetsAt > now)
                holds[record.Name] = found.ResetsAt;
            else if (unknown is not null && unknown.Contains(record.SessionId))
                holds[record.Name] = now + UnknownLimitHold;
        }
        return holds;
    }

    /// <summary>The owner's name for a session the window supervises, else its short id.</summary>
    private string LiveTitleOf(string sessionId) =>
        _railRecords.Values.FirstOrDefault(record => SameId(record.SessionId, sessionId)) is { } record
            ? LiveTitle(record)
            : ShortId(sessionId);
}

/// <summary>
/// The row menu's checkable item "Wake after the limit resets" (limit wake, 2026-11-20). Its own class, so a test can click it without a window.
/// </summary>
internal static class WakeMenuItem
{
    /// <summary>The item's text.</summary>
    public const string Header = "Wake after the limit resets";

    /// <summary>What the item does, as the owner reads it in the tooltip.</summary>
    public const string Tip =
        "When this session has stopped at the usage limit and the limit has reset, Bullshark starts it again with the line: " +
        "'The usage limit has reset. Please continue where you left off.' Once per limit. Never for a session you stopped.";

    /// <summary>The item, checked when the owner has chosen it. A click passes the new value to <paramref name="change"/>.</summary>
    public static MenuItem Create(bool on, Action<bool> change)
    {
        var item = new MenuItem
        {
            Header = Header,
            IsCheckable = true,
            IsChecked = on,
            Padding = new Thickness(10, 4, 16, 4),
            ToolTip = Tip,
        };
        item.Click += (_, _) => change(!on);
        return item;
    }
}
