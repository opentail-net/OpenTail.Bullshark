using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// A session's working folder (the last cwd its transcript names) reaches the digest, and the index cache carries it (P9, round-11 rule). The
/// rail and the Storage page name a folder by it, so a warm build must give the same name as a cold one without reading the file again.
/// </summary>
public sealed class TranscriptIndexCwdCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-cwd-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _cache = Path.Combine(Path.GetTempPath(), "bullshark-cwd-state-" + Guid.NewGuid().ToString("N")[..8], "cache", "transcript-index.json");

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
        try { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(_cache))!, recursive: true); } catch (IOException) { }
    }

    /// <summary>One transcript whose records name the folder "C:\Git\alpha", and one that names none.</summary>
    private string WriteTranscripts()
    {
        var folder = Path.Combine(_root, "C--Git-alpha");
        Directory.CreateDirectory(folder);
        var stamp = "2026-11-01T10:00:00.000Z";
        var named = Path.Combine(folder, "aaaaaaaa-0000-0000-0000-000000000001.jsonl");
        File.WriteAllLines(named, [
            "{\"type\":\"user\",\"timestamp\":\"" + stamp + "\",\"cwd\":\"C:\\\\Git\\\\alpha\",\"message\":{\"role\":\"user\",\"content\":\"hi\"}}",
            "{\"type\":\"system\",\"subtype\":\"turn_duration\",\"durationMs\":900,\"timestamp\":\"" + stamp + "\"}",
        ]);
        var unnamed = Path.Combine(folder, "aaaaaaaa-0000-0000-0000-000000000002.jsonl");
        File.WriteAllLines(unnamed, [
            "{\"type\":\"user\",\"timestamp\":\"" + stamp + "\",\"message\":{\"role\":\"user\",\"content\":\"hello\"}}",
        ]);
        // Written an hour ago, outside the two-minute window in which the cache is not used (round 11 item 1).
        foreach (var path in new[] { named, unnamed }) File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-1));
        return named;
    }

    private static string? CwdOf(TranscriptIndex index, string sessionId) =>
        index.Sessions.Single(digest => digest.SessionId == sessionId).Cwd;

    [Fact]
    public void TheWorkingFolderOfATranscriptIsOnItsDigestAndNullWhenNoneIsNamed()
    {
        WriteTranscripts();

        var index = TranscriptIndex.Build(_root);

        Assert.Equal(@"C:\Git\alpha", CwdOf(index, "aaaaaaaa-0000-0000-0000-000000000001"));
        Assert.Null(CwdOf(index, "aaaaaaaa-0000-0000-0000-000000000002"));
    }

    [Fact]
    public void TheCacheCarriesTheWorkingFolderSoAWarmBuildNamesItWithoutReadingTheFile()
    {
        var named = WriteTranscripts();
        TranscriptIndex.Build(_root, _cache);

        // Same length and write time, different folder in the text: a read would see "omega", the cache's entry still says "alpha".
        var written = File.GetLastWriteTimeUtc(named);
        var length = new FileInfo(named).Length;
        File.WriteAllText(named, File.ReadAllText(named).Replace("alpha", "omega"));
        Assert.Equal(length, new FileInfo(named).Length);
        File.SetLastWriteTimeUtc(named, written);

        var warm = TranscriptIndex.Build(_root, _cache);

        Assert.Equal(@"C:\Git\alpha", CwdOf(warm, "aaaaaaaa-0000-0000-0000-000000000001"));
        Assert.Equal(@"C:\Git\omega", CwdOf(TranscriptIndex.Build(_root, TranscriptReader.ReadFile), "aaaaaaaa-0000-0000-0000-000000000001"));
    }
}
