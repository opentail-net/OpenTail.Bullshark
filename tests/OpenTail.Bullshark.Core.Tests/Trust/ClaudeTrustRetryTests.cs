using OpenTail.Bullshark.Core.Trust;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Trust;

/// <summary>F10: a read of ~/.claude.json that fails is tried once more before it is reported as a problem.</summary>
public sealed class ClaudeTrustRetryTests : IDisposable
{
    private const string Root = @"C:\work\app";
    private const string Complete = "{\"projects\":{\"C:\\\\work\\\\app\":{\"hasTrustDialogAccepted\":true}}}";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-trust-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _path;

    public ClaudeTrustRetryTests()
    {
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, ".claude.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task ATornReadThatIsCompleteOnTheRetryIsNotAProblem()
    {
        File.WriteAllText(_path, "{\"projects\":{");
        // Claude finishes writing the file while the pass waits for its one retry.
        var writer = Task.Run(async () =>
        {
            await Task.Delay(50);
            File.WriteAllText(_path, Complete);
        });

        var trust = ClaudeTrust.Read(_path, TimeSpan.FromMilliseconds(500));
        await writer;

        Assert.Null(trust.Problem);
        Assert.True(trust.IsTrusted(Root));
    }

    [Fact]
    public void AFileThatStaysDamagedIsAProblemAfterTheRetry()
    {
        File.WriteAllText(_path, "{\"projects\":{");

        var trust = ClaudeTrust.Read(_path, TimeSpan.FromMilliseconds(10));

        Assert.NotNull(trust.Problem);
        Assert.Contains("cannot be read", trust.Problem);
        Assert.False(trust.IsTrusted(Root));
    }

    [Fact]
    public void WithNoRetryDelayTheFirstFailureIsAProblemAtOnce()
    {
        File.WriteAllText(_path, "{\"projects\":{");

        var trust = ClaudeTrust.Read(_path, TimeSpan.Zero);

        Assert.NotNull(trust.Problem);
    }

    [Fact]
    public void AMissingFileIsNoProblemAndIsNotRetried()
    {
        var trust = ClaudeTrust.Read(_path, TimeSpan.FromSeconds(30));

        Assert.Null(trust.Problem);
        Assert.False(trust.IsTrusted(Root));
    }
}
