using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Copilot;

/// <summary>
/// Stage G1.1: the Copilot provider id, the ui.json choice, the paths (COPILOT_HOME or the profile) and the session key. Claude's and Codex's results
/// are checked again here, so the additive change is visible in one place.
/// </summary>
public sealed class CopilotProviderIdTests
{
    [Fact]
    public void TheIdAndItsTextAreCopilot()
    {
        Assert.Equal("copilot", ProviderId.Copilot.Value);
        Assert.Equal("copilot", ProviderId.Copilot.ToString());
    }

    [Theory]
    [InlineData("copilot")]
    [InlineData("Copilot")]
    [InlineData(" COPILOT ")]
    public void CopilotInAnyCaseIsCopilot(string text)
    {
        Assert.Equal(ProviderId.Copilot, ProviderChoice.Parse(text));
    }

    [Theory]
    [InlineData("copilot2")]
    [InlineData("copilo")]
    [InlineData("")]
    [InlineData(null)]
    public void AnOtherValueIsNotCopilot(string? text)
    {
        Assert.NotEqual(ProviderId.Copilot, ProviderChoice.Parse(text));
        Assert.Equal(ProviderId.Claude, ProviderChoice.Parse(text));
    }

    [Fact]
    public void TheTextKeptForEachProviderIsItsName()
    {
        Assert.Equal("copilot", ProviderChoice.TextOf(ProviderId.Copilot));
        Assert.Equal(ProviderId.Copilot, ProviderChoice.Parse(ProviderChoice.TextOf(ProviderId.Copilot)));

        // Claude and Codex are unchanged: their text, their parse, and the fallback for an unknown provider.
        Assert.Equal("claude", ProviderChoice.TextOf(ProviderId.Claude));
        Assert.Equal("codex", ProviderChoice.TextOf(ProviderId.Codex));
        Assert.Equal(ProviderId.Codex, ProviderChoice.Parse("codex"));
        Assert.Equal("claude", ProviderChoice.TextOf(new ProviderId("fake")));
    }

    [Fact]
    public void ACopilotSessionKeyHasTheCopilotPrefixAndParsesBack()
    {
        var key = new SessionKey(ProviderId.Copilot, "3f2a9c10-1b2d-4e5f-8a9b-0c1d2e3f4a51");

        Assert.Equal("copilot:3f2a9c10-1b2d-4e5f-8a9b-0c1d2e3f4a51", key.ToString());
        Assert.True(SessionKey.HasProviderPrefix(key.ToString()));
        Assert.Equal(key, SessionKey.Parse(key.ToString()));
        Assert.NotEqual(new SessionKey(ProviderId.Claude, key.SessionId), key);
    }

    [Fact]
    public void ForCopilotHonoursCopilotHomeWhenItIsSet()
    {
        var paths = ProviderPaths.ForCopilot(name => name == "COPILOT_HOME" ? "  C:\\Fixture\\copilot-home  " : null, userProfile: "C:\\Fixture\\profile");

        Assert.Equal("C:\\Fixture\\copilot-home", paths.HomeRoot);
        Assert.Equal(Path.Combine("C:\\Fixture\\copilot-home", "session-state"), paths.HistoryRoot);
        Assert.Null(paths.ArchiveRoot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ForCopilotUsesTheProfileWhenCopilotHomeIsNotSetOrBlank(string? value)
    {
        var paths = ProviderPaths.ForCopilot(name => name == "COPILOT_HOME" ? value : null, userProfile: "C:\\Fixture\\profile");

        Assert.Equal(Path.Combine("C:\\Fixture\\profile", ".copilot"), paths.HomeRoot);
        Assert.Equal(Path.Combine("C:\\Fixture\\profile", ".copilot", "session-state"), paths.HistoryRoot);
        Assert.Null(paths.ArchiveRoot);
    }

    [Fact]
    public void ForCopilotReadsTheVariableNamedByTheConstant()
    {
        Assert.Equal("COPILOT_HOME", ProviderPaths.CopilotHomeVariable);
    }

    [Fact]
    public void ForClaudeAndForCodexAreUnchanged()
    {
        var codex = ProviderPaths.ForCodex(name => name == "CODEX_HOME" ? "C:\\Fixture\\codex-home" : null, userProfile: "C:\\Fixture\\profile");

        Assert.Equal("C:\\Fixture\\codex-home", codex.HomeRoot);
        Assert.Equal(Path.Combine("C:\\Fixture\\codex-home", "sessions"), codex.HistoryRoot);
        Assert.Null(codex.ArchiveRoot);
    }
}
