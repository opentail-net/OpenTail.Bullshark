using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// The Changes tab's list of written files (round 5 item 11): a transcript that is missing, one that is read, and one that cannot be read
/// is a note with the reason, and nothing is thrown to the tab.
/// </summary>
public sealed class TranscriptWritesTests : IDisposable
{
    private const string SessionId = "11111111-2222-3333-4444-555555555555";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-writes-" + Guid.NewGuid().ToString("N")[..8]);

    public TranscriptWritesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private string Transcript()
    {
        var folder = Path.Combine(_root, "project");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, SessionId + ".jsonl");
    }

    private static string WriteRecord(string path) =>
        """{"type":"assistant","timestamp":"2026-11-17T09:05:00Z","message":{"id":"m1","model":"claude-haiku-5-5","content":[{"type":"tool_use","id":"t1","name":"Write","input":{"file_path":"FILE"}}]}}""".Replace("FILE", path.Replace("\\", "\\\\"));

    [Fact]
    public void AMissingTranscriptIsANoteNotAnError()
    {
        var (files, note) = TranscriptWrites.Of(_root, SessionId);

        Assert.Null(files);
        Assert.Contains("No transcript", note);
    }

    [Fact]
    public void TheFilesASessionWroteAreListed()
    {
        var transcript = Transcript();
        var written = Path.Combine(_root, "a.cs");
        File.WriteAllText(transcript, WriteRecord(written) + "\n");

        var (files, note) = TranscriptWrites.Of(_root, SessionId);

        Assert.Null(note);
        Assert.Equal([written], files);
    }

    [Fact]
    public void ATranscriptThatCannotBeReadIsANoteWithTheReasonAndNoError()
    {
        var transcript = Transcript();
        File.WriteAllText(transcript, WriteRecord(Path.Combine(_root, "a.cs")) + "\n");

        // Another program holds the file without sharing it, so it cannot be opened: the tab must be told, not left loading.
        using (new FileStream(transcript, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var (files, note) = TranscriptWrites.Of(_root, SessionId);

            Assert.Null(files);
            Assert.StartsWith("The transcript cannot be read", note);
        }
    }
}
