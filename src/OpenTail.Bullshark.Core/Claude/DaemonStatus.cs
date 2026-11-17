using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.Claude;

/// <summary>What `claude daemon status` said about the background daemon.</summary>
public enum DaemonState
{
    /// <summary>The output says the daemon is running.</summary>
    Running,

    /// <summary>The output says "not running". The command then exits with 1, which is not an error here.</summary>
    NotRunning,

    /// <summary>The output could not be read as either. It is never an error: the supervisor treats it as it did before round 7.</summary>
    Unknown,
}

/// <summary>
/// The background daemon as `claude daemon status` reports it (round 7 item 1). Every field after <see cref="State"/> is null when the
/// output does not say it. <see cref="Raw"/> keeps the text, so a line the parser does not know is still visible to the owner.
/// The "not running" text is checked against 2.1.326 (captured 2026-11-17). The running text is not captured: no daemon was running on the
/// machine it was checked on, so its keys follow `claude daemon --help` ("Show daemon pid, version, uptime").
/// </summary>
public sealed record DaemonStatus(
    DaemonState State,
    int? Pid,
    string? Version,
    string? Uptime,
    bool? ControlReachable,
    int? BackgroundWorkers,
    int? RosterAgeSeconds,
    string? LogPath,
    string Raw)
{
    private static readonly Regex NotRunningLine = new(@"^\s*(daemon:?\s*)?not running\b", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex RunningLine = new(@"^\s*(daemon:?\s*)?running\b", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex PidValue = new(@"\bpid[:\s]+(\d+)", RegexOptions.IgnoreCase);
    private static readonly Regex VersionValue = new(@"\bversion[:\s]+v?(\d+(?:\.\d+)+)", RegexOptions.IgnoreCase);
    private static readonly Regex UptimeValue = new(@"\buptime[:\s]+([^\r\n,)]+)", RegexOptions.IgnoreCase);
    private static readonly Regex ControlLine = new(@"^\s*control\.sock:[^\r\n]*", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex WorkersValue = new(@"bg workers:\s*(\d+)", RegexOptions.IgnoreCase);
    private static readonly Regex RosterValue = new(@"roster\.json:\s*updated (\d+)s ago", RegexOptions.IgnoreCase);
    private static readonly Regex LogValue = new(@"daemon\.log:[^\r\n]*?\bat\s+([^\r\n]+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);

    /// <summary>
    /// The daemon is not running, or its control socket is unreachable. Then the supervisor makes no start, resume or adoption
    /// decision in a pass, because the listing that `claude agents --json` shows then is not trusted (round 7 item 1).
    /// </summary>
    public bool BlocksDecisions => State == DaemonState.NotRunning || ControlReachable == false;

    /// <summary>
    /// The sentence the owner reads when <see cref="BlocksDecisions"/> is true, or null when the daemon is fine or its state is unknown.
    /// </summary>
    public string? Problem => State == DaemonState.NotRunning
        ? "Background daemon is not running — sessions listed as working/blocked are not alive. No session is started or resumed until it runs."
        : ControlReachable == false
            ? "Background daemon control is unreachable — sessions listed as working/blocked may not be alive. No session is started or resumed until it is reachable."
            : null;

    /// <summary>The short form of <see cref="Problem"/> for the status line, or null when there is no problem.</summary>
    public string? StatusProblem => State == DaemonState.NotRunning
        ? "background daemon not running: no session started or resumed"
        : ControlReachable == false
            ? "background daemon control unreachable: no session started or resumed"
            : null;

    /// <summary>A status with nothing known. Used when the command could not be run or timed out.</summary>
    public static DaemonStatus Unknown(string raw = "") =>
        new(DaemonState.Unknown, null, null, null, null, null, null, null, raw);

    /// <summary>
    /// Reads the text of `claude daemon status` (standard output and standard error together). The state comes from a line that starts with
    /// "running" or "not running"; the other fields come from the lines that name them. Text that matches neither is Unknown with every
    /// field null, never an error.
    /// </summary>
    public static DaemonStatus Parse(string output)
    {
        var text = output ?? string.Empty;
        var state = NotRunningLine.IsMatch(text) ? DaemonState.NotRunning
            : RunningLine.IsMatch(text) ? DaemonState.Running
            : DaemonState.Unknown;

        var pid = PidValue.Match(text);
        var version = VersionValue.Match(text);
        var uptime = UptimeValue.Match(text);
        var workers = WorkersValue.Match(text);
        var roster = RosterValue.Match(text);
        var log = LogValue.Match(text);

        return new DaemonStatus(
            State: state,
            Pid: pid.Success && int.TryParse(pid.Groups[1].Value, out var pidValue) ? pidValue : null,
            Version: version.Success ? version.Groups[1].Value : null,
            Uptime: uptime.Success ? uptime.Groups[1].Value.Trim() : null,
            ControlReachable: ControlReachableOf(text),
            BackgroundWorkers: workers.Success && int.TryParse(workers.Groups[1].Value, out var count) ? count : null,
            RosterAgeSeconds: roster.Success && int.TryParse(roster.Groups[1].Value, out var age) ? age : null,
            LogPath: log.Success ? log.Groups[1].Value : null,
            Raw: text.Trim());
    }

    /// <summary>
    /// The control socket line: "unreachable" is checked first, since the word contains "reachable". A line that says it is connected or
    /// listening is reachable. No such line gives null.
    /// </summary>
    private static bool? ControlReachableOf(string text)
    {
        var line = ControlLine.Match(text);
        if (!line.Success) return null;
        var value = line.Value;
        if (value.Contains("unreachable", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Contains("listening", StringComparison.OrdinalIgnoreCase) || value.Contains("connected", StringComparison.OrdinalIgnoreCase)
            || value.Contains("reachable", StringComparison.OrdinalIgnoreCase) || value.Contains(": ok", StringComparison.OrdinalIgnoreCase))
            return true;
        return null;
    }
}
