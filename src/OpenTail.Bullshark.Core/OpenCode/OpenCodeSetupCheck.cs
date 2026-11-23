using OpenTail.Bullshark.Core.Health;

namespace OpenTail.Bullshark.Core.OpenCode;

/// <summary>
/// The opencode row of the setup check (OC-D1, OC-D17). The caller finds the shim on PATH (or has none, <c>null</c>) and passes a file reader; the rows
/// are judged from those. No process is started here, and no PATH is searched here: <see cref="FindShimOnPath"/> is the one search of PATH, and it reads
/// folder and file names only. The version is read from the npm package file beside the shim. The row
/// carries the path in its detail (not in a log). A file the reader cannot read is treated as not read.
/// </summary>
public static class OpenCodeSetupCheck
{
    private const string Area = "opencode CLI";

    /// <summary>The row's name, as the program is named.</summary>
    private const string Name = "opencode";

    /// <summary>The Problem when no opencode program is on PATH.</summary>
    private const string NotOnPath = "opencode is not on PATH: install the opencode CLI (npm), or add it to PATH, then press Check again";

    /// <summary>The Warning when the package file cannot be read (or has no version): the shim is not the npm install's.</summary>
    private const string NotNpm = "version not read: this is not the npm install";

    /// <summary>The longest detail of a row, as the Copilot rows allow (a path can be long).</summary>
    private const int MaxDetail = 300;

    /// <summary>
    /// The program names tried on each PATH folder, in order. This is a private copy of <c>CopilotSetupCheck.ProgramExtensions</c> (that one is private to
    /// the Copilot check): .exe, .cmd and .ps1. PATHEXT is not used, so opencode.ps1 is found on any machine.
    /// </summary>
    private static readonly string[] ProgramExtensions = [".exe", ".cmd", ".ps1"];

    /// <summary>
    /// The full path of the opencode program in the folders of <paramref name="pathVariable"/> (a PATH value), or null. Null means the PATH of this process.
    /// Only folder and file names are read: no program is started.
    /// </summary>
    public static string? FindShimOnPath(string? pathVariable = null) =>
        SetupCheck.FindOnPath(Name, pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty, ProgramExtensions);

    /// <summary>
    /// The row for the shim the caller found. <paramref name="shimPath"/> is null (or blank) when no opencode program is on PATH. <paramref name="readFile"/>
    /// reads a text file and returns null when it cannot be read (the app passes a reader over <c>File.ReadAllText</c>).
    /// </summary>
    public static IReadOnlyList<SetupItem> ForFound(string? shimPath, Func<string, string?> readFile)
    {
        if (string.IsNullOrWhiteSpace(shimPath))
            return [new SetupItem(Area, Name, SetupState.Problem, NotOnPath)];

        var version = ReadVersion(shimPath, readFile);
        if (version is null)
            return [new SetupItem(Area, Name, SetupState.Warning, Detail(NotNpm, shimPath))];

        if (version.StartsWith(OpenCodeVersion.Line + ".", StringComparison.Ordinal))
        {
            return [new SetupItem(Area, Name, SetupState.Ok,
                Detail($"opencode version {version} (read from the package file, no program was started)", shimPath))];
        }

        return [new SetupItem(Area, Name, SetupState.Warning,
            Detail($"opencode version {version} was not the version the app was read against (1.4.x); its data may differ", shimPath))];
    }

    /// <summary>The version in the package file beside the shim, or null when the file is not read or has no version.</summary>
    private static string? ReadVersion(string shimPath, Func<string, string?> readFile)
    {
        string? json;
        try
        {
            json = readFile(OpenCodeVersion.PackagePathBeside(shimPath));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return json is null ? null : OpenCodeVersion.FromPackageJson(json);
    }

    /// <summary>The row's detail: the status, then the path it is about, clipped at <see cref="MaxDetail"/>.</summary>
    private static string Detail(string status, string path) => SetupCheck.Clip($"{status}; found: {path}", MaxDetail);
}
