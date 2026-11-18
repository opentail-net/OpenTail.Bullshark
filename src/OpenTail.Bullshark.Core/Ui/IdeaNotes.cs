using System.Globalization;
using System.Text;

namespace OpenTail.Bullshark.Core.Ui;

/// <summary>
/// Quick capture (batch B3, ideas #7): an idea the owner types is appended to ideas.md, beside the state file, as an unchecked task line for
/// the planner. Nothing is sent to a session. The file is the owner's own Markdown, so the text is kept as typed apart from its line breaks.
/// </summary>
public static class IdeaNotes
{
    public const string FileName = "ideas.md";

    public const string Header = "# Ideas\n\n";

    /// <summary>The longest idea text kept, in characters. A longer one is cut.</summary>
    public const int MaxLength = 500;

    /// <summary>How long a write waits before it is tried once more, when the file is held by another program.</summary>
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(200);

    /// <summary>The ideas.md beside a state file.</summary>
    public static string PathFor(string stateFile) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(stateFile))!, FileName);

    /// <summary>
    /// Adds one idea as the line "- [ ] yyyy-MM-dd HH:mm text" (local time). Line breaks and runs of white space become single spaces, and the
    /// text is cut to <see cref="MaxLength"/> characters. Returns false, and writes nothing, when the text is empty after that. A missing folder
    /// is created, and a new file gets the header. A write that fails because the file is held is tried once more; a second failure throws
    /// <see cref="IOException"/> for the window to report.
    /// </summary>
    public static bool Append(string path, string text, DateTimeOffset now)
    {
        var flat = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (flat.Length == 0) return false;
        if (flat.Length > MaxLength) flat = flat[..MaxLength].TrimEnd();

        var line = $"- [ ] {now.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} {flat}";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        try
        {
            AppendLine(path, line);
        }
        catch (IOException)
        {
            Thread.Sleep(RetryDelay);
            AppendLine(path, line);
        }
        return true;
    }

    /// <summary>Creates the file with its header when it is missing or empty, so the owner can open it. An existing file is not changed.</summary>
    public static void Ensure(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (!File.Exists(path) || new FileInfo(path).Length == 0) File.WriteAllText(path, Header, new UTF8Encoding(false));
    }

    /// <summary>The unchecked idea lines ("- [ ]") of the file, in order. Empty when the file is missing.</summary>
    public static IReadOnlyList<string> ReadOpen(string path)
    {
        if (!File.Exists(path)) return [];
        return File.ReadLines(path).Where(line => line.StartsWith("- [ ]", StringComparison.Ordinal)).ToList();
    }

    /// <summary>
    /// Writes the line at the end of the file, on a line of its own. The file is opened with read and write sharing, so an editor that has it
    /// open does not stop the write. The header goes first in a new or empty file.
    /// </summary>
    private static void AppendLine(string path, string line)
    {
        using var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        var prefix = string.Empty;
        if (stream.Length == 0)
        {
            prefix = Header;
        }
        else
        {
            stream.Seek(-1, SeekOrigin.End);
            if (stream.ReadByte() != '\n') prefix = "\n";
            stream.Seek(0, SeekOrigin.End);
        }

        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(prefix + line + "\n");
        stream.Write(bytes, 0, bytes.Length);
    }
}
