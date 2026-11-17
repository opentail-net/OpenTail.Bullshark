using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// The index can keep what it read from each transcript in a cache file (round 10 item 2). A build with the cache must give what the reader
/// gives, after any change: a transcript that was appended, removed, or damaged, a cache that is damaged, and a cache from another reader.
/// A transcript with the same length and write time as its entry is taken from the cache, and that is the rule these tests hold to.
/// </summary>
public sealed class TranscriptIndexCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-cache-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "bullshark-cache-state-" + Guid.NewGuid().ToString("N")[..8], "cache", "transcript-index.json");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        try { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(_cache))!, recursive: true); } catch (IOException) { }
    }

    private string Project(string name)
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>Six transcripts in two folders, created out of name order. Each copies a shared message, and has a prompt, a file write, a failed call and a title.</summary>
    private List<string> WriteTranscripts()
    {
        var created = new DateTime(2026, 11, 1, 8, 0, 0, DateTimeKind.Utc);
        var folders = new[] { Project("C--Git-alpha"), Project("C--Git-beta") };
        var paths = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var stamp = "2026-11-01T10:" + i.ToString("D2") + ":00.000Z";
            var lines = new List<string>
            {
                "{\"type\":\"user\",\"timestamp\":\"" + stamp + "\",\"message\":{\"role\":\"user\",\"content\":\"hello from file " + i + "\"}}",
                IndexFixture.Assistant("shared", "rshared", 10 + i * 4, stamp, i % 2 == 0 ? "claude-opus-5-5" : "claude-haiku-5-5"),
                "{\"type\":\"assistant\",\"timestamp\":\"" + stamp + "\",\"requestId\":\"rw" + i + "\",\"message\":{\"id\":\"mw" + i +
                    "\",\"model\":\"claude-opus-5-5\",\"role\":\"assistant\",\"content\":[{\"type\":\"tool_use\",\"id\":\"t" + i +
                    "\",\"name\":\"Write\",\"input\":{\"file_path\":\"notes/f" + i + ".md\",\"content\":\"x\"}}],\"usage\":{\"input_tokens\":3," +
                    "\"output_tokens\":9,\"cache_creation_input_tokens\":0,\"cache_read_input_tokens\":50}}}",
                "{\"type\":\"user\",\"timestamp\":\"" + stamp + "\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\"," +
                    "\"tool_use_id\":\"t" + i + "\",\"is_error\":true,\"content\":\"boom\"}]}}",
                "{\"type\":\"system\",\"subtype\":\"turn_duration\",\"durationMs\":1500,\"timestamp\":\"" + stamp + "\"}",
                "{\"type\":\"ai-title\",\"aiTitle\":\"Title " + i + "\",\"timestamp\":\"" + stamp + "\"}",
            };
            var path = Path.Combine(folders[i % 2], $"cccccccc-0000-0000-0000-{i:D12}.jsonl");
            File.WriteAllLines(path, lines);
            File.SetCreationTimeUtc(path, created.AddDays(i * 4 % 6));
            paths.Add(path);
        }
        return paths;
    }

    /// <summary>The index the reader gives for the folder: every transcript read one after another, with no cache.</summary>
    private static string Reader(string root) => IndexFixture.Describe(TranscriptIndex.Build(root, TranscriptReader.ReadFile));

    /// <summary>Changes a file's text without changing its length or its write time, so only its contents differ.</summary>
    private static void ChangeSameLength(string path, string from, string to)
    {
        var written = File.GetLastWriteTimeUtc(path);
        var length = new FileInfo(path).Length;
        File.WriteAllText(path, File.ReadAllText(path).Replace(from, to));
        Assert.Equal(length, new FileInfo(path).Length);
        File.SetLastWriteTimeUtc(path, written);
    }

    [Fact]
    public void AColdBuildWritesTheCacheAndGivesWhatTheReaderGives()
    {
        WriteTranscripts();

        var cold = TranscriptIndex.Build(_root, _cache);

        Assert.True(File.Exists(_cache));
        Assert.Equal(Reader(_root), IndexFixture.Describe(cold));
    }

    [Fact]
    public void AWarmBuildGivesWhatTheReaderGives()
    {
        WriteTranscripts();
        TranscriptIndex.Build(_root, _cache);

        var warm = TranscriptIndex.Build(_root, _cache);

        Assert.Equal(Reader(_root), IndexFixture.Describe(warm));
    }

    [Fact]
    public void ATranscriptWithTheSameLengthAndWriteTimeIsTakenFromTheCache()
    {
        var paths = WriteTranscripts();
        var before = IndexFixture.Describe(TranscriptIndex.Build(_root, _cache));
        ChangeSameLength(paths[2], "hello from file 2", "hello from file 9");

        var warm = IndexFixture.Describe(TranscriptIndex.Build(_root, _cache));

        // The file changed in content only: the cache's entry is used, as the rule says, and the reader would see the change.
        Assert.Equal(before, warm);
        Assert.NotEqual(Reader(_root), warm);
    }

    [Fact]
    public void AChangedTranscriptIsReadAgain()
    {
        var paths = WriteTranscripts();
        TranscriptIndex.Build(_root, _cache);
        File.AppendAllLines(paths[3], [IndexFixture.Assistant("appended", "rappended", 77, "2026-11-01T12:00:00.000Z", "claude-opus-5-5")]);

        Assert.Equal(Reader(_root), IndexFixture.Describe(TranscriptIndex.Build(_root, _cache)));
    }

    [Fact]
    public void ARemovedTranscriptLeavesTheCache()
    {
        var paths = WriteTranscripts();
        TranscriptIndex.Build(_root, _cache);
        File.Delete(paths[1]);

        var warm = TranscriptIndex.Build(_root, _cache);

        Assert.Equal(Reader(_root), IndexFixture.Describe(warm));
        Assert.Equal(5, warm.Sessions.Count);
    }

    [Fact]
    public void ADamagedCacheIsIgnoredAndWrittenAgain()
    {
        WriteTranscripts();
        TranscriptIndex.Build(_root, _cache);
        File.WriteAllText(_cache, "{ this is not a cache");

        var afterDamage = TranscriptIndex.Build(_root, _cache);
        var again = TranscriptIndex.Build(_root, _cache);

        Assert.Equal(Reader(_root), IndexFixture.Describe(afterDamage));
        Assert.Equal(Reader(_root), IndexFixture.Describe(again));
    }

    [Fact]
    public void ACacheWrittenByAnotherReaderIsIgnored()
    {
        var paths = WriteTranscripts();
        TranscriptIndex.Build(_root, _cache);
        ChangeSameLength(paths[4], "hello from file 4", "hello from file 8");
        // The cache names the reader build that wrote it. Another build's cache is not used, so the changed text is read.
        File.WriteAllText(_cache, File.ReadAllText(_cache).Replace("\"reader\":\"", "\"reader\":\"x"));

        Assert.Equal(Reader(_root), IndexFixture.Describe(TranscriptIndex.Build(_root, _cache)));
    }

    [Fact]
    public void AnUnchangedLockedTranscriptIsServedFromTheCache()
    {
        var paths = WriteTranscripts();
        var cold = IndexFixture.Describe(TranscriptIndex.Build(_root, _cache));
        using var held = new FileStream(paths[5], FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        // The entry is for the file as it is now, so the file is not opened: it is listed as it was, and not skipped. The reader would skip it.
        var warm = TranscriptIndex.Build(_root, _cache);

        Assert.Empty(warm.Skipped);
        Assert.Equal(cold, IndexFixture.Describe(warm));
    }

    [Fact]
    public void AChangedLockedTranscriptIsLeftOutAsTheReaderLeavesIt()
    {
        var paths = WriteTranscripts();
        TranscriptIndex.Build(_root, _cache);
        File.AppendAllLines(paths[5], [IndexFixture.Assistant("late", "rlate", 5, "2026-11-01T13:00:00.000Z", "claude-opus-5-5")]);
        using var held = new FileStream(paths[5], FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var warm = TranscriptIndex.Build(_root, _cache);

        Assert.Equal(IndexFixture.Describe(TranscriptIndex.Build(_root, TranscriptReader.ReadFile)), IndexFixture.Describe(warm));
        Assert.Single(warm.Skipped);
    }
}
