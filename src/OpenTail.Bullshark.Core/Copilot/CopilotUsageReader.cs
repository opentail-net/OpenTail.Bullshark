using System.Text.Json;
using OpenTail.Bullshark.Core.Codex;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>One local day of Copilot usage: the sessions that ended that day, their premium requests, and their model tokens (PP-D5).</summary>
public sealed record CopilotUsageDay(DateOnly Day, int Sessions, long PremiumRequests, long InputTokens, long OutputTokens);

/// <summary>
/// The Copilot usage of the window's days, newest first, and how many session files could not be read (a file that cannot be opened, or a read that fails).
/// </summary>
public sealed record CopilotUsageReport(IReadOnlyList<CopilotUsageDay> Days, int SkippedFiles);

/// <summary>
/// The Copilot usage report (PP-D5, 2026-11-23). Each session's last <c>session.shutdown</c> event holds its premium requests (<c>data.totalPremiumRequests</c>)
/// and its per-model input and output tokens (<c>data.modelMetrics.&lt;model&gt;.usage</c>). A session counts on the local day of its shutdown. Premium requests
/// are Copilot's own unit: they are not added to the Claude or Codex token totals. Reads the events files only; writes nothing and runs no program.
/// </summary>
public static class CopilotUsageReader
{
    /// <summary>The event that ends a session, with its totals.</summary>
    private const string ShutdownType = "session.shutdown";

    /// <summary>The events file of each session, in its own folder under the history root.</summary>
    private const string EventsFile = "events.jsonl";

    /// <summary>
    /// The usage of the last <paramref name="days"/> local days up to <paramref name="now"/>, today included, newest day first. A missing history root
    /// gives no days and no skipped files. A session file whose events cannot be read is counted as skipped; a line that is not JSON is skipped quietly,
    /// as the other Copilot readers do.
    /// </summary>
    public static CopilotUsageReport Read(string historyRoot, DateTimeOffset now, int days, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(historyRoot) || days <= 0 || !Directory.Exists(historyRoot)) return new CopilotUsageReport([], 0);

        var today = DateOnly.FromDateTime(now.LocalDateTime);
        var first = today.AddDays(-(days - 1));
        var totals = new Dictionary<DateOnly, (int Sessions, long Premium, long Input, long Output)>();
        var skipped = 0;

        IEnumerable<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories(historyRoot);
        }
        catch (IOException) { return new CopilotUsageReport([], 1); }
        catch (UnauthorizedAccessException) { return new CopilotUsageReport([], 1); }

        foreach (var folder in folders)
        {
            ct.ThrowIfCancellationRequested();
            var file = Path.Combine(folder, EventsFile);
            if (!File.Exists(file)) continue;

            var (shutdown, failed) = LastShutdown(file, ct);
            if (failed)
            {
                skipped++;
                continue;
            }
            if (shutdown is null) continue;

            var day = DateOnly.FromDateTime(shutdown.Value.At.LocalDateTime);
            if (day < first || day > today) continue;

            var (sessions, premium, input, output) = totals.GetValueOrDefault(day);
            totals[day] = (sessions + 1, premium + shutdown.Value.Premium, input + shutdown.Value.Input, output + shutdown.Value.Output);
        }

        var result = totals
            .OrderByDescending(pair => pair.Key)
            .Select(pair => new CopilotUsageDay(pair.Key, pair.Value.Sessions, pair.Value.Premium, pair.Value.Input, pair.Value.Output))
            .ToList();
        return new CopilotUsageReport(result, skipped);
    }

    /// <summary>The totals of one session's last shutdown event.</summary>
    private readonly record struct Shutdown(DateTimeOffset At, long Premium, long Input, long Output);

    /// <summary>
    /// The session's last shutdown (the one with the latest timestamp), or null when the file has none. <c>failed</c> is true when the file cannot be opened
    /// or read.
    /// </summary>
    private static (Shutdown? Shutdown, bool Failed) LastShutdown(string file, CancellationToken ct)
    {
        Shutdown? last = null;
        try
        {
            using var reader = CopilotFiles.OpenText(file);
            foreach (var line in RolloutReader.Lines(reader, ct))
            {
                if (line.Overlong || !line.Text.Contains(ShutdownType, StringComparison.Ordinal)) continue;
                using var document = RolloutReader.TryParseObject(line.Text);
                if (document is null) continue;
                var root = document.RootElement;
                if (!root.TryGetProperty("type", out var type) || type.GetString() != ShutdownType) continue;
                if (RolloutReader.TimestampOf(root) is not { } at) continue;
                if (last is { } previous && previous.At >= at) continue;
                last = Measure(root, at);
            }
            return (last, false);
        }
        catch (IOException) { return (null, true); }
        catch (UnauthorizedAccessException) { return (null, true); }
    }

    /// <summary>The premium requests and the model tokens of one shutdown event. A missing number is zero.</summary>
    private static Shutdown Measure(JsonElement root, DateTimeOffset at)
    {
        long premium = 0, input = 0, output = 0;
        if (root.TryGetProperty("data", out var data))
        {
            premium = Number(data, "totalPremiumRequests");
            if (data.TryGetProperty("modelMetrics", out var models) && models.ValueKind == JsonValueKind.Object)
            {
                foreach (var model in models.EnumerateObject())
                {
                    if (!model.Value.TryGetProperty("usage", out var usage)) continue;
                    input += Number(usage, "inputTokens");
                    output += Number(usage, "outputTokens");
                }
            }
        }
        return new Shutdown(at, premium, input, output);
    }

    /// <summary>A numeric property as a long; zero when it is absent or not a number.</summary>
    private static long Number(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;
}
