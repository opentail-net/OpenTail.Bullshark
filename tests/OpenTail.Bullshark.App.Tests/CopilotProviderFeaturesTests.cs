using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// G2.6 (palette) and G-D7: the Claude-only entries of the palette and the rail's supervision are built only when the window's provider has
/// <see cref="ISupervisedLauncher"/>. Copilot implements none of it, so the Claude-only entries stay out. The window's <c>ClaudeSupervision</c> is
/// <c>ProviderFeatures.Has&lt;ISupervisedLauncher&gt;</c>, and this test checks that rule on a real Copilot provider. No process is started.
/// </summary>
public sealed class CopilotProviderFeaturesTests
{
    [Fact]
    public void CopilotIsNotSupervisedSoTheClaudeOnlyEntriesAreLeftOut()
    {
        IProvider copilot = new CopilotProvider();

        Assert.False(ProviderFeatures.Has<ISupervisedLauncher>(copilot));
        Assert.False(ProviderFeatures.Has<ILiveSource>(copilot));
    }

    [Fact]
    public void CopilotOffersTheHistoryTranscriptAndTerminalInterfaces()
    {
        IProvider copilot = new CopilotProvider();

        Assert.True(ProviderFeatures.Has<IHistorySource>(copilot));
        Assert.True(ProviderFeatures.Has<ITranscriptReader>(copilot));
        Assert.True(ProviderFeatures.Has<ITerminalOpener>(copilot));
    }

    [Fact]
    public void CopilotIsTheCopilotProviderId()
    {
        Assert.Equal(ProviderId.Copilot, new CopilotProvider().Id);
        Assert.Equal("copilot", ProviderId.Copilot.Value);
    }
}
