using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>
/// Starts a new Copilot session with a start text (G1.7, G-D14). The shared launcher (<see cref="CliStartLauncher"/>) writes the text and builds the
/// command line. This class names the Copilot shim (<c>copilot.ps1</c>) and gives the call <c>copilot.ps1 -i &lt;text&gt; --sync-session=off</c>; the
/// sync flag is on every Copilot line.
/// </summary>
public static class CopilotStartLauncher
{
    /// <summary>The Copilot PowerShell shim. The script finds it with Get-Command, so a <c>copilot.exe</c> alone does not count.</summary>
    private const string ShimName = "copilot.ps1";

    /// <summary>The Starts folder that goes with a state file (see <see cref="CliStartLauncher.StartsFolderBeside"/>).</summary>
    public static string StartsFolderBeside(string statePath) => CliStartLauncher.StartsFolderBeside(statePath);

    /// <summary>Writes the start text to a new file in <paramref name="startsFolder"/> and returns its full path (see <see cref="CliStartLauncher.WriteStart"/>).</summary>
    public static string WriteStart(string startsFolder, string text, DateTimeOffset now) => CliStartLauncher.WriteStart(startsFolder, text, now);

    /// <summary>
    /// The command line of a terminal tab that starts Copilot with the text in <paramref name="promptPath"/>: the shared script runs
    /// <c>copilot.ps1 -i &lt;text&gt; --sync-session=off</c> after it has set the folder and deleted the file (see <see cref="CliStartLauncher.BuildScript"/>).
    /// </summary>
    public static string CommandLine(string folder, string promptPath) =>
        CliStartLauncher.CommandLine("Copilot", ShimName, folder, promptPath, $"& $shim.Source -i $prompt {CopilotTerminalOpener.SyncOff}");

    /// <summary>The full path of <c>pwsh.exe</c> on the PATH of this process, or null (see <see cref="CliStartLauncher.FindPwsh()"/>).</summary>
    public static string? FindPwsh() => CliStartLauncher.FindPwsh();

    /// <summary>The full path of <c>pwsh.exe</c> in the folders of <paramref name="searchPath"/>, or null (see <see cref="CliStartLauncher.FindPwsh(string?)"/>).</summary>
    public static string? FindPwsh(string? searchPath) => CliStartLauncher.FindPwsh(searchPath);

    /// <summary>
    /// The full path of <c>copilot.ps1</c> in the folders of <paramref name="searchPath"/>, or null. Null means the PATH of this process (see
    /// <see cref="CliStartLauncher.FindShim(string, string?)"/>).
    /// </summary>
    public static string? FindShim(string? searchPath = null) => CliStartLauncher.FindShim(ShimName, searchPath);

    /// <summary>Deletes the old start files of <paramref name="startsFolder"/> and returns how many were removed (see <see cref="CliStartLauncher.RemoveOldStarts"/>).</summary>
    public static int RemoveOldStarts(string startsFolder, TimeSpan olderThan, DateTimeOffset now) =>
        CliStartLauncher.RemoveOldStarts(startsFolder, olderThan, now);
}
