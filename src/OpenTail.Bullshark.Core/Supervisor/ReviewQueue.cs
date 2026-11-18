using System.Globalization;
using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Claude;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>
/// What the window reads about a finished coder: how many files its transcript says it wrote, and when its transcript was last written (which is
/// when it finished, when nothing ran after it).
/// </summary>
public sealed record ReviewSource(int FilesWritten, DateTimeOffset LastActivity);

/// <summary>One finished coder that the owner has not looked at yet: its name, its session, its file count and when it finished.</summary>
public sealed record ReviewItem(string Name, string SessionId, int FilesWritten, DateTimeOffset FinishedAt);

/// <summary>
/// The "Ready for review" lane (ready-for-review-2026-11-18, D1 and D3): a coder that is listed as `done`, wrote at least one file, and has had
/// no activity since the owner last marked it reviewed. It is pure: the window passes in what it read, and the list is shown apart from
/// "Needs you". It never joins <see cref="AttentionList"/>, and nothing here is sent to a session.
/// </summary>
public static class ReviewQueue
{
    /// <summary>
    /// The coders that are ready, oldest finish first (then by name). A coder with no <paramref name="sources"/> entry has no transcript read, so
    /// it is not ready. A coder whose <see cref="ReviewSource.LastActivity"/> is not later than its mark is not ready; a later one is (it did more work).
    /// </summary>
    public static IReadOnlyList<ReviewItem> From(
        IEnumerable<SessionRecord> records,
        IReadOnlyList<AgentInfo> agents,
        IReadOnlyDictionary<string, ReviewSource> sources,
        IReadOnlyDictionary<string, DateTimeOffset> reviewed)
    {
        var items = new List<ReviewItem>();
        foreach (var record in records)
        {
            if (record.Role != SessionRole.Coder || string.IsNullOrEmpty(record.SessionId)) continue;
            var listed = agents.FirstOrDefault(a => string.Equals(a.SessionId, record.SessionId, StringComparison.OrdinalIgnoreCase));
            if (listed is null || !string.Equals(listed.EffectiveState, "done", StringComparison.OrdinalIgnoreCase)) continue;
            if (!sources.TryGetValue(record.SessionId, out var source) || source.FilesWritten <= 0) continue;
            if (reviewed.TryGetValue(record.SessionId, out var mark) && source.LastActivity <= mark) continue;
            items.Add(new ReviewItem(record.Name, record.SessionId, source.FilesWritten, source.LastActivity));
        }
        return items
            .OrderBy(item => item.FinishedAt)
            .ThenBy(item => item.Name, StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>
/// The reviewed marks, kept in ui.json under <c>reviewed</c>: an object from session id to the ISO time of the session's last activity when the
/// owner marked it. Read tolerantly: a missing, unreadable or non-object value gives no marks, and an entry that is not a time is skipped.
/// </summary>
public static class ReviewedMarks
{
    /// <summary>The marks in a ui.json value. Null or a bad value gives none.</summary>
    public static Dictionary<string, DateTimeOffset> Parse(JsonNode? value)
    {
        var marks = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        if (value is not JsonObject obj) return marks;
        foreach (var pair in obj)
        {
            if (pair.Value is JsonValue json && json.TryGetValue<string>(out var text)
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at))
                marks[pair.Key] = at;
        }
        return marks;
    }

    /// <summary>The marks as the object written to ui.json. A new object each time, since a JSON node has one parent.</summary>
    public static JsonObject Write(IReadOnlyDictionary<string, DateTimeOffset> marks)
    {
        var obj = new JsonObject();
        foreach (var pair in marks.OrderBy(p => p.Key, StringComparer.Ordinal))
            obj[pair.Key] = JsonValue.Create(pair.Value.ToString("o", CultureInfo.InvariantCulture));
        return obj;
    }
}
