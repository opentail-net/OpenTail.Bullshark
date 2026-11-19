using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// A page read on a pool thread can be cancelled when another transcript opens (perf and testability batch, Q3). A read with no cancel gives the same
/// lines as before; a cancelled one throws before it reads the record it was going to read.
/// </summary>
public sealed class TranscriptPagerCancellationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-pager-cancel-" + Guid.NewGuid().ToString("N")[..8]);

    public TranscriptPagerCancellationTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private const string User = "{\"type\":\"user\",\"timestamp\":\"2026-11-14T09:00:00.000Z\",\"message\":{\"role\":\"user\",\"content\":\"hello\"}}";
    private const string Reply = "{\"type\":\"assistant\",\"timestamp\":\"2026-11-14T09:00:09.000Z\",\"message\":{\"id\":\"m2\",\"content\":[{\"type\":\"text\",\"text\":\"Done.\"}]}}";

    [Fact]
    public void APageReadWithANonCancelledTokenIsTheSameAsWithoutOne()
    {
        var path = Path.Combine(_dir, "session.jsonl");
        File.WriteAllLines(path, [User, Reply, User]);
        var pager = TranscriptPager.Open(path);

        var plain = pager.ReadPage(0, 10);
        var withToken = pager.ReadPage(0, 10, CancellationToken.None);

        Assert.Equal(plain.Select(line => line.Text), withToken.Select(line => line.Text));
        Assert.Equal(3, withToken.Count);
    }

    [Fact]
    public void ACancelledPageReadThrowsAndReadsNothing()
    {
        var path = Path.Combine(_dir, "session.jsonl");
        File.WriteAllLines(path, [User, Reply]);
        var pager = TranscriptPager.Open(path);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() => pager.ReadPage(0, 10, cancel.Token));
    }
}
