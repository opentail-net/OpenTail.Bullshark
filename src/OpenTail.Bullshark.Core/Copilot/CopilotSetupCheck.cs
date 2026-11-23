using System.ComponentModel;
using System.Text.RegularExpressions;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>
/// The GitHub Copilot CLI rows of the setup check (G1.6, G-D13, G-O5). When a <c>copilot</c> program (copilot.exe, copilot.cmd or copilot.ps1) is on
/// PATH, there are two rows. The terminal row is the copy that a terminal tab runs, through <c>cmd.exe</c>. The start row is <c>copilot.ps1</c>, the copy
/// that a start from text runs. Each row reads its copy's version with auto-update off. A row is a Problem when its version is older than
/// <see cref="CopilotFlagsVersion.FirstVersionWithFlags"/> (see <see cref="CopilotFlagsVersion"/>), Ok when it is not, and a Warning when the version
/// cannot be read or parsed. The start row warns when copilot.ps1 is not on PATH, because a start from text is not possible then. Nothing else is run.
/// </summary>
public static class CopilotSetupCheck
{
    private const string Area = "Copilot CLI";

    /// <summary>The PowerShell shim a start from text needs (the launcher's script runs it). It is also the start row's name.</summary>
    private const string ShimName = "copilot.ps1";

    /// <summary>The warning of the start row when the shim is not on PATH: the start from text cannot run.</summary>
    private const string ShimMissing = "copilot.ps1 not found on PATH: a start from text is not possible";

    /// <summary>How the terminal copy is named in its problem text and in the log.</summary>
    private const string TerminalCopy = "The terminal's copilot";

    /// <summary>How the start copy is named in its problem text and in the log.</summary>
    private const string StartCopy = "The start's copilot.ps1";

    /// <summary>The program names tried on each PATH folder, in order. PATHEXT is not used, so copilot.ps1 is found on any machine.</summary>
    private static readonly string[] ProgramExtensions = [".exe", ".cmd", ".ps1"];

    /// <summary>How long a version read may run. An assumption, not a measured value: a read that is slower is shown as not read.</summary>
    public static readonly TimeSpan VersionTimeout = TimeSpan.FromSeconds(20);

    /// <summary>The longest detail of a row (a path can be long, so this is more than the Claude and Codex rows' limit).</summary>
    private const int MaxDetail = 300;

    /// <summary>The first version-shaped text (1.0.34, or 1.0.104-3) in the output.</summary>
    private static readonly Regex VersionShape = new(@"\b\d+\.\d+\.\d+(?:-[0-9A-Za-z.]+)?\b", RegexOptions.CultureInvariant);

    /// <summary>The arguments of the terminal copy's version read: through <c>cmd.exe</c>, no update check, no account sync (G-D8, G-D13).</summary>
    public static IReadOnlyList<string> VersionArguments { get; } =
        ["/d", "/c", "copilot", "--version", "--no-auto-update", CopilotTerminalOpener.SyncOff];

    /// <summary>
    /// The arguments of the start copy's version read: <c>copilot.ps1</c> run by PowerShell 7 with the same two flags as the terminal read. The path is
    /// quoted for the single-quoted PowerShell string (see <see cref="CliStartLauncher.Quoted"/>).
    /// </summary>
    public static IReadOnlyList<string> StartVersionArguments(string shimPath) =>
        ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $"& '{CliStartLauncher.Quoted(shimPath)}' --version --no-auto-update {CopilotTerminalOpener.SyncOff}"];

    /// <summary>
    /// The rows. <paramref name="terminalRunner"/> runs the terminal copy's read (the app passes <c>new ProcessCliRunner("cmd.exe")</c>) and
    /// <paramref name="startRunner"/> runs the start copy's read (<c>new ProcessCliRunner("pwsh.exe")</c>). <paramref name="pathVariable"/> is the PATH
    /// that is searched; the process's own PATH is used when it is null. A test fakes both runners. Nothing is read unless a copilot program is found.
    /// <paramref name="log"/> receives one line per row: the copy's name, its state and its version or reason, never a path.
    /// </summary>
    public static async Task<IReadOnlyList<SetupItem>> RunAsync(
        ICliRunner terminalRunner,
        ICliRunner startRunner,
        string? pathVariable = null,
        CancellationToken ct = default,
        Action<string>? log = null,
        string? bundleRoot = null)
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

        // The terminal row: the copy that cmd.exe runs for a terminal tab. The version read is the same as before.
        var terminal = await ReadRowAsync("copilot", TerminalCopy, "bundled version", found, terminalRunner, VersionArguments, log, ct);
        terminal = AcceptDownloadedBundle(terminal, bundleRoot, log);

        // The start row: the copy that a start from text runs. The launcher's script finds copilot.ps1 with Get-Command, the first one on PATH, which
        // is the one searched for here. Without it the start row warns, and the start route refuses to start anyway.
        var shim = SetupCheck.FindOnPath(ShimName, path);
        if (shim is null)
        {
            log?.Invoke($"Copilot setup check, {StartCopy}: Warning. {ShimMissing}");
            return [terminal, new SetupItem(Area, ShimName, SetupState.Warning, ShimMissing)];
        }

        var start = await ReadRowAsync(ShimName, StartCopy, "copilot.ps1 version", shim, startRunner, StartVersionArguments(shim), log, ct);
        return [terminal, start];
    }

    /// <summary>
    /// One row: runs the version read of one copy, judges it, and logs the result without the path. A program that cannot be started (PowerShell 7
    /// missing, for the start copy) is a Warning, not an exception.
    /// </summary>
    private static async Task<SetupItem> ReadRowAsync(
        string name,
        string copy,
        string label,
        string found,
        ICliRunner runner,
        IReadOnlyList<string> arguments,
        Action<string>? log,
        CancellationToken ct)
    {
        CliResult result;
        try
        {
            result = await runner.RunAsync(arguments, null, VersionTimeout, ct);
        }
        catch (Win32Exception)
        {
            var couldNot = $"{label} not read (the program could not be started)";
            log?.Invoke($"Copilot setup check, {copy}: Warning. {couldNot}");
            return new SetupItem(Area, name, SetupState.Warning, Detail(couldNot, found));
        }

        var (state, status) = Judge(result, copy, label);
        log?.Invoke($"Copilot setup check, {copy}: {state}. {status}");
        return new SetupItem(Area, name, state, Detail(status, found));
    }

    /// <summary>
    /// The state and the status text of one read. A timeout, an exit code other than 0, or output with no version is a Warning (the version is not
    /// known, so the flags are not known either). A version older than the flags' version is a Problem. Any other version is Ok.
    /// </summary>
    private static (SetupState State, string Status) Judge(CliResult result, string copy, string label)
    {
        if (result.TimedOut) return (SetupState.Warning, $"{label} not read (timed out)");
        if (result.ExitCode != 0) return (SetupState.Warning, $"{label} not read (exit {result.ExitCode})");

        var match = VersionShape.Match(result.StdOut);
        if (!match.Success) return (SetupState.Warning, $"{label} not read (no version in the output)");

        var version = match.Value;
        if (!CopilotFlagsVersion.HasFlags(version))
        {
            return (SetupState.Problem,
                $"{copy} is version {version}, older than {CopilotFlagsVersion.FirstVersionWithFlags}, the first build with the flags this app passes. "
                + "Update it, or remove the older copy from PATH.");
        }
        return (SetupState.Ok, $"{label} {version} (read with auto-update off)");
    }

    /// <summary>
    /// The terminal row when the launcher's own copy is older than the flags but the newest downloaded bundle has them (PP-D2). The launcher runs that
    /// bundle, so the row is Ok and names it. Any other row is returned unchanged. The log names the bundle version, never a path.
    /// </summary>
    private static SetupItem AcceptDownloadedBundle(SetupItem terminal, string? root, Action<string>? log)
    {
        if (root is null || terminal.State != SetupState.Problem) return terminal;
        var bundle = CopilotBundles.NewestVersion(root);
        if (bundle is null || !CopilotFlagsVersion.HasFlags(bundle)) return terminal;
        var detail = $"runs the downloaded bundle {bundle}; the launcher's own copy is older and is not what the app's commands run";
        log?.Invoke($"Copilot setup check, {TerminalCopy}: Ok. Runs the downloaded bundle {bundle}.");
        return new SetupItem(terminal.Area, terminal.Name, SetupState.Ok, detail);
    }

    /// <summary>The row's detail: the status, then the path it is about, clipped at <see cref="MaxDetail"/>.</summary>
    private static string Detail(string status, string path) => SetupCheck.Clip($"{status}; found: {path}", MaxDetail);
}
