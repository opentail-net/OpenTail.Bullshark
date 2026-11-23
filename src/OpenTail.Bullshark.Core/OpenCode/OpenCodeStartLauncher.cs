using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.OpenCode;

/// <summary>
/// Starts a new opencode session with a start text (OC-D9). The shared launcher (<see cref="CliStartLauncher"/>) writes the text and builds the command
/// line. This class names the opencode shim (<c>opencode.ps1</c>) and gives the final call: <c>&amp; $shim.Source '&lt;folder&gt;' --prompt $prompt</c>. The
/// text is read from the start file by the script and is never on a command line. Nothing is started here.
/// </summary>
public static class OpenCodeStartLauncher
{
    /// <summary>The opencode PowerShell shim. The script finds it with Get-Command, so an <c>opencode.exe</c> alone does not count.</summary>
    public const string ShimName = "opencode.ps1";

    /// <summary>The name the start script gives the CLI in its messages.</summary>
    private const string DisplayName = "opencode";

    /// <summary>
    /// The Starts folder of opencode: a folder named <c>opencode</c> inside the Starts folder that goes with a state file
    /// (<see cref="CliStartLauncher.StartsFolderBeside"/>). It is kept apart from the Copilot start files, so each provider's clean-up sees only its own.
    /// </summary>
    public static string StartsFolderBeside(string statePath) =>
        Path.Combine(CliStartLauncher.StartsFolderBeside(statePath), "opencode");

    /// <summary>Writes the start text to a new file in <paramref name="startsFolder"/> and returns its full path (see <see cref="CliStartLauncher.WriteStart"/>).</summary>
    public static string WriteStart(string startsFolder, string text, DateTimeOffset now) => CliStartLauncher.WriteStart(startsFolder, text, now);

    /// <summary>
    /// The final call of the start script for <paramref name="folder"/>: the shim, the folder as its first argument (single-quoted, with each quote doubled
    /// by <see cref="CliStartLauncher.Quoted"/>), then <c>--prompt</c> and the text the script read into <c>$prompt</c>. A folder with a control character
    /// or a line break throws <see cref="ArgumentException"/>.
    /// </summary>
    public static string FinalCall(string folder)
    {
        CliStartLauncher.RequirePlainPath(folder, nameof(folder));
        return $"& $shim.Source '{CliStartLauncher.Quoted(folder)}' --prompt $prompt";
    }

    /// <summary>
    /// The command line of a terminal tab that starts opencode with the text in <paramref name="promptPath"/>: the shared script checks for the shim, reads
    /// the file, sets the folder, deletes the file, and runs <see cref="FinalCall"/> (see <see cref="CliStartLauncher.BuildScript"/>).
    /// </summary>
    public static string CommandLine(string folder, string promptPath) =>
        CliStartLauncher.CommandLine(DisplayName, ShimName, folder, promptPath, FinalCall(folder));

    /// <summary>The full path of <c>pwsh.exe</c> on the PATH of this process, or null (see <see cref="CliStartLauncher.FindPwsh()"/>).</summary>
    public static string? FindPwsh() => CliStartLauncher.FindPwsh();

    /// <summary>
    /// The full path of <c>opencode.ps1</c> in the folders of <paramref name="searchPath"/>, or null. Null means the PATH of this process (see
    /// <see cref="CliStartLauncher.FindShim(string, string?)"/>). The shim is only looked for, never run.
    /// </summary>
    public static string? FindShim(string? searchPath = null) => CliStartLauncher.FindShim(ShimName, searchPath);

    /// <summary>Deletes the old start files of <paramref name="startsFolder"/> and returns how many were removed (see <see cref="CliStartLauncher.RemoveOldStarts"/>).</summary>
    public static int RemoveOldStarts(string startsFolder, TimeSpan olderThan, DateTimeOffset now) =>
        CliStartLauncher.RemoveOldStarts(startsFolder, olderThan, now);
}
