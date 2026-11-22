using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// Starts a new Codex session with a start text (step R3, decisions R-D6 and R-D8). The shared launcher (<see cref="CliStartLauncher"/>) writes the
/// text and builds the command line. This class names the Codex shim (<c>codex.ps1</c>), checks the model and gives the call: the shim runs with the
/// text as its last argument, after <c>-m</c> and the model when a model is set.
/// </summary>
public static class CodexStartLauncher
{
    /// <summary>The Codex PowerShell shim. The script finds it with Get-Command, so a <c>codex.exe</c> alone does not count.</summary>
    private const string ShimName = "codex.ps1";

    /// <summary>The Starts folder that goes with a state file (see <see cref="CliStartLauncher.StartsFolderBeside"/>).</summary>
    public static string StartsFolderBeside(string statePath) => CliStartLauncher.StartsFolderBeside(statePath);

    /// <summary>Writes the start text to a new file in <paramref name="startsFolder"/> and returns its full path (see <see cref="CliStartLauncher.WriteStart"/>).</summary>
    public static string WriteStart(string startsFolder, string text, DateTimeOffset now) => CliStartLauncher.WriteStart(startsFolder, text, now);

    /// <summary>
    /// The command line of a terminal tab that starts Codex with the text in <paramref name="promptPath"/>. With <paramref name="model"/> (P9, decision
    /// M-D5) the shim runs as <c>-m '&lt;model&gt;' $prompt</c>; the model must pass <see cref="CodexModelList.IsValidModelId"/>, or
    /// <see cref="ArgumentException"/> is thrown before any script is built. Null keeps Codex's own default. The paths are checked as in
    /// <see cref="CliStartLauncher.CommandLine"/>.
    /// </summary>
    public static string CommandLine(string folder, string promptPath, string? model = null)
    {
        if (model is not null && !CodexModelList.IsValidModelId(model))
            throw new ArgumentException("The model id has a character outside lower-case letters, digits, dots and hyphens.", nameof(model));

        var call = model is null ? "& $shim.Source $prompt" : $"& $shim.Source -m '{model}' $prompt";
        return CliStartLauncher.CommandLine("Codex", ShimName, folder, promptPath, call);
    }

    /// <summary>The full path of <c>pwsh.exe</c> on the PATH of this process, or null (see <see cref="CliStartLauncher.FindPwsh()"/>).</summary>
    public static string? FindPwsh() => CliStartLauncher.FindPwsh();

    /// <summary>The full path of <c>pwsh.exe</c> in the folders of <paramref name="searchPath"/>, or null (see <see cref="CliStartLauncher.FindPwsh(string?)"/>).</summary>
    public static string? FindPwsh(string? searchPath) => CliStartLauncher.FindPwsh(searchPath);

    /// <summary>
    /// The full path of <c>codex.ps1</c> in the folders of <paramref name="searchPath"/>, or null. Null means the PATH of this process (see
    /// <see cref="CliStartLauncher.FindShim(string, string?)"/>).
    /// </summary>
    public static string? FindShim(string? searchPath = null) => CliStartLauncher.FindShim(ShimName, searchPath);

    /// <summary>Deletes the old start files of <paramref name="startsFolder"/> and returns how many were removed (see <see cref="CliStartLauncher.RemoveOldStarts"/>).</summary>
    public static int RemoveOldStarts(string startsFolder, TimeSpan olderThan, DateTimeOffset now) =>
        CliStartLauncher.RemoveOldStarts(startsFolder, olderThan, now);
}
