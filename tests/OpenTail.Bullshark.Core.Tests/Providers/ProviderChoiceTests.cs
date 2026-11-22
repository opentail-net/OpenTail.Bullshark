using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Providers;

/// <summary>Stage C4: the provider text ui.json keeps. A missing or unknown value is Claude, so a damaged file starts the window as before.</summary>
public sealed class ProviderChoiceTests
{
    [Fact]
    public void TheKeyIsProvider()
    {
        Assert.Equal("provider", ProviderChoice.Key);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("Codex")]
    [InlineData(" CODEX ")]
    public void CodexInAnyCaseIsCodex(string text)
    {
        Assert.Equal(ProviderId.Codex, ProviderChoice.Parse(text));
    }

    [Theory]
    [InlineData("claude")]
    [InlineData("")]
    [InlineData("codex2")]
    [InlineData(null)]
    public void AnyOtherValueIsClaude(string? text)
    {
        Assert.Equal(ProviderId.Claude, ProviderChoice.Parse(text));
    }

    [Fact]
    public void TheTextKeptIsTheProviderName()
    {
        Assert.Equal("claude", ProviderChoice.TextOf(ProviderId.Claude));
        Assert.Equal("codex", ProviderChoice.TextOf(ProviderId.Codex));
        Assert.Equal(ProviderId.Codex, ProviderChoice.Parse(ProviderChoice.TextOf(ProviderId.Codex)));
    }

    [Theory]
    [InlineData("opencode")]
    [InlineData("OpenCode")]
    [InlineData(" OPENCODE ")]
    public void OpenCodeInAnyCaseIsOpenCode(string text)
    {
        Assert.Equal(ProviderId.OpenCode, ProviderChoice.Parse(text));
    }

    [Fact]
    public void TheTextKeptForOpenCodeIsItsName()
    {
        Assert.Equal("opencode", ProviderChoice.TextOf(ProviderId.OpenCode));
        Assert.Equal(ProviderId.OpenCode, ProviderChoice.Parse(ProviderChoice.TextOf(ProviderId.OpenCode)));
    }
}
