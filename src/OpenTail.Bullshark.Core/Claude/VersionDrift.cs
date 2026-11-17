using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.Claude;

/// <summary>
/// The background daemon runs a different claude version from the installed one (round 7 item 2). Its sessions keep the binary they were
/// started with until they are respawned. Nothing here restarts anything: the owner's button does, after a confirmation.
/// </summary>
public sealed record VersionDrift(string DaemonVersion, string CliVersion)
{
    private static readonly Regex VersionAtStart = new(@"^\s*v?(\d+(?:\.\d+)+)", RegexOptions.Compiled);

    /// <summary>
    /// The version number at the start of a version text: "2.1.326 (Claude Code)" and "v2.1.326" both give "2.1.326". Null when the
    /// text does not start with a number.
    /// </summary>
    public static string? VersionOf(string? text) =>
        text is null ? null : VersionAtStart.Match(text) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>
    /// The drift between the daemon's version and the installed one. Null when they match, or when either is unknown: then no notice is shown.
    /// </summary>
    public static VersionDrift? Of(string? daemonVersion, string? cliVersion)
    {
        var daemon = VersionOf(daemonVersion);
        var cli = VersionOf(cliVersion);
        if (daemon is null || cli is null) return null;
        return string.Equals(daemon, cli, StringComparison.Ordinal) ? null : new VersionDrift(daemon, cli);
    }

    /// <summary>
    /// The drift as the window reads it from the daemon status (round 8 item 7): only a running daemon has a version to compare. A status that
    /// is not running, or is unknown, gives no notice, whatever text it carries.
    /// </summary>
    public static VersionDrift? OfStatus(DaemonStatus daemon, string? cliVersion) =>
        daemon.State == DaemonState.Running ? Of(daemon.Version, cliVersion) : null;

    /// <summary>The short form for the status line.</summary>
    public string StatusText => $"background daemon runs claude {DaemonVersion}, installed {CliVersion}: respawn all to switch";

    /// <summary>The sentence the owner reads on the button's tooltip.</summary>
    public string Message =>
        $"The background daemon runs claude {DaemonVersion}; the installed claude is {CliVersion}. Respawn all restarts the background sessions on {CliVersion}.";
}
