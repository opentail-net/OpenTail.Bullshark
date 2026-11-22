using OpenTail.Bullshark.Core.Claude;

namespace OpenTail.Bullshark.Core.Behaviour;

/// <summary>
/// Reads one CLI's version through the program its launch runs, and checks it against the minimum (R4). The probe runs the version command only, which
/// starts no model turn, and the command is bounded by <see cref="DefaultTimeout"/>. A reading that is <see cref="VersionVerdict.Supported"/> is kept for
/// the window run, so the next launch does not run the command again. Every other result is read again on the next launch, so a fix shows at once.
/// A window keeps one probe per tool.
/// </summary>
public sealed class ToolVersionProbe
{
    /// <summary>How long the version command may run before its reading is "could not determine": about ten seconds.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly string _tool;
    private readonly ICliRunner _runner;
    private readonly IReadOnlyList<string> _arguments;
    private readonly TimeSpan _timeout;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private VersionGateResult? _supported;

    /// <param name="tool">The tool's name, used in the reason text only.</param>
    /// <param name="runner">The runner for the program that the launch runs. See <see cref="ForClaude"/> and <see cref="ForCodex"/>.</param>
    /// <param name="versionArguments">The arguments that print the version.</param>
    /// <param name="timeout">The time limit of the command; <see cref="DefaultTimeout"/> when null.</param>
    public ToolVersionProbe(string tool, ICliRunner runner, IReadOnlyList<string> versionArguments, TimeSpan? timeout = null)
    {
        _tool = tool;
        _runner = runner;
        _arguments = versionArguments;
        _timeout = timeout ?? DefaultTimeout;
    }

    /// <summary>
    /// The probe for Claude Code: <c>claude --version</c>. Pass the runner the window's own <c>claude</c> calls use (a <c>ProcessCliRunner</c> with the
    /// default program), so the probe runs the same program.
    /// </summary>
    public static ToolVersionProbe ForClaude(ICliRunner runner) => new("Claude Code", runner, ["--version"]);

    /// <summary>
    /// The probe for Codex: <c>cmd.exe /d /c codex --version</c>, the line that <c>CodexTerminalOpener</c> runs. The runner must run <c>cmd.exe</c> (a
    /// <c>ProcessCliRunner</c> with "cmd.exe"), because on an npm install <c>codex</c> is a <c>codex.cmd</c> shim.
    /// </summary>
    public static ToolVersionProbe ForCodex(ICliRunner runner) => new("Codex CLI", runner, ["/d", "/c", "codex", "--version"]);

    /// <summary>
    /// The verdict for the installed version against <paramref name="minimum"/>. A Supported reading for the same minimum is returned from the cache,
    /// without running the command. Checks run one at a time, so two launches at once run the command once.
    /// </summary>
    public async Task<VersionGateResult> CheckAsync(string minimum, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_supported is { } cached && cached.Minimum == minimum) return cached;

            var result = await ReadAsync(minimum, cancellationToken);
            if (result.Verdict == VersionVerdict.Supported) _supported = result;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<VersionGateResult> ReadAsync(string minimum, CancellationToken cancellationToken)
    {
        CliResult run;
        try
        {
            run = await _runner.RunAsync(_arguments, null, _timeout, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The type name only: a message can carry a machine path.
            return Unread(minimum, $"the {_tool} version command could not be run ({ex.GetType().Name})");
        }

        if (run.TimedOut) return Unread(minimum, $"the {_tool} version command timed out after {_timeout.TotalSeconds:0} seconds");
        if (run.ExitCode != 0) return Unread(minimum, $"the {_tool} version command exited with code {run.ExitCode}");

        // The version is printed on standard output. Standard error is read only when standard output has no version number.
        var output = VersionGate.VersionIn(run.StdOut) is null ? run.StdErr : run.StdOut;
        return VersionGate.Check(output, minimum);
    }

    private static VersionGateResult Unread(string minimum, string reason) =>
        new(VersionVerdict.CouldNotDetermine, null, minimum, reason);
}
