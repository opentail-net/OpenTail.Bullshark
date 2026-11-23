using System.Windows;
using System.Windows.Controls;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The preview pane shared by the resume screens (the OpenCode dialog plan, O-S1 and O-S2, 2026-11-23). Each test builds the four controls by hand,
/// on the WPF thread, so the pane is checked without a window.
/// </summary>
public sealed class ResumePreviewPaneTests
{
    private static (ResumePreviewPane Pane, TextBlock Header, TextBox Box, Border Block, TextBlock Note) Make()
    {
        var header = new TextBlock();
        var box = new TextBox();
        var block = new Border { Visibility = Visibility.Collapsed };
        var note = new TextBlock { Visibility = Visibility.Collapsed };
        return (new ResumePreviewPane(header, box, block, note), header, box, block, note);
    }

    [Fact]
    public void ShowPutsTheTextAndItsSizeLineOnScreenAndTheTextIsWhatStartSends()
    {
        UiThread.Run(() =>
        {
            var (pane, header, box, block, note) = Make();

            pane.Show("an example text", "About 4 tokens (16 characters). Start sends exactly this text.");

            Assert.Equal("an example text", pane.Text);
            Assert.Equal("an example text", box.Text);
            Assert.Equal("About 4 tokens (16 characters). Start sends exactly this text.", header.Text);
            Assert.Equal(Visibility.Visible, block.Visibility);
            Assert.Equal(Visibility.Collapsed, note.Visibility);
        });
    }

    [Fact]
    public void ShowMessageGivesNoTextForStart()
    {
        UiThread.Run(() =>
        {
            var (pane, header, box, block, _) = Make();
            pane.Show("some text", "size");

            pane.ShowMessage("Reading the session. Nothing is sent to a session.", problem: false);

            Assert.Null(pane.Text);
            Assert.Equal(string.Empty, box.Text);
            Assert.Equal("Reading the session. Nothing is sent to a session.", header.Text);
            Assert.Equal(Visibility.Visible, block.Visibility);
        });
    }

    [Fact]
    public void HideTakesThePreviewAwayAndNoStaleTextStays()
    {
        UiThread.Run(() =>
        {
            var (pane, _, box, block, _) = Make();
            pane.Show("some text", "size");

            pane.Hide();

            Assert.Null(pane.Text);
            Assert.Equal(string.Empty, box.Text);
            Assert.Equal(Visibility.Collapsed, block.Visibility);
        });
    }

    [Fact]
    public void CopyWithNoTextDoesNothing()
    {
        UiThread.Run(() =>
        {
            var (pane, _, _, _, note) = Make();

            pane.Copy();

            Assert.Equal(Visibility.Collapsed, note.Visibility);
        });
    }

    [Fact]
    public void CopyWithTextSaysWhatHappenedAndSendsNothing()
    {
        UiThread.Run(() =>
        {
            var (pane, _, _, _, note) = Make();
            pane.Show("copy me", "size");

            pane.Copy();

            // The clipboard can be held by another program: then the note says so, and the owner can press Copy again.
            Assert.Equal(Visibility.Visible, note.Visibility);
            Assert.Contains(note.Text, new[] { "Copied to the clipboard. Nothing was sent to a session.", "The clipboard is busy. Press Copy again." });
        });
    }
}
