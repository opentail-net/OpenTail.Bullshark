using OpenTail.Bullshark.App;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// Stage OC2e (Part 3): which providers keep their pane when a terminal session tab has no row selected (the rail's selection policy). The expected
/// table is exact: Claude's tab does not keep it, and Codex, Copilot and opencode do.
/// </summary>
public sealed class KeepsPaneWithoutRowTests
{
    [Theory]
    [InlineData("claude", false)]
    [InlineData("codex", true)]
    [InlineData("copilot", true)]
    [InlineData("opencode", true)]
    public void EachProviderKeepsItsPaneWithNoRowSelectedAsTheTableSays(string provider, bool keeps)
    {
        Assert.Equal(keeps, SessionWindow.KeepsPaneWithoutRow(new ProviderId(provider)));
    }
}
