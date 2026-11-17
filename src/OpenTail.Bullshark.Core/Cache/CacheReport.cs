using System.Globalization;
using System.Text;
using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Cache;

/// <summary>One row of the report: the consecutive pairs of one lifetime class falling in one gap bucket.</summary>
public sealed record CacheRow(string Lifetime, string Bucket, long Pairs, double RewriteShare, double ReadShare);

/// <summary>
/// The report that tests the warm and cold rule against the real transcripts (D-S7, "the index re-measures them").
/// For each pair of consecutive requests in a session, the gap between them is put in a bucket, using the limits of the
/// lifetime the earlier request wrote with. For each bucket it reports how often the later request rewrote the context
/// (cache writes are at least half of its context) and how often it read it (cache reads are at least half).
/// Each message counts once (message.id:requestId, F34 to F36).
/// </summary>
public sealed record CacheReportResult(
    string ProjectsRoot,
    int Files,
    long CountedRequests,
    long Pairs,
    int SessionsOneHour,
    int SessionsFiveMinutes,
    int SessionsUnknown,
    IReadOnlyList<CacheRow> Rows);

public static class CacheReport
{
    private sealed record Request(string? Key, DateTimeOffset Time, long Input, long CacheRead, long CacheWrite, long Write1h, long Write5m)
    {
        public long Context => Input + CacheRead + CacheWrite;
    }

    public static CacheReportResult Compute(string projectsRoot)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var rows = new Dictionary<(string Lifetime, string Bucket), (long Pairs, long Rewrites, long Reads)>();
        var sessions = new Dictionary<string, int>(StringComparer.Ordinal);
        long counted = 0, pairs = 0;
        int files = 0;

        if (!Directory.Exists(projectsRoot))
            return new CacheReportResult(projectsRoot, 0, 0, 0, 0, 0, 0, []);

        // Oldest file first, as the index does: a message copied into a later (resumed) file is counted where it first appeared.
        var ordered = new DirectoryInfo(projectsRoot)
            .EnumerateFiles("*.jsonl", SearchOption.AllDirectories)
            .OrderBy(f => f.CreationTimeUtc)
            .ThenBy(f => f.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(f => f.FullName)
            .ToList();

        foreach (var file in ordered)
        {
            files++;
            var requests = ReadRequests(file, seen);
            counted += requests.Count;
            if (requests.Count < 2)
            {
                if (requests.Count == 1) Count(sessions, LifetimeOf(requests[0], CacheLifetime.None));
                continue;
            }

            requests.Sort((a, b) => a.Time.CompareTo(b.Time));
            var lifetime = CacheLifetime.None;
            // The session's lifetime is the one its most recent cache write showed. Carried forward to later requests.
            foreach (var request in requests)
            {
                var own = LifetimeOf(request, lifetime);
                lifetime = own;
            }

            Count(sessions, lifetime == CacheLifetime.None ? "unknown" : lifetime == CacheLifetime.FiveMinutes ? "5m" : "1h");

            var previousLifetime = CacheLifetime.None;
            for (int i = 1; i < requests.Count; i++)
            {
                var previous = requests[i - 1];
                var next = requests[i];
                previousLifetime = LifetimeOf(previous, previousLifetime);
                if (previousLifetime == CacheLifetime.None) continue;

                var (warm, cold) = CacheRule.Thresholds(previousLifetime == CacheLifetime.FiveMinutes ? CacheTtl.FiveMinutes : CacheTtl.OneHour);
                var gap = next.Time - previous.Time;
                var bucket = gap < warm ? "warm" : gap < cold ? "cooling" : "cold";
                var key = (previousLifetime == CacheLifetime.FiveMinutes ? "5m" : "1h", bucket);

                var context = next.Context;
                bool rewrote = context > 0 && next.CacheWrite * 2 >= context;
                bool read = context > 0 && next.CacheRead * 2 >= context;
                rows.TryGetValue(key, out var acc);
                rows[key] = (acc.Pairs + 1, acc.Rewrites + (rewrote ? 1 : 0), acc.Reads + (read ? 1 : 0));
                pairs++;
            }
        }

        var table = rows
            .OrderBy(kv => kv.Key.Lifetime, StringComparer.Ordinal)
            .ThenBy(kv => BucketOrder(kv.Key.Bucket))
            .Select(kv => new CacheRow(kv.Key.Lifetime, kv.Key.Bucket, kv.Value.Pairs,
                kv.Value.Pairs == 0 ? 0 : (double)kv.Value.Rewrites / kv.Value.Pairs,
                kv.Value.Pairs == 0 ? 0 : (double)kv.Value.Reads / kv.Value.Pairs))
            .ToList();

        return new CacheReportResult(projectsRoot, files, counted, pairs,
            sessions.GetValueOrDefault("1h"), sessions.GetValueOrDefault("5m"), sessions.GetValueOrDefault("unknown"), table);
    }

    private enum CacheLifetime { None, OneHour, FiveMinutes }

    private static CacheLifetime LifetimeOf(Request request, CacheLifetime carried) =>
        request.Write1h > 0 ? CacheLifetime.OneHour :
        request.Write5m > 0 ? CacheLifetime.FiveMinutes :
        carried;

    private static void Count(Dictionary<string, int> sessions, string lifetime) =>
        sessions[lifetime] = sessions.GetValueOrDefault(lifetime) + 1;

    private static void Count(Dictionary<string, int> sessions, CacheLifetime lifetime) =>
        Count(sessions, lifetime switch
        {
            CacheLifetime.OneHour => "1h",
            CacheLifetime.FiveMinutes => "5m",
            _ => "unknown",
        });

    private static int BucketOrder(string bucket) => bucket switch
    {
        "warm" => 0,
        "cooling" => 1,
        _ => 2,
    };

    /// <summary>Reads the requests of one file that no earlier file counted. Lines that cannot be read are skipped.</summary>
    private static List<Request> ReadRequests(string path, HashSet<string> seen)
    {
        var requests = new List<Request>();
        try
        {
            using var stream = TranscriptFile.Open(path);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (!line.Contains("\"usage\"", StringComparison.Ordinal)) continue;
                var request = Parse(line);
                if (request is null) continue;
                if (request.Key is not null && !seen.Add(request.Key)) continue;
                requests.Add(request);
            }
        }
        catch (IOException)
        {
            // A file that cannot be read now is left out of the report, and the report says how many files it read.
        }
        return requests;
    }

    private static Request? Parse(string line)
    {
        // A line that does not parse, or whose values cannot be read, is not a request (round 12 item A1).
        if (!TranscriptJson.TryParse(line, out var document)) return null;

        using (document)
        {
            try
            {
                return RequestOf(document.RootElement);
            }
            catch (Exception ex) when (TranscriptJson.IsUnreadable(ex))
            {
                return null;
            }
        }
    }

    private static Request? RequestOf(JsonElement root)
    {
        if (TranscriptJson.Str(root, "type") != "assistant") return null;
        if (!root.TryGetProperty("message", out var message) ||
            !message.TryGetProperty("usage", out var usage) ||
            usage.ValueKind != JsonValueKind.Object)
            return null;
        if (TranscriptJson.Time(root, "timestamp") is not { } time) return null;

        string? key = TranscriptJson.Str(message, "id") is { } messageId && TranscriptJson.Str(root, "requestId") is { } requestId
            ? $"{messageId}:{requestId}"
            : null;

        long write1h = 0, write5m = 0;
        if (usage.TryGetProperty("cache_creation", out var split) && split.ValueKind == JsonValueKind.Object)
        {
            write1h = TranscriptJson.Long(split, "ephemeral_1h_input_tokens");
            write5m = TranscriptJson.Long(split, "ephemeral_5m_input_tokens");
        }

        return new Request(key, time,
            TranscriptJson.Long(usage, "input_tokens"),
            TranscriptJson.Long(usage, "cache_read_input_tokens"),
            TranscriptJson.Long(usage, "cache_creation_input_tokens"),
            write1h,
            write5m);
    }
}
