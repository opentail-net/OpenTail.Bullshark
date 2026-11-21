using OpenTail.Bullshark.App;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// Stage A4 and A6: a tab records the provider that opened it. The window's provider can change later (the dropdown, stage C); an open tab
/// keeps its own. Only Claude exists in stage A, so the second provider here is a stand-in value, not a window switch.
/// </summary>
public sealed class TabOwnershipTests
{
    private static readonly ProviderId Fake = new("fake");

    [Fact]
    public void ATabKeepsTheProviderThatOpenedItWhenTheActiveProviderChanges()
    {
        var active = ProviderId.Claude;
        var opened = new SessionWindow.OpenTab(SessionWindow.TabKind.Session, "s1", "One", active);

        active = Fake;
        var later = new SessionWindow.OpenTab(SessionWindow.TabKind.Session, "s2", "Two", active);

        Assert.Equal(ProviderId.Claude, opened.Provider);
        Assert.Equal(Fake, later.Provider);
    }

    [Fact]
    public void ATabWithNoProviderIsClaudes()
    {
        var tab = new SessionWindow.OpenTab(SessionWindow.TabKind.Setup, "setup", "New session");

        Assert.Equal(ProviderId.Claude, tab.Provider);
    }
}
