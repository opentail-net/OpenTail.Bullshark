using System.Text;

namespace OpenTail.Bullshark.Core.Instructions;

/// <summary>
/// Claude Code's configuration folder: CLAUDE_CONFIG_DIR when it is set, otherwise ~/.claude. The user's CLAUDE.md is in this folder.
/// </summary>
public static class ClaudeConfig
{
    public static string Dir
    {
        get
        {
            var set = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            return string.IsNullOrWhiteSpace(set)
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude")
                : set.Trim();
        }
    }
}

/// <summary>What a file looked like when it was read: its length and last write time. A save compares it with the file before writing.</summary>
public sealed record FileStamp(long Length, long WrittenTicks)
{
    /// <summary>The stamp of a file that exists, or null when it does not.</summary>
    public static FileStamp? Of(string path)
    {
        if (!File.Exists(path)) return null;
        var info = new FileInfo(path);
        return new FileStamp(info.Length, info.LastWriteTimeUtc.Ticks);
    }
}

public enum SaveOutcome
{
    /// <summary>Written. The previous version, when there was one, is kept beside it as a .bak file.</summary>
    Saved,

    /// <summary>The file changed on disk since it was read, so nothing was written.</summary>
    Changed,

    /// <summary>The write failed. Nothing was replaced, and any temporary file was removed.</summary>
    Failed,
}

public sealed record SaveResult(SaveOutcome Outcome, FileStamp? Stamp, string Message);

/// <summary>
/// The CLAUDE.md files the instructions editor reads and writes. A save writes a temporary file and then
/// replaces the original, so a crash mid-save cannot leave an empty file, and it keeps the previous version as .bak. A save never
/// overwrites a change made on disk after the file was read: it reports Changed and leaves the file alone.
/// </summary>
public static class InstructionFiles
{
    public static string UserPath => Path.Combine(ClaudeConfig.Dir, "CLAUDE.md");

    /// <summary>
    /// A project's instruction file: ./CLAUDE.md or ./.claude/CLAUDE.md, whichever exists. When neither exists, a new file goes at
    /// ./CLAUDE.md.
    /// </summary>
    public static string ProjectPath(string folder)
    {
        var first = Path.Combine(folder, "CLAUDE.md");
        if (File.Exists(first)) return first;
        var second = Path.Combine(folder, ".claude", "CLAUDE.md");
        return File.Exists(second) ? second : first;
    }

    /// <summary>The text of a file, or empty when it does not exist.</summary>
    public static string Read(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;

    /// <summary>
    /// Writes the text, unless the file's stamp differs from <paramref name="readStamp"/> (the stamp taken when it was read; null when
    /// the file did not exist then).
    /// </summary>
    public static SaveResult Save(string path, string text, FileStamp? readStamp)
    {
        var now = FileStamp.Of(path);
        if (!Equals(now, readStamp))
            return new SaveResult(SaveOutcome.Changed, now, "The file changed on disk after it was opened.");

        var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temp, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            if (now is null)
            {
                File.Move(temp, path);
                return new SaveResult(SaveOutcome.Saved, FileStamp.Of(path), "Created.");
            }
            File.Replace(temp, path, path + ".bak", ignoreMetadataErrors: true);
            return new SaveResult(SaveOutcome.Saved, FileStamp.Of(path), "The previous version is kept as " + Path.GetFileName(path) + ".bak.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            return new SaveResult(SaveOutcome.Failed, FileStamp.Of(path), ex.Message);
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            if (File.Exists(file)) File.Delete(file);
        }
        catch (IOException)
        {
            // The temporary file is left for the owner to remove; the original was not touched.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
