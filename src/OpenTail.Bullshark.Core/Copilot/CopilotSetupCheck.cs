using System.Text.RegularExpressions;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Health;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>
/// The GitHub Copilot CLI row of the setup check (G1.6, G-D13, G-O5). It has one item: whether a <c>copilot</c> program (copilot.exe, copilot.cmd or
/// copilot.ps1) is on PATH, shown with its path. When it is, the bundled version is read with <c>copilot --version --no-auto-update</c> through
/// <c>cmd.exe</c>, and the row says that this is the bundled version of the copy the app runs (auto-update is off for the read). Nothing else is run.
/// </summary>
public static class CopilotSetupCheck
{
    private const string Area = "Copilot CLI";

    /// <summary>The program names tried on each PATH folder, in order. PATHEXT is not used, so copilot.ps1 is found on any machine.</summary>
    private static readonly string[] ProgramExtensions = [".exe", ".cmd", ".ps1"];

    /// <summary>How long the version read may run. An assumption, not a measured value: a read that is slower is shown as not read.</summary>
    public static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(20);

    /// <summary>The longest detail of the row (a path can be long, so this is more than the Claude and Codex rows' limit).</summary>
    private const int MaxDetail = 300;

    /// <summary>The first version-shaped text (1.0.34, or 1.0.104-3) in the output.</summary>
    private static readonly Regex VersionShape = new(@"\b\d+\.\d+\.\d+(?:-[0-9A-Za-z.]+)?\b", RegexOptions.CultureInvariant);

    /// <summary>The arguments of the version read: through <c>cmd.exe</c>, no update check, no account sync (G-D8, G-D13).</summary>
    public static IReadOnlyList<string> VersionArguments { get; } =
        ["/d", "/c", "copilot", "--version", "--no-auto-update", CopilotTerminalOpener.SyncOff];

    /// <summary>
    /// The one row. <paramref name="pathVariable"/> is the PATH that is searched; the process's own PATH is used when it is null. <paramref name="runner"/>
    /// runs the version read (the app passes <c>new ProcessCliRunner("cmd.exe")</c>); a test fakes it. Only when the program is found is the runner used.
    /// </summary>
    public static async Task<IReadOnlyList<SetupItem>> RunAsync(ICliRunner runner, string? pathVariable = null, CancellationToken ct = default)
    {
        var path = pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var found = SetupCheck.FindOnPath("copilot", path, ProgramExtensions);
        if (found is null)
        {
            return
            [
                new SetupItem(Area, "copilot", SetupState.Problem, "not found on PATH: install GitHub Copilot CLI, or add it to PATH, then press Check again"),
            ];
        }

        // Every command line the app builds carries --sync-session=off (G-D8), the version read included.
        // The status comes first and the path last, so a clipped detail keeps what was read; the path is the copy this app starts.
        var result = await runner.RunAsync(VersionArguments, null, VersionTimeout, ct);
        if (result.TimedOut)
        {
            return [new SetupItem(Area, "copilot", SetupState.Warning, Detail("bundled version not read (timed out)", found))];
        }
        if (result.ExitCode != 0)
        {
            return [new SetupItem(Area, "copilot", SetupState.Warning, Detail($"bundled version not read (exit {result.ExitCode})", found))];
        }

        var match = VersionShape.Match(result.StdOut);
        var detail = match.Success
            ? Detail($"bundled version {match.Value} (read with auto-update off)", found)
            : Detail("bundled version not read (no version in the output)", found);
        return [new SetupItem(Area, "copilot", SetupState.Ok, detail)];
    }

    /// <summary>The row's detail: the status, then the path it is about, clipped at <see cref="MaxDetail"/>.</summary>
    private static string Detail(string status, string path) => SetupCheck.Clip($"{status}; found: {path}", MaxDetail);
}
