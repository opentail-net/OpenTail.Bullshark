namespace OpenTail.Bullshark.Core.Reports;

/// <summary>Which read-only report the headless host was asked for.</summary>
public enum HostReport
{
    /// <summary>No report was asked for. The host starts no session, so it has nothing else to do.</summary>
    None,
    Usage,
    Cache,
}

/// <summary>
/// The command line of the headless host (OpenTail.Bullshark.Supervisor). It runs one read-only report, <c>--usage-report</c> or
/// <c>--cache-report</c>, on the transcripts in the folder given after <c>--projects</c>, or in the default folder when there is none.
/// <see cref="Problem"/> is set when <c>--projects</c> has no folder after it: nothing, a blank, or another flag.
/// </summary>
public sealed record HostCommand(HostReport Report, string Projects, string? Problem)
{
    /// <summary>The message for a <c>--projects</c> with no folder after it.</summary>
    public const string ProjectsNeedsFolder = "--projects needs a folder after it: --projects <folder>.";

    /// <summary>
    /// Reads the arguments. <c>--usage-report</c> wins when both reports are asked for. The folder is the argument straight after the
    /// first <c>--projects</c>, and it is never a flag: <c>--projects --cache-report</c> is a problem, not a folder with that name.
    /// Other arguments are ignored.
    /// </summary>
    public static HostCommand Parse(IReadOnlyList<string> args, string defaultProjects)
    {
        var report = args.Contains("--usage-report") ? HostReport.Usage
            : args.Contains("--cache-report") ? HostReport.Cache
            : HostReport.None;

        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] != "--projects") continue;

            var next = i + 1 < args.Count ? args[i + 1] : null;
            if (string.IsNullOrWhiteSpace(next) || next.StartsWith("--", StringComparison.Ordinal))
                return new HostCommand(report, defaultProjects, ProjectsNeedsFolder);
            return new HostCommand(report, next, null);
        }
        return new HostCommand(report, defaultProjects, null);
    }
}
