using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The read-only transcript of a past Copilot session (G2.4, copied from the Codex transcript partial, parity P7). "View transcript" on a Copilot row's menu
/// opens it as a tab of its own (<see cref="TabKind.CopilotTranscript"/>, keyed by the session's id). The messages are read by
/// <see cref="CopilotTranscriptReader.ReadSession"/> off the UI thread, and the view holds at most <see cref="ProviderTranscriptText.MaxMessages"/> of them.
/// Nothing from the transcript is logged: the count and the exception type only.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The Copilot transcript views, by the key of their tab. A view leaves this map when its tab closes (<see cref="RemoveCopilotTranscript"/>).</summary>
    private readonly Dictionary<SessionKey, ProviderTranscriptView> _copilotTranscripts = new();

    /// <summary>
    /// Opens the transcript of a Copilot session as a tab. A session whose tab is already open is shown, not read again. A new tab shows "Reading the
    /// session…" until the read ends. The tab is not kept across a restart.
    /// </summary>
    private void OpenCopilotTranscript(SessionSummary session)
    {
        var id = session.Key.SessionId;
        var title = CopilotRowTitle(MarkOf(ProviderId.Copilot, id)?.Name, session);
        var open = _tabs.FirstOrDefault(t => t.Kind == TabKind.CopilotTranscript && t.Provider == ProviderId.Copilot && SameId(t.Key, id));
        if (open is not null)
        {
            SetActive(open);
            if (_copilotTranscripts.TryGetValue(KeyOf(open), out var shown)) ShowOnly(shown);
            return;
        }

        var view = new ProviderTranscriptView { Visibility = Visibility.Hidden };
        view.ApplyTheme(Palette);
        view.ShowReading(title);
        var tab = AddTab(TabKind.CopilotTranscript, id, title, ProviderId.Copilot);
        _copilotTranscripts[KeyOf(tab)] = view;
        Pane.Children.Add(view);
        SetActive(tab);
        ShowOnly(view);
        _ = LoadCopilotTranscriptAsync(view, id, title);
    }

    /// <summary>
    /// Reads a session's messages off the UI thread and gives them to its view, capped at <see cref="ProviderTranscriptText.MaxMessages"/>. The whole file is
    /// read so the header can give the full count; the view keeps the first messages only. A failure shows its reason in the tab and in the status line, and
    /// is logged by type only.
    /// </summary>
    private async Task LoadCopilotTranscriptAsync(ProviderTranscriptView view, string sessionId, string title)
    {
        var provider = _copilot ??= new CopilotProvider();
        try
        {
            var lines = await Task.Run(() => CopilotTranscriptReader.ReadSession(provider.Paths, sessionId, CancellationToken.None, int.MaxValue));
            view.ShowMessages(title, ProviderTranscriptText.Capped(lines), lines.Count);
            AppLog.Write($"copilot transcript read: {lines.Count} messages");
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Write($"copilot transcript not read: {ex.GetType().Name}");
            view.ShowProblem(title, $"The session could not be read. {ex.Message}");
            SetStatus($"Transcript not read: {ex.GetType().Name}.");
        }
    }

    /// <summary>Takes a closed Copilot transcript tab's view out of the pane (CloseTab). The view is not shown again.</summary>
    private void RemoveCopilotTranscript(SessionKey key)
    {
        if (!_copilotTranscripts.Remove(key, out var view)) return;
        (view.Parent as Panel)?.Children.Remove(view);
    }
}
