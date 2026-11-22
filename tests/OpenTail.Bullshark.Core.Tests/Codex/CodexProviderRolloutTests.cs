using OpenTail.Bullshark.Core.Codex;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>
/// The first check of the Codex resume ladder (R7): whether a rollout is found for the saved id, through the provider. Synthetic rollouts in a temp
/// folder only; the folder is removed with the test.
/// </summary>
public sealed class CodexProviderRolloutTests : IDisposable
{
    private readonly CodexHome _home = new();

    public void Dispose() => _home.Dispose();

    [Fact]
    public void ASavedIdWithARolloutIsFound()
    {
        _home.WritePlain(CodexHome.NameFor(CodexHome.SessionId), CodexHome.Standard(CodexHome.SessionId));
        var provider = new CodexProvider(_home.Paths());

        Assert.True(provider.HasRollout(CodexHome.SessionId));
    }

    [Fact]
    public void ASavedIdWithNoRolloutIsNotFoundWhileAnotherIsThere()
    {
        _home.WritePlain(CodexHome.NameFor(CodexHome.SecondId), CodexHome.Standard(CodexHome.SecondId));
        var provider = new CodexProvider(_home.Paths());

        Assert.False(provider.HasRollout(CodexHome.SessionId));
    }

    [Fact]
    public void WithNoHistoryFolderNothingIsFound()
    {
        var provider = new CodexProvider(_home.Paths());

        Assert.False(provider.HasRollout(CodexHome.SessionId));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a*b")]
    [InlineData("a/b")]
    public void AnIdThatWouldWidenTheSearchIsRefusedNotSearched(string id)
    {
        var provider = new CodexProvider(_home.Paths());

        Assert.Throws<ArgumentException>(() => provider.HasRollout(id));
    }
}
