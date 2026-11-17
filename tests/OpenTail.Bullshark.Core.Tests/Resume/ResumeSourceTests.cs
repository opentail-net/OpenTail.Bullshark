using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.Core.Tests.Resume;

public sealed class ResumeSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-source-" + Guid.NewGuid().ToString("N")[..8]);

    public ResumeSourceTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void TheFolderIsTheFirstCwdInTheRecords()
    {
        var path = Path.Combine(_dir, "session.jsonl");
        File.WriteAllLines(path,
        [
            "{\"type\":\"meta\"}",
            "{\"type\":\"user\",\"cwd\":\"C:\\\\Git\\\\split\",\"message\":{\"role\":\"user\",\"content\":\"hi\"}}",
        ]);

        Assert.Equal(@"C:\Git\split", ResumeSource.WorkingDirectoryOf(path));
    }

    [Fact]
    public void NoFolderInTheRecordsGivesNull()
    {
        var path = Path.Combine(_dir, "session.jsonl");
        File.WriteAllLines(path, ["{\"type\":\"meta\"}"]);

        Assert.Null(ResumeSource.WorkingDirectoryOf(path));
    }

    [Fact]
    public void ASessionBeingWrittenIsReadForItsFolder()
    {
        // Claude holds the transcript open for writing. The folder is still read: the transcript is opened with the writer's sharing.
        var path = Path.Combine(_dir, "live.jsonl");
        File.WriteAllLines(path, ["{\"type\":\"user\",\"cwd\":\"C:\\\\Git\\\\live\"}"]);

        using var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        Assert.Equal(@"C:\Git\live", ResumeSource.WorkingDirectoryOf(path));
    }
}
