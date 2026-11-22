using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// The scoped refresh (R9) brings an index up to date from the memo of an earlier full read. The result must be the index a full read gives for
/// the same folder state, after each kind of change, except for the one accepted blind spot (a same-length copy with an older write time under an
/// unchanged count and newest write time), which these tests pin down, and which a full build sees.
/// </summary>
public sealed class TranscriptIndexScopedTests : IDisposable
{
    private const string FirstStamp = "2026-11-01T10:00:00.000Z";
    private const string SecondStamp = "2026-11-01T11:00:00.000Z";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-scoped-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _state = Path.Combine(Path.GetTempPath(), "bullshark-scoped-state-" + Guid.NewGuid().ToString("N")[..8]);

    private string Cache => Path.Combine(_state, "cache", "transcript-index.json");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        try { Directory.Delete(_state, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static readonly DateTime AnHourAgo = DateTime.UtcNow.AddHours(-1);

    /// <summary>A transcript with one prompt and one answer, stamped at <paramref name="stamp"/>, last written at <paramref name="written"/>.</summary>
    private string Transcript(string folder, string id, string prompt, string stamp, DateTime written)
    {
        var dir = Path.Combine(_root, folder);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, id + ".jsonl");
        File.WriteAllLines(path, new[]
        {
            "{\"type\":\"user\",\"timestamp\":\"" + stamp + "\",\"message\":{\"role\":\"user\",\"content\":\"" + prompt + "\"}}",
            IndexFixture.Assistant("m-" + id, "r-" + id, 12, stamp, "claude-opus-5-5"),
        });
        File.SetLastWriteTimeUtc(path, written);
        return path;
    }

    /// <summary>The index a full read gives for the root now: every transcript read, with no memo and no cache.</summary>
    private string Reader() => IndexFixture.Describe(TranscriptIndex.Build(_root));

    private static string Describe(TranscriptIndex index) => IndexFixture.Describe(index);

    [Fact]
    public void AScopedRefreshGivesWhatAFullReadGivesAfterEachChange()
    {
        var alpha1 = Transcript("C--Git-alpha", "aaaaaaaa-0000-0000-0000-000000000001", "first prompt", FirstStamp, AnHourAgo);
        Transcript("C--Git-alpha", "aaaaaaaa-0000-0000-0000-000000000002", "second prompt", SecondStamp, AnHourAgo);
        var beta1 = Transcript("C--Git-beta", "bbbbbbbb-0000-0000-0000-000000000003", "third prompt", FirstStamp, AnHourAgo);

        var full = TranscriptIndex.BuildFull(_root, Cache);
        var memo = full.Memo;
        Assert.Equal(Reader(), Describe(full.Index));
        Assert.Equal(2, full.FoldersRead);
        Assert.Equal(0, full.FoldersReused);

        // Nothing has changed: both folders are reused, and no transcript is read again.
        var same = TranscriptIndex.RefreshScoped(_root, memo);
        Assert.Equal(Reader(), Describe(same.Index));
        Assert.Empty(same.ReadFiles);
        Assert.Equal(2, same.FoldersReused);
        Assert.Equal(0, same.FoldersRead);

        // A line is appended to one transcript, and its write time is set back to an hour ago: only the total length shows the change.
        File.AppendAllLines(alpha1, new[] { IndexFixture.Assistant("m-extra", "r-extra", 30, SecondStamp, "claude-opus-5-5") });
        File.SetLastWriteTimeUtc(alpha1, AnHourAgo);
        var appended = TranscriptIndex.RefreshScoped(_root, memo);
        Assert.Equal(Reader(), Describe(appended.Index));
        Assert.Equal(2, appended.ReadFiles.Count);
        Assert.Contains(new FileInfo(alpha1).FullName, appended.ReadFiles);
        Assert.Equal(1, appended.FoldersRead);
        Assert.Equal(1, appended.FoldersReused);
        memo = appended.Memo;

        // The only transcript of beta is written 30 minutes ago instead of an hour: the count and the total length are the same, and the newest
        // write time moves, so the folder is read again.
        File.SetLastWriteTimeUtc(beta1, DateTime.UtcNow.AddMinutes(-30));
        var newer = TranscriptIndex.RefreshScoped(_root, memo);
        Assert.Equal(Reader(), Describe(newer.Index));
        Assert.Single(newer.ReadFiles);
        Assert.Equal(1, newer.FoldersRead);
        Assert.Equal(1, newer.FoldersReused);
        memo = newer.Memo;

        // A transcript is written now, so it is live. Its folder's count changes, and the folder is read.
        var live = Transcript("C--Git-beta", "bbbbbbbb-0000-0000-0000-000000000004", "fourth prompt", SecondStamp, DateTime.UtcNow);
        var added = TranscriptIndex.RefreshScoped(_root, memo);
        Assert.Equal(Reader(), Describe(added.Index));
        Assert.Contains(new FileInfo(live).FullName, added.ReadFiles);
        memo = added.Memo;

        // A transcript of alpha is deleted: alpha's count changes. beta's live transcript is read again as it always is, so both folders are read.
        File.Delete(alpha1);
        var deleted = TranscriptIndex.RefreshScoped(_root, memo);
        Assert.Equal(Reader(), Describe(deleted.Index));
        Assert.Equal(2, deleted.FoldersRead);
        Assert.Equal(0, deleted.FoldersReused);

        Assert.Equal(Reader(), Describe(TranscriptIndex.BuildFull(_root, Cache).Index));
    }

    [Fact]
    public void ALiveTranscriptIsReadAgainEvenWhenItsFolderIsUnchanged()
    {
        var live = Transcript("C--Git-gamma", "cccccccc-0000-0000-0000-000000000005", "live prompt", FirstStamp, DateTime.UtcNow);
        Transcript("C--Git-delta", "dddddddd-0000-0000-0000-000000000006", "old prompt", FirstStamp, AnHourAgo);
        var memo = TranscriptIndex.BuildFull(_root, Cache).Memo;

        // Nothing changed in either folder, but the live transcript is written within the window, so it is read again. The other folder is reused.
        var refresh = TranscriptIndex.RefreshScoped(_root, memo);

        Assert.Single(refresh.ReadFiles);
        Assert.Contains(new FileInfo(live).FullName, refresh.ReadFiles);
        Assert.Equal(1, refresh.FoldersRead);
        Assert.Equal(1, refresh.FoldersReused);
        Assert.Equal(Reader(), Describe(refresh.Index));
    }

    [Fact]
    public void ATranscriptThatCouldNotBeReadIsReadAgainWhenItCanBe()
    {
        var path = Transcript("C--Git-epsilon", "eeeeeeee-0000-0000-0000-000000000007", "locked prompt", FirstStamp, AnHourAgo);
        TranscriptRefresh failed;
        // Held with no sharing: the read fails, and the transcript is listed as skipped.
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            failed = TranscriptIndex.BuildFull(_root, Cache);
        }
        Assert.Single(failed.Index.Skipped);

        // Released: the folder's stamp is unchanged, but the failed read is not kept, so the transcript is read and is in the index.
        var refresh = TranscriptIndex.RefreshScoped(_root, failed.Memo);

        Assert.Empty(refresh.Index.Skipped);
        Assert.Single(refresh.ReadFiles);
        Assert.Equal(Reader(), Describe(refresh.Index));
    }

    [Fact]
    public void ASameLengthOlderCopyIsKeptByAScopedRefreshUntilTheNextFullBuild()
    {
        // The blind spot (R9, accepted by the owner). The folder holds an older transcript B and a newer transcript A. B's text is replaced by a
        // copy of the same length, written earlier than B was: the count, the newest write time and the total length are all unchanged.
        var older = Transcript("C--Git-zeta", "ffffffff-0000-0000-0000-000000000008", "hello from file 0", FirstStamp, AnHourAgo.AddHours(-1));
        Transcript("C--Git-zeta", "ffffffff-0000-0000-0000-000000000009", "newest prompt", SecondStamp, AnHourAgo);
        var memo = TranscriptIndex.BuildFull(_root, Cache).Memo;
        var before = Reader();

        File.WriteAllText(older, File.ReadAllText(older).Replace("hello from file 0", "hello from file 9"));
        File.SetLastWriteTimeUtc(older, AnHourAgo.AddHours(-2));
        Assert.NotEqual(before, Reader());

        // The scoped refresh keeps the folder's reads, so it still shows the old text. This is the accepted blind spot.
        var scoped = TranscriptIndex.RefreshScoped(_root, memo);
        Assert.Equal(before, Describe(scoped.Index));
        Assert.Empty(scoped.ReadFiles);
        Assert.Equal(1, scoped.FoldersReused);

        // The full build compares the write time with the cache, sees the change, and gives what a read gives.
        var full = TranscriptIndex.BuildFull(_root, Cache);
        Assert.Equal(Reader(), Describe(full.Index));
        Assert.NotEqual(before, Describe(full.Index));
    }

    [Fact]
    public void AMissingFolderGivesAnEmptyIndexAndAnEmptyMemo()
    {
        var missing = TranscriptIndex.BuildFull(_root, Cache);
        Assert.Empty(missing.Index.Sessions);
        Assert.Equal(0, missing.Memo.FolderCount);

        var refresh = TranscriptIndex.RefreshScoped(_root, missing.Memo);
        Assert.Empty(refresh.Index.Sessions);
        Assert.Empty(refresh.ReadFiles);
        Assert.Equal(0, refresh.FoldersRead);
        Assert.Equal(0, refresh.FoldersReused);
    }
}
