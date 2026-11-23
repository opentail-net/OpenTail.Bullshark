using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.OpenCode;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The read-only transcript of a past opencode session (OC2, copied from the Copilot transcript partial, parity P7). "View transcript" on an opencode row's
/// menu opens it as a tab of its own, keyed by the session's id with the opencode provider. The lines are read by
/// <see cref="OpenCodeTranscriptReader.ReadPageAsync"/> off the UI thread, capped at <see cref="OpenCodeTranscriptReader.MaxLines"/> (5,000, OC-D5), and
/// shown by the shared transcript view. Nothing from the transcript is logged: the count and the exception type only.
/// </summary>
public partial class SessionWindow
{
    /// <summary>The opencode transcript views, by the key of their tab. A view leaves this map when its tab closes (<see cref="RemoveOpenCodeTranscript"/>).</summary>
    private readonly Dictionary<SessionKey, ProviderTranscriptView> _openCodeTranscripts = new();

    /// <summary>
    /// Opens the transcript of an opencode session as a tab. A session whose tab is already open is shown, not read again. A new tab shows "Reading the
    /// session…" until the read ends. The tab is not kept across a restart.
    /// </summary>
    private void OpenCodeTranscriptTab(SessionSummary session)
    {
        var id = session.Key.SessionId;
        var title = OpenCodeRowTitle(MarkOf(ProviderId.OpenCode, id)?.Name, session);
        var open = _tabs.FirstOrDefault(t => t.Kind == TabKind.OpenCodeTranscript && t.Provider == ProviderId.OpenCode && SameId(t.Key, id));
        if (open is not null)
        {
            SetActive(open);
            if (_openCodeTranscripts.TryGetValue(KeyOf(open), out var shown)) ShowOnly(shown);
            return;
        }

        var view = new ProviderTranscriptView { Visibility = Visibility.Hidden };
        view.ApplyTheme(Palette);
        view.ShowReading(title);
        var tab = AddTab(TabKind.OpenCodeTranscript, id, title, ProviderId.OpenCode);
        _openCodeTranscripts[KeyOf(tab)] = view;
        Pane.Children.Add(view);
        SetActive(tab);
        ShowOnly(view);
        _ = LoadOpenCodeTranscriptAsync(view, id, title);
    }

    /// <summary>
    /// Reads a session's lines off the UI thread and gives them to its view. A database that cannot be read shows its fixed phrase in the tab and in the
    /// status line, and is not shown as an empty session. A failure of the read is logged by type only.
    /// </summary>
    private async Task LoadOpenCodeTranscriptAsync(ProviderTranscriptView view, string sessionId, string title)
    {
        try
        {
            // The database is checked now, not from the list: an empty page from a database that cannot be read is not a session with no messages.
            // It is opened off the UI thread, as the resume screen does: the open can wait for a lock.
            var location = CurrentOpenCodeLocation();
            var (status, probe) = await Task.Run(() => OpenCodeDatabase.Open(location), CancellationToken.None);
            probe?.Dispose();
            if (status != OpenCodeHistoryStatus.Ok)
            {
                var text = OpenCodeHistorySource.TextOf(status);
                view.ShowProblem(title, text);
                SetStatus(text);
                return;
            }

            var reader = new OpenCodeTranscriptReader(location);
            var page = await reader.ReadPageAsync(sessionId, 0, OpenCodeTranscriptReader.MaxLines, CancellationToken.None);
            var lines = OpenCodeTranscriptLines(page.Lines);
            // The reader does not report the whole count (TotalLines is null), so the view's count is the lines it was given.
            view.ShowMessages(title, ProviderTranscriptText.Capped(lines), lines.Count);
            AppLog.Write($"opencode transcript read: {lines.Count} messages");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Write($"opencode transcript not read: {ex.GetType().Name}");
            view.ShowProblem(title, $"The session could not be read. {ex.Message}");
            SetStatus($"Transcript not read: {ex.GetType().Name}.");
        }
    }

    /// <summary>
    /// The reader's lines as the shared view takes them (<see cref="TranscriptLine"/>). The reader gives each line as "kind: text", where the kind is
    /// <c>you</c> or <c>opencode</c>, which hold no colon, so the first ": " ends the kind. A line with no ": " is all text, with an empty kind. Numbered
    /// from 1, with no time (the reader gives none). Pure.
    /// </summary>
    internal static IReadOnlyList<TranscriptLine> OpenCodeTranscriptLines(IReadOnlyList<string> lines)
    {
        var result = new List<TranscriptLine>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var split = line.IndexOf(": ", StringComparison.Ordinal);
            var kind = split < 0 ? string.Empty : line[..split];
            var text = split < 0 ? line : line[(split + 2)..];
            result.Add(new TranscriptLine(i + 1, kind, null, text, Collapsed: false));
        }
        return result;
    }

    /// <summary>Takes a closed opencode transcript tab's view out of the pane (CloseTab). The view is not shown again.</summary>
    private void RemoveOpenCodeTranscript(SessionKey key)
    {
        if (!_openCodeTranscripts.Remove(key, out var view)) return;
        (view.Parent as Panel)?.Children.Remove(view);
    }
}
