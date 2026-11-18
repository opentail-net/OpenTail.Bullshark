using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Library;

/// <summary>
/// Which past sessions the magic rename may name (magic rename, D1, D2 and D7). Pure: the history, the owner's marks, the live session
/// ids and the clock are passed in. A live session, a subagent transcript, an archived session and a session with a Claude custom title are
/// never named. The owner's own name is never replaced by the batch; a single session may be renamed with the owner's confirmation.
/// </summary>
public static class MagicCandidates
{
    /// <summary>A session with no message for this long counts as gone cold (D1).</summary>
    public static readonly TimeSpan ColdAge = TimeSpan.FromHours(2);

    /// <summary>A session with fewer messages than this is too short to name (D1).</summary>
    public const int MinMessages = 4;

    /// <summary>How many automatic names the window makes in an hour (D7c). The count is of every magic name in the library, by its time.</summary>
    public const int AutoPerHour = 3;

    /// <summary>What the row menu offers for one past session (D7a, D2).</summary>
    public enum RenameKind
    {
        /// <summary>No magic rename is offered.</summary>
        None,

        /// <summary>The session has no name: "Magic rename".</summary>
        Fresh,

        /// <summary>The session has a magic name and has had messages since it was named: "Magic rename again".</summary>
        Again,

        /// <summary>The session has the owner's own name: "Magic rename", after a confirm that says the name will be replaced.</summary>
        ReplaceManual,
    }

    /// <summary>The rules that hold for every magic rename: not a subagent, not archived, not live, with a last message, and no custom title.</summary>
    public static bool Eligible(SessionDigest digest, LibraryEntry? mark, ISet<string> live) =>
        !digest.IsSubagent
        && mark?.Archived != true
        && !live.Contains(digest.SessionId)
        && digest.LastActivity is not null
        && IsBlank(digest.CustomTitle);

    /// <summary>What the row menu offers for a past session, for one rename (D7a). The 2-hour rule does not apply to a single rename.</summary>
    public static RenameKind KindFor(SessionDigest digest, LibraryEntry? mark, ISet<string> live)
    {
        if (!Eligible(digest, mark, live)) return RenameKind.None;
        if (IsBlank(mark?.Name)) return RenameKind.Fresh;
        if (mark!.MagicAt is not { } magicAt) return RenameKind.ReplaceManual;
        return digest.LastActivity > magicAt ? RenameKind.Again : RenameKind.None;
    }

    /// <summary>
    /// True when a session is cold and may be named by the batch or automatically (D1): eligible, with no name of any kind, not yet named by
    /// magic, at least <see cref="MinMessages"/> messages, and its last message older than <see cref="ColdAge"/>.
    /// </summary>
    public static bool IsCold(SessionDigest digest, LibraryEntry? mark, ISet<string> live, DateTimeOffset now) =>
        Eligible(digest, mark, live)
        && IsBlank(mark?.Name)
        && mark?.MagicAt is null
        && digest.UserMessages + digest.AssistantMessages >= MinMessages
        && now - digest.LastActivity!.Value > ColdAge;

    /// <summary>The cold sessions, the most recent first.</summary>
    public static IReadOnlyList<SessionDigest> Cold(
        IEnumerable<SessionDigest> history,
        IReadOnlyDictionary<string, LibraryEntry> marks,
        ISet<string> live,
        DateTimeOffset now) =>
        history
            .Where(digest => IsCold(digest, MarkOf(marks, digest.SessionId), live, now))
            .OrderByDescending(digest => digest.LastActivity)
            .ToList();

    /// <summary>
    /// The names the library already gives sessions of one project folder, so the prompt can show them (D3). At most
    /// <see cref="MagicName.MaxUsedNames"/>, most recent first.
    /// </summary>
    public static IReadOnlyList<string> NamesInFolder(
        IEnumerable<SessionDigest> history,
        IReadOnlyDictionary<string, LibraryEntry> marks,
        string projectFolder)
    {
        var sessions = history
            .Where(digest => string.Equals(digest.ProjectFolder, projectFolder, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(digest => digest.LastActivity);
        return sessions
            .Select(digest => MarkOf(marks, digest.SessionId)?.Name)
            .Where(name => !IsBlank(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MagicName.MaxUsedNames)
            .ToList();
    }

    /// <summary>Every name the library holds, compared without case: the names a new magic name must not repeat (D3).</summary>
    public static HashSet<string> TakenNames(IReadOnlyDictionary<string, LibraryEntry> marks, IEnumerable<string> exceptSessionIds)
    {
        var except = exceptSessionIds.ToHashSet(StringComparer.Ordinal);
        return marks
            .Where(pair => !except.Contains(pair.Key) && !IsBlank(pair.Value.Name))
            .Select(pair => pair.Value.Name!.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>How many magic names were given at or after <paramref name="since"/>, by the time they were given (D7c).</summary>
    public static int NamedSince(IReadOnlyDictionary<string, LibraryEntry> marks, DateTimeOffset since) =>
        marks.Values.Count(mark => !IsBlank(mark.Name) && mark.MagicNamedAt is { } at && at >= since);

    private static LibraryEntry? MarkOf(IReadOnlyDictionary<string, LibraryEntry> marks, string sessionId) =>
        marks.TryGetValue(sessionId, out var mark) ? mark : null;

    private static bool IsBlank(string? text) => string.IsNullOrWhiteSpace(text);
}
