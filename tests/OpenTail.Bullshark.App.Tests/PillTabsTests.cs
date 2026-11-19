using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using OpenTail.Bullshark.App;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The pill row (perf and testability batch, Q7): one chosen pill, one tab stop for the row, and a click chooses a pill and raises Chosen. Structure
/// only; the pills use the application's own style (PillToggle).
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class PillTabsTests
{
    private static List<ToggleButton> PillsOf(PillTabs pills) =>
        LogicalTree.Walk(pills).OfType<ToggleButton>().ToList();

    /// <summary>A pill row with the app's pill style (PillToggle, in Styles/Controls.xaml), loaded here: the app's own start is not run by the tests.</summary>
    private static PillTabs NewPills()
    {
        var pills = new PillTabs();
        pills.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/OpenTail.Bullshark.App;component/Styles/Controls.xaml", UriKind.Absolute),
        });
        return pills;
    }

    [Fact]
    public void TheChosenPillIsTheOnlyTabStop()
    {
        UiThread.Run(() =>
        {
            var pills = NewPills();
            pills.SetItems(["Away", "Usage", "Timeline"], selected: 1);

            Assert.Equal(1, pills.Selected);
            Assert.Equal(new[] { false, true, false }, PillsOf(pills).Select(pill => pill.IsTabStop));
            Assert.Equal(new[] { false, true, false }, PillsOf(pills).Select(pill => pill.IsChecked == true));
        });
    }

    [Fact]
    public void WithNothingChosenTheFirstPillIsTheTabStop()
    {
        UiThread.Run(() =>
        {
            var pills = NewPills();
            pills.SetItems(["Away", "Usage"], selected: -1);

            Assert.Equal(-1, pills.Selected);
            Assert.Equal(new[] { true, false }, PillsOf(pills).Select(pill => pill.IsTabStop));
        });
    }

    [Fact]
    public void AClickChoosesAPillAndRaisesChosen()
    {
        UiThread.Run(() =>
        {
            var pills = NewPills();
            pills.SetItems(["Away", "Usage", "Timeline"], selected: 0);
            int? chosen = null;
            pills.Chosen += index => chosen = index;

            PillsOf(pills)[2].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            Assert.Equal(2, chosen);
            Assert.Equal(2, pills.Selected);
        });
    }
}
