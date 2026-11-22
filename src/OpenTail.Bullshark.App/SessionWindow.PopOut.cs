using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Zoom;

namespace OpenTail.Bullshark.App;

/// <summary>
/// A session tab can open its session in its own window (docs/plans/window.md, T3). The tab releases its terminal and closes first,
/// so a session is attached in one place at a time. When its window closes, the session comes back to the main window.
/// </summary>
public partial class SessionWindow
{
    private readonly List<DetachedSessionWindow> _detached = new();
    private bool _closingWindows;

    private async void PopOut(OpenTab tab)
    {
        // Claude's session tabs only: a Codex terminal is not a Claude session and has no window of its own (C3).
        if (tab.Kind != TabKind.Session || tab.Provider != ProviderId.Claude) return;
        if (string.IsNullOrEmpty(tab.State) || tab.State == "not listed")
        {
            SetStatus("Not running: there is no session to open in its own window.");
            return;
        }

        var sessionId = tab.Key;
        // A session id that is not a valid short id is never attached, so the window is not opened and nothing is closed (F4).
        if (ClaudeCli.AttachCommandLineOf(sessionId) is null)
        {
            SetStatus("Not opened: the session id is not a valid id, so it cannot be attached.");
            return;
        }
        if (_terminals.TryGetValue(KeyOf(tab), out var terminal) && terminal.ConPTYTerm is { } connection)
        {
            // Ctrl+Z drops "claude attach" back to its shell, which detaches it cleanly; the session keeps running. Wait for that before
            // the new window attaches, so the session never has two clients.
            try
            {
                connection.WriteToTerm("\u001a");
            }
            catch (Exception ex)
            {
                AppLog.Write($"pop-out: detach key not sent: {ex.GetType().Name}");
            }
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        DetachTerminal(KeyOf(sessionId));
        CloseTab(tab);
        // The rail must not show this session in the main window again while its own window has it.
        if (_selected is { Kind: RailKind.Session } selected && selected.Key == sessionId) _selected = null;

        var window = new DetachedSessionWindow(sessionId, tab.Title, _options.WorkDir, ZoomLevel.Scale(_zoomPercent), Palette)
        {
            Owner = this,
        };
        window.Returned += () => SessionReturned(window, sessionId);
        _detached.Add(window);
        window.Show();
    }

    private void SessionReturned(DetachedSessionWindow window, string sessionId)
    {
        _detached.Remove(window);
        if (_closingWindows) return;
        _hiddenSessions.Remove(KeyOf(sessionId));
        RefreshRail();
        _ = ShowRailItemAsync(new RailTag(RailKind.Session, sessionId));
    }

    /// <summary>True while a session is shown in its own window. The main window must not attach a second client to it.</summary>
    private bool IsInOwnWindow(string sessionId) => _detached.Any(w => SameId(w.SessionId, sessionId));

    /// <summary>Closes the windows of sessions opened in their own windows. Their terminals are released; the sessions keep running.</summary>
    private void CloseDetachedWindows()
    {
        _closingWindows = true;
        foreach (var window in _detached.ToList()) window.Close();
    }
}
