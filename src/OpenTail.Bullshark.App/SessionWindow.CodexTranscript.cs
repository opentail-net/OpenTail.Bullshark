using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The read-only transcript of a past Codex session (parity P7, decisions T-D1 to T-D5). "View transcript" on a Codex row's menu opens it as a tab of its own
/// (<see cref="TabKind.CodexTranscript"/>, keyed by the session's id). The messages are read by <see cref="CodexTurns.ReadSession"/> off the UI thread, and
/// the view holds at most <see cref="CodexTranscriptText.MaxMessages"/> of them. Nothing from the transcript is logged: the count and the exception type only.
/// Claude's transcript tab is not used and not changed.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The Codex transcript views, by the key of their tab. A view leaves this map when its tab closes (<see cref="RemoveCodexTranscript"/>).</summary>
    private readonly Dictionary<SessionKey, CodexTranscriptView> _codexTranscripts = new();

    /// <summary>True once the <c>--codex-transcript</c> screenshot has opened a transcript, so it opens once.</summary>
    private bool _codexTranscriptShotOpened;

    /// <summary>
    /// Opens the transcript of a Codex session as a tab (T-D3). A session whose tab is already open is shown, not read again. A new tab shows
    /// "Reading the session…" until the read ends (T-D4). The tab is not kept across a restart.
    /// </summary>
    private void OpenCodexTranscript(SessionSummary session)
    {
        var id = session.Key.SessionId;
        var title = CodexRowTitle(MarkOf(ProviderId.Codex, id)?.Name, session);
        var open = _tabs.FirstOrDefault(t => t.Kind == TabKind.CodexTranscript && t.Provider == ProviderId.Codex && SameId(t.Key, id));
        if (open is not null)
        {
            SetActive(open);
            if (_codexTranscripts.TryGetValue(KeyOf(open), out var shown)) ShowOnly(shown);
            return;
        }

        var view = new CodexTranscriptView { Visibility = Visibility.Hidden };
        view.ApplyTheme(Palette);
        view.ShowReading(title);
        var tab = AddTab(TabKind.CodexTranscript, id, title, ProviderId.Codex);
        _codexTranscripts[KeyOf(tab)] = view;
        Pane.Children.Add(view);
        SetActive(tab);
        ShowOnly(view);
        _ = LoadCodexTranscriptAsync(view, id, title);
    }

    /// <summary>
    /// Reads a session's messages off the UI thread and gives them to its view, capped (T-D5). A failure shows its reason in the tab and in the status
    /// line, and is logged by type only. A cancellation is not expected here: the read is not cancelled when the tab closes, and its result is then not shown.
    /// </summary>
    private async Task LoadCodexTranscriptAsync(CodexTranscriptView view, string sessionId, string title)
    {
        var provider = _codex ??= new CodexProvider();
        try
        {
            var lines = await Task.Run(() => CodexTurns.ReadSession(provider.Paths, sessionId, CancellationToken.None));
            view.ShowMessages(title, CodexTranscriptText.Capped(lines), lines.Count);
            AppLog.Write($"codex transcript read: {lines.Count} messages");
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Write($"codex transcript not read: {ex.GetType().Name}");
            view.ShowProblem(title, $"The session could not be read. {ex.Message}");
            SetStatus($"Transcript not read: {ex.GetType().Name}.");
        }
    }

    /// <summary>Takes a closed Codex transcript tab's view out of the pane (CloseTab). The view is not shown again.</summary>
    private void RemoveCodexTranscript(SessionKey key)
    {
        if (!_codexTranscripts.Remove(key, out var view)) return;
        (view.Parent as Panel)?.Children.Remove(view);
    }

    /// <summary>
    /// The screenshot option (<c>--codex-transcript</c>, test only): once the Codex history has loaded, the transcript of its first session opens as a tab,
    /// for the screenshots. Nothing else opens it.
    /// </summary>
    private void OpenCodexTranscriptForShot()
    {
        if (!_options.CodexTranscript || _codexTranscriptShotOpened) return;
        _codexTranscriptShotOpened = true;
        if (_codexSessions.Count == 0)
        {
            AppLog.Write("codex transcript screenshot: no Codex session in the history");
            return;
        }
        OpenCodexTranscript(_codexSessions[0]);
    }
}
