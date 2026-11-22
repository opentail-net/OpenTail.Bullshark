using System.Text;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>
/// The scratch folders of the Copilot summary call (G-D15). Each call gets a new folder under <c>CopilotScratch</c>, beside the state file's
/// <c>Summaries</c> and <c>Starts</c>. The folder gets a marker file first, then three subfolders: <c>work</c>, the call's working folder (the transcript
/// and the instructions go there and nothing else); <c>config</c> and <c>log</c>, the Copilot config and log folders, which sit beside <c>work</c>, not
/// inside it. Only a marked folder is ever deleted. The folder is deleted when the call ends, after its process tree has ended (with a few short retries
/// for a file lock). A startup run removes marked folders older than <see cref="StartupAge"/>.
/// </summary>
public static class CopilotScratchFolder
{
    /// <summary>The folder under the state folder that holds the scratch folders.</summary>
    public const string ParentName = "CopilotScratch";

    /// <summary>The marker file each scratch folder holds from its creation. A folder without it is never deleted here.</summary>
    public const string MarkerName = "opentail-scratch.marker";

    /// <summary>The call's working folder under the scratch folder. It holds the transcript and the instructions, and the model's <c>view</c> tool reads it.</summary>
    public const string WorkFolderName = "work";

    /// <summary>The Copilot config folder (<c>--config-dir</c>) under the scratch folder. It is not inside the working folder.</summary>
    public const string ConfigFolderName = "config";

    /// <summary>The Copilot log folder (<c>--log-dir</c>) under the scratch folder. It is not inside the working folder.</summary>
    public const string LogFolderName = "log";

    /// <summary>
    /// How old a marked folder must be before a startup run removes it. Zero, because OpenTail is single-instance: no summary call from another instance
    /// can be using a scratch folder when the app starts, so a folder left by a failed delete is removed at the next start, not a day later.
    /// </summary>
    public static readonly TimeSpan StartupAge = TimeSpan.Zero;

    /// <summary>The attempts made to delete a folder that a file lock holds, and the pause before each retry.</summary>
    private const int DeleteAttempts = 5;

    private static readonly TimeSpan RetryPause = TimeSpan.FromMilliseconds(200);

    /// <summary>The scratch parent for a state file: <c>CopilotScratch</c> in the state file's folder.</summary>
    public static string ParentBeside(string statePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(statePath))!, ParentName);

    /// <summary>
    /// Creates a new scratch folder under <paramref name="parent"/>. It writes the marker file first, then creates the <c>work</c>, <c>config</c> and
    /// <c>log</c> subfolders. Returns the folder's full path. An IO failure is thrown to the caller.
    /// </summary>
    public static string Create(string parent)
    {
        Directory.CreateDirectory(parent);
        var folder = Path.GetFullPath(Path.Combine(parent, Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        File.WriteAllText(
            Path.Combine(folder, MarkerName),
            "OpenTail scratch folder for a Copilot summary. It is deleted when the call ends.",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        foreach (var name in new[] { WorkFolderName, ConfigFolderName, LogFolderName })
        {
            Directory.CreateDirectory(Path.Combine(folder, name));
        }
        return folder;
    }

    /// <summary>True when the folder exists and holds the marker file.</summary>
    public static bool IsScratch(string folder) =>
        Directory.Exists(folder) && File.Exists(Path.Combine(folder, MarkerName));

    /// <summary>
    /// Deletes a marked scratch folder and everything in it. A folder without the marker is left alone and the result is false. A file lock is
    /// retried a few times with a short pause; after that the folder is left for the startup run, and the result is false. Nothing is logged.
    /// </summary>
    public static bool Delete(string folder)
    {
        if (!IsScratch(folder)) return false;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= DeleteAttempts) return false;
                Thread.Sleep(RetryPause);
            }
        }
    }

    /// <summary>
    /// The same rules as <see cref="Delete"/>, with the pause before a retry awaited, so the caller's thread is not blocked. It takes no cancellation
    /// token: the cleanup must run even after the call was cancelled.
    /// </summary>
    public static async Task<bool> DeleteAsync(string folder)
    {
        if (!IsScratch(folder)) return false;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Delete(folder, recursive: true);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= DeleteAttempts) return false;
                await Task.Delay(RetryPause);
            }
        }
    }

    /// <summary>
    /// Deletes the marked folders under <paramref name="parent"/> whose marker was written before <paramref name="now"/> minus
    /// <paramref name="olderThan"/>. Folders without the marker are not touched. Returns the number removed; a missing parent gives zero.
    /// </summary>
    public static int RemoveOld(string parent, TimeSpan olderThan, DateTimeOffset now)
    {
        if (!Directory.Exists(parent)) return 0;
        var cutoff = now - olderThan;
        var removed = 0;
        foreach (var folder in new DirectoryInfo(parent).EnumerateDirectories())
        {
            var marker = Path.Combine(folder.FullName, MarkerName);
            if (!File.Exists(marker)) continue;
            if (new DateTimeOffset(File.GetLastWriteTimeUtc(marker), TimeSpan.Zero) >= cutoff) continue;
            if (Delete(folder.FullName)) removed++;
        }
        return removed;
    }
}
