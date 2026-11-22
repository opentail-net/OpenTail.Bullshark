using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// Opens Codex rollouts for reading, a line at a time (stage C1, C2), and holds the JSON helpers both readers share. A
/// <c>.jsonl.zst</c> rollout is decoded as a stream while it is read (B1): the BCL's <c>ZstandardStream</c> on net11.0, and
/// <c>ZstdSharp.DecompressionStream</c> (ZstdSharp.Port) on net10.0, which the Codex-on-net10 plan adds (Z1). Nothing is decompressed to a
/// file. The file is opened with sharing, so a Codex process that is still writing it is not blocked. Lines are read through <see cref="Lines"/>,
/// which caps the length of one line (F-D) and checks the cancellation token before each line.
/// </summary>
internal static class RolloutReader
{
    /// <summary>The options of the folder walk: sub-folders are searched, and a folder the user cannot open is skipped, not fatal.</summary>
    public static readonly EnumerationOptions Walk = new() { RecurseSubdirectories = true, IgnoreInaccessible = true };

    /// <summary>The options of a listing of one folder: sub-folders are not searched, and a folder the user cannot open is skipped, not fatal.</summary>
    public static readonly EnumerationOptions OneFolder = new() { RecurseSubdirectories = false, IgnoreInaccessible = true };

    /// <summary>
    /// The longest line read into memory, in characters: 16 MB (F-D). A longer line is read to its line break without being stored, and it
    /// is reported as <see cref="RolloutLine.Overlong"/>.
    /// </summary>
    public const int MaxLineLength = 16 * 1024 * 1024;

    /// <summary>The UUID at the end of a rollout's file name (<c>rollout-&lt;timestamp&gt;-&lt;uuid&gt;</c>), without the extension.</summary>
    private static readonly Regex UuidAtEnd = new(
        @"-([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})$", RegexOptions.CultureInvariant);

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

    /// <summary>
    /// The rollouts directly in one folder, in path order: sub-folders are not searched. A folder the user cannot open gives none, not an error,
    /// as in <see cref="Walk"/>. The caller checks that the folder exists.
    /// </summary>
    public static List<string> FilesIn(string folder) =>
        new DirectoryInfo(folder)
            .EnumerateFiles("rollout-*", OneFolder)
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
            if (path.EndsWith(".zst", StringComparison.OrdinalIgnoreCase)) stream = DecodeZstd(stream);
            return new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// The one place the decoder differs by target: the zstd stream is decoded as it is read, in memory, and closing the returned stream closes
    /// <paramref name="compressed"/>. net11.0 uses the BCL's <c>ZstandardStream</c>; net10.0 uses <c>ZstdSharp.DecompressionStream</c> from
    /// ZstdSharp.Port 0.8.8 (Z0 checked that it decodes the BCL's output byte for byte). Its <c>leaveOpen</c> defaults to true, so it is set to
    /// false here, or the file would stay open after the reader is closed.
    /// </summary>
    private static Stream DecodeZstd(Stream compressed)
    {
#if NET11_0_OR_GREATER
        return new System.IO.Compression.ZstandardStream(compressed, System.IO.Compression.CompressionMode.Decompress);
#else
        return new ZstdSharp.DecompressionStream(compressed, leaveOpen: false);
#endif
    }

    /// <summary>
    /// The lines of a text reader, one at a time. The token is checked before each line, so a cancelled listing stops between lines. Line
    /// breaks are <c>\n</c>, <c>\r</c> or <c>\r\n</c>, as in <see cref="TextReader.ReadLine"/>; a last line without a break is a line, and
    /// an empty input has none. A line longer than <see cref="MaxLineLength"/> characters is read to its break and not stored: it comes back
    /// as <see cref="RolloutLine.Overlong"/> with empty text.
    /// </summary>
    public static IEnumerable<RolloutLine> Lines(TextReader reader, CancellationToken ct)
    {
        var splitter = new LineSplitter(reader);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (!splitter.Next(out var text, out var overlong)) yield break;
            yield return new RolloutLine(text, overlong);
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

    /// <summary>One line from <see cref="Lines"/>: its text, or <see cref="Overlong"/> when the line passed the cap and its text was discarded.</summary>
    public readonly record struct RolloutLine(string Text, bool Overlong);

    /// <summary>The chunked reader behind <see cref="Lines"/>: it fills a buffer from the reader and cuts lines out of it.</summary>
    private sealed class LineSplitter(TextReader reader)
    {
        private readonly char[] _chunk = new char[8192];
        private readonly StringBuilder _text = new();

        /// <summary>The next unread index in <see cref="_chunk"/>.</summary>
        private int _next;

        /// <summary>The number of valid characters in <see cref="_chunk"/>; zero at the end of the input.</summary>
        private int _end;

        /// <summary>True when the last line ended with <c>\r</c>, so a <c>\n</c> right after it belongs to that same break.</summary>
        private bool _skipLineFeed;

        /// <summary>Reads the next line. False at the end of the input, with no line.</summary>
        public bool Next(out string text, out bool overlong)
        {
            _text.Clear();
            overlong = false;
            var consumed = false;
            while (true)
            {
                if (_next == _end)
                {
                    _end = reader.Read(_chunk, 0, _chunk.Length);
                    _next = 0;
                    if (_end == 0)
                    {
                        text = overlong ? string.Empty : _text.ToString();
                        return consumed;
                    }
                }

                if (_skipLineFeed)
                {
                    _skipLineFeed = false;
                    if (_chunk[_next] == '\n')
                    {
                        _next++;
                        continue;
                    }
                }

                var start = _next;
                var stop = start;
                while (stop < _end && _chunk[stop] is not ('\n' or '\r')) stop++;
                if (stop > start)
                {
                    consumed = true;
                    if (!overlong)
                    {
                        if (_text.Length + (stop - start) > MaxLineLength)
                        {
                            overlong = true;
                            _text.Clear();
                        }
                        else
                        {
                            _text.Append(_chunk, start, stop - start);
                        }
                    }
                }

                if (stop < _end)
                {
                    // A line break ends the line and is consumed; a '\r' may be followed by the '\n' of the same break.
                    _skipLineFeed = _chunk[stop] == '\r';
                    _next = stop + 1;
                    text = overlong ? string.Empty : _text.ToString();
                    return true;
                }
                _next = stop;
            }
        }
    }
}
