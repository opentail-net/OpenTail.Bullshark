using System.Globalization;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The pure parts of a provider's transcript view (parity P7, decisions T-D1, T-D2 and T-D5): the cap, the count and cap lines, the scope line, the time
/// label and the plain-text copy. No window is made, so these are tested alone. Codex and Copilot share them; each message's label is the one its reader gives.
/// </summary>
public static class ProviderTranscriptText
{
    /// <summary>
    /// The most messages the view holds (T-D5). The view keeps its messages in memory and draws each one, so a very long session is cut here and the
    /// view says so, which keeps a long session from freezing the window. A longer session shows its first messages only. The Copilot reader caps its
    /// read at the same number (CopilotTranscriptReader.MaxMessages, 5000).
    /// </summary>
    public const int MaxMessages = 5000;

    /// <summary>The line at the top of the view: what is left out (T-D1). Tool calls and their results are not in this version (R-D5).</summary>
    public const string ScopeLine = "Messages only. Tool calls and their output are not shown.";

    /// <summary>The first <paramref name="max"/> messages, in order. The same list when there are not that many more than it.</summary>
    public static IReadOnlyList<TranscriptLine> Capped(IReadOnlyList<TranscriptLine> lines, int max = MaxMessages) =>
        lines.Count <= max ? lines : lines.Take(max).ToList();

    /// <summary>"0 messages", "1 message", "4 messages", "5,001 messages".</summary>
    public static string MessageCount(int total) => total == 1 ? "1 message" : $"{Number(total)} messages";

    /// <summary>The line at the end of a capped view, "Showing the first 5,000 of 5,001 messages.", or null when every message is shown.</summary>
    public static string? CapNote(int total, int shown) =>
        total > shown ? $"Showing the first {Number(shown)} of {Number(total)} messages." : null;

    /// <summary>
    /// The copy of the messages: each as "label: text" (the label is the reader's, for example "you"), the blocks separated by a blank line. Empty for no messages. The text of a message
    /// keeps its own line breaks.
    /// </summary>
    public static string PlainText(IReadOnlyList<TranscriptLine> lines)
    {
        var blank = Environment.NewLine + Environment.NewLine;
        return string.Join(blank, lines.Select(line => $"{line.Kind}: {line.Text}"));
    }

    /// <summary>A message's time as the view shows it: local time, "yyyy-MM-dd HH:mm" as the Away and Limits views show it. Empty when the record has none.</summary>
    public static string TimeLabel(DateTimeOffset? at) =>
        at is { } when ? when.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : string.Empty;

    private static string Number(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
