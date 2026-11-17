using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenTail.Bullshark.Core.Activity;

/// <summary>
/// The file that keeps what the index read from each transcript, between builds (round 10 item 2). For each transcript it holds the length
/// and the write time the file had when it was read, the summary the read gave, and the messages it counted, in the order it counted them.
/// A build that finds a transcript with the same length and write time takes its entry and does not read the file. The file names the
/// reader build that wrote it, so a build of another reader does not use it. A file that cannot be read or written is no cache: the build
/// reads every transcript.
/// </summary>
internal static class IndexCache
{
    /// <summary>The module id of this build of the reader. It changes with the code, so a cache written by another build is not used.</summary>
    private static readonly string ReaderId = typeof(IndexCache).Module.ModuleVersionId.ToString("N");

    /// <summary>The file as written: the reader build and one entry per transcript.</summary>
    public sealed class Document
    {
        [JsonPropertyName("reader")]
        public string Reader { get; set; } = string.Empty;

        [JsonPropertyName("files")]
        public List<Entry> Files { get; set; } = [];
    }

    /// <summary>One transcript as it was read.</summary>
    public sealed class Entry
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("length")]
        public long Length { get; set; }

        /// <summary>The write time the file had when it was read, in UTC ticks.</summary>
        [JsonPropertyName("written")]
        public long Written { get; set; }

        [JsonPropertyName("summary")]
        public SummaryDto Summary { get; set; } = new();

        /// <summary>The messages the transcript counted, in the order it counted them.</summary>
        [JsonPropertyName("messages")]
        public List<MessageDto> Messages { get; set; } = [];
    }

    /// <summary>The parts of a transcript's summary that the index keeps. Times are written in the round-trip format, with their offset.</summary>
    public sealed class SummaryDto
    {
        [JsonPropertyName("firstAt")]
        public string? FirstAt { get; set; }

        [JsonPropertyName("lastAt")]
        public string? LastAt { get; set; }

        [JsonPropertyName("turns")]
        public int Turns { get; set; }

        [JsonPropertyName("users")]
        public int Users { get; set; }

        [JsonPropertyName("assistants")]
        public int Assistants { get; set; }

        [JsonPropertyName("tools")]
        public int Tools { get; set; }

        /// <summary>The tool names and their counts, in the order they were first seen.</summary>
        [JsonPropertyName("toolNames")]
        public List<NameCountDto> ToolNames { get; set; } = [];

        [JsonPropertyName("filesWritten")]
        public List<string> FilesWritten { get; set; } = [];

        [JsonPropertyName("lastText")]
        public string? LastText { get; set; }

        [JsonPropertyName("lastModel")]
        public string? LastModel { get; set; }

        [JsonPropertyName("contextTokens")]
        public long ContextTokens { get; set; }

        [JsonPropertyName("firstUserText")]
        public string? FirstUserText { get; set; }

        [JsonPropertyName("aiTitle")]
        public string? AiTitle { get; set; }

        [JsonPropertyName("customTitle")]
        public string? CustomTitle { get; set; }

        [JsonPropertyName("recap")]
        public string? Recap { get; set; }

        [JsonPropertyName("recapAt")]
        public string? RecapAt { get; set; }

        [JsonPropertyName("compactions")]
        public int Compactions { get; set; }

        [JsonPropertyName("continues")]
        public string? Continues { get; set; }

        [JsonPropertyName("failed")]
        public int Failed { get; set; }
    }

    public sealed class NameCountDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("count")]
        public int Count { get; set; }
    }

    /// <summary>A counted message: its key, its model and its largest usage (the fields of <see cref="UsageLedger.Entry"/>).</summary>
    public sealed class MessageDto
    {
        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("input")]
        public long Input { get; set; }

        [JsonPropertyName("output")]
        public long Output { get; set; }

        [JsonPropertyName("cacheCreation")]
        public long CacheCreation { get; set; }

        [JsonPropertyName("cacheRead")]
        public long CacheRead { get; set; }
    }

    /// <summary>The entries of a cache file by transcript path, or null when there is no usable cache (none, unreadable, or another reader's).</summary>
    public static Dictionary<string, Entry>? Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var document = JsonSerializer.Deserialize<Document>(stream);
            if (document is null || document.Reader != ReaderId) return null;

            var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (var entry in document.Files)
            {
                if (entry is not null && entry.Path.Length > 0) entries[entry.Path] = entry;
            }
            return entries;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Writes the cache: to a temporary file first, which then takes the place of the old one, so a reader never sees half of it. A file that
    /// cannot be written leaves the old cache in place, and nothing is thrown.
    /// </summary>
    public static void Save(string path, IReadOnlyList<Entry> entries)
    {
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using (var stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, new Document { Reader = ReaderId, Files = entries.ToList() });
            }
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            DeleteQuietly(temp);
        }
    }

    /// <summary>The entry of a transcript as its read left it.</summary>
    public static Entry EntryOf(
        string path,
        long length,
        DateTime written,
        TranscriptSummary summary,
        IEnumerable<KeyValuePair<string, UsageLedger.Entry>> messages) => new()
    {
        Path = path,
        Length = length,
        Written = written.Ticks,
        Summary = new SummaryDto
        {
            FirstAt = FormatTime(summary.FirstTimestamp),
            LastAt = FormatTime(summary.LastTimestamp),
            Turns = summary.CompletedTurns,
            Users = summary.UserMessages,
            Assistants = summary.AssistantMessages,
            Tools = summary.ToolCalls,
            ToolNames = (summary.ToolNames ?? new Dictionary<string, int>())
                .Select(pair => new NameCountDto { Name = pair.Key, Count = pair.Value }).ToList(),
            FilesWritten = (summary.FilesWritten ?? []).ToList(),
            LastText = summary.LastAssistantText,
            LastModel = summary.LastModel,
            ContextTokens = summary.ContextTokensAtLastTurn,
            FirstUserText = summary.FirstUserText,
            AiTitle = summary.AiTitle,
            CustomTitle = summary.CustomTitle,
            Recap = summary.LastRecap,
            RecapAt = FormatTime(summary.LastRecapAt),
            Compactions = summary.Compactions,
            Continues = summary.ContinuesSessionId,
            Failed = summary.FailedToolCalls,
        },
        Messages = messages.Select(pair => new MessageDto
        {
            Key = pair.Key,
            Model = pair.Value.Model,
            Input = pair.Value.Usage.Input,
            Output = pair.Value.Usage.Output,
            CacheCreation = pair.Value.Usage.CacheCreation,
            CacheRead = pair.Value.Usage.CacheRead,
        }).ToList(),
    };

    /// <summary>
    /// The summary and the ledger of a transcript from its entry, as the read that wrote the entry gave them. The usage by model of the summary
    /// is left empty: the index sets it from the ledger, as it does for a read.
    /// </summary>
    public static (TranscriptSummary Summary, UsageLedger Ledger) Restore(Entry entry)
    {
        var ledger = new UsageLedger();
        foreach (var message in entry.Messages)
        {
            ledger.Record(message.Key, message.Model, entry.Path,
                new TokenUsage(message.Input, message.Output, message.CacheCreation, message.CacheRead));
        }

        var s = entry.Summary;
        var toolNames = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in s.ToolNames) toolNames[item.Name] = item.Count;
        var summary = new TranscriptSummary(
            Records: 0,
            UserMessages: s.Users,
            AssistantMessages: s.Assistants,
            ToolCalls: s.Tools,
            CompletedTurns: s.Turns,
            LastAssistantText: s.LastText,
            LastTimestamp: ParseTime(s.LastAt),
            LastModel: s.LastModel,
            UsageByModel: new Dictionary<string, TokenUsage>(),
            FirstTimestamp: ParseTime(s.FirstAt),
            ToolNames: toolNames,
            FilesWritten: s.FilesWritten,
            ContextTokensAtLastTurn: s.ContextTokens,
            FirstUserText: s.FirstUserText,
            AiTitle: s.AiTitle,
            CustomTitle: s.CustomTitle,
            LastRecap: s.Recap,
            LastRecapAt: ParseTime(s.RecapAt),
            Compactions: s.Compactions,
            ContinuesSessionId: s.Continues,
            FailedToolCalls: s.Failed);
        return (summary, ledger);
    }

    private static string? FormatTime(DateTimeOffset? time) => time?.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseTime(string? text) =>
        text is null ? null : DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more to do: the temporary file is left behind, and the old cache is still in place.
        }
    }
}
