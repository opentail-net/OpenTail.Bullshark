using System.Text;
using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Setup;

/// <summary>
/// The reset time of the usage limit Claude Code last reported on this computer, for the "Start when the usage limit resets" choice
/// (gems item 5, D5.6). When a limit is hit, Claude Code writes an assistant entry to the session transcript: its <c>apiError</c> is
/// <c>usage_limit_reached</c>, and <c>apiErrorParams.rate_limit_info</c> holds a <c>status</c> and a <c>resetsAt</c> that is a Unix time in
/// seconds (checked on the owner's transcripts, 2026-11-18). The reset time is read from that structure, never from the text. Only a
/// <c>status</c> of "rejected" counts.
/// </summary>
public static class LimitReset
{
    /// <summary>The margin after the reset time before a waiting run starts.</summary>
    private static readonly TimeSpan Margin = TimeSpan.FromMinutes(2);

    /// <summary>How far back a transcript is looked at. A weekly limit lasts about a week, so an older file cannot hold a limit still in force.</summary>
    private static readonly TimeSpan Window = TimeSpan.FromDays(8);

    /// <summary>The last bytes of a transcript that are read (D4). A limit entry is written when the limit is hit, so it is near the end.</summary>
    private const int TailBytes = 256 * 1024;

    /// <summary>The largest Unix time in seconds that <see cref="DateTimeOffset.FromUnixTimeSeconds"/> accepts (9999-12-31T23:59:59Z).</summary>
    private const long MaxUnixSeconds = 253402300799;

    private const string LimitError = "usage_limit_reached";
    private const string Rejected = "rejected";

    /// <summary>
    /// The reset time of the limit that resets last among the limits still in force at <paramref name="now"/> (D2), or null when none
    /// is in force (D3). It reads the transcripts under <paramref name="projectsRoot"/> that were written in the last eight days,
    /// subagent transcripts included, and only the tail of each. A folder or a file that cannot be read is skipped.
    /// </summary>
    public static DateTimeOffset? Find(string projectsRoot, DateTimeOffset now)
    {
        DateTimeOffset? latest = null;
        foreach (var file in RecentTranscripts(projectsRoot, now))
        {
            if (LatestInTail(file.FullName, now) is { } reset && (latest is null || reset > latest)) latest = reset;
        }
        return latest;
    }

    /// <summary>The time a waiting run starts for a limit that resets at <paramref name="resetsAt"/>: the margin after it.</summary>
    public static DateTimeOffset StartFor(DateTimeOffset resetsAt) => resetsAt + Margin;

    /// <summary>
    /// The reset time one transcript line reports: a rejected usage limit that resets after <paramref name="now"/>. Otherwise null,
    /// including for a line that is not JSON, a value that cannot be read, and a reset that is not in the future.
    /// </summary>
    internal static DateTimeOffset? FromLine(string line, DateTimeOffset now)
    {
        if (!TranscriptJson.TryParse(line, out var document)) return null;
        using (document)
        {
            try
            {
                var root = document.RootElement;
                if (TranscriptJson.Str(root, "apiError") != LimitError) return null;
                var info = Child(Child(root, "apiErrorParams"), "rate_limit_info");
                if (info is not { } limit || TranscriptJson.Str(limit, "status") != Rejected) return null;
                if (TranscriptJson.Number(limit, "resetsAt") is not { } seconds || seconds is < 0 or > MaxUnixSeconds) return null;
                var resetsAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
                return resetsAt > now ? resetsAt : null;
            }
            catch (Exception ex) when (TranscriptJson.IsUnreadable(ex))
            {
                return null;
            }
        }
    }

    /// <summary>
    /// The transcripts written within the window. A transcript that was last written before the window cannot hold an entry from it.
    /// Subagent transcripts sit under their session's folder, so the search covers every depth. A folder that cannot be listed gives
    /// the files found before it failed.
    /// </summary>
    private static List<FileInfo> RecentTranscripts(string projectsRoot, DateTimeOffset now)
    {
        var files = new List<FileInfo>();
        if (!Directory.Exists(projectsRoot)) return files;
        var since = now.UtcDateTime - Window;
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
    /// The latest reset time among the limit entries in the last <see cref="TailBytes"/> of a transcript, or null. The file is opened
    /// the way every transcript reader opens it. A file that cannot be opened or read gives null.
    /// </summary>
    private static DateTimeOffset? LatestInTail(string path, DateTimeOffset now)
    {
        try
        {
            using var stream = TranscriptFile.Open(path);
            var start = Math.Max(0, stream.Length - TailBytes);
            // The read begins one byte before the cut. If that byte ends a line, the next line starts at the cut and is read whole.
            // Otherwise the first line read is the end of a line that began before the cut, and it is dropped.
            stream.Seek(Math.Max(0, start - 1), SeekOrigin.Begin);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
            if (start > 0) reader.ReadLine();

            DateTimeOffset? latest = null;
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                // The cheap test first: only a line that names the error is parsed.
                if (!line.Contains(LimitError, StringComparison.Ordinal)) continue;
                if (FromLine(line, now) is { } reset && (latest is null || reset > latest)) latest = reset;
            }
            return latest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>The object a property of an object holds, or null when the parent is missing, is not an object, or the property is not one.</summary>
    private static JsonElement? Child(JsonElement? parent, string property) =>
        parent is { ValueKind: JsonValueKind.Object } owner && owner.TryGetProperty(property, out var child) &&
        child.ValueKind == JsonValueKind.Object
            ? child
            : null;
}
