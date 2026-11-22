using OpenTail.Bullshark.Core.Claude;

namespace OpenTail.Bullshark.Core.Behaviour;

/// <summary>
/// Reads one CLI's version through the program its launch runs, and checks it against the minimum (R4). The probe runs the version command only, which
/// starts no model turn, and the command is bounded by <see cref="DefaultTimeout"/>. A reading that is <see cref="VersionVerdict.Supported"/> is kept for
/// the window run, so the next launch does not run the command again. A reading that is not Supported (too old, or could not be read) is kept for the
/// recheck window (<c>versions.recheckSeconds</c>), so a launch that is refused is not checked again every few seconds. A kept reading is used only for
/// the same minimum, and only inside the window; after it, or with a window of zero, the command runs again and a fix shows at once.
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
    private readonly Func<TimeSpan> _recheckWindow;
    private readonly Func<DateTimeOffset> _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private VersionGateResult? _supported;
    private VersionGateResult? _kept;
    private DateTimeOffset _keptAt;

    /// <param name="tool">The tool's name, used in the reason text only.</param>
    /// <param name="runner">The runner for the program that the launch runs. See <see cref="ForClaude"/> and <see cref="ForCodex"/>.</param>
    /// <param name="versionArguments">The arguments that print the version.</param>
    /// <param name="timeout">The time limit of the command; <see cref="DefaultTimeout"/> when null.</param>
    /// <param name="recheckWindow">
    /// How long a reading that is not Supported is kept (<c>versions.recheckSeconds</c>). It is read at each check, so a changed setting applies at the
    /// next check. Null, zero or less keeps nothing: the command runs on every check.
    /// </param>
    /// <param name="clock">The clock that times the kept readings; <see cref="DateTimeOffset.UtcNow"/> when null.</param>
    public ToolVersionProbe(
        string tool,
        ICliRunner runner,
        IReadOnlyList<string> versionArguments,
        TimeSpan? timeout = null,
        Func<TimeSpan>? recheckWindow = null,
        Func<DateTimeOffset>? clock = null)
    {
        _tool = tool;
        _runner = runner;
        _arguments = versionArguments;
        _timeout = timeout ?? DefaultTimeout;
        _recheckWindow = recheckWindow ?? (() => TimeSpan.Zero);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// The probe for Claude Code: <c>claude --version</c>. Pass the runner the window's own <c>claude</c> calls use (a <c>ProcessCliRunner</c> with the
    /// default program), so the probe runs the same program. <paramref name="recheckWindow"/> is the setting's window (see the constructor).
    /// </summary>
    public static ToolVersionProbe ForClaude(ICliRunner runner, Func<TimeSpan>? recheckWindow = null) =>
        new("Claude Code", runner, ["--version"], recheckWindow: recheckWindow);

    /// <summary>
    /// The probe for Codex: <c>cmd.exe /d /c codex --version</c>, the line that <c>CodexTerminalOpener</c> runs. The runner must run <c>cmd.exe</c> (a
    /// <c>ProcessCliRunner</c> with "cmd.exe"), because on an npm install <c>codex</c> is a <c>codex.cmd</c> shim.
    /// </summary>
    public static ToolVersionProbe ForCodex(ICliRunner runner, Func<TimeSpan>? recheckWindow = null) =>
        new("Codex CLI", runner, ["/d", "/c", "codex", "--version"], recheckWindow: recheckWindow);

    /// <summary>
    /// The verdict for the installed version against <paramref name="minimum"/>. A Supported reading for the same minimum is returned from the cache,
    /// without running the command. A reading that is not Supported for the same minimum is returned from the cache while it is inside the recheck
    /// window. Checks run one at a time, so two launches at once run the command once.
    /// </summary>
    public async Task<VersionGateResult> CheckAsync(string minimum, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_supported is { } cached && cached.Minimum == minimum) return cached;

            var window = _recheckWindow();
            if (_kept is { } kept && kept.Minimum == minimum && window > TimeSpan.Zero && KeptInside(window)) return kept;

            var result = await ReadAsync(minimum, cancellationToken);
            if (result.Verdict == VersionVerdict.Supported)
            {
                _supported = result;
                _kept = null;
            }
            else
            {
                _kept = result;
                _keptAt = _clock();
            }
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>True when the kept reading was taken less than <paramref name="window"/> ago. A time before it (a clock set back) is not inside the window.</summary>
    private bool KeptInside(TimeSpan window)
    {
        var age = _clock() - _keptAt;
        return age >= TimeSpan.Zero && age < window;
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
