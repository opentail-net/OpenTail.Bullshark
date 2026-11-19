using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// The line starts of a transcript are kept per file with its key (path, length, write time), so a jump does not scan the file again, and a changed
/// file is scanned again (T5 batch). Each test uses its own <see cref="TranscriptPagerCache"/>, so no other test can change its counts.
/// <see cref="TranscriptPager.ReadRaw"/> gives one line as written (T3 batch).
/// </summary>
public sealed class TranscriptPagerCacheTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-pagercache-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly TranscriptPagerCache _cache = new();

    private const string User = "{\"type\":\"user\",\"timestamp\":\"2026-11-19T09:00:00.000Z\",\"message\":{\"role\":\"user\",\"content\":\"make hello.txt\"}}";
    private const string Reply = "{\"type\":\"assistant\",\"timestamp\":\"2026-11-19T09:00:09.000Z\",\"message\":{\"id\":\"m2\",\"content\":[{\"type\":\"text\",\"text\":\"Done.\"}]}}";

    public TranscriptPagerCacheTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string NewFile(params string[] lines)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".jsonl");
        File.WriteAllLines(path, lines);
        return path;
    }

    [Fact]
    public void AnUnchangedFileIsScannedOnceAndTheSecondOpenReusesTheStarts()
    {
        var path = NewFile(User, Reply);

        var first = _cache.Open(path);
        var second = _cache.Open(path);

        Assert.Equal(1, _cache.ScansOf(path));
        Assert.Equal(first.LineCount, second.LineCount);
        Assert.Equal("Done.", second.ReadPage(1, 1).Single().Text);
    }

    [Fact]
    public void AnAppendedFileIsScannedAgainAndTheNewLineIsThere()
    {
        var path = NewFile(User);
        Assert.Equal(1, _cache.Open(path).LineCount);

        File.AppendAllText(path, Reply + "\n");
        var grown = _cache.Open(path);

        Assert.Equal(2, grown.LineCount);
        Assert.Equal(2, _cache.ScansOf(path));
        Assert.Equal("Done.", grown.ReadPage(1, 1).Single().Text);
    }

    [Fact]
    public void AFileWithTheSameLengthButANewWriteTimeIsScannedAgain()
    {
        var path = NewFile(User);
        _cache.Open(path);

        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
        _cache.Open(path);

        Assert.Equal(2, _cache.ScansOf(path));
    }

    [Fact]
    public void EachFileKeepsItsOwnStarts()
    {
        var one = NewFile(User);
        var two = NewFile(User, Reply, Reply);

        _cache.Open(one);
        _cache.Open(two);
        _cache.Open(one);

        Assert.Equal(1, _cache.ScansOf(one));
        Assert.Equal(1, _cache.ScansOf(two));
        Assert.Equal(3, _cache.Open(two).LineCount);
    }

    [Fact]
    public void PastItsLimitTheCacheDropsAFileAndItStartsAgainWhenOpened()
    {
        var small = new TranscriptPagerCache(maxFiles: 2);
        var files = Enumerable.Range(0, 3).Select(_ => NewFile(User)).ToList();

        foreach (var file in files) small.Open(file);

        // Only two of the three are kept: one of them was dropped, and it is scanned again when opened.
        var dropped = files.Where(file => small.ScansOf(file) == 0).ToList();
        Assert.Single(dropped);
        small.Open(dropped[0]);
        Assert.Equal(1, small.ScansOf(dropped[0]));
    }

    [Fact]
    public void AZeroLimitIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranscriptPagerCache(maxFiles: 0));
    }

    [Fact]
    public void TheLeastRecentlyUsedFileIsDroppedNotTheOldestOpened()
    {
        // a was opened first, but it was used again after b: b is the least recently used, so b goes when c is opened (review R11).
        var small = new TranscriptPagerCache(maxFiles: 2);
        var a = NewFile(User);
        var b = NewFile(User);
        var c = NewFile(User);

        small.Open(a);
        small.Open(b);
        small.Open(a);
        small.Open(c);

        Assert.Equal(1, small.ScansOf(a));
        Assert.Equal(0, small.ScansOf(b));
        Assert.Equal(1, small.ScansOf(c));
    }

    [Fact]
    public void TheFileJustOpenedIsNeverTheOneDropped()
    {
        var one = new TranscriptPagerCache(maxFiles: 1);
        var first = NewFile(User);
        var second = NewFile(User);

        one.Open(first);
        one.Open(second);

        Assert.Equal(0, one.ScansOf(first));
        Assert.Equal(1, one.ScansOf(second));
    }

    [Fact]
    public async Task TwoOpensOfOneFileAtTheSameTimeScanItOnce()
    {
        // The scan is slow and counted: a second open waits for the first one's scan, and finds its starts (review R11).
        var scans = 0;
        var cache = new TranscriptPagerCache(scan: _ =>
        {
            Interlocked.Increment(ref scans);
            Thread.Sleep(200);
            return [0L, 10L];
        });
        var path = NewFile(User, Reply);

        var opened = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => cache.Open(path))));

        Assert.Equal(1, scans);
        Assert.Equal(1, cache.ScansOf(path));
        Assert.All(opened, pager => Assert.Equal(2, pager.LineCount));
    }

    [Fact]
    public void TheSharedPagerOpenGivesTheSameLinesAsAnOwnCache()
    {
        var path = NewFile(User, Reply);

        // The shared cache is used by the views and shared with other tests, so only what it returns is checked here, not its counts.
        Assert.Equal(_cache.Open(path).LineCount, TranscriptPager.Open(path).LineCount);
        Assert.Equal("Done.", TranscriptPager.Open(path).ReadPage(1, 1).Single().Text);
    }

    [Fact]
    public void ReadRawGivesTheLineAsItIsWritten()
    {
        var path = NewFile(User, Reply);
        var pager = _cache.Open(path);

        Assert.Equal(Reply, pager.ReadRaw(2));
        Assert.Equal(User, pager.ReadRaw(1));
    }

    [Fact]
    public void ReadRawOutsideTheFileIsNull()
    {
        var pager = _cache.Open(NewFile(User));

        Assert.Null(pager.ReadRaw(0));
        Assert.Null(pager.ReadRaw(2));
    }

    [Fact]
    public void ReadRawKeepsALineThatIsNotJsonAndAUnicodeLineWhole()
    {
        const string unicode = "{\"type\":\"user\",\"message\":{\"content\":\"café 😀\"}}";
        var pager = _cache.Open(NewFile("not json at all", unicode));

        Assert.Equal("not json at all", pager.ReadRaw(1));
        Assert.Equal(unicode, pager.ReadRaw(2));
    }
}
