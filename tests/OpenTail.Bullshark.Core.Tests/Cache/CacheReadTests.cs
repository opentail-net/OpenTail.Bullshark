using System.Text.Json;
using OpenTail.Bullshark.Core.Cache;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Cache;

/// <summary>Reliability batch R11: a cache read that meets a replace is tried once more after 100 ms; no other failure is retried.</summary>
public sealed class CacheReadTests
{
    /// <summary>A read that throws each error in turn, then returns the value. The pause is recorded, not waited.</summary>
    private static Func<int> Scripted(params Exception?[] failures)
    {
        var calls = 0;
        return () =>
        {
            var index = calls++;
            if (index < failures.Length && failures[index] is { } error) throw error;
            return 42;
        };
    }

    [Fact]
    public void AFailureOnceIsRetriedAfterTheDelayAndSucceeds()
    {
        var waits = new List<TimeSpan>();

        var value = CacheRead.Retry(Scripted(new IOException("sharing violation")), waits.Add);

        Assert.Equal(42, value);
        Assert.Equal([CacheRead.RetryDelay], waits);
        Assert.Equal(TimeSpan.FromMilliseconds(100), CacheRead.RetryDelay);
    }

    [Fact]
    public void ADamagedFileOnceIsRetried()
    {
        var waits = new List<TimeSpan>();

        var value = CacheRead.Retry(Scripted(new JsonException("half written")), waits.Add);

        Assert.Equal(42, value);
        Assert.Single(waits);
    }

    [Fact]
    public void ASecondFailureIsTheCallersAndNothingMoreIsTried()
    {
        var waits = new List<TimeSpan>();
        var read = Scripted(new IOException("first"), new IOException("second"), null);

        var error = Assert.Throws<IOException>(() => CacheRead.Retry(read, waits.Add));

        Assert.Equal("second", error.Message);
        Assert.Single(waits);
    }

    [Fact]
    public void AMissingFileIsNotRetried()
    {
        var waits = new List<TimeSpan>();
        var calls = 0;

        Assert.Throws<FileNotFoundException>(() => CacheRead.Retry<int>(() => { calls++; throw new FileNotFoundException("gone"); }, waits.Add));

        Assert.Equal(1, calls);
        Assert.Empty(waits);
    }

    [Fact]
    public void AMissingFolderIsNotRetried()
    {
        var calls = 0;

        Assert.Throws<DirectoryNotFoundException>(() => CacheRead.Retry<int>(() => { calls++; throw new DirectoryNotFoundException("gone"); }, _ => { }));

        Assert.Equal(1, calls);
    }

    [Fact]
    public void AnyOtherErrorIsNotRetried()
    {
        var calls = 0;

        Assert.Throws<ArgumentException>(() => CacheRead.Retry<int>(() => { calls++; throw new ArgumentException("bad"); }, _ => { }));

        Assert.Equal(1, calls);
    }

    [Fact]
    public void AReadThatWorksIsNotPaused()
    {
        var waits = new List<TimeSpan>();

        Assert.Equal(42, CacheRead.Retry(() => 42, waits.Add));

        Assert.Empty(waits);
    }

    [Fact]
    public void ReplaceErrorsAreTheIOAndJsonOnesOtherThanMissingFiles()
    {
        Assert.True(CacheRead.IsReplaceError(new IOException()));
        Assert.True(CacheRead.IsReplaceError(new JsonException()));
        Assert.False(CacheRead.IsReplaceError(new FileNotFoundException()));
        Assert.False(CacheRead.IsReplaceError(new InvalidOperationException()));
    }
}
