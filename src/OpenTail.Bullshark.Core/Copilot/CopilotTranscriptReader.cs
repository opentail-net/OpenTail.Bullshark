using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>
/// The messages of a Copilot session, in order (G1.3, G-D17). A message is a <c>user.message</c> (kind <c>you</c>) or an <c>assistant.message</c>
/// (kind <c>copilot</c>), its text being <c>data.content</c>. System, tool and other events are skipped, and so is a message with no text. The
/// transcript shows at most <see cref="MaxMessages"/> messages, the first in the file (P7, as the Codex reader's pages stop when full).
/// </summary>
public sealed class CopilotTranscriptReader(ProviderPaths paths) : ITranscriptReader
{
    /// <summary>The most messages a transcript shows (P7).</summary>
    public const int MaxMessages = TranscriptLimits.MaxMessages;

    /// <summary>
    /// A page of messages from <paramref name="firstLine"/> (zero-based, a message index), at most <paramref name="count"/> of them, each as
    /// <c>you: text</c> or <c>copilot: text</c>. <c>FirstLine</c> is clamped to the message count, a negative count gives no lines, and
    /// <c>TotalLines</c> is null. An unsafe id throws <see cref="ArgumentException"/>; a session with no events file throws
    /// <see cref="FileNotFoundException"/>.
    /// </summary>
    public Task<TranscriptPage> ReadPageAsync(string sessionId, int firstLine, int count, CancellationToken ct) => Task.Run(() =>
    {
        var messages = ReadSession(paths, sessionId, ct);
        var start = Math.Max(0, firstLine);
        var take = Math.Max(0, count);
        var lines = messages.Skip(start).Take(take).Select(Render).ToList();
        return new TranscriptPage(lines, Math.Min(start, messages.Count), null);
    }, ct);

    /// <summary>
    /// The messages of one session, in file order, read from its events file. The id is checked first (an unsafe id throws
    /// <see cref="ArgumentException"/>), and a session with no events file throws <see cref="FileNotFoundException"/>. At most
    /// <paramref name="maxMessages"/> messages are kept: the transcript uses the default; a summary passes no cap, so it can keep the newest text.
    /// </summary>
    public static List<TranscriptLine> ReadSession(ProviderPaths paths, string sessionId, CancellationToken ct, int maxMessages = MaxMessages)
    {
        CopilotFiles.RequireSafeId(sessionId);
        var path = CopilotFiles.EventsPath(paths, sessionId)
            ?? throw new FileNotFoundException($"No events for session {sessionId}.", sessionId + ".jsonl");

        var messages = new List<TranscriptLine>();
        using var reader = CopilotFiles.OpenText(path);
        foreach (var line in RolloutReader.Lines(reader, ct))
        {
            if (messages.Count >= maxMessages) break;
            if (line.Overlong || string.IsNullOrWhiteSpace(line.Text)) continue;
            using var document = RolloutReader.TryParseObject(line.Text);
            if (document is null) continue;
            if (MessageOf(document.RootElement) is not { } message) continue;
            messages.Add(new TranscriptLine(messages.Count + 1, message.Kind, RolloutReader.TimestampOf(document.RootElement), message.Text, Collapsed: false));
        }
        return messages;
    }

    /// <summary>The kind and text of a message event, or null for any other event and for a message with no text.</summary>
    private static (string Kind, string Text)? MessageOf(JsonElement record)
    {
        var kind = RolloutReader.StringOf(record, "type") switch
        {
            "user.message" => "you",
            "assistant.message" => "copilot",
            _ => null,
        };
        if (kind is null) return null;
        if (!record.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return null;
        return RolloutReader.StringOf(data, "content") is { } text ? (kind, text) : null;
    }

    /// <summary>A message as the transcript and the summary show it: its kind, a colon, a space, and its text.</summary>
    internal static string Render(TranscriptLine message) => message.Kind + ": " + message.Text;
}
