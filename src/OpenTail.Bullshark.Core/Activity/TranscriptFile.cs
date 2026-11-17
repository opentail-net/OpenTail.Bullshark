using System.Text;

namespace OpenTail.Bullshark.Core.Activity;

/// <summary>
/// Opens a transcript for reading while Claude may still be writing it, or may remove it. Every reader of a transcript goes
/// through here, so the sharing rule is the same everywhere: the file is read with the writer's sharing (read, write and delete),
/// and a file that cannot be opened at all throws, which the callers handle per file.
/// </summary>
public static class TranscriptFile
{
    /// <summary>Opens a transcript for reading, sharing read, write and delete with whoever holds it.</summary>
    public static FileStream Open(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>
    /// The lines of a transcript, read lazily as UTF-8 (a byte order mark is detected, as <see cref="File.ReadLines(string)"/> does).
    /// The file is open while the lines are read, and is closed when the sequence ends or is disposed.
    /// </summary>
    public static IEnumerable<string> ReadLines(string path)
    {
        using var stream = Open(path);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) is not null) yield return line;
    }
}
