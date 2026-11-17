using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.Claude;

/// <summary>What `claude daemon status` said about the background daemon.</summary>
public enum DaemonState
{
    /// <summary>The output says the daemon is running.</summary>
    Running,

    /// <summary>The output says "not running". The command then exits with 1, which is not an error here.</summary>
    NotRunning,

    /// <summary>The output could not be read as either. It is never an error: the listing is used as it is.</summary>
    Unknown,
}

/// <summary>
/// The background daemon as `claude daemon status` reports it (round 7 item 1, revised by the planner). Every field after <see cref="State"/>
/// is null when the output does not say it. <see cref="Raw"/> keeps the text, so a line the parser does not know is still visible to the owner.
/// The "not running" text is checked against 2.1.326 (captured 2026-11-17). The running text is not captured: no daemon was running on the
/// machine it was checked on, so its keys follow `claude daemon --help` ("Show daemon pid, version, uptime").
/// <para>
/// "Not running" is the normal idle state: `claude daemon --help` says the daemon "runs on demand and exits once no client is connected and no
/// background session is running". So it never stops a pass. What it changes is how a listed session that claims working or blocked is read
/// (<see cref="IdleListing"/>).
/// </para>
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
    /// <summary>
    /// The state a listed session takes while the daemon is not running, when the listing says working or blocked. Such a session cannot be
    /// alive without the daemon, so it is shown and decided on as stale. Written into the state records, so the change is seen when it returns.
    /// </summary>
    public const string StaleState = "stale";

    private static readonly Regex NotRunningLine = new(@"^\s*(daemon:?\s*)?not running\b", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex RunningLine = new(@"^\s*(daemon:?\s*)?running\b", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex PidValue = new(@"\bpid[:\s]+(\d+)", RegexOptions.IgnoreCase);
    private static readonly Regex VersionValue = new(@"\bversion[:\s]+v?(\d+(?:\.\d+)+)", RegexOptions.IgnoreCase);
    private static readonly Regex UptimeValue = new(@"\buptime[:\s]+([^\r\n,)]+)", RegexOptions.IgnoreCase);
    private static readonly Regex ControlLine = new(@"^\s*control\.sock:[^\r\n]*", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    private static readonly Regex WorkersValue = new(@"bg workers:\s*(\d+)", RegexOptions.IgnoreCase);
    private static readonly Regex RosterValue = new(@"roster\.json:\s*updated (\d+)s ago", RegexOptions.IgnoreCase);
    private static readonly Regex LogValue = new(@"daemon\.log:[^\r\n]*?\bat\s+([^\r\n]+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);

    /// <summary>True when the output says the daemon is not running (the normal state while no background session runs).</summary>
    public bool IsNotRunning => State == DaemonState.NotRunning;

    /// <summary>What the owner reads for a session state: a stale session says "not running (daemon idle)"; any other state is shown as it is.</summary>
    public static string DisplayState(string state) => state == StaleState ? "not running (daemon idle)" : state;

    /// <summary>The listed states that need a running daemon to be true: working and blocked.</summary>
    public static bool ClaimsLive(string? state) =>
        string.Equals(state, "working", StringComparison.OrdinalIgnoreCase) || string.Equals(state, "blocked", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The listing as the supervisor and the window read it. While the daemon is not running, a listed session that claims working or blocked
    /// is shown as <see cref="StaleState"/>, with no waiting reason. It is not alive, so it is not "blocked" and nothing waits for the owner on
    /// it. The state it was listed with is kept in <see cref="AgentInfo.ListedState"/>, so the policy can tell a stale working session (lost:
    /// resumed under the rules for a lost one) from a stale blocked one (alerted once, never resumed; round 8 item 1). Any other listing, and any
    /// listing while the daemon is running or its state is unknown, comes back as it is.
    /// </summary>
    public IReadOnlyList<AgentInfo> IdleListing(IReadOnlyList<AgentInfo> agents)
    {
        if (!IsNotRunning || !agents.Any(a => ClaimsLive(a.State))) return agents;
        return agents.Select(a => ClaimsLive(a.State) ? a with { State = StaleState, WaitingFor = null, ListedState = a.State } : a).ToList();
    }

    /// <summary>
    /// How many listed background sessions claim working or blocked. Zero unless the daemon is not running. The count is taken from the
    /// listing as `claude agents --json` printed it, so it is what the owner sees in the listing.
    /// </summary>
    public int StaleCount(IReadOnlyList<AgentInfo> agents) =>
        IsNotRunning ? agents.Count(a => ClaimsLive(a.State)) : 0;

    /// <summary>
    /// The sentence for the Needs-you group: the daemon is not running and at least one listed session still claims working or blocked (an
    /// inconsistent listing). It never blocks anything. Null when there is nothing to say.
    /// </summary>
    public string? NeedsYouNotice(IReadOnlyList<AgentInfo> agents) =>
        StaleCount(agents) is var count and > 0
            ? $"Background daemon is not running: {count} session(s) listed as working/blocked are not alive."
            : null;

    /// <summary>The short form of <see cref="NeedsYouNotice"/> for the status line, or null.</summary>
    public string? StatusNotice(IReadOnlyList<AgentInfo> agents) =>
        StaleCount(agents) is var count and > 0
            ? $"background daemon not running: {count} listed session(s) not alive"
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
    /// listening is reachable. No such line gives null. The value is kept for the owner; it changes no decision.
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
