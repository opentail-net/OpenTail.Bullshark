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

    /// <summary>
    /// The lines of a transcript as their UTF-8 bytes, split where <see cref="StreamReader.ReadLine"/> splits them (a line feed, a carriage
    /// return, or both together) and without a leading byte order mark. Each slice is valid until the next line is asked for, because the
    /// bytes are read into one buffer that is reused (round 12 item A6). Used by the index, which parses each line as UTF-8.
    /// </summary>
    internal static IEnumerable<ReadOnlyMemory<byte>> ReadLineBytes(string path)
    {
        using var stream = Open(path);
        var chunk = new byte[256 * 1024];
        var line = new byte[16 * 1024];
        var length = 0;
        var first = true;
        var skipLineFeed = false;
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            var position = 0;
            // A carriage return ended the last line of the previous chunk: a line feed that opens this one is part of that break.
            if (skipLineFeed)
            {
                skipLineFeed = false;
                if (chunk[0] == (byte)'\n') position = 1;
            }
            while (position < read)
            {
                var at = FindBreak(chunk, position, read);
                if (at < 0)
                {
                    Append(ref line, ref length, chunk, position, read - position);
                    break;
                }
                Append(ref line, ref length, chunk, position, at - position);
                var terminator = chunk[at];
                yield return Slice(line, length, ref first);
                length = 0;
                position = at + 1;
                if (terminator == (byte)'\r')
                {
                    if (position < read)
                    {
                        if (chunk[position] == (byte)'\n') position++;
                    }
                    else
                    {
                        skipLineFeed = true;
                    }
                }
            }
        }
        if (length > 0) yield return Slice(line, length, ref first);
    }

    /// <summary>The index of the next line feed or carriage return in [from, to), or -1.</summary>
    private static int FindBreak(byte[] chunk, int from, int to)
    {
        var at = chunk.AsSpan(from, to - from).IndexOfAny((byte)'\n', (byte)'\r');
        return at < 0 ? -1 : from + at;
    }

    /// <summary>Appends bytes to the line being read, growing its buffer when needed.</summary>
    private static void Append(ref byte[] line, ref int length, byte[] source, int from, int count)
    {
        if (length + count > line.Length)
        {
            var bigger = new byte[Math.Max(line.Length * 2, length + count)];
            Buffer.BlockCopy(line, 0, bigger, 0, length);
            line = bigger;
        }
        Buffer.BlockCopy(source, from, line, length, count);
        length += count;
    }

    /// <summary>The bytes of one line, without the byte order mark that opens the file (a stream reader drops it too).</summary>
    private static ReadOnlyMemory<byte> Slice(byte[] line, int length, ref bool first)
    {
        var start = first && length >= 3 && line[0] == 0xEF && line[1] == 0xBB && line[2] == 0xBF ? 3 : 0;
        first = false;
        return new ReadOnlyMemory<byte>(line, start, length - start);
    }
}
