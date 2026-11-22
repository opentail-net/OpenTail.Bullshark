namespace OpenTail.Bullshark.Core.Activity;

/// <summary>
/// What a read of the transcripts kept for the next scoped refresh (R9): for each folder, its stamp and the read of each of its files, keyed by
/// full name. It is never changed after it is made: a refresh makes a new one, so the window can give it to a background read.
/// </summary>
public sealed class TranscriptIndexMemo
{
    internal TranscriptIndexMemo(IReadOnlyDictionary<string, MemoFolder> folders) => Folders = folders;

    /// <summary>The folders by key (compared without case). Each holds its stamp and its reads.</summary>
    internal IReadOnlyDictionary<string, MemoFolder> Folders { get; }

    /// <summary>The number of folders the memo holds.</summary>
    public int FolderCount => Folders.Count;

    /// <summary>A memo with no folders: a refresh from it reads every folder.</summary>
    internal static TranscriptIndexMemo Empty() => new(new Dictionary<string, MemoFolder>(StringComparer.OrdinalIgnoreCase));

    /// <summary>One folder as its read left it: the stamp taken from the listing before the reads, and the read of each file by full name.</summary>
    internal sealed record MemoFolder(FolderStamp Stamp, IReadOnlyDictionary<string, TranscriptIndex.FileRead> Reads);
}

/// <summary>
/// The result of a refresh of the index (R9): the index; the memo for the next scoped refresh; the full names of the files this refresh read
/// (every file for a full build, and the files a scoped refresh did not take from its memo); and how many folders were reused whole (no file of
/// them read) and how many had a file read.
/// </summary>
public sealed record TranscriptRefresh(
    TranscriptIndex Index,
    TranscriptIndexMemo Memo,
    IReadOnlySet<string> ReadFiles,
    int FoldersReused,
    int FoldersRead);
