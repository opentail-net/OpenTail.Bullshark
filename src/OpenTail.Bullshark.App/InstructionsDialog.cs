using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenTail.Bullshark.Core.Instructions;

namespace OpenTail.Bullshark.App;

/// <summary>One CLAUDE.md the editor can open: the label on the list and the file's path.</summary>
public sealed record InstructionEntry(string Label, string Path);

/// <summary>
/// The instructions editor. It lists the user's CLAUDE.md and the project files of the folders Claude has been
/// trusted in. Save writes only if the file has not changed on disk since it was opened, keeps the previous version as .bak, and never
/// discards text without asking. Ctrl+S saves.
/// </summary>
public sealed class InstructionsDialog : Window
{
    private readonly RailPalette _palette;
    private readonly IReadOnlyList<InstructionEntry> _entries;
    private readonly ListBox _list = new() { Width = 260, Margin = new Thickness(0, 0, 12, 0) };
    private readonly TextBox _editor = new();
    private readonly TextBlock _path = new() { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _save = new() { Content = "Save", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
    private readonly Button _reload = new() { Content = "Reload from disk", Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 8, 0) };
    private InstructionEntry? _current;
    private int _index = -1;
    private FileStamp? _stamp;
    private string _loadedText = string.Empty;

    /// <summary>The file's text as it was read, or as it was last saved. A save with no edit writes this back exactly (round 5 item 14).</summary>
    private string _fileText = string.Empty;

    /// <summary>The line ending of the open file (LF or CRLF). A save writes its text with this ending, so the file keeps it.</summary>
    private string _ending = "\r\n";
    private bool _dirty;
    private bool _reverting;

    public InstructionsDialog(RailPalette palette, IReadOnlyList<InstructionEntry> entries)
    {
        _palette = palette;
        _entries = entries;
        Title = "Instructions (CLAUDE.md)";
        Width = 920;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = RailPalette.Brush(palette.Background);
        Foreground = RailPalette.Brush(palette.Text);

        var root = new DockPanel { Margin = new Thickness(14) };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var close = new Button { Content = "Close", Padding = new Thickness(14, 4, 14, 4), IsCancel = true };
        _save.Click += (_, _) => Save();
        _reload.Click += (_, _) => Reload();
        close.Click += (_, _) => Close();
        buttons.Children.Add(_save);
        buttons.Children.Add(_reload);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        DockPanel.SetDock(_status, Dock.Bottom);
        root.Children.Add(_status);

        var intro = new TextBlock
        {
            Text = "Claude reads these files at the start of each session: your file applies everywhere, a project's file applies in that project.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = RailPalette.Brush(palette.Muted),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8),
        };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        var body = new DockPanel();
        DockPanel.SetDock(_list, Dock.Left);
        body.Children.Add(_list);
        var editing = new DockPanel();
        DockPanel.SetDock(_path, Dock.Top);
        editing.Children.Add(_path);
        _editor.AcceptsReturn = true;
        _editor.AcceptsTab = true;
        _editor.TextWrapping = TextWrapping.Wrap;
        _editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _editor.FontFamily = new FontFamily("Consolas");
        _editor.FontSize = 13;
        editing.Children.Add(_editor);
        body.Children.Add(editing);
        root.Children.Add(body);
        Content = root;

        foreach (var entry in entries) _list.Items.Add(entry.Label);
        _list.SelectionChanged += (_, _) => { if (!_reverting) SelectEntry(_list.SelectedIndex); };
        _editor.TextChanged += (_, _) => SetDirty(_editor.Text != _loadedText);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Save();
                e.Handled = true;
            }
        };
        Closing += (_, e) =>
        {
            if (_dirty && !ConfirmDiscard("close the editor")) e.Cancel = true;
        };

        if (entries.Count > 0) _list.SelectedIndex = 0;
        else _status.Text = "No CLAUDE.md file to edit yet.";
    }

    private void SelectEntry(int index)
    {
        if (index < 0 || index >= _entries.Count || index == _index) return;
        if (_dirty && !ConfirmDiscard("open another file"))
        {
            RevertSelection();
            return;
        }
        if (!Open(index)) RevertSelection();
    }

    /// <summary>Puts the list selection back on the file that is still open.</summary>
    private void RevertSelection()
    {
        _reverting = true;
        _list.SelectedIndex = _index;
        _reverting = false;
    }

    /// <summary>
    /// Opens a file in the editor. The file is read first: when it cannot be read, nothing changes. The previous file stays current, with its
    /// text, and the error is shown. Only a file that was read becomes the current one, so a save cannot write its text over another file.
    /// </summary>
    private bool Open(int index)
    {
        var entry = _entries[index];
        string text;
        FileStamp? stamp;
        try
        {
            text = InstructionFiles.Read(entry.Path);
            stamp = FileStamp.Of(entry.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = $"Not opened: {ex.Message}";
            return false;
        }

        _index = index;
        _current = entry;
        _stamp = stamp;
        _ending = InstructionFiles.LineEndingOf(text);
        _fileText = text;
        _editor.Text = text;
        // The editor reports its line breaks in its own form (CRLF), so the unchanged text is compared in that form.
        _loadedText = _editor.Text;
        SetDirty(false);
        _path.Text = entry.Path;
        _status.Text = _stamp is null ? "No file yet. Save creates it."
            : InstructionFiles.HasMixedLineEndings(text)
                ? "This file mixes line endings. An edit gives every line break the more common one, " +
                  (_ending == "\n" ? "LF" : "CRLF") + "; a save with no edit keeps the file as it is."
                : string.Empty;
        return true;
    }

    private void SetDirty(bool dirty)
    {
        _dirty = dirty;
        if (_current is null) return;
        _path.Text = _current.Path + (dirty ? "   · unsaved changes" : string.Empty);
    }

    /// <summary>
    /// Saves the text. If the file changed on disk since it was opened, the owner is asked first: saving replaces that change, which is
    /// kept in the .bak file.
    /// </summary>
    private void Save()
    {
        if (_current is null) return;
        var editorText = _editor.Text;
        var text = InstructionFiles.TextToSave(_fileText, editorText);
        var result = InstructionFiles.Save(_current.Path, text, _stamp);
        if (result.Outcome == SaveOutcome.Changed)
        {
            var overwrite = MessageBox.Show(
                this,
                "This file changed on disk after you opened it. Saving now replaces that change; it is kept in the .bak file. Save anyway?",
                "Changed on disk",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (overwrite != MessageBoxResult.Yes)
            {
                _status.Text = "Not saved. The file changed on disk; reload to see its current text.";
                return;
            }
            result = InstructionFiles.Save(_current.Path, text, FileStamp.Of(_current.Path));
        }

        if (result.Outcome != SaveOutcome.Saved)
        {
            _status.Text = "Not saved: " + result.Message;
            return;
        }
        _stamp = result.Stamp;
        _fileText = text;
        _loadedText = editorText;
        SetDirty(_editor.Text != _loadedText);
        _status.Text = "Saved. " + result.Message;
    }

    private void Reload()
    {
        if (_current is null) return;
        if (_dirty && !ConfirmDiscard("reload the file from disk")) return;
        Open(_index);
    }

    private bool ConfirmDiscard(string action) =>
        MessageBox.Show(this, $"You have unsaved changes. Discard them to {action}?", "Unsaved changes",
            MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
