using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Cache;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Setup;

/// <summary>
/// The file that keeps what the limit history read from each transcript, between reads (the same pattern as <c>Activity/IndexCache</c>). For
/// each transcript it holds the length and the last write time it had when it was read, its usage-limit entries, and its usage records with a
/// time (the message key, the model and the token totals). Counts, times, types and totals only: no message text is kept. A read takes a
/// transcript's entry when the length and write time are the same, and does not read the file. The file names the reader build that wrote
/// it, so another build's cache is not used. A file that cannot be read or written is no cache: the read goes to the transcripts, and a
/// failed write is said to the log, never thrown.
/// </summary>
internal static class LimitHistoryCache
{
    /// <summary>The module id of this build of the reader. It changes with the code, so a cache written by another build is not used.</summary>
    private static readonly string ReaderId = typeof(LimitHistoryCache).Module.ModuleVersionId.ToString("N");

    /// <summary>How long a save waits for the file's lock. Short, so that a read of the window does not stall behind another window's save.</summary>
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(1);

    /// <summary>The age past which a temporary file of the cache is a leftover of a crash: a save takes well under a second.</summary>
    private static readonly TimeSpan StaleTemporary = TimeSpan.FromMinutes(10);

    /// <summary>The file as written: the reader build and one entry per transcript.</summary>
    public sealed class Document
    {
        [JsonPropertyName("reader")]
        public string Reader { get; set; } = string.Empty;

        [JsonPropertyName("files")]
        public List<Entry>? Files { get; set; }
    }

    /// <summary>One transcript as it was read.</summary>
    public sealed class Entry
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("length")]
        public long Length { get; set; }

        /// <summary>The last write time the file had when it was read, in UTC ticks.</summary>
        [JsonPropertyName("written")]
        public long Written { get; set; }

        [JsonPropertyName("limits")]
        public List<LimitDto>? Limits { get; set; }

        [JsonPropertyName("usage")]
        public List<UsageDto>? Usage { get; set; }
    }

    /// <summary>One usage-limit entry of a transcript: its time (null when it has none), its type, its status and its reset time.</summary>
    public sealed class LimitDto
    {
        /// <summary>The entry's time in UTC ticks, or null when the entry has none.</summary>
        [JsonPropertyName("time")]
        public long? Time { get; set; }

        /// <summary>The offset the entry's time was written with, in ticks.</summary>
        [JsonPropertyName("offset")]
        public long? Offset { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("status")]
        public string? Status { get; set; }

        /// <summary>The reset time as a Unix time in seconds, the form the transcript writes it in.</summary>
        [JsonPropertyName("resetsAt")]
        public long ResetsAt { get; set; }
    }

    /// <summary>One usage record of a transcript: its time, its message key and model, and its token totals.</summary>
    public sealed class UsageDto
    {
        /// <summary>The record's time in UTC ticks.</summary>
        [JsonPropertyName("time")]
        public long Time { get; set; }

        /// <summary>The offset the record's time was written with, in ticks.</summary>
        [JsonPropertyName("offset")]
        public long Offset { get; set; }

        [JsonPropertyName("key")]
        public string? Key { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("input")]
        public long Input { get; set; }

        [JsonPropertyName("output")]
        public long Output { get; set; }

        [JsonPropertyName("cacheCreation")]
        public long CacheCreation { get; set; }

        [JsonPropertyName("cacheRead")]
        public long CacheRead { get; set; }
    }

    /// <summary>
    /// The entries of a cache file by transcript path, or null when there is no usable cache (none, unreadable, damaged, or another build's).
    /// Any failure is a cache that is not used, and is said to <paramref name="log"/> once; nothing is thrown. A missing file is not said: the
    /// first read has none. Temporary files of an old crashed save are removed here.
    /// </summary>
    public static Dictionary<string, Entry>? Load(string path, Action<string>? log)
    {
        SweepStaleTemporaries(path, log);
        try
        {
            if (!File.Exists(path)) return null;
            // Read without the lock: a read that meets the replace is tried once more (reliability batch R11).
            var document = CacheRead.Retry(() => ReadDocument(path));
            if (document is null || document.Reader != ReaderId) return null;

            var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            foreach (var entry in document.Files ?? [])
            {
                if (entry?.Path is { Length: > 0 } key) entries[key] = entry;
            }
            return entries;
        }
        catch (Exception ex)
        {
            log?.Invoke($"limit history cache not used ({ex.GetType().Name}): {ex.Message}");
            return null;
        }
    }

    /// <summary>The cache file as it is on disk (read without the lock, see <see cref="Load"/>).</summary>
    private static Document? ReadDocument(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Document>(stream);
    }

    /// <summary>
    /// Writes the cache under the file lock (waiting <see cref="LockWait"/>), through <see cref="FileLock.WriteAtomic"/>: a temporary file that then
    /// takes the place of the old one, so a reader never sees half of it. A file that cannot be written, or a lock that is not free in time, leaves
    /// the old cache in place and is said to <paramref name="log"/>. Nothing is thrown.
    /// </summary>
    public static void Save(string path, IReadOnlyList<Entry> entries, Action<string>? log)
    {
        try
        {
            var text = JsonSerializer.Serialize(new Document { Reader = ReaderId, Files = entries.ToList() });
            using (FileLock.Hold(path, LockWait))
            {
                FileLock.WriteAtomic(path, text);
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"limit history cache not saved ({ex.GetType().Name}): {ex.Message}");
        }
    }

    /// <summary>The entry of a transcript as its read left it: the length and write time the window listed it with, and its records.</summary>
    public static Entry EntryOf(FileInfo file, FileRecords records) => new()
    {
        Path = records.Path,
        Length = file.Length,
        Written = file.LastWriteTimeUtc.Ticks,
        Limits = records.Limits.Select(limit => new LimitDto
        {
            Time = limit.Written?.UtcTicks,
            Offset = limit.Written?.Offset.Ticks,
            Type = limit.Type,
            Status = limit.Status,
            ResetsAt = limit.ResetsAt.ToUnixTimeSeconds(),
        }).ToList(),
        Usage = records.Usage.Select(usage => new UsageDto
        {
            Time = usage.Time.UtcTicks,
            Offset = usage.Time.Offset.Ticks,
            Key = usage.Key,
            Model = usage.Model,
            Input = usage.Usage.Input,
            Output = usage.Usage.Output,
            CacheCreation = usage.Usage.CacheCreation,
            CacheRead = usage.Usage.CacheRead,
        }).ToList(),
    };

    /// <summary>
    /// The records of a transcript from its entry, as the read that wrote the entry gave them. Null when the entry does not restore (a missing value,
    /// or a time out of range): such an entry is read again.
    /// </summary>
    public static FileRecords? Restore(Entry entry)
    {
        try
        {
            if (entry.Path.Length == 0) return null;
            var limits = new List<LimitEntry>();
            foreach (var limit in entry.Limits ?? [])
            {
                if (limit.Type is null || limit.Status is null) return null;
                var written = limit.Time is { } time ? TimeOf(time, limit.Offset ?? 0) : (DateTimeOffset?)null;
                limits.Add(new LimitEntry(written, limit.Type, limit.Status, DateTimeOffset.FromUnixTimeSeconds(limit.ResetsAt)));
            }
            var usage = new List<UsageRecord>();
            foreach (var record in entry.Usage ?? [])
            {
                if (record.Key is null || record.Model is null) return null;
                usage.Add(new UsageRecord(
                    TimeOf(record.Time, record.Offset),
                    record.Key,
                    record.Model,
                    new TokenUsage(record.Input, record.Output, record.CacheCreation, record.CacheRead)));
            }
            return new FileRecords(entry.Path, limits, usage);
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>A time from its UTC ticks and its offset ticks, the same instant and offset it was written with.</summary>
    private static DateTimeOffset TimeOf(long utcTicks, long offsetTicks) =>
        new DateTimeOffset(utcTicks, TimeSpan.Zero).ToOffset(TimeSpan.FromTicks(offsetTicks));

    /// <summary>
    /// Deletes the cache's temporary files older than <see cref="StaleTemporary"/>. A save that stopped between the write and the move leaves one, and
    /// it holds the counts of the transcripts. Only names that start with the cache file's own name are swept. A file that cannot be deleted is left
    /// for the next read.
    /// </summary>
    private static void SweepStaleTemporaries(string path, Action<string>? log)
    {
        try
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (folder is null || !Directory.Exists(folder)) return;
            var cutoff = DateTime.UtcNow - StaleTemporary;
            foreach (var temp in new DirectoryInfo(folder).EnumerateFiles(Path.GetFileName(path) + ".*.tmp"))
            {
                try
                {
                    if (temp.LastWriteTimeUtc < cutoff) temp.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log?.Invoke($"limit history cache temporary file not removed ({ex.GetType().Name}): {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            // A path that cannot be resolved, or a folder that cannot be listed: the cache is still read, and nothing is thrown to the caller.
            log?.Invoke($"limit history cache temporary files not swept ({ex.GetType().Name}): {ex.Message}");
        }
    }
}
