using System.Windows;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The Continue action (Continue plan, C5): opens the last session of the scope in its resume screen, at the mode the decision gives (CN-D5). It runs
/// from the Continue button beside the provider box and from the palette. It picks the session (<see cref="ContinueDecision"/>), shows its open tab when
/// it has one, and otherwise opens that provider's resume screen. Opening a screen starts nothing and makes no summary (CN-D3), and nothing is typed
/// into a session (CN-D7). Claude's sessions are the rail's digests; Codex's and Copilot's are read again, off the UI thread, by the window's own loaders.
/// </summary>
public partial class SessionWindow
{
    /// <summary>True while Continue looks for its session. A press meanwhile is refused with a status line.</summary>
    private bool _continuing;

    /// <summary>How long one press waits for a Codex or Copilot list before it gives up (S3). The read goes on, and a later press uses its list.</summary>
    private static readonly TimeSpan ContinueListWait = TimeSpan.FromSeconds(30);

    /// <summary>The Continue button beside the provider box.</summary>
    private void ContinueButton_Click(object sender, RoutedEventArgs e) => ContinueLastSession();

    /// <summary>Continue: the last session of the scope, in its resume screen or its open tab. A failure is logged by name, as the other fire-and-forget tasks are.</summary>
    private void ContinueLastSession() => FireAndForget.Observe(ContinueLastSessionAsync(), "continue last session");

    private async Task ContinueLastSessionAsync()
    {
        if (_continuing)
        {
            SetStatus("Continue is already looking for a session. Wait a moment.");
            return;
        }
        _continuing = true;
        try
        {
            await ContinueOnceAsync();
        }
        finally
        {
            _continuing = false;
        }
    }

    /// <summary>One Continue: the scope's sessions, the decision, then the open tab, the list check and the resume screen.</summary>
    private async Task ContinueOnceAsync()
    {
        // Archived sessions are left out by their marks. Without the marks (an unreadable library) an archived session could be picked, so nothing is.
        if (_libraryError is not null)
        {
            SetStatus("Not continued: the library cannot be read, so archived sessions cannot be left out. Nothing opened.");
            return;
        }

        var settings = _behaviour.Continue;
        var providers = ContinueProvidersOf(settings.Scope);
        var history = _history;
        if (providers.Contains(ProviderId.Claude) && history is null)
        {
            SetStatus("Claude's history is still loading. Try Continue again in a moment.");
            return;
        }

        var sessions = new List<SessionSummary>();
        foreach (var scopeProvider in providers)
        {
            if (scopeProvider == ProviderId.Claude)
            {
                sessions.AddRange(history!.Select(ClaudeProvider.SummaryOf));
                continue;
            }
            var listed = await ContinueListAsync(scopeProvider);
            if (listed.TimedOut)
            {
                SetStatus($"The {ContinueDecision.NameOf(scopeProvider)} history is taking too long. Try Continue again in a moment.");
                return;
            }
            if (listed.Sessions is null)
            {
                SetStatus($"Not continued: the {ContinueDecision.NameOf(scopeProvider)} history could not be read. Nothing opened.");
                return;
            }
            sessions.AddRange(listed.Sessions);
        }

        var decision = ContinueDecision.Decide(settings, _activeProvider.Id, sessions, ContinueMarks());
        var picked = decision.Session;
        // The log holds counts, provider names and the mode name only: no id, title or folder.
        var modeName = decision.Mode is { } mode ? mode.ToString() : "screen default";
        AppLog.Write($"continue: {sessions.Count} sessions in scope {ContinueScopeText.ToText(settings.Scope)}, " +
                     (picked is null ? "none to continue" : $"one chosen from {ContinueDecision.NameOf(picked.Key.Provider)}") +
                     $", mode {modeName}");
        if (picked is null)
        {
            SetStatus("Nothing to continue in this scope.");
            return;
        }

        var provider = picked.Key.Provider;
        var id = picked.Key.SessionId;
        var digest = provider == ProviderId.Claude ? history?.FirstOrDefault(d => SameId(d.SessionId, id)) : null;
        if (provider == ProviderId.Claude && digest is null)
        {
            SetStatus("Not continued: the session is not in the history any more. Refresh the rail, then try again.");
            return;
        }
        var title = digest is not null ? DisplayTitle(digest)
            : provider == ProviderId.Codex ? CodexRowTitle(MarkOf(ProviderId.Codex, id)?.Name, picked)
            : provider == ProviderId.OpenCode ? OpenCodeRowTitle(MarkOf(ProviderId.OpenCode, id)?.Name, picked)
            : CopilotRowTitle(MarkOf(ProviderId.Copilot, id)?.Name, picked);

        // A session whose tab is open is shown, never started a second time (the rule of a resume).
        if (ShowOpenContinueTab(provider, id))
        {
            SetStatus($"Continue: showed the open tab of {title} ({ContinueDecision.NameOf(provider)}).");
            return;
        }

        // The refusal is the one the screen would give for its list, so no screen is opened for a session it would refuse. A missing folder is not refused
        // here: the screen opens and shows the folder notice, which blocks Start.
        var refusal = ContinueScreenRefusal(picked);
        if (refusal is not null)
        {
            SetStatus(refusal);
            return;
        }

        // The screen sets the status line at once as it opens (its first line), which replaces this one. The title and the mode then show in the
        // screen's heading and its selected radio. The line is set here so it shows wherever a screen does not write its own first.
        SetStatus(ContinueDecision.PickedText(title, provider, decision.Mode));
        if (digest is not null) _ = ResumeAsync(digest, decision.Mode);
        else if (provider == ProviderId.Codex) ShowCodexResume(picked, forScreenshot: false, screenshotSummary: false, initialMode: decision.Mode);
        else if (provider == ProviderId.Copilot) ShowCopilotResume(picked, forScreenshot: false, shotMode: string.Empty, initialMode: decision.Mode);
        // The opencode pick opens its own screen at the decided mode, as the others do.
        else if (provider == ProviderId.OpenCode) ShowOpenCodeResume(picked, initialMode: decision.Mode);
    }

    /// <summary>The providers whose sessions the scope holds: the active provider, or every provider (CN-D1), opencode included. The list is <see cref="ContinueDecision.ProvidersOf"/>.</summary>
    private IReadOnlyList<ProviderId> ContinueProvidersOf(ContinueScope scope) =>
        ContinueDecision.ProvidersOf(scope, _activeProvider.Id, CodexProvider.Available);

    /// <summary>
    /// The listed sessions of Codex or Copilot, read again by the window's own loader: a fresh read off the UI thread, which also keeps the rail's list
    /// current (the Copilot screen checks that list). A read already running is awaited, not skipped, so the list is the one it gives (S1). The wait is
    /// <see cref="ContinueListWait"/> (S3): past it, TimedOut is true and the read goes on. Sessions is null when the read did not give a list.
    /// </summary>
    private async Task<(bool TimedOut, IReadOnlyList<SessionSummary>? Sessions)> ContinueListAsync(ProviderId provider)
    {
        var load = provider == ProviderId.Codex ? LoadCodexHistoryAsync()
            : provider == ProviderId.OpenCode ? LoadOpenCodeHistoryAsync()
            : LoadCopilotHistoryAsync();
        if (await Task.WhenAny(load, Task.Delay(ContinueListWait)) != load) return (true, null);
        await load;
        if (provider == ProviderId.Codex) return (false, _codexRead && !_codexFailed ? _codexSessions : null);
        if (provider == ProviderId.OpenCode) return (false, _openCodeRead && !_openCodeFailed ? _openCodeSessions : null);
        return (false, _copilotRead && !_copilotFailed ? _copilotSessions : null);
    }

    /// <summary>
    /// The marks of every provider, keyed by <c>provider:id</c> (the chooser's key). They are the window's views of its last library read, so a session's
    /// mark is found under its own provider, never under another provider's entry with the same id.
    /// </summary>
    private Dictionary<string, LibraryEntry> ContinueMarks()
    {
        var keyed = new Dictionary<string, LibraryEntry>(StringComparer.Ordinal);
        foreach (var provider in new[] { ProviderId.Claude, ProviderId.Codex, ProviderId.Copilot, ProviderId.OpenCode })
        {
            foreach (var pair in MarksOf(provider)) keyed[new SessionKey(provider, pair.Key).ToString()] = pair.Value;
        }
        return keyed;
    }

    /// <summary>
    /// The refusal that Continue gives before the session's resume screen opens, or null when the screen may open (<see cref="ContinueDecision.ScreenRefusal"/>).
    /// Only a Copilot session that is not in the Copilot list is refused, with the screen's own text. A missing folder, or one that is not on record, is not
    /// refused here: the screen opens and shows the folder notice, which blocks Start. Each screen's Start checks its folder again.
    /// </summary>
    private string? ContinueScreenRefusal(SessionSummary session) =>
        ContinueScreenRefusalOf(session.Key.Provider, IsCopilotListed(session.Key.SessionId), CopilotRailText.NotListed);

    /// <summary>
    /// The refusal of a Continue pick, by its provider. An opencode session is not refused: its resume screen opens at the decided mode. Every other
    /// provider keeps <see cref="ContinueDecision.ScreenRefusal"/>. Pure.
    /// </summary>
    internal static string? ContinueScreenRefusalOf(ProviderId provider, bool copilotListed, string copilotNotListed) =>
        ContinueDecision.ScreenRefusal(provider, copilotListed, copilotNotListed);

    /// <summary>Shows the open tab of a session of the given provider. True when there was one to show.</summary>
    private bool ShowOpenContinueTab(ProviderId provider, string id) =>
        provider == ProviderId.Claude ? ShowOpenClaudeTab(id)
        : provider == ProviderId.Codex ? ShowOpenCodexTab(id)
        : provider == ProviderId.OpenCode ? ShowOpenCodeTab(id)
        : ShowOpenCopilotTab(id);

    /// <summary>Shows the terminal tab of a Claude session that is open in this window, as a rail click does. True when there was one to show.</summary>
    private bool ShowOpenClaudeTab(string id)
    {
        var open = _tabs.FirstOrDefault(t => t.Kind == TabKind.Session && t.Provider == ProviderId.Claude && SameId(t.Key, id));
        if (open is null) return false;
        ActivateTab(open);
        return true;
    }
}
