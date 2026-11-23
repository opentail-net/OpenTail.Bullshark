using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The preview pane of a resume screen (the OpenCode dialog plan, O-S1, 2026-11-23): the size line and the text read-only, or one line that says why there
/// is none, and the Copy note. It is the same on every resume screen. It was written out in each screen; moved here without a change to what any of them
/// shows.
/// </summary>
public sealed class ResumePreviewPane
{
    private readonly TextBlock _header;
    private readonly TextBox _box;
    private readonly UIElement _block;
    private readonly TextBlock _note;

    public ResumePreviewPane(TextBlock header, TextBox box, UIElement block, TextBlock note)
    {
        _header = header;
        _box = box;
        _block = block;
        _note = note;
    }

    /// <summary>The text shown, which Start sends. Null when no text is shown.</summary>
    public string? Text { get; private set; }

    /// <summary>Shows a built text: its size line, and the text read-only.</summary>
    public void Show(string text, string sizeLine)
    {
        Text = text;
        _header.Text = sizeLine;
        _header.ClearValue(TextBlock.ForegroundProperty);
        _box.Text = text;
        _box.CaretIndex = 0;
        _box.ScrollToHome();
        _note.Visibility = Visibility.Collapsed;
        _block.Visibility = Visibility.Visible;
    }

    /// <summary>Shows one line in the preview area and no text. A problem is shown in the danger colour. There is then no text for Start.</summary>
    public void ShowMessage(string message, bool problem)
    {
        Text = null;
        _header.Text = message;
        if (problem) _header.SetResourceReference(TextBlock.ForegroundProperty, "UiDanger");
        else _header.ClearValue(TextBlock.ForegroundProperty);
        _box.Text = string.Empty;
        _note.Visibility = Visibility.Collapsed;
        _block.Visibility = Visibility.Visible;
    }

    /// <summary>Takes the preview away. The text is cleared too, so no stale text stays in the dialog.</summary>
    public void Hide()
    {
        Text = null;
        _block.Visibility = Visibility.Collapsed;
        _box.Text = string.Empty;
        _note.Visibility = Visibility.Collapsed;
    }

    /// <summary>The Copy button: puts the shown text on the clipboard. Nothing is sent to a session.</summary>
    public void Copy()
    {
        if (Text is not { } text) return;
        try
        {
            Clipboard.SetText(text);
            _note.Text = "Copied to the clipboard. Nothing was sent to a session.";
            _note.ClearValue(TextBlock.ForegroundProperty);
        }
        catch (ExternalException)
        {
            // Another program holds the clipboard: the owner can press Copy again.
            _note.Text = "The clipboard is busy. Press Copy again.";
            _note.SetResourceReference(TextBlock.ForegroundProperty, "UiDanger");
        }
        _note.Visibility = Visibility.Visible;
    }
}

/// <summary>Enables or disables a control and dims it when it is off, as the resume screens have always done.</summary>
internal static class ResumeScreenElements
{
    public static void Enable(UIElement element, bool enabled)
    {
        element.IsEnabled = enabled;
        element.Opacity = enabled ? 1 : 0.5;
    }
}
