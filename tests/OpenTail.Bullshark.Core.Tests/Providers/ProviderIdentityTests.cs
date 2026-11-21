using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Providers;

/// <summary>Stage A6: the same session id under two providers is two keys, and a missing interface is answered without a crash.</summary>
public sealed class ProviderIdentityTests
{
    private static readonly ProviderId Fake = new("fake");

    [Fact]
    public void TheSameSessionIdUnderTwoProvidersIsTwoKeys()
    {
        var claude = new SessionKey(ProviderId.Claude, "s1");
        var fake = new SessionKey(Fake, "s1");

        Assert.NotEqual(claude, fake);
        var byKey = new Dictionary<SessionKey, string> { [claude] = "claude", [fake] = "fake" };
        Assert.Equal(2, byKey.Count);
        Assert.Equal("claude", byKey[claude]);
        Assert.Equal("fake", byKey[fake]);
    }

    [Fact]
    public void TheTextFormIsProviderColonId()
    {
        Assert.Equal("claude:s1", new SessionKey(ProviderId.Claude, "s1").ToString());
        Assert.Equal("codex:019e", new SessionKey(ProviderId.Codex, "019e").ToString());
        Assert.Equal("fake:s1", new SessionKey(Fake, "s1").ToString());
    }

    [Fact]
    public void AStoredTextWithAProviderPrefixParsesToThatProvider()
    {
        Assert.Equal(new SessionKey(Fake, "s1"), SessionKey.Parse("fake:s1"));
        Assert.Equal(new SessionKey(ProviderId.Codex, "019e"), SessionKey.Parse("codex:019e"));
        Assert.Equal(new SessionKey(ProviderId.Claude, "3f2a"), SessionKey.Parse("claude:3f2a"));
    }

    [Fact]
    public void ABareIdIsClaudesKey()
    {
        const string id = "17a94483-99fc-4005-9fa9-dbd63958653d";
        Assert.Equal(new SessionKey(ProviderId.Claude, id), SessionKey.Parse(id));
    }

    [Theory]
    [InlineData("Fake:s1")]
    [InlineData("1abc:s1")]
    [InlineData(":s1")]
    [InlineData("no-colon-here")]
    public void TextWithoutAProviderPrefixIsBare(string text)
    {
        Assert.False(SessionKey.HasProviderPrefix(text));
        Assert.Equal(new SessionKey(ProviderId.Claude, text), SessionKey.Parse(text));
    }

    [Fact]
    public void AProviderThatImplementsAnInterfaceHasThatFeature()
    {
        var fake = new FakeProvider();
        Assert.True(ProviderFeatures.Has<IHistorySource>(fake));
        Assert.True(ProviderFeatures.Has<ITerminalOpener>(fake));
        Assert.False(ProviderFeatures.Has<ISupervisedLauncher>(fake));
    }

    [Fact]
    public void AProviderWithoutTerminalOpenerHasNoTerminalFeatureAndNoCrash()
    {
        IProvider provider = new HistoryOnlyProvider();

        Assert.True(ProviderFeatures.Has<IHistorySource>(provider));
        Assert.False(ProviderFeatures.Has<ITerminalOpener>(provider));
        Assert.False(ProviderFeatures.Has<ILiveSource>(provider));
    }
}
