using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>The one list of failures a user action reports in the status line (round 4, item 2).</summary>
public sealed class StoreErrorsTests
{
    [Fact]
    public void TheFailuresOfAStoreAndOfAFileTheUserCannotOpenAreExpected()
    {
        Assert.True(StoreErrors.IsExpected(new StoreUnreadableException("runs.json", "damaged")));
        Assert.True(StoreErrors.IsExpected(new TimeoutException("held by another process")));
        Assert.True(StoreErrors.IsExpected(new IOException("the disk")));
        Assert.True(StoreErrors.IsExpected(new FileNotFoundException("gone")));
        Assert.True(StoreErrors.IsExpected(new DirectoryNotFoundException("gone")));
        Assert.True(StoreErrors.IsExpected(new UnauthorizedAccessException("denied")));
    }

    [Fact]
    public void AnythingElseIsNotExpectedAndGoesToTheCatchAll()
    {
        Assert.False(StoreErrors.IsExpected(new InvalidOperationException("a bug")));
        Assert.False(StoreErrors.IsExpected(new ArgumentException("a path")));
        Assert.False(StoreErrors.IsExpected(new NullReferenceException()));
        Assert.False(StoreErrors.IsExpected(new OperationCanceledException()));
    }
}
