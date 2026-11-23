using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Continue;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The opencode resume screen opened on a stored last-turns mode (RS-D27): the mode reads as Summary, so the summary radio is checked, the tail radio is not,
/// and nothing is previewed or called at open. Headless: the preview callback records each request. The sample text is made up.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class OpenCodeLastTurnsStartTests
{
    private const string TailText = "you: an example message\nopencode: an example reply";

    private static T Named<T>(FrameworkElement window, string name) where T : class =>
        window.FindName(name) as T ?? throw new InvalidOperationException($"no element named {name}");

    [Fact]
    public void AStoredLastTurnsOpensOnTheSummaryRadioAndMakesNothing()
    {
        UiThread.Run(() =>
        {
            AppResources.Ensure();
            var asked = new List<OpenCodeResumeChoice>();
            var prompt = new OpenCodeResumePrompt(
                "Resume: an example session",
                "2 turns · last active 1 day ago · in C:\\Work\\example",
                RailPalette.DarkPalette,
                (choice, ct) =>
                {
                    asked.Add(choice);
                    return Task.FromResult(new OpenCodePreview(TailText));
                },
                initialMode: ContinueMode.LastTurns);

            Assert.True(Named<RadioButton>(prompt, "SummaryChoice").IsChecked == true);
            Assert.False(Named<RadioButton>(prompt, "TailChoice").IsChecked == true);
            Assert.Empty(asked);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }
}
