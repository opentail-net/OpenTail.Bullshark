using OpenTail.Bullshark.Core.Activity;

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

/// <summary>
/// The limit history (A4): the usage-limit hits of the last days, from the transcripts on this computer, for the read-only Limits page.
/// Counts and times only; no prompt or message text is kept. Rules:
/// <list type="bullet">
/// <item>A hit is a rejected usage-limit entry. The same limit (type and reset time) written again, as a session retries, is one hit, at its earliest entry.</item>
/// <item>Tokens before a hit are the usage of the messages written in the five hours up to the hit (both ends included), across all transcripts
/// of the window. A message with the same id and request is counted once, as <see cref="UsageLedger"/> counts it.</item>
/// <item>The transcripts read are the <c>.jsonl</c> files, subagent ones included, last written since the window starts five hours earlier. A
/// transcript older than that cannot hold a message of the window. Each is read in full, as a stream. A file that cannot be read is skipped.</item>
/// <item>A day is the local date of a hit's time.</item>
/// </list>
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
        string projectsRoot, DateTimeOffset now, int days = DefaultDays, TimeZoneInfo? zone = null)
    {
        var clock = zone ?? TimeZoneInfo.Local;
        var since = now.AddDays(-days);
        var files = LimitEntries.Recent(projectsRoot, since.UtcDateTime - TokenWindow);

        var found = FirstHits(files, since, now);
        if (found.Count == 0) return ([], []);

        // The tokens of each hit: the messages in its window. Ordered by time, so that a message finds its hits by a search.
        var ordered = found.OrderBy(hit => hit.Time).ToList();
        var times = ordered.Select(hit => hit.Time).ToArray();
        var ledgers = ordered.Select(_ => new UsageLedger()).ToArray();
        var from = ordered[0].Time - TokenWindow;
        var to = ordered[^1].Time;
        foreach (var file in files)
        {
            foreach (var usage in UsageIn(file.FullName, from, to)) Count(usage, times, ledgers, file.FullName);
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
    /// The rejected limits of the window, one per (type, reset time), each at its earliest entry. A limit entry that cannot be placed in time
    /// (no timestamp) is left out. A file that cannot be read gives none of its entries.
    /// </summary>
    private static List<(DateTimeOffset Time, string Type, DateTimeOffset ResetsAt)> FirstHits(List<FileInfo> files, DateTimeOffset since, DateTimeOffset now)
    {
        var earliest = new Dictionary<(string Type, DateTimeOffset ResetsAt), DateTimeOffset>();
        foreach (var file in files)
        {
            foreach (var entry in LimitEntries.Whole(file.FullName))
            {
                if (entry.Status != Rejected || entry.Written is not { } written || written < since || written > now) continue;
                var key = (entry.Type, entry.ResetsAt);
                if (!earliest.TryGetValue(key, out var known) || written < known) earliest[key] = written;
            }
        }
        return earliest.Select(pair => (pair.Value, pair.Key.Type, pair.Key.ResetsAt)).ToList();
    }

    /// <summary>
    /// The usage records of one transcript that fall between <paramref name="from"/> and <paramref name="to"/>. The whole file is read first,
    /// so a file that fails part way gives none of its records, as it gives no limit entries.
    /// </summary>
    private static List<(DateTimeOffset Time, string Key, string Model, TokenUsage Usage)> UsageIn(string path, DateTimeOffset from, DateTimeOffset to)
    {
        var found = new List<(DateTimeOffset Time, string Key, string Model, TokenUsage Usage)>();
        try
        {
            foreach (var record in TranscriptReader.UsageRecords(TranscriptFile.ReadLines(path), path))
            {
                if (record.Time is { } time && time >= from && time <= to) found.Add((time, record.Key, record.Model, record.Usage));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        return found;
    }

    /// <summary>
    /// Counts one message in every hit whose window holds it: a hit at time T holds a message at t when T - 5 hours is at or before t, and t is at
    /// or before T. The hits are sorted by time, so the first hit at or after t is found by a search, and the hits after it are checked in turn.
    /// </summary>
    private static void Count(
        (DateTimeOffset Time, string Key, string Model, TokenUsage Usage) usage,
        DateTimeOffset[] times, UsageLedger[] ledgers, string file)
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
