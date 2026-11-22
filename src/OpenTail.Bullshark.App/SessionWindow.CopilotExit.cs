using Wpf.Ui.Terminal;
using Wpf.Ui.Terminal.Internals;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The ended state of a Copilot terminal tab (G2.8, G-D20): the same watch as Codex's (<c>SessionWindow.Codex.cs</c>, R6). A tab whose process ends on its
/// own is marked "(ended)" and its status line says "Ended; no exit code was reported." The classification is <see cref="CopilotExit"/> in Core.
/// </summary>
public partial class SessionWindow
{
    /// <summary>
    /// Watches for the start of a Copilot terminal's process. The connection raises <c>TermReady</c> on its start thread once the process object exists,
    /// so the watch reads it there and starts one wait for it. The watch is one-shot: it unsubscribes as it fires.
    /// </summary>
    private void WatchCopilotExit(OpenTab tab, TermPTY term)
    {
        EventHandler? started = null;
        started = (_, _) =>
        {
            term.TermReady -= started;
            // The process is set before TermReady is raised. Null only when the start produced no process: then there is nothing to wait for.
            if (term.Process is not { } process) return;
            FireAndForget.Observe(WaitForCopilotExitAsync(tab, process), "copilot exit wait");
        };
        term.TermReady += started;
    }

    /// <summary>
    /// The one wait for a Copilot process. A pool thread blocks in <c>WaitForExit</c> until the process ends, then the end is handled on the UI thread.
    /// A process that has ended before the wait began is an end, not a failure. A dispatcher that is shutting down takes no more work.
    /// </summary>
    private async Task WaitForCopilotExitAsync(OpenTab tab, IProcess process)
    {
        await Task.Run(() =>
        {
            try
            {
                process.WaitForExit();
            }
            catch (ArgumentException)
            {
                // The process had already ended: nothing left to wait for.
            }
        }).ConfigureAwait(false);
        if (Dispatcher.HasShutdownStarted) return;
        await Dispatcher.InvokeAsync(() => CopilotProcessEnded(tab, process));
    }

    /// <summary>
    /// Handles the end of a Copilot terminal's process on the UI thread. The end is classified and logged with its expected flag and text only. An expected
    /// end changes nothing. Any other end marks the tab ended, but only when the tab is still open and its process is still the one that ended.
    /// </summary>
    private void CopilotProcessEnded(OpenTab tab, IProcess process)
    {
        var record = CopilotExit.Classify(tab.ClosedByOwner, _appShuttingDown, exitCode: null);
        AppLog.Write($"copilot tab process ended: expected {record.Expected}; {record.Text}");
        if (record.Expected) return;
        if (!_tabs.Contains(tab)) return;
        if (!_terminals.TryGetValue(KeyOf(tab), out var terminal) || !ReferenceEquals(terminal.ConPTYTerm?.Process, process)) return;

        tab.Ended = true;
        tab.Title = CopilotExit.EndedTitle(tab.Title);
        RefreshTabStrip();
        SetStatus(record.Text);
    }
}
