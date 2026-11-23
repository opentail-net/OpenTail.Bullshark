using System.IO;
using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The folder notice on the three resume screens, headless. A folder that is missing, or not on record, is shown on the screen and blocks Start; the
/// chooser, the choices and the preview stay usable. A folder that exists shows no notice and Start is as it was. No Start is clicked and no Preview is
/// pressed. The preview callbacks return finished text at once, so no model call is made and no CLI process starts. The folders are temp paths: one
/// that does not exist, and the temp folder itself. No state folder is read or written.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ResumeScreenFolderTests
{
    private const string TailText = "you: an example message\nreply: an example reply";
    private const string Heading = "Resume: an example session";
    private const string Facts = "2 turns · last active 1 day ago · in C:\\Work\\example";

    /// <summary>A folder under the temp folder that does not exist.</summary>
    private static string MissingFolder() => Path.Combine(Path.GetTempPath(), "opentail-missing-" + Guid.NewGuid().ToString("N"));

    private static ResumePrompt NewClaudePrompt(string folder, bool folderExists)
    {
        AppResources.Ensure();
        // A small context: Full is the recommendation, so Start is on at once (a summary waits for Make summary, RS-D21).
        return new ResumePrompt(Heading, Facts, contextTokens: 10_000, RailPalette.DarkPalette, "opus", "high",
            previewAsync: (choice, kept, token) => Task.FromResult(new ResumePromptPreview(new ResumePreview(TailText, 0, true, null))),
            folder: folder, folderExists: folderExists);
    }

    private static CodexResumePrompt NewCodexPrompt(string? folder, bool folderExists)
    {
        AppResources.Ensure();
        return new CodexResumePrompt(Heading, Facts, RailPalette.DarkPalette,
            previewAsync: (choice, kept, token) => Task.FromResult(new CodexPreview(TailText, null)),
            folder: folder, folderExists: folderExists);
    }

    private static CopilotResumePrompt NewCopilotPrompt(string? folder, bool folderExists)
    {
        AppResources.Ensure();
        return new CopilotResumePrompt(Heading, Facts, RailPalette.DarkPalette,
            previewAsync: (choice, kept, token) => Task.FromResult(new CopilotPreview(TailText, null)),
            folder: folder, folderExists: folderExists);
    }

    private static T Named<T>(Window window, string name) where T : class =>
        window.FindName(name) as T ?? throw new InvalidOperationException($"no element named {name}");

    [Fact]
    public void ClaudeAMissingFolderShowsItsNoticeAndStartStaysOn()
    {
        UiThread.Run(() =>
        {
            var folder = MissingFolder();
            var prompt = NewClaudePrompt(folder, folderExists: false);
            var notice = Named<TextBlock>(prompt, "FolderNotice");
            Assert.Equal(Visibility.Visible, notice.Visibility);
            Assert.Equal(ResumeFolderNotice.For(folder, folderExists: false), notice.Text);
            Assert.True(Named<Button>(prompt, "ResumeButton").IsEnabled);
            Assert.True(Named<Button>(prompt, "ResumeMenuButton").IsEnabled);
            Assert.False(Named<MenuItem>(prompt, "CopyItem").IsEnabled);
        });
    }

    [Fact]
    public void ClaudeAnExistingFolderShowsNoNoticeAndStartIsOn()
    {
        UiThread.Run(() =>
        {
            var prompt = NewClaudePrompt(Path.GetTempPath(), folderExists: true);
            Assert.Equal(Visibility.Collapsed, Named<TextBlock>(prompt, "FolderNotice").Visibility);
            Assert.True(Named<Button>(prompt, "ResumeButton").IsEnabled);
        });
    }

    [Fact]
    public void CodexAMissingFolderShowsItsNoticeAndStartStaysOn()
    {
        UiThread.Run(() =>
        {
            var folder = MissingFolder();
            var prompt = NewCodexPrompt(folder, folderExists: false);
            var notice = Named<TextBlock>(prompt, "FolderNotice");
            Assert.Equal(Visibility.Visible, notice.Visibility);
            Assert.Equal(ResumeFolderNotice.For(folder, folderExists: false), notice.Text);
            // The Zero text is built at once, with no press: its text is ready and Copy is on. Start stays on: Start makes the folder again.
            Assert.True(Named<Button>(prompt, "CopyButton").IsEnabled);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
            // The Start menu is for a summary only (RS-D2): on in Summary (Zero included), whatever the folder notice says, and off in Full.
            Assert.True(Named<Button>(prompt, "TargetMenuButton").IsEnabled);
            Named<RadioButton>(prompt, "FullChoice").IsChecked = true;
            Assert.False(Named<Button>(prompt, "TargetMenuButton").IsEnabled);
            Assert.True(Named<RadioButton>(prompt, "SummaryChoice").IsEnabled);
        });
    }

    [Fact]
    public void CodexAnExistingFolderShowsNoNoticeAndStartIsOn()
    {
        UiThread.Run(() =>
        {
            var prompt = NewCodexPrompt(Path.GetTempPath(), folderExists: true);
            Assert.Equal(Visibility.Collapsed, Named<TextBlock>(prompt, "FolderNotice").Visibility);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }

    [Fact]
    public void CodexASessionWithNoFolderOnRecordShowsTheNoticeAndBlocksStart()
    {
        UiThread.Run(() =>
        {
            var prompt = NewCodexPrompt(folder: null, folderExists: false);
            Assert.Equal(ResumeFolderNotice.For(null, folderExists: false), Named<TextBlock>(prompt, "FolderNotice").Text);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }

    [Fact]
    public void CopilotAMissingFolderShowsItsNoticeAndStartStaysOn()
    {
        UiThread.Run(() =>
        {
            var folder = MissingFolder();
            var prompt = NewCopilotPrompt(folder, folderExists: false);
            var notice = Named<TextBlock>(prompt, "FolderNotice");
            Assert.Equal(Visibility.Visible, notice.Visibility);
            Assert.Equal(ResumeFolderNotice.For(folder, folderExists: false), notice.Text);
            Assert.True(Named<Button>(prompt, "CopyButton").IsEnabled);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
            // The Start menu is for a summary only (RS-D2): on in Summary (Zero included), whatever the folder notice says, and off in Full.
            Assert.True(Named<Button>(prompt, "TargetMenuButton").IsEnabled);
            Named<RadioButton>(prompt, "FullChoice").IsChecked = true;
            Assert.False(Named<Button>(prompt, "TargetMenuButton").IsEnabled);
            Assert.True(Named<RadioButton>(prompt, "SummaryChoice").IsEnabled);
        });
    }

    [Fact]
    public void CopilotAnExistingFolderShowsNoNoticeAndStartIsOn()
    {
        UiThread.Run(() =>
        {
            var prompt = NewCopilotPrompt(Path.GetTempPath(), folderExists: true);
            Assert.Equal(Visibility.Collapsed, Named<TextBlock>(prompt, "FolderNotice").Visibility);
            Assert.True(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }

    [Fact]
    public void CopilotASessionWithNoFolderOnRecordShowsTheNoticeAndBlocksStart()
    {
        UiThread.Run(() =>
        {
            var prompt = NewCopilotPrompt(folder: null, folderExists: false);
            Assert.Equal(ResumeFolderNotice.For(null, folderExists: false), Named<TextBlock>(prompt, "FolderNotice").Text);
            Assert.False(Named<Button>(prompt, "StartButton").IsEnabled);
        });
    }
}
