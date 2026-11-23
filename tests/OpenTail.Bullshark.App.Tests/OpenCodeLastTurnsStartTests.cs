using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Continue;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The opencode resume screen opened on a stored last-turns mode (RS-D27): the mode reads as Summary at Zero with the tail on, so the summary radio and the
/// Zero level are checked, and the Zero text is built at once with no summary call. Headless: the preview callback records each request. The sample text
/// is made up.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class OpenCodeLastTurnsStartTests
{
    private const string TailText = "you: an example message\nopencode: an example reply";

    private static T Named<T>(FrameworkElement window, string name) where T : class =>
        window.FindName(name) as T ?? throw new InvalidOperationException($"no element named {name}");

    [Fact]
    public void AStoredLastTurnsOpensOnZeroWithTheTailAndMakesNoSummaryCall()
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
            Assert.True(Named<RadioButton>(prompt, "ZeroLevel").IsChecked == true);
            Assert.True(Named<CheckBox>(prompt, "TailBox").IsChecked == true);
            // The Zero text is built at once (no level, so no summary call), and Start is on with it.
            var choice = Assert.Single(asked);
            Assert.Null(choice.Level);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }
}
