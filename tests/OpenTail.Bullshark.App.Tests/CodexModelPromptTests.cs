using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Codex;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The model dialog of "New Codex session…" (P9, decision M-D4 b), headless. The model list is a finished task of synthetic ids. OK keeps the chosen
/// id, Cancel keeps nothing, and a list that could not be read gives Codex's default (no model).
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class CodexModelPromptTests
{
    private static CodexModelPrompt NewPrompt(Task<CodexModelReading> models)
    {
        AppResources.Ensure();
        return new CodexModelPrompt("New Codex session in an example folder", RailPalette.DarkPalette, models);
    }

    private static T Named<T>(CodexModelPrompt prompt, string name) where T : class =>
        prompt.FindName(name) as T ?? throw new InvalidOperationException($"no element named {name}");

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    [Fact]
    public void OkKeepsTheDefaultModelAndCancelKeepsNothing()
    {
        UiThread.Run(() =>
        {
            var models = Task.FromResult(new CodexModelReading(
                [
                    new CodexModelEntry("example-default", "Example Default", IsDefault: true),
                    new CodexModelEntry("example-other", "Example Other", IsDefault: false),
                ],
                Hidden: 0,
                Dropped: 0,
                CodexModelProblem.None));

            var ok = NewPrompt(models);

            Assert.True(Named<Button>(ok, "OkButton").IsEnabled);
            Click(Named<Button>(ok, "OkButton"));
            Assert.True(ok.Accepted);
            Assert.Equal("example-default", ok.Model);

            var cancel = NewPrompt(models);
            Click(Named<Button>(cancel, "CancelButton"));
            Assert.False(cancel.Accepted);
            Assert.Null(cancel.Model);
        });
    }

    [Fact]
    public void AListThatCouldNotBeReadGivesCodexsDefaultOnOk()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt(Task.FromResult(new CodexModelReading([], 0, 0, CodexModelProblem.TimedOut)));

            Assert.Equal(1, Named<ComboBox>(prompt, "ModelBox").Items.Count);
            Click(Named<Button>(prompt, "OkButton"));

            Assert.True(prompt.Accepted);
            Assert.Null(prompt.Model);
        });
    }

    [Fact]
    public void OkIsOffWhileTheListIsBeingRead()
    {
        UiThread.Run(() =>
        {
            var prompt = NewPrompt(new TaskCompletionSource<CodexModelReading>().Task);

            Assert.False(Named<Button>(prompt, "OkButton").IsEnabled);
            Assert.False(Named<ComboBox>(prompt, "ModelBox").IsEnabled);
        });
    }
}
