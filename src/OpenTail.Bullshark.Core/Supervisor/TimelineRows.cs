using System.Globalization;

namespace OpenTail.Bullshark.Core.Supervisor;

/// <summary>The rows the newest part of a timeline shows, and how many older rows are left out of it.</summary>
public sealed record TimelinePage(IReadOnlyList<SupervisorEvent> Rows, int Older);

/// <summary>
/// What the run timeline shows (round 5 item 10): the events of one run, the newest rows of them, and the text of a row. The window draws
/// the page; the rules are here so that a test can reach them.
/// </summary>
public static class TimelineRows
{
    /// <summary>The rows drawn at once. "Show older" adds this many more, so the window never draws a whole log of tens of thousands of lines.</summary>
    public const int DefaultLimit = 2000;

    /// <summary>
    /// The events of one run, in the order they were written. An event that names its run belongs to that run, whatever its session is
    /// called now. An event written before run ids were kept (no run id) belongs to the run that has its session name, as before. With a
    /// <paramref name="session"/> name, only that session's events are kept; null means every session of the run.
    /// </summary>
    public static IReadOnlyList<SupervisorEvent> ForRun(IEnumerable<SupervisorEvent> events, RunRecord run, string? session)
    {
        var names = run.Specs.Select(spec => spec.Name).ToHashSet(StringComparer.Ordinal);
        return events
            .Where(e => (e.RunId is { } id ? string.Equals(id, run.RunId, StringComparison.Ordinal) : names.Contains(e.Session))
                        && (session is null || string.Equals(e.Session, session, StringComparison.Ordinal)))
            .ToList();
    }

    /// <summary>The newest <paramref name="limit"/> events, in order, and the number of older events that are not in the page.</summary>
    public static TimelinePage Newest(IReadOnlyList<SupervisorEvent> events, int limit)
    {
        var older = Math.Max(0, events.Count - Math.Max(0, limit));
        return new TimelinePage(events.Skip(older).ToList(), older);
    }

    /// <summary>
    /// One row of the timeline: the local date and time to the second, the session, the event, and its detail. The date is shown because
    /// a log spans days, and a row with the time alone reads as the time of today.
    /// </summary>
    public static string Line(SupervisorEvent e) =>
        $"{e.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}  {e.Session,-28} {e.Event,-24} {e.Detail}";
}
