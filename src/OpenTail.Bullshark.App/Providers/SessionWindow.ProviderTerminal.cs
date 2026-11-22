using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Providers;
using Wpf.Ui.Terminal;
using Wpf.Ui.Terminal.Internals;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The parts of a provider's terminal tab that Codex and Copilot share (R7): the tab open, the watch for the end of the tab's process, and the builder of a
/// row's menu. Each provider's partial keeps its own actions, labels and status lines, and passes them in.
/// </summary>
public partial class SessionWindow
{
    /// <summary>
    /// Opens a provider's terminal tab: a terminal that runs <paramref name="command"/> in <paramref name="folder"/>, owned by this window. Its key is
    /// <paramref name="id"/>. The exit watch is in place before the control is shown, as its process starts when the control loads (R6). The caller sets
    /// the status line.
    /// </summary>
    private OpenTab OpenProviderTab(ProviderTerminalRules rules, string id, string title, string command, string folder)
    {
        var terminal = new ExtendedTerminalControl
        {
            StartupCommandLine = command,
            WorkingDirectory = folder,
            Visibility = Visibility.Hidden,
        };
        _terminals[new SessionKey(rules.Provider, id)] = terminal;
        var tab = AddTab(TabKind.Session, id, title, rules.Provider);
        // Watched before the control is shown: its process starts when the control loads, so the watch must be in place by then (R6).
        if (terminal.ConPTYTerm is { } term) WatchProviderExit(tab, term, rules);
        Pane.Children.Add(terminal);
        ConfigureTerminal(terminal);
        SetActive(tab);
        ShowOnly(terminal);
        return tab;
    }

    /// <summary>
    /// Watches for the start of a provider terminal's process (R6). The connection raises <c>TermReady</c> on its start thread once the process object
    /// exists, so the watch reads it there and starts one wait for it. The watch is one-shot: it unsubscribes as it fires, so a process gets one wait.
    /// </summary>
    private void WatchProviderExit(OpenTab tab, TermPTY term, ProviderTerminalRules rules)
    {
        EventHandler? started = null;
        started = (_, _) =>
        {
            term.TermReady -= started;
            // The process is set before TermReady is raised. Null only when the start produced no process: then there is nothing to wait for.
            if (term.Process is not { } process) return;
            FireAndForget.Observe(WaitForProviderExitAsync(tab, process, rules), $"{rules.LogName} exit wait");
        };
        term.TermReady += started;
    }

    /// <summary>
    /// The one wait for a provider's process (R6). A pool thread blocks in <c>WaitForExit</c> until the process ends, then the end is handled on the UI
    /// thread. A failed wait is logged by <see cref="FireAndForget"/>. A dispatcher that is shutting down takes no more work, as the app is closing.
    /// </summary>
    private async Task WaitForProviderExitAsync(OpenTab tab, IProcess process, ProviderTerminalRules rules)
    {
        // A process that has ended before this wait began can make WaitForExit throw (its id is gone). That is an end, not a failure, so it is handled as one.
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
        await Dispatcher.InvokeAsync(() => ProviderProcessEnded(tab, process, rules));
    }

    /// <summary>
    /// Handles the end of a provider terminal's process on the UI thread (R6). The end is classified, and logged with its expected flag and text only.
    /// An expected end changes nothing. Any other end marks the tab ended, but only when the tab is still open and its process is still the one that
    /// ended, so an old end never changes a newer tab. Nothing is retried, and the tab stays open.
    /// </summary>
    private void ProviderProcessEnded(OpenTab tab, IProcess process, ProviderTerminalRules rules)
    {
        var record = rules.Classify(tab.ClosedByOwner, _appShuttingDown, null);
        // The resume step is set only on a Codex resume tab (Copilot's tabs never set it), so Copilot's line has no step.
        var step = tab.LadderStep is { } ladder ? $"resume step {ladder}; " : string.Empty;
        AppLog.Write($"{rules.LogName} tab process ended: {step}expected {record.Expected}; {record.Text}");
        if (record.Expected) return;
        if (!_tabs.Contains(tab)) return;
        if (!_terminals.TryGetValue(KeyOf(tab), out var terminal) || !ReferenceEquals(terminal.ConPTYTerm?.Process, process)) return;

        tab.Ended = true;
        tab.Title = rules.EndedTitle(tab.Title);
        RefreshTabStrip();
        SetStatus(record.Text);
    }

    /// <summary>
    /// The right-click menu of a provider's row: the items in the order given, each with its header and its action (R7). A partial passes its own items,
    /// labels and order, so the menu's shape is shared and its content is not.
    /// </summary>
    private ContextMenu ProviderRowMenu(params (string Header, Action Action)[] items)
    {
        var menu = StyledMenu();
        foreach (var (header, action) in items) menu.Items.Add(MenuItemFor(header, action));
        return menu;
    }
}
