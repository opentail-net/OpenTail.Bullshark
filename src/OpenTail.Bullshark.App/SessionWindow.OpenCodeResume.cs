using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.OpenCode;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The opencode resume screen (OC3, a copy of the Copilot resume partial, OC-D16). "Resume…" on an opencode row opens <see cref="OpenCodeResumePrompt"/>.
/// In full opens the session's terminal tab (<c>opencode -s &lt;id&gt;</c>, OC-D8). From the last turns, the preview is the transcript tail with the
/// opencode header (no model call while the preview is built), and Start writes it to a start file that a PowerShell 7 script hands to opencode, which
/// submits it at once (OC-D9, one model turn). A summary (OC-O4, decided 2026-11-23) is one <c>opencode run</c> call, made by the screen's Make summary
/// button, in memory only: the app keeps no summary file. Nothing starts before Start.
/// </summary>
public partial class SessionWindow
{
    /// <summary>True while the opencode resume screen is open or its start runs. A second request is refused with a status line.</summary>
    private bool _openCodeResuming;

    /// <summary>
    /// Opens the opencode resume screen for a session (OC3). It opens on <paramref name="initialMode"/>, or on the last turns when that is null.
    /// Nothing starts until the owner presses Start.
    /// </summary>
    private void ShowOpenCodeResume(SessionSummary session, ContinueMode? initialMode = null) =>
        FireAndForget.Observe(ShowOpenCodeResumeAsync(session, initialMode), "opencode resume screen");

    private async Task ShowOpenCodeResumeAsync(SessionSummary session, ContinueMode? initialMode)
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
        // The messages are read once when the screen opens, for the Make summary check (RS-D26). The screen opens without the check when they cannot be read.
        var lines = await TryReadOpenCodeLinesAsync(session);
        var dialog = new OpenCodeResumePrompt($"Resume: {title}", facts, Palette,
            (choice, ct) => BuildOpenCodeStartTextAsync(session, choice, ct), initialMode: initialMode, folder, FolderExists(folder),
            summaryHasLines: SummaryHasLinesOf(lines, "opencode"), lines: lines?.Count)
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
                case OpenCodeResumeMode.Summary:
                    // Zero (no level) is the tail alone: the start is not a summary's (RS-D22).
                    StartOpenCodeFromText(dialog.PreviewText, folder, title, summary: choice.Level is not null);
                    break;
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
                OpenCodeStartLauncher.StartsFolderBeside(_options.StatePath), OpenCodeStartLauncher.StartFileAge, DateTimeOffset.Now);
            if (removed > 0) AppLog.Write($"old opencode start files removed: {removed}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"old opencode start files not removed: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// The start-up sweep of the opencode scratch folders and of the old start files (OC-D16, plan section 4). It runs from the same point as
    /// <c>SweepCopilotScratch</c>, so only in the window that holds the state's single-instance guard. The scratch folders are swept with the Copilot
    /// startup age (zero: no other instance can be using one); the start files with <see cref="OpenCodeStartLauncher.StartFileAge"/>. Only marked folders
    /// and start-shaped file names are removed (see <c>OpenCodeSummarizer.RemoveOld</c> and <c>CliStartLauncher.RemoveOldStarts</c>). Each sweep logs its
    /// count, or the exception type when it fails, and the window starts anyway.
    /// </summary>
    private static void SweepOpenCodeAtStartup(string statePath)
    {
        try
        {
            var removed = OpenCodeSummarizer.RemoveOld(OpenCodeSummarizer.ParentBeside(statePath), CopilotScratchFolder.StartupAge, DateTimeOffset.UtcNow);
            AppLog.Write($"opencode scratch folders removed at startup: {removed}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"opencode scratch folders not removed at startup: {ex.GetType().Name}");
        }

        try
        {
            var removed = OpenCodeStartLauncher.RemoveOldStarts(
                OpenCodeStartLauncher.StartsFolderBeside(statePath), OpenCodeStartLauncher.StartFileAge, DateTimeOffset.UtcNow);
            AppLog.Write($"opencode start files removed at startup: {removed}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"opencode start files not removed at startup: {ex.GetType().Name}");
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
    /// The last-turns or summary start (OC-D9): the preview's text, as shown, is written to a start file in the opencode Starts folder, and a terminal tab
    /// runs PowerShell 7 with it in the session's folder. Refused, with a status line and no file written, when PowerShell 7 or <c>opencode.ps1</c> is not
    /// on PATH. The status line after a start says one model turn is made. The log holds no title and no path.
    /// </summary>
    private void StartOpenCodeFromText(string? text, string folder, string title, bool summary)
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
        SetStatus(summary ? OpenCodeResumeRules.StartedSummaryLine(title) : OpenCodeResumeRules.StartedLine(title));
    }

    /// <summary>
    /// The start text of an opencode session (OC-D9, OC3 summary), built for the preview and the start, off the UI thread. The database is opened and read
    /// read-only (OC-D2) by <see cref="OpenCodeTranscriptReader"/>. The last turns are copied word for word (<see cref="ProviderResumeText.Tail"/>, with the
    /// opencode label) under the header of <see cref="ProviderResumeText.Compose"/>, with no session line. A summary (OC-O4, decided 2026-11-23) is first made
    /// by one <c>opencode run</c> call (<see cref="OpenCodeSummarizer"/>), and the text is the summary, then the last turns. The summary is kept in memory
    /// only. A session with no messages gives an empty text. A database that cannot be read gives its fixed phrase. A failure is logged by type only and
    /// shown in the screen. The reader gives the first 5,000 lines only (<c>OpenCodeTranscriptReader.cs</c>), so a longer session is refused here rather
    /// than given the wrong turns or a summary of part of it.
    /// </summary>
    private async Task<OpenCodePreview> BuildOpenCodeStartTextAsync(SessionSummary session, OpenCodeResumeChoice choice, CancellationToken ct)
    {
        var sessionId = session.Key.SessionId;
        var summarised = choice.Mode == OpenCodeResumeMode.Summary;
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
                return new OpenCodePreview(
                    string.Empty, summarised ? OpenCodeResumeRules.LongSessionSummaryRefusal : OpenCodeResumeRules.LongSessionRefusal);
            }
            var lines = OpenCodeTranscriptLines(page.Lines);
            AppLog.Write($"opencode start text: {lines.Count} messages read");
            if (lines.Count == 0) return new OpenCodePreview(string.Empty, Lines: lines.Count);

            // Zero (no level) makes no summary. A summary reads only the messages before the tail (RS-D20, RS-D23); the tail is copied from all of them.
            var level = choice.Level;
            string? summary = null;
            if (summarised && level is { } chosen)
            {
                var part = ResumeText.SummaryPart(lines, choice.Turns, ResumeText.TailTokenCap, turn => ProviderResumeText.TurnTokens(turn, "opencode"));
                summary = await MakeOpenCodeSummaryAsync(part, chosen, ct);
            }
            var tail = ProviderResumeText.Tail(lines, "opencode", choice.Turns);
            var start = ProviderResumeText.Compose("opencode", summary, level, tail, choice.Turns);
            AppLog.Write($"opencode start text built: {start.Length:N0} characters");
            var kept = summary is null || level is null ? null : new KeptSummary(level.Value, summary, Saved: false, Turns: choice.Turns, Lines: lines.Count);
            return new OpenCodePreview(start, null, kept, lines.Count);
        }
        catch (OpenCodeSummaryException ex)
        {
            // The summary's message names the reason (a timeout, an exit code, an empty reply): it never carries the transcript or the output.
            AppLog.Write($"opencode summary not made: {ex.GetType().Name}");
            return new OpenCodePreview(string.Empty, $"The summary was not made: {ex.Message}.");
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The log holds the type only: the message can name the database's path.
            AppLog.Write($"opencode start text not built: {ex.GetType().Name}");
            return new OpenCodePreview(string.Empty, $"The start text could not be built ({ex.GetType().Name}).");
        }
    }

    /// <summary>
    /// Makes the summary of the session's messages at <paramref name="level"/> by one <c>opencode run</c> call (<see cref="OpenCodeSummarizer"/>, which runs
    /// <c>cmd.exe</c> with the <c>opencode</c> shim). The summary is returned to the caller and kept in memory only: no file is written. The log holds its
    /// length, not its text. A failure throws <see cref="OpenCodeSummaryException"/>.
    /// </summary>
    private async Task<string> MakeOpenCodeSummaryAsync(IReadOnlyList<TranscriptLine> lines, SummaryLevel level, CancellationToken ct)
    {
        var made = await new OpenCodeSummarizer().SummariseAsync(_options.StatePath, lines, level, ct);
        AppLog.Write($"opencode summary made: {made.Length:N0} characters");
        return made;
    }

    /// <summary>
    /// The session's messages, read when the opencode resume screen opens, for its Make summary check (RS-D26). Null when they cannot be read, or when the
    /// session is longer than the reader's cap (the screen then refuses it at its own read): the check is then not made. Read-only; a failure is logged by type.
    /// </summary>
    private async Task<IReadOnlyList<TranscriptLine>?> TryReadOpenCodeLinesAsync(SessionSummary session)
    {
        try
        {
            var page = await new OpenCodeTranscriptReader(CurrentOpenCodeLocation())
                .ReadPageAsync(session.Key.SessionId, 0, OpenCodeTranscriptReader.MaxLines, CancellationToken.None);
            return page.Lines.Count >= OpenCodeTranscriptReader.MaxLines ? null : OpenCodeTranscriptLines(page.Lines);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"opencode messages not read for the screen: {ex.GetType().Name}");
            return null;
        }
    }
}
