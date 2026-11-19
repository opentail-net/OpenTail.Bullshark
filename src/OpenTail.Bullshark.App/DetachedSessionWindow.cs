using System;
using System.Threading.Tasks;
using System.Windows;
using OpenTail.Bullshark.Core.Claude;
using Wpf.Ui.Terminal;

namespace OpenTail.Bullshark.App;

/// <summary>
/// A session in its own window (docs/plans/tabs-and-panes.md, T3). It attaches afresh with claude attach; a terminal is never moved
/// between windows. Closing the window releases its terminal, and the session keeps running. Raises <see cref="Returned"/> once the
/// terminal is released, so the main window can show the session again.
/// </summary>
public sealed class DetachedSessionWindow : Window
{
    private readonly ExtendedTerminalControl _terminal = new();

    /// <summary>The session this window shows.</summary>
    public string SessionId { get; }

    public DetachedSessionWindow(string sessionId, string title, string workDir, double zoomScale, RailPalette palette)
    {
        SessionId = sessionId;
        Title = title;
        Width = 900;
        Height = 560;
        Background = RailPalette.Brush(palette.Background);
        // The caller checks the id first (SessionWindow.PopOut); a bad id never reaches a command line (F4).
        _terminal.StartupCommandLine = ClaudeCli.AttachCommandLineOf(sessionId)
            ?? throw new ArgumentException("The session id is not 8 hex characters, so it cannot be attached.", nameof(sessionId));
        _terminal.WorkingDirectory = workDir;
        SessionWindow.ApplyZoomTo(_terminal, zoomScale);
        Content = _terminal;
        Closing += (_, e) =>
        {
            if (_released) return;
            e.Cancel = true;
            _ = ReleaseThenCloseAsync();
        };
        Closed += (_, _) => Returned?.Invoke();
    }

    /// <summary>Raised after the window has closed and its terminal has been released.</summary>
    public event Action? Returned;

    private bool _released;

    /// <summary>
    /// Closing detaches the terminal the way a pop-out does: Ctrl+Z drops the attach to its shell, and the window waits for that before it
    /// really closes. The session keeps running, and the main window attaches again only after this.
    /// </summary>
    private async Task ReleaseThenCloseAsync()
    {
        if (_terminal.ConPTYTerm is { } connection)
        {
            try
            {
                connection.WriteToTerm("\u001a");
            }
            catch (Exception ex)
            {
                AppLog.Write($"detached window: detach key not sent: {ex.GetType().Name}: {ex.Message}");
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        ReleaseTerminal();
        _released = true;
        Close();
    }

    private void ReleaseTerminal()
    {
        try
        {
            var term = _terminal.DisconnectConPTYTerm();
            term?.Dispose();
        }
        catch (Exception ex)
        {
            AppLog.Write($"detached window: release failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
