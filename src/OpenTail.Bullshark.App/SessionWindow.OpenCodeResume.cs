using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.OpenCode;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The opencode resume screen (OC3, a copy of the Copilot resume partial, OC-D16). "Resume…" on an opencode row opens <see cref="OpenCodeResumePrompt"/>.
/// In full opens the session's terminal tab (<c>opencode -s &lt;id&gt;</c>, OC-D8). From the last turns, the preview is the transcript tail with the
/// opencode header (no model call while the preview is built), and Start writes it to a start file that a PowerShell 7 script hands to opencode, which
/// submits it at once (OC-D9, one model turn). The summary is off (OC-D19): nothing in this partial makes one. Nothing starts before Start.
/// </summary>
public partial class SessionWindow
{
    /// <summary>True while the opencode resume screen is open or its start runs. A second request is refused with a status line.</summary>
    private bool _openCodeResuming;

    /// <summary>Opens the opencode resume screen for a session (OC3). Nothing starts until the owner presses Start.</summary>
    private void ShowOpenCodeResume(SessionSummary session) =>
        FireAndForget.Observe(ShowOpenCodeResumeAsync(session), "opencode resume screen");

    private async Task ShowOpenCodeResumeAsync(SessionSummary session)
    {
        if (_openCodeResuming)
        {
            SetStatus("An opencode resume is already open or running. Wait for it to finish.");
            return;
        }
        var id = session.Key.SessionId;
        var folder = session.Folder;
        // The list is checked before the screen. The folder is not: a missing folder is shown on the screen, and Start makes it again (ResumeFolderNotice).
        if (!IsOpenCodeListed(id))
        {
            SetStatus(OpenCodeRailText.NotListed);
            return;
        }
        if (!OpenCodeSessionId.IsValid(id))
        {
            SetStatus(OpenCodeRailText.InvalidId);
            return;
        }
        RemoveOldOpenCodeStartsQuietly();

        var title = OpenCodeRowTitle(MarkOf(ProviderId.OpenCode, id)?.Name, session);
        var turns = session.UserMessages is { } count ? $"{count} {(count == 1 ? "turn" : "turns")}" : "turns not counted";
        var facts = $"{turns} · last active {AgeOf(session.LastActivity)} · {FolderFact.For(folder)}";
        var dialog = new OpenCodeResumePrompt($"Resume: {title}", facts, Palette,
            (choice, ct) => BuildOpenCodeStartTextAsync(session, choice, ct), initialMode: null, folder, FolderExists(folder))
        {
            Owner = this,
        };

        _openCodeResuming = true;
        try
        {
            SetStatus("Choose how to continue. Nothing runs until you press Start.");
            dialog.ShowDialog();
            if (dialog.Choice is not { } choice)
            {
                SetStatus("Not resumed.");
                return;
            }
            // The folder is made again first if it is gone, before anything runs in it. A folder that cannot be made refuses.
            if (folder is null)
            {
                SetStatus(OpenCodeRailText.FolderMissing(folder));
                return;
            }
            if (!EnsureSessionFolder(folder, out var folderRefusal))
            {
                SetStatus(folderRefusal!);
                return;
            }
            // The list is checked again: it may have changed while the screen was open.
            if (!IsOpenCodeListed(id))
            {
                SetStatus(OpenCodeRailText.NotListed);
                return;
            }
            switch (choice.Mode)
            {
                case OpenCodeResumeMode.Full:
                    ResumeOpenCodeInFull(id, title, folder);
                    break;
                case OpenCodeResumeMode.LastTurns:
                    StartOpenCodeFromText(dialog.PreviewText, folder, title);
                    break;
                default:
                    // The summary is off (OC-D19, OC-O4): the screen does not offer it, and this refuses it too.
                    SetStatus("Not started: the opencode summary is off until the owner decides where it is written (OC-O4).");
                    return;
            }
            if (choice.Remember) RememberResumeMode(ProviderId.OpenCode, id, OpenCodeResumeRules.ContinueModeOf(choice.Mode));
        }
        finally
        {
            _openCodeResuming = false;
        }
    }

    /// <summary>True when the id is in the app's own list of opencode sessions (the list the rail shows). No silent fallback to another session.</summary>
    private bool IsOpenCodeListed(string id) => _openCodeSessions.Any(listed => SameId(listed.Key.SessionId, id));

    /// <summary>
    /// Deletes the opencode start files that are more than a day old (the Copilot rule). The files live in the opencode folder of the Starts folder beside the
    /// state file (<see cref="OpenCodeStartLauncher.StartsFolderBeside"/>). A file or folder that cannot be read is logged by type, and the screen opens all the same.
    /// </summary>
    private void RemoveOldOpenCodeStartsQuietly()
    {
        try
        {
            var removed = OpenCodeStartLauncher.RemoveOldStarts(
                OpenCodeStartLauncher.StartsFolderBeside(_options.StatePath), TimeSpan.FromDays(1), DateTimeOffset.Now);
            if (removed > 0) AppLog.Write($"old opencode start files removed: {removed}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"old opencode start files not removed: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// In full (OC-D8): the session's terminal tab, in its folder, with <c>opencode -s &lt;id&gt;</c>. An id that does not have the opencode shape is refused
    /// with a status line that does not carry it. A tab that is already open is shown, not started again.
    /// </summary>
    private void ResumeOpenCodeInFull(string id, string title, string folder)
    {
        if (ShowOpenCodeTab(id))
        {
            SetStatus(OpenCodeResumeRules.OpenedLine(title));
            return;
        }
        if (!OpenCodeSessionId.IsValid(id))
        {
            SetStatus(OpenCodeRailText.InvalidId);
            return;
        }
        var command = OpenCodeOpener.ResumeCommandLine(id);
        OpenCodeTab(id, title, command, folder);
        SetStatus(OpenCodeResumeRules.OpenedLine(title));
    }

    /// <summary>
    /// The last-turns start (OC-D9): the preview's text, as shown, is written to a start file in the opencode Starts folder, and a terminal tab runs PowerShell 7
    /// with it in the session's folder. Refused, with a status line and no file written, when PowerShell 7 or <c>opencode.ps1</c> is not on PATH. The status line
    /// after a start says one model turn is made. The log holds no title and no path.
    /// </summary>
    private void StartOpenCodeFromText(string? text, string folder, string title)
    {
        if (text is not { Length: > 0 })
        {
            SetStatus("Not started: there is no start text to send.");
            return;
        }
        if (OpenCodeStartLauncher.FindPwsh() is null)
        {
            SetStatus(OpenCodeResumeRules.NoPwshOnPath);
            return;
        }
        // The start runs opencode.ps1 (the script finds it with Get-Command). The file is not written when that shim is missing.
        if (OpenCodeStartLauncher.FindShim() is null)
        {
            SetStatus(OpenCodeResumeRules.NoShimOnPath);
            return;
        }

        string command;
        try
        {
            var path = OpenCodeStartLauncher.WriteStart(OpenCodeStartLauncher.StartsFolderBeside(_options.StatePath), text, DateTimeOffset.Now);
            command = OpenCodeStartLauncher.CommandLine(folder, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Write($"opencode start file not written: {ex.GetType().Name}");
            SetStatus($"Not started: the start text could not be written ({ex.GetType().Name}).");
            return;
        }

        OpenCodeTab("new-" + Guid.NewGuid().ToString("N"), "opencode: resume of " + title, command, folder);
        SetStatus(OpenCodeResumeRules.StartedLine(title));
    }

    /// <summary>
    /// The last-turns start text of an opencode session (OC-D9), built once for the preview and the start, off the UI thread. The database is opened and
    /// read read-only (OC-D2) by <see cref="OpenCodeTranscriptReader"/>. The last turns are copied word for word (<see cref="ProviderResumeText.Tail"/>, with
    /// the opencode label) under the header of <see cref="ProviderResumeText.Compose"/>, with no session line. No model call is made here. A session with no
    /// messages gives an empty text. A database that cannot be read gives its fixed phrase. A failure is logged by type only and shown in the screen.
    /// The reader gives the first 5,000 lines only (<c>OpenCodeTranscriptReader.cs</c>), so a longer session is refused here rather than given the wrong turns.
    /// The summary is not built in this stage: the Summary radio is off (OC-D19, OC-O4). The summary goes in this method's place (OC3 summary): one
    /// <c>opencode run</c> with the transcript on stdin and an empty scratch <c>--dir</c>, behind its own guard. Nothing here makes one.
    /// </summary>
    private async Task<OpenCodePreview> BuildOpenCodeStartTextAsync(SessionSummary session, OpenCodeResumeChoice choice, CancellationToken ct)
    {
        var sessionId = session.Key.SessionId;
        try
        {
            var location = CurrentOpenCodeLocation();
            // The database is checked first: an empty page from a database that cannot be read is not a session with no messages.
            var (status, probe) = await Task.Run(() => OpenCodeDatabase.Open(location), ct);
            probe?.Dispose();
            if (status != OpenCodeHistoryStatus.Ok)
            {
                AppLog.Write($"opencode start text not built: database status {status}");
                return new OpenCodePreview(string.Empty, OpenCodeHistorySource.TextOf(status));
            }

            var page = await new OpenCodeTranscriptReader(location).ReadPageAsync(sessionId, 0, OpenCodeTranscriptReader.MaxLines, ct);
            if (page.Lines.Count >= OpenCodeTranscriptReader.MaxLines)
            {
                AppLog.Write($"opencode start text refused: {page.Lines.Count} lines is the reader's cap");
                return new OpenCodePreview(string.Empty, OpenCodeResumeRules.LongSessionRefusal);
            }
            var lines = OpenCodeTranscriptLines(page.Lines);
            AppLog.Write($"opencode start text: {lines.Count} messages read");
            if (lines.Count == 0) return new OpenCodePreview(string.Empty);

            var tail = ProviderResumeText.Tail(lines, "opencode", choice.Turns);
            var start = ProviderResumeText.Compose("opencode", summary: null, level: null, tail, choice.Turns);
            AppLog.Write($"opencode start text built: {start.Length:N0} characters");
            return new OpenCodePreview(start);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The log holds the type only: the message can name the database's path.
            AppLog.Write($"opencode start text not built: {ex.GetType().Name}");
            return new OpenCodePreview(string.Empty, $"The start text could not be built ({ex.GetType().Name}).");
        }
    }
}
