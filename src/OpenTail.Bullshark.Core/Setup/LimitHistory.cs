using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Codex;

namespace OpenTail.Bullshark.Core.Setup;

/// <summary>
/// One limit hit: a rejected usage limit as the transcripts first wrote it (the earliest entry of that limit). TokensBefore is what the
/// transcripts on this computer used in the five hours up to the hit, counted once per message (see <see cref="LimitHistory"/>).
/// </summary>
public sealed record LimitHit(DateTimeOffset Time, string Type, DateTimeOffset ResetsAt, TokenUsage TokensBefore);

/// <summary>The hits of one local day, as a count per limit type. A day with no hit is not listed.</summary>
public sealed record LimitDay(DateOnly Day, IReadOnlyDictionary<string, int> HitsByType)
{
    /// <summary>All hits on the day, whatever their type.</summary>
    public int Total => HitsByType.Values.Sum();
}

/// <summary>What the Limits page shows, from one read: the hits and their days (as <see cref="LimitHistory.ReadWithCache"/> gives them) and the usage report.</summary>
public sealed record LimitsReport(IReadOnlyList<LimitHit> Hits, IReadOnlyList<LimitDay> Days, UsageReport Usage);

/// <summary>
/// The limit history (A4): the usage-limit hits of the last days, from the transcripts on this computer, for the read-only Limits page.
/// Counts and times only; no prompt or message text is kept. Rules:
/// <list type="bullet">
/// <item>A hit is a rejected usage-limit entry. The same limit (type and reset time) written again, as a session retries, is one hit, at its earliest entry.</item>
/// <item>Tokens before a hit are the usage of the messages written in the five hours up to the hit (both ends included), across all transcripts
/// of the window. A message with the same id and request is counted once, as <see cref="UsageLedger"/> counts it.</item>
/// <item>The transcripts read are the <c>.jsonl</c> files, subagent ones included, last written since the window starts five hours earlier. A
/// transcript older than that cannot hold a message of the window. Each is read in full, once, as a stream. A file that cannot be read is skipped.</item>
/// <item>A day is the local date of a hit's time.</item>
/// </list>
/// <see cref="ReadWithCache"/> gives the same result, and reads a transcript again only when it has changed since the last read.
/// Other machines and claude.ai chat use the same limit, and their usage is not visible here.
/// </summary>
public static class LimitHistory
{
    /// <summary>The number of days the Limits page covers.</summary>
    public const int DefaultDays = 30;

    /// <summary>How long before a hit its tokens are counted (the window of the tokens before a hit).</summary>
    public static readonly TimeSpan TokenWindow = TimeSpan.FromHours(5);

    private const string Rejected = "rejected";

    /// <summary>
    /// The limit hits written in the last <paramref name="days"/> days up to <paramref name="now"/>, newest first, and the days they fall on, newest
    /// first. <paramref name="zone"/> sets the local date (the computer's zone when null). A missing folder gives empty lists.
    /// </summary>
    public static (IReadOnlyList<LimitHit> Hits, IReadOnlyList<LimitDay> Days) Read(
        string projectsRoot, DateTimeOffset now, int days = DefaultDays, TimeZoneInfo? zone = null) =>
        Compute(projectsRoot, now, days, zone, cachePath: null, log: null);

    /// <summary>
    /// The same result as <see cref="Read"/>, with what each transcript held kept in <paramref name="cachePath"/> between reads. A transcript whose
    /// length and last write time are the same as when it was read is not read again. The cache holds counts, times, types, message keys and token
    /// totals; no message text. A cache that is missing, unreadable, damaged or from another build is not used, and is rebuilt. A cache that cannot
    /// be written is said to <paramref name="log"/>, and nothing is thrown.
    /// </summary>
    public static (IReadOnlyList<LimitHit> Hits, IReadOnlyList<LimitDay> Days) ReadWithCache(
        string projectsRoot, DateTimeOffset now, string cachePath, int days = DefaultDays, TimeZoneInfo? zone = null, Action<string>? log = null) =>
        Compute(projectsRoot, now, days, zone, cachePath, log);

    /// <summary>
    /// The Limits page's read: the hits and days of <see cref="ReadWithCache"/>, and the usage report (<see cref="UsageReport"/>) of the same transcripts.
    /// The transcripts are listed and read once, so both come from one set of files and one cache save. The usage report is built even when there is no
    /// hit. Its local days are the <paramref name="days"/> days ending on the local date of <paramref name="now"/>.
    /// <para>
    /// <paramref name="usage"/> sets the attribution of usage to a day (R8; default: the session's last update, and reasoning as output). The hits
    /// are Claude's limit hits and keep their own rule (per message, in the five hours before the hit): Codex usage never enters them.
    /// <paramref name="codexHistoryRoot"/> is the Codex history folder (its rollouts are read fresh on each call, as <see cref="CodexUsageReader.ReadFolder"/>
    /// does); null leaves Codex out of the report.
    /// </para>
    /// </summary>
    public static LimitsReport ReadReportWithCache(
        string projectsRoot,
        DateTimeOffset now,
        string cachePath,
        int days = DefaultDays,
        TimeZoneInfo? zone = null,
        Action<string>? log = null,
        UsageSettings? usage = null,
        string? codexHistoryRoot = null)
    {
        var clock = zone ?? TimeZoneInfo.Local;
        var settings = usage ?? new UsageSettings();
        var since = now.AddDays(-days);
        var reads = ReadWindow(projectsRoot, since, cachePath, log);
        var (hits, byDay) = HitsOf(reads, since, now, clock);
        IReadOnlyList<CodexSessionUsage> codex = [];
        if (codexHistoryRoot is not null)
        {
            codex = CodexUsageReader.ReadFolder(codexHistoryRoot, since, settings.ReasoningAsOutput, log);
        }
        return new LimitsReport(hits, byDay, UsageReport.Build(reads, codex, projectsRoot, now, days, clock, settings.Attribution));
    }

    private static (IReadOnlyList<LimitHit> Hits, IReadOnlyList<LimitDay> Days) Compute(
        string projectsRoot, DateTimeOffset now, int days, TimeZoneInfo? zone, string? cachePath, Action<string>? log)
    {
        var clock = zone ?? TimeZoneInfo.Local;
        var since = now.AddDays(-days);
        return HitsOf(ReadWindow(projectsRoot, since, cachePath, log), since, now, clock);
    }

    /// <summary>
    /// The records of the window's transcripts: the ones last written since <paramref name="since"/> less <see cref="TokenWindow"/>, read or taken from
    /// the cache. Every reader of the window goes through here, so they share the file set and the cache save.
    /// </summary>
    private static List<FileRecords> ReadWindow(string projectsRoot, DateTimeOffset since, string? cachePath, Action<string>? log) =>
        RecordsOf(LimitEntries.Recent(projectsRoot, since.UtcDateTime - TokenWindow), cachePath, log);

    /// <summary>The hits of the window, newest first, and the days they fall on, newest first. Empty when there is no hit.</summary>
    private static (IReadOnlyList<LimitHit> Hits, IReadOnlyList<LimitDay> Days) HitsOf(
        List<FileRecords> reads, DateTimeOffset since, DateTimeOffset now, TimeZoneInfo clock)
    {
        var found = FirstHits(reads, since, now);
        if (found.Count == 0) return ([], []);

        // The tokens of each hit: the messages in its window. Ordered by time, so that a message finds its hits by a search.
        var ordered = found.OrderBy(hit => hit.Time).ToList();
        var times = ordered.Select(hit => hit.Time).ToArray();
        var ledgers = ordered.Select(_ => new UsageLedger()).ToArray();
        var from = ordered[0].Time - TokenWindow;
        var to = ordered[^1].Time;
        foreach (var file in reads)
        {
            foreach (var usage in file.Usage)
            {
                if (usage.Time >= from && usage.Time <= to) Count(usage, times, ledgers, file.Path);
            }
        }

        var hits = ordered
            .Select((hit, i) => new LimitHit(hit.Time, hit.Type, hit.ResetsAt, ledgers[i].Total))
            .OrderByDescending(hit => hit.Time)
            .ToList();
        var byDay = hits
            .GroupBy(hit => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(hit.Time, clock).DateTime))
            .OrderByDescending(day => day.Key)
            .Select(day => new LimitDay(
                day.Key,
                day.GroupBy(hit => hit.Type)
                    .OrderBy(type => type.Key, StringComparer.Ordinal)
                    .ToDictionary(type => type.Key, type => type.Count(), StringComparer.Ordinal)))
            .ToList();
        return (hits, byDay);
    }

    /// <summary>
    /// The records of each transcript of the window, in the order of <paramref name="files"/>. With a cache, a transcript whose length and last write
    /// time match its entry takes the entry, and the others are read. A transcript that cannot be read gives none, and is not cached. The cache is
    /// saved when a transcript was read, or when the set of cached transcripts has changed (a transcript left the window, or was added).
    /// </summary>
    private static List<FileRecords> RecordsOf(List<FileInfo> files, string? cachePath, Action<string>? log)
    {
        var cached = cachePath is null ? null : LimitHistoryCache.Load(cachePath, log);
        var reads = new List<FileRecords>(files.Count);
        var entries = new List<LimitHistoryCache.Entry>(files.Count);
        var readNow = false;
        foreach (var file in files)
        {
            // The length and the write time are the ones the file had when the window listed it, before any read. A file that changes during its read
            // is then read again next time, because its entry would not match.
            FileRecords? records = null;
            if (cached is not null && cached.TryGetValue(file.FullName, out var entry) &&
                entry.Length == file.Length && entry.Written == file.LastWriteTimeUtc.Ticks)
            {
                records = LimitHistoryCache.Restore(entry);
            }
            if (records is null)
            {
                records = ReadFile(file.FullName);
                if (records is null) continue;
                readNow = true;
            }
            reads.Add(records);
            entries.Add(LimitHistoryCache.EntryOf(file, records));
        }
        if (cachePath is not null && (cached is null || cached.Count != entries.Count || readNow))
        {
            LimitHistoryCache.Save(cachePath, entries, log);
        }
        return reads;
    }

    /// <summary>
    /// The rejected limits of the window, one per (type, reset time), each at its earliest entry. A limit entry that cannot be placed in time
    /// (no timestamp) is left out. A transcript that gave no records gives none of its entries.
    /// </summary>
    private static List<(DateTimeOffset Time, string Type, DateTimeOffset ResetsAt)> FirstHits(List<FileRecords> reads, DateTimeOffset since, DateTimeOffset now)
    {
        var earliest = new Dictionary<(string Type, DateTimeOffset ResetsAt), DateTimeOffset>();
        foreach (var file in reads)
        {
            foreach (var entry in file.Limits)
            {
                if (entry.Status != Rejected || entry.Written is not { } written || written < since || written > now) continue;
                var key = (entry.Type, entry.ResetsAt);
                if (!earliest.TryGetValue(key, out var known) || written < known) earliest[key] = written;
            }
        }
        return earliest.Select(pair => (pair.Value, pair.Key.Type, pair.Key.ResetsAt)).ToList();
    }

    /// <summary>
    /// The records of one transcript, read in one pass: its usage-limit entries (as <see cref="LimitEntries.Whole"/> finds them) and its usage
    /// records with a time. The whole file is read first into the records, so a file that fails part way gives none of them. Null when the file
    /// cannot be read (locked, removed, or denied).
    /// </summary>
    private static FileRecords? ReadFile(string path)
    {
        var limits = new List<LimitEntry>();
        var usage = new List<UsageRecord>();
        try
        {
            foreach (var record in TranscriptReader.UsageRecords(WithLimits(TranscriptFile.ReadLines(path), limits), path))
            {
                if (record.Time is { } time) usage.Add(new UsageRecord(time, record.Key, record.Model, record.Usage));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return new FileRecords(path, limits, usage);
    }

    /// <summary>
    /// The lines, each passed on as it is read. The usage-limit entries among them are added to <paramref name="limits"/> on the way, so that one pass
    /// finds both the entries and the usage. The test for an entry is the one <see cref="LimitEntries.Whole"/> uses.
    /// </summary>
    private static IEnumerable<string> WithLimits(IEnumerable<string> lines, List<LimitEntry> limits)
    {
        foreach (var line in lines)
        {
            if (line.Contains(LimitEntries.LimitError, StringComparison.Ordinal) && LimitEntries.FromLine(line) is { } entry) limits.Add(entry);
            yield return line;
        }
    }

    /// <summary>
    /// Counts one message in every hit whose window holds it: a hit at time T holds a message at t when T - 5 hours is at or before t, and t is at
    /// or before T. The hits are sorted by time, so the first hit at or after t is found by a search, and the hits after it are checked in turn.
    /// </summary>
    private static void Count(UsageRecord usage, DateTimeOffset[] times, UsageLedger[] ledgers, string file)
    {
        var last = usage.Time + TokenWindow;
        // The first hit at or after the message's time: a lower bound, so that equal times are all reached.
        var low = 0;
        var high = times.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (times[middle] < usage.Time) low = middle + 1;
            else high = middle;
        }
        for (var i = low; i < times.Length && times[i] <= last; i++)
        {
            ledgers[i].Record(usage.Key, usage.Model, file, usage.Usage);
        }
    }
}

/// <summary>What one transcript holds for the limit history: its path, its usage-limit entries and its usage records with a time, in file order.</summary>
internal sealed record FileRecords(string Path, IReadOnlyList<LimitEntry> Limits, IReadOnlyList<UsageRecord> Usage);

/// <summary>One usage record with its time: the usage of an assistant message, keyed as <see cref="UsageLedger"/> keys it.</summary>
internal sealed record UsageRecord(DateTimeOffset Time, string Key, string Model, TokenUsage Usage);
