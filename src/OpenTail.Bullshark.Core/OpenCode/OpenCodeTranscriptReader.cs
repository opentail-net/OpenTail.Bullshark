using Microsoft.Data.Sqlite;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.OpenCode;

/// <summary>
/// The lines of an opencode session, in order (OC-D5): one line per text part of a user message (<c>you: text</c>) or of an assistant message
/// (<c>opencode: text</c>). A synthetic part is left out; an ignored part is kept; tool and other parts are left out. The transcript shows at most
/// <see cref="MaxLines"/> lines, the first ones, as the Copilot reader does. A reader made with another line limit reads that many: the resume passes
/// <c>int.MaxValue</c>, so its summary and tail come from the whole session. An invalid id, a missing or wrong database, or a database error gives an
/// empty page; the id is checked before the database is opened. A message or part row whose data is not valid JSON is left out (json_valid), and the rest
/// of the transcript is read.
/// </summary>
public sealed class OpenCodeTranscriptReader(DatabasePath location, int lineLimit = TranscriptLimits.MaxMessages) : ITranscriptReader, IMessageTextSource
{
    /// <summary>The most lines a transcript shows: the shared cap (<see cref="TranscriptLimits.MaxMessages"/>).</summary>
    public const int MaxLines = TranscriptLimits.MaxMessages;

    /// <summary>The messages of a session, in order: the id and the line kind (<c>you</c> or <c>opencode</c>), null for any other role.</summary>
    private const string MessagesSql = "SELECT id, json_extract(data, '$.role') FROM message WHERE session_id = @sessionId AND json_valid(data) = 1 ORDER BY time_created, id";

    /// <summary>
    /// The text parts of a session, in order. A part with <c>synthetic</c> true is left out; a part with no <c>synthetic</c> key is kept (IFNULL), as is
    /// an ignored part.
    /// </summary>
    private const string TextPartsSql =
        "SELECT message_id, json_extract(data, '$.text') FROM part WHERE session_id = @sessionId AND json_valid(data) = 1 "
        + "AND json_extract(data, '$.type') = 'text' AND IFNULL(json_extract(data, '$.synthetic'), 0) <> 1 ORDER BY time_created, id";

    /// <summary>
    /// A page of lines from <paramref name="firstLine"/> (zero-based), at most <paramref name="count"/> of them. <c>FirstLine</c> is clamped to the line
    /// count, a negative count gives no lines, and <c>TotalLines</c> is null (the page is cut at the cap, and the count is not reported).
    /// </summary>
    public Task<TranscriptPage> ReadPageAsync(string sessionId, int firstLine, int count, CancellationToken ct) => Task.Run(() =>
    {
        if (!OpenCodeSessionId.IsValid(sessionId)) return EmptyPage();
        var lines = ReadLines(sessionId, ct);
        var start = Math.Min(Math.Max(0, firstLine), lines.Count);
        return new TranscriptPage(lines.Skip(start).Take(Math.Max(0, count)).ToList(), start, null);
    }, ct);

    /// <summary>
    /// The text of every text part of every message of the session, in order, with no cap (PP-D4). Synthetic parts are left out, as in the transcript. An
    /// unsafe id gives no texts; a database that is not read gives no texts (the same rule as the transcript).
    /// </summary>
    public Task<IReadOnlyList<string>> MessageTextsAsync(string sessionId, CancellationToken ct) => Task.Run<IReadOnlyList<string>>(() =>
    {
        if (!OpenCodeSessionId.IsValid(sessionId)) return [];
        var (_, connection) = OpenCodeDatabase.Open(location);
        if (connection is null) return [];

        try
        {
            using (connection)
            {
                var messages = ReadMessages(connection, sessionId, ct);
                var textParts = ReadTextParts(connection, sessionId, ct);
                var texts = new List<string>();
                foreach (var (messageId, kind) in messages)
                {
                    if (kind is null || !textParts.TryGetValue(messageId, out var parts)) continue;
                    texts.AddRange(parts);
                }
                return texts;
            }
        }
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }, ct);

    private static TranscriptPage EmptyPage() => new([], 0, null);

    /// <summary>The session's lines, at most the reader's line limit (<see cref="MaxLines"/> unless given). Empty when the database is not read (its status is not Ok) or a read fails.</summary>
    private List<string> ReadLines(string sessionId, CancellationToken ct)
    {
        var (_, connection) = OpenCodeDatabase.Open(location);
        if (connection is null) return [];

        try
        {
            using (connection)
            {
                var messages = ReadMessages(connection, sessionId, ct);
                var textParts = ReadTextParts(connection, sessionId, ct);
                var lines = new List<string>();
                foreach (var (messageId, kind) in messages)
                {
                    if (kind is null || !textParts.TryGetValue(messageId, out var parts)) continue;
                    foreach (var text in parts)
                    {
                        if (lines.Count >= lineLimit) return lines;
                        lines.Add(kind + ": " + text);
                    }
                }
                return lines;
            }
        }
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static List<(string MessageId, string? Kind)> ReadMessages(SqliteConnection connection, string sessionId, CancellationToken ct)
    {
        var messages = new List<(string MessageId, string? Kind)>();
        using var command = connection.CreateCommand();
        command.CommandText = MessagesSql;
        command.Parameters.AddWithValue("@sessionId", sessionId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.GetValue(0) is not string id) continue;
            messages.Add((id, KindOf(reader.GetValue(1) as string)));
        }
        return messages;
    }

    /// <summary>The text parts of the session, grouped by message id, in the query's order.</summary>
    private static Dictionary<string, List<string>> ReadTextParts(SqliteConnection connection, string sessionId, CancellationToken ct)
    {
        var textParts = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.CommandText = TextPartsSql;
        command.Parameters.AddWithValue("@sessionId", sessionId);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            if (reader.GetValue(0) is not string messageId || reader.GetValue(1) is not string text) continue;
            if (!textParts.TryGetValue(messageId, out var parts))
            {
                parts = new List<string>();
                textParts[messageId] = parts;
            }
            parts.Add(text);
        }
        return textParts;
    }

    /// <summary>The line kind of a message role: <c>you</c> for a user, <c>opencode</c> for an assistant, null for any other role.</summary>
    private static string? KindOf(string? role) => role switch
    {
        "user" => "you",
        "assistant" => "opencode",
        _ => null,
    };
}
