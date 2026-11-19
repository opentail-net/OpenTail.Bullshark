using OpenTail.Bullshark.Core.Ui;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Ui;

/// <summary>
/// The selection after a rail refresh (perf and testability batch, Q6 a). Each branch of the App's old RestoreSelection has its row here, and the
/// pane is kept exactly where it was kept before: a search, a transcript, changes, and with no row left also Insights and Settings.
/// </summary>
public sealed class SelectionPolicyTests
{
    [Fact]
    public void TheSelectedRowStaysSelectedWhateverThePaneIs()
    {
        foreach (var pane in Enum.GetValues<PaneKind>())
            Assert.Equal(SelectionAction.KeepSelected, SelectionPolicy.Decide(selectedFound: true, hasFallback: false, pane));
    }

    [Theory]
    [InlineData(PaneKind.Search)]
    [InlineData(PaneKind.Transcript)]
    [InlineData(PaneKind.Changes)]
    [InlineData(PaneKind.Insights)]
    [InlineData(PaneKind.Settings)]
    public void WithNoRowLeftThesePanesAreKept(PaneKind pane) =>
        Assert.Equal(SelectionAction.KeepPaneWithoutSelection, SelectionPolicy.Decide(selectedFound: false, hasFallback: false, pane));

    [Theory]
    [InlineData(PaneKind.Session)]
    [InlineData(PaneKind.Setup)]
    public void WithNoRowLeftTheSetupShows(PaneKind pane) =>
        Assert.Equal(SelectionAction.ShowSetupWithoutSelection, SelectionPolicy.Decide(selectedFound: false, hasFallback: false, pane));

    [Fact]
    public void WithNoActiveTabAndNoRowTheSetupShows()
    {
        Assert.Equal(SelectionAction.ShowSetupWithoutSelection, SelectionPolicy.Decide(selectedFound: false, hasFallback: false, active: null));
    }

    [Theory]
    [InlineData(PaneKind.Search)]
    [InlineData(PaneKind.Transcript)]
    [InlineData(PaneKind.Changes)]
    public void AFallbackRowIsSelectedWithoutTakingTheSearchTranscriptOrChangesPane(PaneKind pane) =>
        Assert.Equal(SelectionAction.SelectFallbackKeepPane, SelectionPolicy.Decide(selectedFound: false, hasFallback: true, pane));

    [Theory]
    [InlineData(PaneKind.Session)]
    [InlineData(PaneKind.Setup)]
    [InlineData(PaneKind.Insights)]
    [InlineData(PaneKind.Settings)]
    public void AFallbackRowIsSelectedAndItsPaneShows(PaneKind pane) =>
        Assert.Equal(SelectionAction.SelectFallbackAndShow, SelectionPolicy.Decide(selectedFound: false, hasFallback: true, pane));

    [Fact]
    public void AFallbackRowWithNoActiveTabShows()
    {
        Assert.Equal(SelectionAction.SelectFallbackAndShow, SelectionPolicy.Decide(selectedFound: false, hasFallback: true, active: null));
    }
}
