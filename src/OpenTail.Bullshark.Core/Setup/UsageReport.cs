using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Codex;

namespace OpenTail.Bullshark.Core.Setup;

/// <summary>The tokens of a usage total, as the Limits page reads them.</summary>
public static class UsageTokens
{
    /// <summary>Input + output + cache written: the "New" figure. Cache read is not in it; the page shows it apart.</summary>
    public static long NewTokens(this TokenUsage usage) => usage.Input + usage.Output + usage.CacheCreation;
}

/// <summary>
/// One local day of the usage report: the total of its messages (a message belongs to the day its usage is attributed to, by
/// <c>usage.attribution</c>; with <c>event</c>, the day of its earliest record), and the project with the most New tokens that day (ties by name).
/// <see cref="TopProject"/> is null when no message is counted on the day.
/// </summary>
public sealed record UsageDay(DateOnly Day, TokenUsage Total, string? TopProject);

/// <summary>One project of the usage report: its total, and how many days of the window it had any usage.</summary>
public sealed record UsageProject(string Project, TokenUsage Total, int ActiveDays);

/// <summary>
/// The tokens per local day and per project over the last days, counted once per message across all transcripts (the largest value per field, as
/// <see cref="UsageLedger"/> keeps it). It is built from the same transcripts as the limit hits, in the same read (see <see cref="LimitHistory.ReadReportWithCache"/>).
/// <para>
/// <see cref="Days"/> lists every day of the window, newest first, with zero days included. <see cref="Projects"/> is ordered by New tokens, descending, then
/// by name; it has at most <see cref="TopProjectCount"/> rows, and when more projects have usage the last row is the "Other" row, which holds the rest
/// (<see cref="OtherProjects"/> is how many: 0 means there is no Other row). Read the last row as Other only when <see cref="OtherProjects"/> is above 0,
/// since a project folder could itself be named "Other". The rows of <see cref="Days"/> and <see cref="Projects"/> both add up to <see cref="Total"/>.
/// </para>
/// <para>
/// A project is the first folder under the projects root (the encoded working folder of the session); a subagent transcript belongs to the folder
/// it is in. A transcript written directly in the root is "(root)". No text is kept: counts, dates and folder names only.
/// </para>
/// </summary>
public sealed record UsageReport(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<UsageDay> Days,
    IReadOnlyList<UsageProject> Projects,
    int OtherProjects,
    TokenUsage Total)
{
    /// <summary>The number of named project rows before the Other row.</summary>
    public const int TopProjectCount = 10;

    /// <summary>The name of the row that holds the projects past the top ones.</summary>
    public const string OtherName = "Other";

    /// <summary>The project of a transcript that is written directly in the projects root.</summary>
    internal const string RootProject = "(root)";

    /// <summary>
    /// The report for the local days <paramref name="days"/> long, ending on the local date of <paramref name="now"/>. <paramref name="reads"/> is the
    /// window's Claude transcripts as the limit history read them; the order of the list does not matter (the files are taken in path order here).
    /// <paramref name="codex"/> is the window's Codex sessions (one record per rollout). Each usage record counts at the time
    /// <paramref name="attribution"/> gives it (<see cref="UsageAttributionRule"/>): a message's copies are combined first (the largest value per
    /// field, and the earliest attributed time), then a message is left out when that time is after <paramref name="now"/> or before the first day.
    /// </summary>
    internal static UsageReport Build(
        IReadOnlyList<FileRecords> reads,
        IReadOnlyList<CodexSessionUsage> codex,
        string projectsRoot,
        DateTimeOffset now,
        int days,
        TimeZoneInfo zone,
        UsageAttribution attribution)
    {
        var today = LocalDate(now, zone);
        var first = today.AddDays(1 - days);

        // One message once across the files: its attributed time (the earliest among its copies), the project of the first file it is seen in (in path
        // order), and the largest value per field. A Claude session is a transcript file; its start and last update are its first and last usage record.
        var messages = new Dictionary<string, Message>(StringComparer.Ordinal);
        foreach (var file in reads.OrderBy(read => read.Path, StringComparer.Ordinal))
        {
            var project = ProjectOf(file.Path, projectsRoot);
            var sessionStart = file.Usage.Count > 0 ? file.Usage.Min(record => record.Time) : default;
            var sessionLast = file.Usage.Count > 0 ? file.Usage.Max(record => record.Time) : default;
            foreach (var record in file.Usage)
            {
                var at = UsageAttributionRule.TimeOf(attribution, record.Time, sessionStart, sessionLast);
                AddCopy(messages, record.Key, at, project, record.Usage);
            }
        }

        // A Codex session is one record, keyed by its file: no two Codex records are one message. Its own time is its last total.
        foreach (var session in codex)
        {
            var at = UsageAttributionRule.TimeOf(attribution, session.LastTotal, session.FirstTotal, session.LastTotal);
            AddCopy(messages, "codex:" + session.Path, at, session.Project, session.Usage);
        }

        // Each counted message goes to its day and its project. Days are kept with their projects, so a day's total and its top project come from one place.
        var byDay = new Dictionary<DateOnly, Dictionary<string, TokenUsage>>();
        var byProject = new Dictionary<string, TokenUsage>(StringComparer.Ordinal);
        var activeDays = new Dictionary<string, HashSet<DateOnly>>(StringComparer.Ordinal);
        foreach (var message in messages.Values)
        {
            if (message.Earliest > now) continue;
            var day = LocalDate(message.Earliest, zone);
            if (day < first || day > today) continue;

            if (!byDay.TryGetValue(day, out var projectsOfDay)) byDay[day] = projectsOfDay = new Dictionary<string, TokenUsage>(StringComparer.Ordinal);
            projectsOfDay[message.Project] = projectsOfDay.TryGetValue(message.Project, out var dayUsage)
                ? dayUsage.Add(message.Usage)
                : message.Usage;
            byProject[message.Project] = byProject.TryGetValue(message.Project, out var projectUsage)
                ? projectUsage.Add(message.Usage)
                : message.Usage;
            if (!activeDays.TryGetValue(message.Project, out var dates)) activeDays[message.Project] = dates = new HashSet<DateOnly>();
            dates.Add(day);
        }

        var dayRows = new List<UsageDay>(Math.Max(0, days));
        for (var day = today; day >= first; day = day.AddDays(-1))
        {
            if (!byDay.TryGetValue(day, out var projectsOfDay))
            {
                dayRows.Add(new UsageDay(day, TokenUsage.Zero, null));
                continue;
            }
            var top = projectsOfDay
                .OrderByDescending(pair => pair.Value.NewTokens())
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .First().Key;
            dayRows.Add(new UsageDay(day, Sum(projectsOfDay.Values), top));
        }

        var ranked = byProject
            .Select(pair => new UsageProject(pair.Key, pair.Value, activeDays[pair.Key].Count))
            .OrderByDescending(project => project.Total.NewTokens())
            .ThenBy(project => project.Project, StringComparer.Ordinal)
            .ToList();
        var projects = ranked.Take(TopProjectCount).ToList();
        var rest = ranked.Skip(TopProjectCount).ToList();
        if (rest.Count > 0)
        {
            var restDays = new HashSet<DateOnly>();
            foreach (var project in rest) restDays.UnionWith(activeDays[project.Project]);
            projects.Add(new UsageProject(OtherName, Sum(rest.Select(project => project.Total)), restDays.Count));
        }

        return new UsageReport(first, today, dayRows, projects, rest.Count, Sum(byProject.Values));
    }

    /// <summary>The first folder under the projects root, or <see cref="RootProject"/> for a transcript written directly in it.</summary>
    private static string ProjectOf(string path, string projectsRoot)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(projectsRoot), path);
        var parts = relative.Split(Path.DirectorySeparatorChar);
        return parts.Length > 1 ? parts[0] : RootProject;
    }

    /// <summary>
    /// Adds one copy of a message: a new message takes the copy's time, project and usage; a known one keeps the earliest attributed time and the
    /// largest value per field (F36). The project is the one of the first copy added, which is the first file in path order.
    /// </summary>
    private static void AddCopy(Dictionary<string, Message> messages, string key, DateTimeOffset at, string project, TokenUsage usage)
    {
        if (messages.TryGetValue(key, out var known))
        {
            if (at < known.Earliest) known.Earliest = at;
            known.Usage = Max(known.Usage, usage);
        }
        else
        {
            messages[key] = new Message(at, project, usage);
        }
    }

    private static DateOnly LocalDate(DateTimeOffset at, TimeZoneInfo zone) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

    private static TokenUsage Sum(IEnumerable<TokenUsage> usages) => usages.Aggregate(TokenUsage.Zero, (sum, usage) => sum.Add(usage));

    /// <summary>The largest value per field (F36), as <see cref="UsageLedger"/> keeps it.</summary>
    private static TokenUsage Max(TokenUsage a, TokenUsage b) => new(
        Math.Max(a.Input, b.Input), Math.Max(a.Output, b.Output),
        Math.Max(a.CacheCreation, b.CacheCreation), Math.Max(a.CacheRead, b.CacheRead));

    /// <summary>One message while the report is built: its attributed time (the earliest among its copies), its project and its largest usage.</summary>
    private sealed class Message(DateTimeOffset earliest, string project, TokenUsage usage)
    {
        public DateTimeOffset Earliest { get; set; } = earliest;

        public string Project { get; } = project;

        public TokenUsage Usage { get; set; } = usage;
    }
}
