using System.Text;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Setup;
using Xunit;
using static OpenTail.Bullshark.Core.Tests.Setup.LimitLines;

namespace OpenTail.Bullshark.Core.Tests.Setup;

/// <summary>
/// The stop reaches the session digest (limit wake, 2026-11-20): from the index's read, from the index cache on the next build, and from the reader
/// that the rest of the summary uses. Each test writes its transcripts into a temp folder that it removes.
/// </summary>
public sealed class LimitStopDigestTests : IDisposable
{
    private static readonly DateTimeOffset Hit = new(2026, 11, 20, 10, 0, 7, TimeSpan.Zero);
    private static readonly DateTimeOffset Reset = new(2026, 11, 20, 12, 20, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-limitstop-" + Guid.NewGuid().ToString("N"));

    public LimitStopDigestTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string ProjectsRoot => Path.Combine(_root, "projects");

    private string WriteSession(string id, IEnumerable<string> lines)
    {
        var folder = Path.Combine(ProjectsRoot, "C--synthetic");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, id + ".jsonl");
        File.WriteAllText(path, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        return path;
    }

    [Fact]
    public void ADigestOfATranscriptThatEndsAtALimitCarriesTheStop()
    {
        WriteSession("stopped-1", [UserMessage(Hit.AddMinutes(-1)), Assistant(Hit.AddMinutes(-1)), Limit(Hit, Reset)]);

        var digest = TranscriptIndex.Build(ProjectsRoot).Sessions.Single();

        Assert.Equal(new LimitStop(Hit, Reset, "five_hour"), digest.LimitStop);
    }

    [Fact]
    public void ADigestOfATranscriptThatWasWokenHasNoStop()
    {
        WriteSession("woken-1", [Limit(Hit, Reset), UserMessage(Reset.AddMinutes(2))]);

        var digest = TranscriptIndex.Build(ProjectsRoot).Sessions.Single();

        Assert.Null(digest.LimitStop);
    }

    [Fact]
    public void TheCachedDigestCarriesTheStopOnTheNextBuild()
    {
        // Written an hour ago, so it is outside the live window and the second build takes its entry from the cache.
        var path = WriteSession("stopped-2", [Assistant(Hit.AddMinutes(-1)), Limit(Hit, Reset)]);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-1));
        var cache = Path.Combine(_root, "cache", "index.json");
        var expected = new LimitStop(Hit, Reset, "five_hour");

        var first = TranscriptIndex.Build(ProjectsRoot, cache).Sessions.Single();
        var cacheText = File.ReadAllText(cache);
        var second = TranscriptIndex.Build(ProjectsRoot, cache).Sessions.Single();

        Assert.Equal(expected, first.LimitStop);
        Assert.Contains("limitStop", cacheText);
        Assert.Equal(expected, second.LimitStop);
    }

    [Fact]
    public void TheTextReaderGivesTheSameStopAsTheIndex()
    {
        var path = WriteSession("stopped-3", [Assistant(Hit.AddMinutes(-1)), Limit(Hit, Reset), Metadata("system", Hit.AddSeconds(1))]);

        var summary = TranscriptReader.ReadFile(path);

        Assert.Equal(new LimitStop(Hit, Reset, "five_hour"), summary.LimitStop);
    }

    [Fact]
    public void ALimitStopAtTheEndOfALongTranscriptIsReadFromItsTail()
    {
        // A transcript far longer than the tail window still gives its stop, from the bytes at its end.
        // 1,000 lines of about 330 bytes is more than the 256 KB tail window.
        var filler = Enumerable.Range(0, 1000).Select(n => Assistant(Hit.AddMinutes(-1000 + n), "old-" + n));
        var path = WriteSession("stopped-4", filler.Append(Limit(Hit, Reset)));

        Assert.Equal(new LimitStop(Hit, Reset, "five_hour"), LimitStop.ReadTail(path));
    }
}
