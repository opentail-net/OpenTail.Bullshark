using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenTail.Bullshark.Core.Zoom;
using Wpf.Ui.Terminal;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The zoom of the session text (zoom.md): the transcripts and the terminals. A control in the bottom right, as in Word, changes it with a
/// slider, two buttons and a click on the percentage. Ctrl+plus, Ctrl+minus and Ctrl+0 do the same, and the window reads them even while
/// a terminal has the keyboard (checked 2026-11-16). The zoom is saved in ui.json.
/// </summary>
public partial class SessionWindow
{
    private int _zoomPercent = ZoomLevel.Default;
    private bool _zoomSyncing;

    /// <summary>Reads the saved zoom, applies it, and shows it on the control. Called once, when the window is built.</summary>
    private void InitZoom()
    {
        _zoomPercent = ZoomStore.Load(_options.StatePath);
        ApplyZoomToAll();
        SyncZoomControls();
    }

    /// <summary>Sets the zoom, applies it to the transcripts and the terminals, and saves it. A value between two steps is taken to the nearest step.</summary>
    private void SetZoom(int percent)
    {
        _zoomPercent = ZoomLevel.Nearest(percent);
        ApplyZoomToAll();
        SyncZoomControls();
        ZoomStore.Save(_options.StatePath, _zoomPercent);
    }

    /// <summary>Applies the current zoom to the transcript and to every terminal the window shows.</summary>
    private void ApplyZoomToAll()
    {
        var scale = ZoomLevel.Scale(_zoomPercent);
        _transcript.ApplyZoom(scale);
        foreach (var terminal in AllTerminals()) ApplyZoomTo(terminal, scale);
    }

    /// <summary>Gives a terminal that is being created the current zoom. Called when the terminal is added to the pane.</summary>
    private void ConfigureTerminal(ExtendedTerminalControl terminal) => ApplyZoomTo(terminal, ZoomLevel.Scale(_zoomPercent));

    /// <summary>
    /// Sets a terminal's font size. The library applies the size only when the terminal has a theme (checked 2026-11-16), so the theme is
    /// set with the terminal's own colours (<see cref="TerminalColours"/>). Setting it again applies the new size to a running terminal.
    /// </summary>
    private static void ApplyZoomTo(ExtendedTerminalControl terminal, double scale)
    {
        terminal.FontSizeWhenSettingTheme = (int)Math.Round(TerminalFont.Size * 72.0 / 96.0 * scale);
        terminal.Theme = TerminalColours.Create();
    }

    /// <summary>One step in or out, or back to 100 (Ctrl+0).</summary>
    private void ZoomBy(ZoomKey key)
    {
        switch (key)
        {
            case ZoomKey.In:
                SetZoom(ZoomLevel.Up(_zoomPercent));
                break;
            case ZoomKey.Out:
                SetZoom(ZoomLevel.Down(_zoomPercent));
                break;
            case ZoomKey.Reset:
                SetZoom(ZoomLevel.Default);
                break;
        }
    }

    private void ZoomOut_Click(object sender, MouseButtonEventArgs e) => ZoomBy(ZoomKey.Out);

    private void ZoomIn_Click(object sender, MouseButtonEventArgs e) => ZoomBy(ZoomKey.In);

    private void ZoomPercent_Click(object sender, MouseButtonEventArgs e) => ZoomBy(ZoomKey.Reset);

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_zoomSyncing) return;
        SetZoom(ZoomLevel.AtIndex((int)Math.Round(e.NewValue)));
    }

    /// <summary>Shows the zoom on the control: the slider's position and the percentage. Setting the slider does not change the zoom.</summary>
    private void SyncZoomControls()
    {
        _zoomSyncing = true;
        try
        {
            ZoomSlider.Value = ZoomLevel.IndexOf(_zoomPercent);
            ZoomPercentText.Text = $"{_zoomPercent}%";
        }
        finally
        {
            _zoomSyncing = false;
        }
    }
}
