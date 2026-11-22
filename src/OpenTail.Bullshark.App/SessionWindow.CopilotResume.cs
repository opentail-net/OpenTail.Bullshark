using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The Copilot resume screen (step G3.2, copied from the Codex partial, G-D12 and G-D14). "Resume…" on a Copilot row opens
/// <see cref="CopilotResumePrompt"/>, which shows the start text of each way of continuing before anything starts. One method builds that text for the
/// preview and for the start, so the text shown is the text sent. A summary is made only when the owner presses Make summary (one Copilot call, through
/// <see cref="CopilotSummarizer"/>). Start opens a new terminal tab that runs PowerShell 7 with the text in a file (<see cref="CopilotStartLauncher"/>).
/// In full is the full resume, <see cref="ResumeCopilot"/>, which is not changed.
/// </summary>
public partial class SessionWindow
{
    /// <summary>True while the Copilot resume screen is open or its start runs. A second request is refused with a status line.</summary>
    private bool _copilotResuming;

    /// <summary>True while a Copilot summary call runs. Another call is refused with a status line.</summary>
    private bool _copilotSummaryRunning;

    /// <summary>True once the <c>--copilot-resume</c> screenshot has opened the screen, so it opens once.</summary>
    private bool _copilotResumeShotOpened;

    /// <summary>Opens the Copilot resume screen for a session (G3.1). Nothing starts until the owner presses Start.</summary>
    private void ResumeCopilotScreen(SessionSummary session) => ShowCopilotResume(session, forScreenshot: false, shotMode: string.Empty);

    /// <summary>
    /// Opens the screen as a dialog the owner answers, or, for the screenshot option only, shows it without an answer and takes the picture of it
    /// (<c>_shotDialog</c>, as the Codex screen does). With <paramref name="shotMode"/> "summary", the summary mode is chosen and Make summary is pressed
    /// once when the screen has loaded; with "full", In full is chosen. Anything else shows the last turns.
    /// </summary>
    private void ShowCopilotResume(SessionSummary session, bool forScreenshot, string shotMode, ContinueMode? initialMode = null) =>
        FireAndForget.Observe(ShowCopilotResumeAsync(session, forScreenshot, shotMode, initialMode), "Copilot resume screen");

    private async Task ShowCopilotResumeAsync(SessionSummary session, bool forScreenshot, string shotMode, ContinueMode? initialMode = null)
    {
        if (_copilotResuming)
        {
            SetStatus("A Copilot resume is already open or running. Wait for it to finish.");
            return;
        }
        var id = session.Key.SessionId;
        var folder = session.Folder;
        // The list is checked before the screen. The folder is not: a missing folder is shown on the screen, and Start is blocked there (ResumeFolderNotice).
        if (!IsCopilotListed(id))
        {
            SetStatus(CopilotRailText.NotListed);
            return;
        }
        RemoveOldCopilotStartsQuietly();

        var title = CopilotRowTitle(MarkOf(ProviderId.Copilot, id)?.Name, session);
        var turns = session.UserMessages is { } count ? $"{count} {(count == 1 ? "turn" : "turns")}" : "turns not counted";
        var facts = $"{turns} · last active {AgeOf(session.LastActivity)} · {FolderFact.For(folder)}";
        var dialog = new CopilotResumePrompt($"Resume: {title}", facts, Palette,
            (choice, kept, ct) => BuildCopilotStartTextAsync(session, choice, kept, ct, choice.Target), initialMode, folder, FolderExists(folder))
        {
            Owner = this,
        };

        _copilotResuming = true;
        if (forScreenshot)
        {
            _shotDialog = dialog;
            if (shotMode == "summary") dialog.Loaded += (_, _) => dialog.MakeSummaryForScreenshot();
            else if (shotMode == "full") dialog.Loaded += (_, _) => dialog.ChooseFullForScreenshot();
            dialog.Show();
            return;
        }
        try
        {
            SetStatus("Choose how to continue. Nothing runs until you press Start.");
            dialog.ShowDialog();
            if (dialog.Choice is not { } choice)
            {
                SetStatus("Not resumed.");
                return;
            }
            if (choice.Mode == CopilotResumeMode.Full)
            {
                ResumeCopilot(session);
                if (choice.Remember) RememberResumeMode(ProviderId.Copilot, id, RememberChoice.ModeOf(choice.Mode));
                return;
            }
            // The list and the folder are checked again: either may have changed while the screen was open (G-D12).
            var again = CopilotResumeRules.RefusalFor(IsCopilotListed(id), folder, FolderExists(folder));
            if (again is not null || folder is null)
            {
                SetStatus(again ?? CopilotRailText.FolderMissing(folder));
                return;
            }
            if (choice.Target != ProviderId.Copilot)
            {
                // A start in another platform (S3b-B): the cross text the preview showed goes to that platform's start.
                await StartCrossAsync(ProviderId.Copilot, choice.Target, RememberChoice.ModeOf(choice.Mode), folder, dialog.PreviewText, title);
            }
            else
            {
                StartCopilotFromText(choice, dialog.PreviewText, folder, title);
            }
            if (choice.Remember) RememberResumeMode(ProviderId.Copilot, id, RememberChoice.ModeOf(choice.Mode));
        }
        finally
        {
            _copilotResuming = false;
        }
    }

    /// <summary>True when the id is in the app's own list of Copilot sessions (G-D12). The list is the one the rail shows.</summary>
    private bool IsCopilotListed(string id) => _copilotSessions.Any(listed => SameId(listed.Key.SessionId, id));

    /// <summary>True when a folder is on record and exists. A missing or blank folder is not there.</summary>
    private static bool FolderExists(string? folder) => folder is { Length: > 0 } && Directory.Exists(folder);

    /// <summary>
    /// Deletes the Copilot start files that are more than a day old (G-D14, the Codex rule R-D8). A folder or file that cannot be read is logged by type,
    /// and the screen opens all the same.
    /// </summary>
    private void RemoveOldCopilotStartsQuietly()
    {
        try
        {
            var removed = CopilotStartLauncher.RemoveOldStarts(
                CopilotStartLauncher.StartsFolderBeside(_options.StatePath), TimeSpan.FromDays(1), DateTimeOffset.Now);
            if (removed > 0) AppLog.Write($"old copilot start files removed: {removed}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"old copilot start files not removed: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// The start text of a Copilot resume for a choice, built by one method for the preview and for the start. The messages are read off the UI thread.
    /// For a summary, the summary is made (one Copilot call) unless the kept one is for the level; it is saved as a file. The last turns are copied
    /// (<see cref="CopilotResumeText.Tail"/>) and the text is composed (<see cref="CopilotResumeText.Compose"/>). A session with no messages gives an empty
    /// text and makes no call. A failure is logged by type only, shown in the screen, and never escapes this method. A cancellation passes through to the
    /// screen, which is quiet about it. With a <paramref name="target"/> other than Copilot (null is Copilot), the text is the cross-platform start text
    /// (S3b-A): the target's checks run before the summary call (RS-D13), and the summary is the one Copilot makes, with its own guards.
    /// </summary>
    private async Task<CopilotPreview> BuildCopilotStartTextAsync(
        SessionSummary session, CopilotResumeChoice choice, KeptSummary? kept, CancellationToken ct, ProviderId? target = null)
    {
        var provider = _copilot ??= new CopilotProvider();
        var sessionId = session.Key.SessionId;
        var to = target ?? ProviderId.Copilot;
        var folder = session.Folder ?? string.Empty;
        try
        {
            if (to != ProviderId.Copilot)
            {
                var crossRefusal = await CrossPlatformStartText.RefusalBeforeSummaryAsync(
                    ProviderId.Copilot, to, RememberChoice.ModeOf(choice.Mode), () => CrossTargetCheckAsync(ProviderId.Copilot, to, folder, ct));
                if (crossRefusal is not null)
                {
                    AppLog.Write("copilot cross start refused: no summary");
                    return new CopilotPreview(string.Empty, null, crossRefusal);
                }
            }

            // The whole session is read (no cap): the default cap keeps the first messages, and the tail needs the newest turns (as Codex's tail reads all).
            var lines = await Task.Run(() => CopilotTranscriptReader.ReadSession(provider.Paths, sessionId, ct, int.MaxValue), ct);
            AppLog.Write($"copilot start text: {lines.Count} messages read");
            if (lines.Count == 0) return new CopilotPreview(string.Empty, null);

            KeptSummary? summary = null;
            if (choice.Level is { } level)
            {
                if (kept is not null && kept.MatchesLevel(level))
                {
                    summary = kept;
                }
                else
                {
                    // Only the window that holds the single-instance guard sweeps the scratch folders at start-up. A summary made here could have its
                    // folder swept by another window, so it is refused. The kept summary above is not refused.
                    if (_supervisor is null)
                    {
                        SetStatus(CopilotResumeRules.SummaryNeedsGuard);
                        return new CopilotPreview(string.Empty, null, CopilotResumeRules.SummaryNeedsGuard);
                    }
                    if (_copilotSummaryRunning)
                    {
                        SetStatus(CopilotResumeRules.SummaryRunning);
                        return new CopilotPreview(string.Empty, null, CopilotResumeRules.SummaryRunning);
                    }
                    summary = await MakeCopilotSummaryAsync(session, provider, level, ct);
                }
            }

            if (to != ProviderId.Copilot)
            {
                var cross = CrossStartTextFor(ProviderId.Copilot, to, folder, summary, choice.Level, lines, choice.Turns);
                if (!cross.Fits) return new CopilotPreview(string.Empty, null, "Not started: " + CrossPlatformStartText.TooLongMessage);
                AppLog.Write($"copilot cross start text built: {cross.Text.Length:N0} characters");
                return new CopilotPreview(cross.Text, summary);
            }

            var tail = CopilotResumeText.Tail(lines, choice.Turns);
            var start = CopilotResumeText.Compose(summary?.Text, choice.Level, tail, choice.Turns, sessionId);
            AppLog.Write($"copilot start text built: {start.Length:N0} characters");
            return new CopilotPreview(start, summary);
        }
        catch (Exception ex) when (ex is CopilotSummaryException or ArgumentException or FileNotFoundException or IOException
                                       or UnauthorizedAccessException or NotSupportedException)
        {
            // The log holds the type only: the message can name the session's folder or file.
            AppLog.Write($"copilot start text not built: {ex.GetType().Name}");
            var problem = ex is CopilotSummaryException summaryFailure
                ? $"The summary was not made: {summaryFailure.Message}."
                : $"The start text could not be built ({ex.GetType().Name}).";
            SetStatus($"Not started: {problem}");
            return new CopilotPreview(string.Empty, null, problem);
        }
    }

    /// <summary>
    /// Makes one summary by one Copilot call (<see cref="CopilotSummarizer"/>, which runs <c>cmd.exe</c> with the <c>copilot</c> shim), keeps it as a
    /// file, and returns it for the level. Only one such call runs at a time (<see cref="_copilotSummaryRunning"/>). The log holds counts only.
    /// </summary>
    private async Task<KeptSummary> MakeCopilotSummaryAsync(SessionSummary session, CopilotProvider provider, SummaryLevel level, CancellationToken ct)
    {
        _copilotSummaryRunning = true;
        try
        {
            var made = await new CopilotSummarizer().SummariseSessionAsync(provider.Paths, session.Key.SessionId, _options.StatePath, level, ct);
            AppLog.Write($"copilot summary made: {made.Length:N0} characters");
            var saved = await SaveCopilotSummaryFileAsync(session, level, made);
            return new KeptSummary(level, made, Saved: saved);
        }
        finally
        {
            _copilotSummaryRunning = false;
        }
    }

    /// <summary>
    /// Keeps a summary as a file in the Summaries folder beside the state file, as the Codex and Claude summaries are kept (<see cref="SummaryFiles"/>).
    /// The file's name and path are not logged: the session's title can hold the owner's words. A file that cannot be written is logged by type, and the
    /// summary is still used. Returns true when the file was written.
    /// </summary>
    private async Task<bool> SaveCopilotSummaryFileAsync(SessionSummary session, SummaryLevel level, string summary)
    {
        var folder = SummaryFiles.FolderBeside(_options.StatePath);
        var title = CopilotRowTitle(MarkOf(ProviderId.Copilot, session.Key.SessionId)?.Name, session);
        var id = session.Key.SessionId;
        try
        {
            await Task.Run(() => SummaryFiles.Save(folder, title, level, id, summary, DateTimeOffset.Now));
            AppLog.Write($"copilot summary saved as a file: {summary.Length:N0} characters");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"copilot summary not saved as a file: {ex.GetType().Name}");
            return false;
        }
    }

    /// <summary>
    /// Starts the text the preview showed, in a new Copilot terminal tab (G-D14). The text is <paramref name="text"/>, as shown: it is not built again. The
    /// tab runs PowerShell 7 (<c>pwsh.exe</c>) with the text in a file in the Starts folder, in the session's folder. Without PowerShell 7 nothing starts;
    /// the preview's Copy is the way out. A launch that the shim check refuses keeps the file and says so in the tab (the Core script does that); the
    /// status line says where the text is kept.
    /// </summary>
    private void StartCopilotFromText(CopilotResumeChoice choice, string? text, string folder, string title)
    {
        if (text is not { Length: > 0 })
        {
            SetStatus("Not started: there is no start text to send.");
            return;
        }
        if (CopilotStartLauncher.FindPwsh() is null)
        {
            SetStatus("Not started: PowerShell 7 (pwsh.exe) is not on PATH. The start text is in the preview: use Copy and start Copilot yourself.");
            return;
        }
        // The start runs copilot.ps1 (the launcher's script). The terminal's own copilot may be copilot.exe, so the file is not written when the shim is missing.
        if (CopilotStartLauncher.FindShim() is null)
        {
            SetStatus(CopilotResumeRules.NoShimOnPath);
            return;
        }

        string command;
        try
        {
            var path = CopilotStartLauncher.WriteStart(CopilotStartLauncher.StartsFolderBeside(_options.StatePath), text, DateTimeOffset.Now);
            command = CopilotStartLauncher.CommandLine(folder, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Write($"copilot start file not written: {ex.GetType().Name}");
            SetStatus($"Not started: the start text could not be written ({ex.GetType().Name}).");
            return;
        }

        OpenCopilotTab("new-" + Guid.NewGuid().ToString("N"), "Copilot: resume of " + title, command, folder);
        SetStatus(CopilotResumeRules.StartedLine(choice.Level, choice.Turns));
    }

    /// <summary>
    /// The screenshot option (<c>--copilot-resume</c>, test only): once the Copilot history has loaded, the resume screen opens on its first session, and
    /// <c>--shot</c> takes the picture of it. The mode is the option's value: <c>full</c>, <c>summary</c>, or the last turns when none is given. Nothing else
    /// opens it.
    /// </summary>
    private void OpenCopilotResumeForShot()
    {
        if (!_options.CopilotResume || _copilotResumeShotOpened) return;
        _copilotResumeShotOpened = true;
        if (_copilotSessions.Count == 0)
        {
            AppLog.Write("copilot resume screenshot: no Copilot session in the history");
            return;
        }
        ShowCopilotResume(_copilotSessions[0], forScreenshot: true, shotMode: _options.CopilotResumeShot);
    }
}
