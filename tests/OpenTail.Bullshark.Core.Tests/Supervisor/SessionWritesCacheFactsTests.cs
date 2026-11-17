using System.Text;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>
/// Round 12 item A3: the incremental writes cache must give what the whole-file read gives. The Changes tab and the merge check take their
/// files and folder from <see cref="SessionWritesCache.Read"/> (item B3), so the same transcript must give the same <c>Files</c> (in order)
/// and <c>Folder</c> as <see cref="TranscriptWrites.Facts"/>, read once, and also when the transcript grows between reads.
/// </summary>
public sealed class SessionWritesCacheFactsTests : IDisposable
{
    private const string SessionId = "c0c0c0c0-0000-0000-0000-000000000003";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-writes-facts-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _projects;
    private readonly string _transcript;

    public SessionWritesCacheFactsTests()
    {
        _projects = Path.Combine(_root, "projects");
        Directory.CreateDirectory(Path.Combine(_projects, "C--W-project"));
        _transcript = Path.Combine(_projects, "C--W-project", SessionId + ".jsonl");
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    /// <summary>A JSON string value for a path: backslashes are escaped, as a transcript writes them.</summary>
    private static string Json(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal);

    /// <summary>A user record that names a working folder (a blank one when <paramref name="folder"/> is only spaces).</summary>
    private static string Cwd(string folder, string timestamp) =>
        "{\"type\":\"user\",\"timestamp\":\"" + timestamp + "\",\"cwd\":\"" + Json(folder) +
        "\",\"message\":{\"role\":\"user\",\"content\":\"a message\"}}";

    /// <summary>An assistant record that writes one file with the given tool.</summary>
    private static string Write(string messageId, string tool, string path, string timestamp) =>
        "{\"type\":\"assistant\",\"timestamp\":\"" + timestamp + "\",\"requestId\":\"req-" + messageId + "\",\"message\":{\"id\":\"" +
        messageId + "\",\"model\":\"claude-opus-5-5\",\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"tool-" + messageId +
        "\",\"name\":\"" + tool + "\",\"input\":{\"file_path\":\"" + Json(path) + "\"}}],\"usage\":{\"input_tokens\":3,\"output_tokens\":5," +
        "\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":0}}}";

    /// <summary>The fixture: relative and absolute writes, several folders, a file written twice, and a blank folder.</summary>
    private static readonly string[] Lines =
    [
        Cwd("C:\\W\\alpha", "2026-11-17T09:00:00Z"),
        Write("m1", "Write", "src/a.cs", "2026-11-17T09:00:05Z"),
        Write("m2", "Edit", "C:\\W\\beta\\b.cs", "2026-11-17T09:00:10Z"),
        Cwd("C:\\W\\gamma", "2026-11-17T09:00:15Z"),
        Write("m3", "Write", "src/a.cs", "2026-11-17T09:00:20Z"),
        Write("m4", "Write", "docs/d.md", "2026-11-17T09:00:25Z"),
        Cwd("   ", "2026-11-17T09:00:30Z"),
        Write("m5", "MultiEdit", "C:\\W\\delta\\e.txt", "2026-11-17T09:00:35Z"),
    ];

    private void AssertSame(TranscriptWrites.TranscriptFacts facts, SessionWrites writes)
    {
        Assert.NotNull(facts.Files);
        Assert.Equal(facts.Files, writes.Files);
        Assert.Equal(facts.Folder, writes.Folder);
    }

    [Fact]
    public void TheCacheGivesTheSameFilesInOrderAndFolderAsTheWholeFileRead()
    {
        File.WriteAllText(_transcript, string.Join("\n", Lines) + "\n", new UTF8Encoding(false));

        var facts = TranscriptWrites.Facts(_projects, SessionId);
        var writes = new SessionWritesCache().Read(SessionId, _transcript);

        AssertSame(facts, writes);
        // The expected values, so a change to either side shows.
        Assert.Equal(["src/a.cs", "C:\\W\\beta\\b.cs", "docs/d.md", "C:\\W\\delta\\e.txt"], writes.Files);
        Assert.Equal("C:\\W\\gamma", writes.Folder);
    }

    [Fact]
    public void TheCacheMatchesAfterTheTranscriptGrowsBetweenReads()
    {
        var cache = new SessionWritesCache();
        var half = Lines.Length / 2;
        File.WriteAllText(_transcript, string.Join("\n", Lines.Take(half)) + "\n", new UTF8Encoding(false));
        AssertSame(TranscriptWrites.Facts(_projects, SessionId), cache.Read(SessionId, _transcript));

        // The rest is appended in two steps: the second step ends with a line still being written (no newline yet).
        File.AppendAllText(_transcript, string.Join("\n", Lines.Skip(half).Take(2)) + "\n", new UTF8Encoding(false));
        AssertSame(TranscriptWrites.Facts(_projects, SessionId), cache.Read(SessionId, _transcript));

        var rest = Lines.Skip(half + 2).ToList();
        var partial = rest[0];
        File.AppendAllText(_transcript, partial[..(partial.Length / 2)], new UTF8Encoding(false));
        AssertSame(TranscriptWrites.Facts(_projects, SessionId), cache.Read(SessionId, _transcript));

        File.AppendAllText(_transcript, partial[(partial.Length / 2)..] + "\n" + string.Join("\n", rest.Skip(1)) + "\n", new UTF8Encoding(false));
        AssertSame(TranscriptWrites.Facts(_projects, SessionId), cache.Read(SessionId, _transcript));
    }

    [Fact]
    public void ATranscriptWithNoFolderGivesNoFolderFromEither()
    {
        File.WriteAllText(_transcript, Write("m9", "Write", "relative/only.cs", "2026-11-17T10:00:00Z") + "\n", new UTF8Encoding(false));

        var facts = TranscriptWrites.Facts(_projects, SessionId);
        var writes = new SessionWritesCache().Read(SessionId, _transcript);

        AssertSame(facts, writes);
        Assert.Null(writes.Folder);
        Assert.Equal(["relative/only.cs"], writes.Files);
    }

    [Fact]
    public void AByteOrderMarkDoesNotChangeEitherSide()
    {
        File.WriteAllText(_transcript, string.Join("\n", Lines) + "\n", new UTF8Encoding(true));

        AssertSame(TranscriptWrites.Facts(_projects, SessionId), new SessionWritesCache().Read(SessionId, _transcript));
    }
}
