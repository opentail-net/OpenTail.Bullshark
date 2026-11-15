using System.IO;

namespace OpenTail.Bullshark.Core.Storage;

/// <summary>
/// Copies the owner's data from the folder the app used before the rename (%LOCALAPPDATA%\OverClaude) into the new one
/// (%LOCALAPPDATA%\OpenTail.Bullshark). It copies only what the new folder lacks: it never overwrites and never deletes,
/// so the old folder stays as a backup. The app and the supervisor call it only when they are not given --state, so a
/// test run never copies the owner's data.
/// </summary>
public static class AppDataMigration
{
    /// <summary>The folder name before the rename. It appears only here, to find the old data.</summary>
    public const string PreviousFolderName = "OverClaude";

    /// <summary>The folder name from the rename on.</summary>
    public const string FolderName = "OpenTail.Bullshark";

    private const string StateFile = "state.json";

    public static string CurrentRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), FolderName);

    public static string PreviousRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), PreviousFolderName);

    /// <summary>Copies from the real old folder to the real new one. Call it before anything writes to the new folder.</summary>
    public static int CopyPreviousIfMissing(Action<string>? log = null) =>
        CopyPreviousIfMissing(PreviousRoot, CurrentRoot, log);

    /// <summary>
    /// Returns the number of files copied. It runs when the old folder has a state file and the new one does not. The
    /// trigger is the state file rather than the folder, because the first log write creates the new folder.
    /// </summary>
    public static int CopyPreviousIfMissing(string previousRoot, string currentRoot, Action<string>? log = null)
    {
        try
        {
            if (!File.Exists(Path.Combine(previousRoot, StateFile))) return 0;
            if (File.Exists(Path.Combine(currentRoot, StateFile))) return 0;
            var copied = CopyMissing(previousRoot, currentRoot, log);
            log?.Invoke($"data copied from {previousRoot}: {copied} file(s)");
            return copied;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"data not copied from {previousRoot}: {ex.Message}");
            return 0;
        }
    }

    private static int CopyMissing(string from, string to, Action<string>? log)
    {
        var copied = 0;
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            var target = Path.Combine(to, Path.GetFileName(file));
            if (File.Exists(target)) continue;
            try
            {
                File.Copy(file, target);
                copied++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log?.Invoke($"not copied {file}: {ex.Message}");
            }
        }
        foreach (var dir in Directory.GetDirectories(from))
            copied += CopyMissing(dir, Path.Combine(to, Path.GetFileName(dir)), log);
        return copied;
    }
}
