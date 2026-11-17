using OpenTail.Bullshark.Core.Storage;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Storage;

/// <summary>
/// The storage report and the transcript archive (gems item 6). Every file lives in a temp folder; the archive moves files there and
/// nothing is deleted. A real transcript under the owner's Claude folder is never touched.
/// </summary>
public sealed class StorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-storage-" + Guid.NewGuid().ToString("N")[..8]);
    private string Projects => Path.Combine(_root, "projects");
    private string Archive => Path.Combine(_root, "archive");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Write(string relative, int bytes, DateTime? written = null)
    {
        var path = Path.Combine(Projects, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, new string('x', bytes));
        // A file written now is "recent" (round 5 item 4), so a file without a date is written on an earlier day.
        File.SetLastWriteTimeUtc(path, DateTime.SpecifyKind(written ?? Day(1), DateTimeKind.Utc));
        return path;
    }

    private static DateTime Day(int day) => new(2026, 11, day, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void TheReportListsEachProjectLargestFirstWithItsCountSizeAndTimes()
    {
        Write(@"projA\s1.jsonl", 100, Day(1));
        Write(@"projA\s2.jsonl", 50, Day(10));
        Write(@"projA\s1\subagents\agent.jsonl", 30, Day(2));
        Write(@"projA\notes.txt", 999);            // not a transcript
        Write(@"projB\s3.jsonl", 200, Day(5));

        var report = StorageReport.Read(Projects);

        Assert.Equal(["projB", "projA"], report.Select(p => p.Name));
        Assert.Equal(200, report[0].Bytes);
        Assert.Equal(1, report[0].Transcripts);
        Assert.Equal(180, report[1].Bytes);
        Assert.Equal(3, report[1].Transcripts);
        Assert.Equal(new DateTimeOffset(Day(1)), report[1].Oldest);
        Assert.Equal(new DateTimeOffset(Day(10)), report[1].Newest);
    }

    [Fact]
    public void AMissingProjectsFolderGivesAnEmptyReport()
    {
        Assert.Empty(StorageReport.Read(Path.Combine(_root, "nowhere")));
    }

    [Fact]
    public void FilesOfAFolderAreItsSessionTranscriptsNewestFirstWithoutSubagents()
    {
        Write(@"projA\s1.jsonl", 100, Day(1));
        Write(@"projA\s2.jsonl", 50, Day(10));
        Write(@"projA\s1\subagents\agent.jsonl", 30, Day(20));

        var files = StorageReport.FilesOf(Path.Combine(Projects, "projA"));

        Assert.Equal(["s2", "s1"], files.Select(f => f.SessionId));
    }

    [Fact]
    public void AnArchiveMovesOnlyTheUnprotectedSessionsAndKeepsTheirFolder()
    {
        var s1 = Write(@"projA\s1.jsonl", 10);
        var s2 = Write(@"projA\s2.jsonl", 10);
        var s3 = Write(@"projA\s3.jsonl", 10);

        var outcomes = TranscriptArchive.Archive([s1, s2, s3], Projects, Archive, new HashSet<string> { "s2" });

        Assert.Equal([true, false, true], outcomes.Select(o => o.Moved));
        Assert.False(File.Exists(s1));
        Assert.True(File.Exists(s2));   // listed by claude: it stays
        Assert.False(File.Exists(s3));
        Assert.True(File.Exists(Path.Combine(Archive, "projA", "s1.jsonl")));
        Assert.Contains("listed by claude", outcomes[1].Message);
    }

    [Fact]
    public void AFileOutsideTheProjectsFolderIsRefused()
    {
        Directory.CreateDirectory(_root);
        var outside = Path.Combine(_root, "elsewhere.jsonl");
        File.WriteAllText(outside, "x");

        var outcome = Assert.Single(TranscriptArchive.Archive([outside], Projects, Archive, new HashSet<string>()));

        Assert.False(outcome.Moved);
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void AnArchiveNeverOverwritesAFileOfTheSameName()
    {
        var source = Write(@"projA\s4.jsonl", 10);
        Directory.CreateDirectory(Path.Combine(Archive, "projA"));
        File.WriteAllText(Path.Combine(Archive, "projA", "s4.jsonl"), "older");

        var outcome = Assert.Single(TranscriptArchive.Archive([source], Projects, Archive, new HashSet<string>()));

        Assert.False(outcome.Moved);
        Assert.True(File.Exists(source));
        Assert.Equal("older", File.ReadAllText(Path.Combine(Archive, "projA", "s4.jsonl")));
    }

    [Fact]
    public void RestorePutsTheFileBackAtTheSamePath()
    {
        var original = Write(@"projA\s1.jsonl", 42);
        TranscriptArchive.Archive([original], Projects, Archive, new HashSet<string>());
        var archived = Path.Combine(Archive, "projA", "s1.jsonl");

        var outcome = Assert.Single(TranscriptArchive.Restore([archived], Projects, Archive));

        Assert.True(outcome.Moved);
        Assert.True(File.Exists(original));
        Assert.Equal(42, new FileInfo(original).Length);
        Assert.False(File.Exists(archived));
    }

    [Fact]
    public void RestoreRefusesToOverwriteAFileThatIsBackInPlace()
    {
        var original = Write(@"projA\s1.jsonl", 5);
        TranscriptArchive.Archive([original], Projects, Archive, new HashSet<string>());
        var archived = Path.Combine(Archive, "projA", "s1.jsonl");
        File.WriteAllText(original, "a newer one");

        var outcome = Assert.Single(TranscriptArchive.Restore([archived], Projects, Archive));

        Assert.False(outcome.Moved);
        Assert.Equal("a newer one", File.ReadAllText(original));
        Assert.True(File.Exists(archived));
    }

    [Fact]
    public void TheArchiveListsWhatWasArchived()
    {
        var s1 = Write(@"projA\s1.jsonl", 10, Day(3));
        var s3 = Write(@"projB\s3.jsonl", 10, Day(7));
        TranscriptArchive.Archive([s1, s3], Projects, Archive, new HashSet<string>());

        var listed = TranscriptArchive.List(Archive);

        Assert.Equal(["s3", "s1"], listed.Select(f => f.SessionId));
    }

    [Fact]
    public void NothingIsArchivedBeforeClaudesSessionListHasBeenRead()
    {
        // The listing is unknown (null), so no file may move, not even an old one that nothing lists.
        var old = Write(@"projA\s1.jsonl", 10, Day(1));

        var outcome = Assert.Single(TranscriptArchive.Archive([old], Projects, Archive, null));

        Assert.False(outcome.Moved);
        Assert.Contains("not been read yet", outcome.Message);
        Assert.True(File.Exists(old));
    }

    [Fact]
    public void ATranscriptWrittenInTheLastDayIsNotArchivedAndAnOlderOneIs()
    {
        var now = new DateTimeOffset(Day(10), TimeSpan.Zero);
        var fresh = Write(@"projA\fresh.jsonl", 10, Day(10).AddHours(-2));    // two hours before now
        var justOld = Write(@"projA\older.jsonl", 10, Day(9).AddHours(-1));    // 25 hours before now (Day(9) is 09:00)

        var outcomes = TranscriptArchive.Archive([fresh, justOld], Projects, Archive, new HashSet<string>(), now);

        Assert.False(outcomes[0].Moved);
        Assert.Contains("last 24 hours", outcomes[0].Message);
        Assert.True(File.Exists(fresh));
        Assert.True(outcomes[1].Moved);
        Assert.False(File.Exists(justOld));
    }

    [Fact]
    public void AFileWrittenExactlyADayAgoMayBeArchived()
    {
        var now = new DateTimeOffset(Day(10), TimeSpan.Zero);
        var edge = Write(@"projA\edge.jsonl", 10, Day(9));

        var outcome = Assert.Single(TranscriptArchive.Archive([edge], Projects, Archive, new HashSet<string>(), now));

        Assert.True(outcome.Moved);
    }
}
