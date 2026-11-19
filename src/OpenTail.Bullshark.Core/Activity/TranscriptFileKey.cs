namespace OpenTail.Bullshark.Core.Activity;

/// <summary>
/// The identity of a transcript as it is on disk: its path, its length and its last write time (UTC). Two keys that are equal name the same bytes, as far
/// as the file system says, so a value read for a key (the line starts, the find hits, the turn facts) is kept while the key is the same and read again
/// once it is not (T5 batch).
/// </summary>
public sealed record TranscriptFileKey(string Path, long Length, DateTime WriteTimeUtc)
{
    /// <summary>The key of a file now. Throws the file system's exceptions when the file cannot be seen (a caller handles them per file).</summary>
    public static TranscriptFileKey Of(string path)
    {
        var info = new FileInfo(path);
        return new TranscriptFileKey(path, info.Length, info.LastWriteTimeUtc);
    }
}
