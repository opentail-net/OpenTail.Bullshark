namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>
/// The Copilot bundles that the launcher has downloaded (PP-D2, 2026-11-23). The WinGet <c>copilot.exe</c> is a launcher: when it runs, it runs the newest
/// completed bundle under <c>%LOCALAPPDATA%\copilot\pkg\win32-x64\&lt;version&gt;</c>. A bundle counts when its folder name is a version and its
/// <c>.extraction-complete</c> file is there. Nothing is run and nothing is written.
/// </summary>
public static class CopilotBundles
{
    /// <summary>The file a finished extraction leaves in a bundle's folder.</summary>
    private const string Marker = ".extraction-complete";

    /// <summary>The folder the launcher downloads bundles into (<c>%LOCALAPPDATA%\copilot\pkg\win32-x64</c>).</summary>
    public static string DefaultRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "copilot", "pkg", "win32-x64");

    /// <summary>The newest completed bundle's version under <paramref name="root"/>, or null when there is none or the folder cannot be read.</summary>
    public static string? NewestVersion(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return null;
        try
        {
            string? newest = null;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (!CopilotFlagsVersion.IsVersion(name)) continue;
                if (!File.Exists(Path.Combine(dir, Marker))) continue;
                if (newest is null || CopilotFlagsVersion.Compare(name, newest) > 0) newest = name;
            }
            return newest;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
