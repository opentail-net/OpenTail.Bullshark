using OpenTail.Bullshark.Core.Health;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// The Codex CLI row of the setup check (stage C5, C-D7). It has one item: whether a <c>codex</c> program (codex.exe, codex.cmd or codex.ps1)
/// is on PATH, with the same wording rules as the Claude check. Only the PATH is searched. The program is not run, so the version is not read
/// here: a version call is out of scope for stage C, and it would start codex on every refresh.
/// </summary>
public static class CodexSetupCheck
{
    private const string Area = "Codex CLI";

    /// <summary>The program names tried on each PATH folder, in order. PATHEXT is not used, so codex.ps1 is found on any machine.</summary>
    private static readonly string[] ProgramExtensions = [".exe", ".cmd", ".ps1"];

    /// <summary>
    /// The one row. <paramref name="pathVariable"/> is the PATH that is searched; the process's own PATH is used when it is null.
    /// </summary>
    public static IReadOnlyList<SetupItem> Run(string? pathVariable = null)
    {
        var path = pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var found = SetupCheck.FindOnPath("codex", path, ProgramExtensions);
        return
        [
            found is null
                ? new SetupItem(Area, "codex", SetupState.Problem, "not found on PATH: install Codex CLI, or add it to PATH, then press Check again")
                : new SetupItem(Area, "codex", SetupState.Ok, SetupCheck.Clip($"found: {found}")),
        ];
    }
}
