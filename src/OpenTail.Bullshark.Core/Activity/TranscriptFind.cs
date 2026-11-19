using System.Text.Json;
using OpenTail.Bullshark.Core.Search;

namespace OpenTail.Bullshark.Core.Activity;

/// <summary>The lines a find matched, in file order (one-based), and whether the search stopped at the cap with more hits left.</summary>
public sealed record FindResult(IReadOnlyList<int> Lines, bool Capped);

/// <summary>
/// Find in one transcript (find and jump batch, T4). The query is one substring, matched without case, as the history search matches a word (its
/// <see cref="HistorySearch.RawProbe"/> is the cheap pre-filter). What is searched is the text a line shows, with a tool result whole: a hit in the
/// hidden part of a cut result counts. A line is a hit once, however often the query is in it. Pure: lines in, line numbers out.
/// </summary>
public static class TranscriptFind
{
    /// <summary>The most hit lines one search keeps. Past it the search stops and the result says so.</summary>
    public const int MaxHits = 2000;

    /// <summary>The hit lines of a transcript for a query. Throws <see cref="OperationCanceledException"/> when the token is cancelled.</summary>
    public static FindResult Run(IEnumerable<string> lines, string query, CancellationToken cancellationToken = default, int maxHits = MaxHits)
    {
        var wanted = query.Trim();
        var hits = new List<int>();
        if (wanted.Length == 0 || maxHits <= 0) return new FindResult(hits, false);

        // A line that lacks the probe cannot hold the query, so it is not parsed (the probe is unescaped in the raw JSON).
        var probe = HistorySearch.RawProbe(wanted);
        var number = 0;
        foreach (var raw in lines)
        {
            number++;
            cancellationToken.ThrowIfCancellationRequested();
            if (probe.Length > 0 && !raw.Contains(probe, StringComparison.OrdinalIgnoreCase)) continue;

            var text = SearchableText(raw);
            if (text is null || !Contains(text, wanted)) continue;

            hits.Add(number);
            if (hits.Count > maxHits)
            {
                hits.RemoveAt(hits.Count - 1);
                return new FindResult(hits, true);
            }
        }
        return new FindResult(hits, false);
    }

    /// <summary>True when the text holds the query, without case.</summary>
    public static bool Contains(string text, string query) =>
        query.Length > 0 && text.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The places of the query in the text, left to right, without overlap: "aaaa" holds "aa" at 0 and at 2, not at 1. Each place is its start and length,
    /// for the highlight.
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> Ranges(string text, string query)
    {
        var ranges = new List<(int Start, int Length)>();
        if (query.Length == 0) return ranges;
        var at = 0;
        while (at <= text.Length - query.Length)
        {
            var found = text.IndexOf(query, at, StringComparison.OrdinalIgnoreCase);
            if (found < 0) break;
            ranges.Add((found, query.Length));
            at = found + query.Length;
        }
        return ranges;
    }

    /// <summary>
    /// The text a raw record shows and that find searches: the message, each tool result whole, and each tool call's name, file and command.
    /// Null for a line that shows nothing (metadata, a line that is not JSON, one whose values cannot be read).
    /// </summary>
    public static string? SearchableText(string raw)
    {
        if (!TranscriptJson.TryParse(raw, out var document)) return null;
        using (document)
        {
            try
            {
                var root = document.RootElement;
                if (TranscriptLine.FromRoot(0, root) is null) return null;
                return Collect(root);
            }
            catch (Exception ex) when (TranscriptJson.IsUnreadable(ex))
            {
                return null;
            }
        }
    }

    private static string? Collect(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object) return null;
        if (!message.TryGetProperty("content", out var content)) return null;
        if (content.ValueKind == JsonValueKind.String) return content.GetString();
        if (content.ValueKind != JsonValueKind.Array) return null;

        var parts = new List<string>();
        foreach (var block in content.EnumerateArray())
        {
            switch (TranscriptJson.Str(block, "type"))
            {
                case "text":
                    parts.Add(TranscriptJson.Str(block, "text") ?? string.Empty);
                    break;

                case "tool_result":
                    parts.Add(TranscriptLine.FullResultText(block));
                    break;

                case "tool_use":
                    parts.Add(TranscriptJson.Str(block, "name") ?? string.Empty);
                    if (block.TryGetProperty("input", out var input))
                    {
                        if (TranscriptJson.Str(input, "file_path") is { } file) parts.Add(file);
                        if (TranscriptJson.Str(input, "command") is { } command) parts.Add(command);
                    }
                    break;
            }
        }
        return string.Join('\n', parts);
    }
}
