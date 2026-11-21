using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Setup;

/// <summary>
/// A session that stopped at a usage limit (limit wake, 2026-11-20): when the limit was hit (<see cref="HitAt"/>, the timestamp of the limit entry),
/// when it resets (<see cref="ResetsAt"/>, from the entry's <c>rate_limit_info</c>), and the limit type as Claude Code wrote it. The stop lasts until
/// a record that is not a limit entry follows: a user entry that is not a tool result (a wake, or the owner's own message), or any other assistant
/// entry. Metadata records in between do not end it.
/// </summary>
public sealed record LimitStop(DateTimeOffset HitAt, DateTimeOffset ResetsAt, string Type)
{
    /// <summary>
    /// The stop at the end of the lines, or null. The lines are read in order by <see cref="LimitStopTracker"/>, the same rule the index's
    /// digest uses. A line that is not JSON is skipped.
    /// </summary>
    public static LimitStop? Find(IEnumerable<string> lines)
    {
        var tracker = new LimitStopTracker();
        foreach (var line in lines)
        {
            if (!TranscriptJson.TryParse(line, out var document)) continue;
            using (document) tracker.Observe(document.RootElement);
        }
        return tracker.Current;
    }

    /// <summary>
    /// The stop at the end of a transcript, read from its last bytes only (the same window as <see cref="LimitEntries.Tail"/>). A stop is recent
    /// in that window or not at all. A file that cannot be opened or read gives null.
    /// </summary>
    public static LimitStop? ReadTail(string path) => TryReadTail(path, out var stop) ? stop : null;

    /// <summary>
    /// The same read as <see cref="ReadTail"/>, with a failed read told apart from "no stop": true when the file was read (<paramref name="stop"/> is
    /// the stop, or null when there is none), false when it could not be opened or read (<paramref name="stop"/> is null then). A transcript that
    /// cannot be read says nothing about whether its session is stopped, so the limit probe needs the difference.
    /// </summary>
    public static bool TryReadTail(string path, out LimitStop? stop)
    {
        try
        {
            stop = Find(LimitEntries.TailLines(path));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            stop = null;
            return false;
        }
    }

    /// <summary>
    /// Whether the stop is recent enough to show or to wake: its limit was hit within <see cref="LimitEntries.LimitWindow"/> of <paramref name="now"/>.
    /// An older stop is not shown and not woken, since a limit lasts about a week at most.
    /// </summary>
    public bool IsRecent(DateTimeOffset now) => now - HitAt <= LimitEntries.LimitWindow;
}

/// <summary>
/// The state of one transcript's stop, record by record, in file order. The last usage-limit entry is the candidate: a stop when its status is
/// "rejected" and its time can be read, and no later user entry (other than a tool result) or other assistant entry has come after it. A usage-limit
/// entry that follows another replaces it (a retry is one hit, the last counts). A record whose values cannot be read changes nothing. Used by
/// <see cref="LimitStop.Find"/> and by the index's summary, so both give the same stop.
/// </summary>
internal sealed class LimitStopTracker
{
    private const string Rejected = "rejected";

    /// <summary>The last usage-limit entry seen, or null when the last assistant or non-tool-result user record ended the stop.</summary>
    private LimitEntry? _last;

    /// <summary>The stop as the records read so far give it, or null.</summary>
    public LimitStop? Current => _last is { Status: Rejected, Written: { } hit } last ? new LimitStop(hit, last.ResetsAt, last.Type) : null;

    /// <summary>Takes one parsed record in file order.</summary>
    public void Observe(JsonElement root)
    {
        try
        {
            var type = TranscriptJson.Str(root, "type");
            if (type == "assistant")
            {
                // A usage-limit error is the limit entry itself. Any other assistant entry ends a stop.
                _last = TranscriptJson.Str(root, "apiError") == LimitEntries.LimitError ? LimitEntries.OfRecord(root) : null;
            }
            else if (type == "user" && !IsToolResult(root))
            {
                // A user entry that is not a tool result is a wake, or a message the owner typed.
                _last = null;
            }
        }
        catch (Exception ex) when (TranscriptJson.IsUnreadable(ex))
        {
            // A record whose values cannot be read counts for nothing.
        }
    }

    /// <summary>True when the user record's message carries a tool result block.</summary>
    private static bool IsToolResult(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object) return false;
        if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) return false;
        foreach (var block in content.EnumerateArray())
        {
            if (TranscriptJson.Str(block, "type") == "tool_result") return true;
        }
        return false;
    }
}
