using OpenTail.Bullshark.Core.Behaviour;
using OpenTail.Bullshark.Core.Claude;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The minimum CLI version before a launch (R4, decisions of 2026-11-22). Each launch point asks <see cref="ClaudeLaunchCheckAsync"/> or
/// <see cref="CodexLaunchCheckAsync"/> before anything is created. A version below the minimum refuses the launch, with a status line that names the
/// installed and the minimum versions and asks for an update; no update command is run or shown. A version that cannot be read refuses or goes on
/// with a warning, as <c>versions.unreadable</c> says. The probe runs `--version` off the UI thread, and a Supported reading is kept for the window run.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The probe of Claude Code: the default program, the same one the window's own `claude` calls run.</summary>
    private readonly ToolVersionProbe _claudeVersion = ToolVersionProbe.ForClaude(new ProcessCliRunner());

    /// <summary>The probe of Codex: the same `cmd.exe /d /c codex` line that a Codex terminal tab runs.</summary>
    private readonly ToolVersionProbe _codexVersion = ToolVersionProbe.ForCodex(new ProcessCliRunner("cmd.exe"));

    /// <summary>
    /// The answer of a launch check. <see cref="Refusal"/> is the status text that says why nothing starts, or null when the launch may go on.
    /// <see cref="Warning"/> is the text that says the version could not be read, shown with the start; null when the version was read.
    /// </summary>
    private sealed record LaunchCheck(string? Refusal, string? Warning);

    /// <summary>
    /// The note of the last supervisor pass about a refused start, or a version warning. The pass runs on a pool thread and writes it there; the window
    /// reads it after the pass, in the pass's status line.
    /// </summary>
    private string? _passLaunchNote;

    /// <summary>The check before a Claude launch: the installed Claude Code against behaviour.json's minimum.</summary>
    private Task<LaunchCheck> ClaudeLaunchCheckAsync(CancellationToken cancellationToken = default) =>
        LaunchCheckAsync(_claudeVersion, "Claude Code", _behaviour.Versions.Minimum.Claude, cancellationToken);

    /// <summary>The check before a Codex launch: the installed Codex CLI against behaviour.json's minimum.</summary>
    private Task<LaunchCheck> CodexLaunchCheckAsync(CancellationToken cancellationToken = default) =>
        LaunchCheckAsync(_codexVersion, "Codex CLI", _behaviour.Versions.Minimum.Codex, cancellationToken);

    private async Task<LaunchCheck> LaunchCheckAsync(
        ToolVersionProbe probe, string tool, string minimum, CancellationToken cancellationToken)
    {
        // The probe starts the program, which the process start does on the calling thread: it runs on a pool thread, and the window waits for the answer (R4).
        var reading = await Task.Run(async () => await probe.CheckAsync(minimum, cancellationToken), cancellationToken);
        switch (reading.Verdict)
        {
            case VersionVerdict.Supported:
                return new LaunchCheck(null, null);

            case VersionVerdict.TooOld:
                // The log names the tool and the verdict only: the installed version is in the status line, and nothing else is logged.
                AppLog.Write($"{tool} launch refused: the installed version is below the minimum");
                return new LaunchCheck(
                    $"Not started: {tool} {reading.Installed} is older than {minimum}, the version this build needs. " +
                    $"Update {tool} to {minimum} or later, then start again.",
                    null);

            default:
                if (_behaviour.Versions.Unreadable == UnreadableVersion.Refuse)
                {
                    AppLog.Write($"{tool} launch refused: the version could not be read");
                    return new LaunchCheck(
                        $"Not started: the {tool} version could not be read ({reading.Reason}). Check that {tool} runs, then try again.",
                        null);
                }
                AppLog.Write($"{tool} launch goes on: the version could not be read");
                return new LaunchCheck(null, $"Warning: the {tool} version could not be read ({reading.Reason}); it starts anyway.");
        }
    }

    /// <summary>The text with the warning of an unreadable version in front of it, when there is one.</summary>
    private static string WithWarning(string? warning, string text) => warning is null ? text : $"{warning} {text}";

    /// <summary>
    /// The supervisor pass asks before each start and each resume (<c>SupervisorRunner.LaunchRefusal</c>): the refusal of the Claude check, or null to
    /// start. It runs on a pool thread, so it writes only the note, which the window shows after the pass.
    /// </summary>
    private async Task<string?> PassLaunchRefusalAsync(CancellationToken cancellationToken)
    {
        var check = await ClaudeLaunchCheckAsync(cancellationToken);
        _passLaunchNote = check.Refusal ?? check.Warning ?? _passLaunchNote;
        return check.Refusal;
    }
}
