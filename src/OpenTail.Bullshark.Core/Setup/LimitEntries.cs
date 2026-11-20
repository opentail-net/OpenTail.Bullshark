using System.Text;
using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Setup;

/// <summary>
/// One usage-limit entry of a transcript: when it was written (null when its timestamp cannot be read), the limit type as Claude Code wrote it
/// (<c>rateLimitType</c>, "unknown" when it is missing), its <c>status</c> (only "rejected" is a limit in force), and the reset time.
/// </summary>
internal sealed record LimitEntry(DateTimeOffset? Written, string Type, string Status, DateTimeOffset ResetsAt);

/// <summary>
/// The reader of the usage-limit entries in the session transcripts (gems item 5, A2 and A4). <see cref="LimitReset"/>, <see cref="LimitStatus"/>
/// and <see cref="LimitHistory"/> share it. Each reads by line: a line that is not JSON, or one of whose values cannot be read, is skipped. A file
/// that cannot be opened or read gives no entries, and the other files are still read. Only the transcript files are read (through
/// <see cref="TranscriptFile"/>), and no prompt or message text is kept.
/// </summary>
internal static class LimitEntries
{
    /// <summary>How far back a transcript is looked at. A weekly limit lasts about a week, so an older file cannot hold a limit still in force.</summary>
    internal static readonly TimeSpan LimitWindow = TimeSpan.FromDays(8);

    /// <summary>The last bytes of a transcript that are read by <see cref="Tail"/> (D4). A limit entry is written when the limit is hit, so it is near the end.</summary>
    private const int TailBytes = 256 * 1024;

    /// <summary>The largest Unix time in seconds that <see cref="DateTimeOffset.FromUnixTimeSeconds"/> accepts (9999-12-31T23:59:59Z).</summary>
    private const long MaxUnixSeconds = 253402300799;

    /// <summary>The error a limit entry carries in <c>apiError</c>.</summary>
    internal const string LimitError = "usage_limit_reached";

    /// <summary>The type given to a limit entry that names none.</summary>
    internal const string UnknownType = "unknown";

    /// <summary>
    /// The transcripts last written at or after <paramref name="since"/> (UTC): every <c>.jsonl</c> under the projects folder, subagent transcripts
    /// included. A transcript last written before the time cannot hold an entry from after it. A folder that cannot be listed gives the files
    /// found before it failed. A missing folder gives none.
    /// </summary>
    internal static List<FileInfo> Recent(string projectsRoot, DateTime since)
    {
        var files = new List<FileInfo>();
        if (!Directory.Exists(projectsRoot)) return files;
        try
        {
            foreach (var file in new DirectoryInfo(projectsRoot).EnumerateFiles("*.jsonl", SearchOption.AllDirectories))
            {
                if (file.LastWriteTimeUtc >= since) files.Add(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The files found so far are read; the rest of the folder is not.
        }
        return files;
    }

    /// <summary>
    /// The limit entry one transcript line holds, or null for a line that is not one: not JSON, not a usage-limit error, no
    /// <c>rate_limit_info</c>, or a reset time that is missing or out of range. The status and the reset are not filtered here.
    /// </summary>
    internal static LimitEntry? FromLine(string line)
    {
        if (!TranscriptJson.TryParse(line, out var document)) return null;
        using (document) return OfRecord(document.RootElement);
    }

    /// <summary>
    /// The limit entry one parsed record holds, under the rule of <see cref="FromLine"/>. A record whose values cannot be read is null.
    /// </summary>
    internal static LimitEntry? OfRecord(JsonElement root)
    {
        try
        {
            if (TranscriptJson.Str(root, "apiError") != LimitError) return null;
            var info = Child(Child(root, "apiErrorParams"), "rate_limit_info");
            if (info is not { } limit) return null;
            var status = TranscriptJson.Str(limit, "status") ?? string.Empty;
            if (TranscriptJson.Number(limit, "resetsAt") is not { } seconds || seconds is < 0 or > MaxUnixSeconds) return null;
            return new LimitEntry(WrittenOf(root), TypeOf(limit), status, DateTimeOffset.FromUnixTimeSeconds(seconds));
        }
        catch (Exception ex) when (TranscriptJson.IsUnreadable(ex))
        {
            return null;
        }
    }

    /// <summary>
    /// The limit entries of the last <see cref="TailBytes"/> of a transcript. A file that cannot be opened or read gives none.
    /// </summary>
    internal static List<LimitEntry> Tail(string path)
    {
        var entries = new List<LimitEntry>();
        try
        {
            foreach (var line in TailLines(path))
            {
                // The cheap test first: only a line that names the error is parsed.
                if (line.Contains(LimitError, StringComparison.Ordinal) && FromLine(line) is { } entry) entries.Add(entry);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        return entries;
    }

    /// <summary>
    /// The lines of the last <see cref="TailBytes"/> of a transcript, read the way every transcript reader opens it. The read begins one byte
    /// before the cut; a first line that is cut is dropped. Throws when the file cannot be opened or read.
    /// </summary>
    internal static List<string> TailLines(string path)
    {
        using var stream = TranscriptFile.Open(path);
        var start = Math.Max(0, stream.Length - TailBytes);
        // The read begins one byte before the cut. If that byte ends a line, the next line starts at the cut and is read whole.
        // Otherwise the first line read is the end of a line that began before the cut, and it is dropped.
        stream.Seek(Math.Max(0, start - 1), SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        if (start > 0) reader.ReadLine();
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null) lines.Add(line);
        return lines;
    }

    /// <summary>
    /// The limit entries of a whole transcript, read as a stream. The cheap test runs first: only a line that names the error is parsed. A file
    /// that cannot be opened or read gives none.
    /// </summary>
    internal static List<LimitEntry> Whole(string path)
    {
        var entries = new List<LimitEntry>();
        try
        {
            foreach (var line in TranscriptFile.ReadLines(path))
            {
                if (line.Contains(LimitError, StringComparison.Ordinal) && FromLine(line) is { } entry) entries.Add(entry);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        return entries;
    }

    /// <summary>The entry's timestamp, read as the transcripts write it (ISO UTC). Null when it is missing or cannot be read.</summary>
    private static DateTimeOffset? WrittenOf(JsonElement root)
    {
        try
        {
            return TranscriptJson.Time(root, "timestamp");
        }
        catch (Exception ex) when (TranscriptJson.IsUnreadable(ex))
        {
            return null;
        }
    }

    /// <summary>The entry's limit type. "unknown" when it is missing or cannot be read, so that the entry still counts.</summary>
    private static string TypeOf(JsonElement limit)
    {
        try
        {
            return TranscriptJson.Str(limit, "rateLimitType") ?? UnknownType;
        }
        catch (Exception ex) when (TranscriptJson.IsUnreadable(ex))
        {
            return UnknownType;
        }
    }

    /// <summary>The object a property of an object holds, or null when the parent is missing, is not an object, or the property is not one.</summary>
    private static JsonElement? Child(JsonElement? parent, string property) =>
        parent is { ValueKind: JsonValueKind.Object } owner && owner.TryGetProperty(property, out var child) &&
        child.ValueKind == JsonValueKind.Object
            ? child
            : null;
}
