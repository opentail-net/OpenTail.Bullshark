using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// Opens Codex rollouts for reading, a line at a time (stage C1, C2), and holds the JSON helpers both readers share. A
/// <c>.jsonl.zst</c> rollout is decoded as a stream with the BCL's <c>ZstandardStream</c>, which exists in net11.0 only (B1, C-D2): on
/// net10.0 a compressed rollout cannot be opened, and <see cref="CanDecodeCompressed"/> is false. Nothing is decompressed to a file. The
/// file is opened with sharing, so a Codex process that is still writing it is not blocked.
/// </summary>
internal static class RolloutReader
{
    /// <summary>The options of the folder walk: sub-folders are searched, and a folder the user cannot open is skipped, not fatal.</summary>
    public static readonly EnumerationOptions Walk = new() { RecurseSubdirectories = true, IgnoreInaccessible = true };

    /// <summary>The UUID at the end of a rollout's file name (<c>rollout-&lt;timestamp&gt;-&lt;uuid&gt;</c>), without the extension.</summary>
    private static readonly Regex UuidAtEnd = new(
        @"-([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})$", RegexOptions.CultureInvariant);

    /// <summary>True when this build can decode a compressed rollout (net11.0 and later).</summary>
    public static bool CanDecodeCompressed
    {
        get
        {
#if NET11_0_OR_GREATER
            return true;
#else
            return false;
#endif
        }
    }

    /// <summary>True for a file name a rollout has: <c>rollout-*.jsonl</c> or <c>rollout-*.jsonl.zst</c>.</summary>
    public static bool IsRolloutName(string fileName) =>
        fileName.StartsWith("rollout-", StringComparison.OrdinalIgnoreCase)
        && (fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".jsonl.zst", StringComparison.OrdinalIgnoreCase));

    /// <summary>The file name without its <c>.jsonl</c> or <c>.jsonl.zst</c> extension.</summary>
    public static string StemOf(string fileName)
    {
        if (fileName.EndsWith(".jsonl.zst", StringComparison.OrdinalIgnoreCase)) return fileName[..^10];
        if (fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)) return fileName[..^6];
        return fileName;
    }

    /// <summary>The UUID at the end of a rollout's file name, or null when the name has none.</summary>
    public static string? UuidFromFileName(string path)
    {
        var match = UuidAtEnd.Match(StemOf(Path.GetFileName(path)));
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Every rollout under the history root, in path order. The caller checks that the root exists.</summary>
    public static List<string> Files(string historyRoot) =>
        new DirectoryInfo(historyRoot)
            .EnumerateFiles("rollout-*", Walk)
            .Where(file => IsRolloutName(file.Name))
            .Select(file => file.FullName)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>The rollout as text, decoded. Dispose the reader to close the file (and the decoder that owns it).</summary>
    public static StreamReader Open(string path)
    {
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        try
        {
            if (path.EndsWith(".zst", StringComparison.OrdinalIgnoreCase))
            {
#if NET11_0_OR_GREATER
                stream = new System.IO.Compression.ZstandardStream(stream, System.IO.Compression.CompressionMode.Decompress);
#else
                throw new NotSupportedException("Codex rollouts are compressed; reading them needs a net11.0 build (C-D2).");
#endif
            }
            return new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>The record as a JSON object, or null when the line is not one (not JSON, or JSON that is not an object).</summary>
    public static JsonDocument? TryParseObject(string line)
    {
        try
        {
            var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
            document.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The text of a string property, trimmed; null when the property is missing, not a string, or blank.</summary>
    public static string? StringOf(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString()?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>The record's own timestamp (the top-level <c>timestamp</c>), or null when it is missing or does not parse.</summary>
    public static DateTimeOffset? TimestampOf(JsonElement record)
    {
        var text = StringOf(record, "timestamp");
        return text is not null && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
            ? at
            : null;
    }
}
