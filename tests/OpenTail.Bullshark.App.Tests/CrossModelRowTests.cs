using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The model row and the cost line of a resume screen for a cross start (resume source and target plan, S4, RS-D12). Pure: no window is made.
/// </summary>
public sealed class CrossModelRowTests
{
    [Fact]
    public void ShowsModel_IsTrueOnlyForTheOwnPlatform()
    {
        Assert.True(CrossModelRow.ShowsModel(ProviderId.Claude, ProviderId.Claude));
        Assert.True(CrossModelRow.ShowsModel(ProviderId.Codex, ProviderId.Codex));
        Assert.True(CrossModelRow.ShowsModel(ProviderId.Copilot, ProviderId.Copilot));

        Assert.False(CrossModelRow.ShowsModel(ProviderId.Claude, ProviderId.Codex));
        Assert.False(CrossModelRow.ShowsModel(ProviderId.Claude, ProviderId.Copilot));
        Assert.False(CrossModelRow.ShowsModel(ProviderId.Codex, ProviderId.Claude));
        Assert.False(CrossModelRow.ShowsModel(ProviderId.Codex, ProviderId.Copilot));
        Assert.False(CrossModelRow.ShowsModel(ProviderId.Copilot, ProviderId.Claude));
        Assert.False(CrossModelRow.ShowsModel(ProviderId.Copilot, ProviderId.Codex));
    }

    [Fact]
    public void CostLineForCross_NamesClaudeAndItsDefault()
    {
        Assert.Equal(
            "The new Claude session starts at its own default model and effort.",
            CrossModelRow.CostLineForCross(ProviderId.Claude));
    }

    [Fact]
    public void CostLineForCross_NamesCodexAndItsDefault()
    {
        Assert.Equal(
            "The new Codex session starts at its own default model and effort.",
            CrossModelRow.CostLineForCross(ProviderId.Codex));
    }

    [Fact]
    public void CostLineForCross_NamesCopilotAndItsDefault()
    {
        Assert.Equal(
            "The new Copilot session starts at its own default model and effort.",
            CrossModelRow.CostLineForCross(ProviderId.Copilot));
    }
}
