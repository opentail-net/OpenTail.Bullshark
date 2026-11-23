using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The Codex resume screen (steps R6 and R7, decisions R-D1 to R-D10). "Resume…" on a Codex row opens <see cref="CodexResumePrompt"/>, which shows the
/// start text of each way of continuing before anything starts. One method builds that text for the preview and for the start, so the text shown is
/// the text sent. A summary is made only when the owner presses Make summary (one <c>codex exec</c> call, R-D3). Start opens a new terminal tab that
/// runs PowerShell 7 with the text in a file (R3). In full is the existing resume, <see cref="ResumeCodex"/>, which is not changed.
/// </summary>
public partial class SessionWindow
{
    /// <summary>True while the Codex resume screen is open or its start runs. A second request is refused with a status line, as <c>_resuming</c> is.</summary>
    private bool _codexResuming;

    /// <summary>True once the <c>--codex-resume</c> screenshot has opened the screen, so it opens once.</summary>
    private bool _codexResumeShotOpened;

    /// <summary>
    /// The models the account is offered (P9, M-D1): read once per window run, on first use, and kept. The read is off the UI thread and never fails
    /// (a problem is in the reading); the same reading serves the resume screen and the new-session dialog.
    /// </summary>
    private Task<CodexModelReading>? _codexModels;

    /// <summary>The model list of this window run (see <see cref="_codexModels"/>): started on the first call, and the same task after that.</summary>
    private Task<CodexModelReading> CodexModels() => _codexModels ??= ReadCodexModelsAsync();

    /// <summary>
    /// Reads the model list through <c>codex app-server</c> off the UI thread, and logs its counts and problem type only (never the ids or the reply).
    /// </summary>
    private async Task<CodexModelReading> ReadCodexModelsAsync()
    {
        var reading = await Task.Run(() => new CodexModelList().ReadAsync(CancellationToken.None));
        AppLog.Write(
            $"codex model list read: {reading.Models.Count} models, {reading.Hidden} hidden, {reading.Dropped} dropped, problem {reading.Problem}");
        return reading;
    }

    /// <summary>
    /// Opens the Codex resume screen for a session (R6). Nothing starts until the owner presses Start. A session whose folder is missing opens the screen
    /// with the folder notice: Start (and In full) is blocked there, and the previews stay on. The full resume (<see cref="ResumeCodex"/>) keeps its own behaviour.
    /// </summary>
    private void ResumeCodexScreen(SessionSummary session) => ShowCodexResume(session, forScreenshot: false, screenshotSummary: false);

    /// <summary>
    /// Opens the screen as a dialog the owner answers, or, for the screenshot option only, shows it without an answer and takes the picture of it
    /// (<c>_shotDialog</c>, as the shortcut sheet does). With <paramref name="screenshotSummary"/>, the summary mode is chosen and Make summary is
    /// pressed once when the screen has loaded.
    /// </summary>
    private void ShowCodexResume(SessionSummary session, bool forScreenshot, bool screenshotSummary, ContinueMode? initialMode = null) =>
        FireAndForget.Observe(ShowCodexResumeAsync(session, forScreenshot, screenshotSummary, initialMode), "Codex resume screen");

    private async Task ShowCodexResumeAsync(SessionSummary session, bool forScreenshot, bool screenshotSummary, ContinueMode? initialMode = null)
    {
        if (_codexResuming)
        {
            SetStatus("A Codex resume is already open or running. Wait for it to finish.");
            return;
        }
        // A missing folder does not refuse here: the screen opens and shows the folder notice (ResumeFolderNotice), which blocks Start.
        var folder = session.Folder ?? string.Empty;
        RemoveOldStartsQuietly();

        var title = CodexRowTitle(MarkOf(ProviderId.Codex, session.Key.SessionId)?.Name, session);
        var turns = session.UserMessages is { } count ? $"{count} {(count == 1 ? "turn" : "turns")}" : "turns not counted";
        var facts = $"{turns} · last active {AgeOf(session.LastActivity)} · {FolderFact.For(folder)}";
        // The messages are read once when the screen opens, for the Make summary check (RS-D26). The screen opens without the check when they cannot be read.
        var provider = _codex ??= new CodexProvider();
        var lines = await TryReadLinesAsync(() => CodexTurns.ReadSession(provider.Paths, session.Key.SessionId, CancellationToken.None));
        var dialog = new CodexResumePrompt($"Resume: {title}", facts, Palette,
            (choice, kept, writeByTarget, ct) => BuildCodexStartTextAsync(session, folder, choice, kept, writeByTarget, ct, choice.Target), CodexModels(), initialMode,
            folder, FolderExists(folder), summaryHasLines: SummaryHasLinesOf(lines, "codex"), lines: lines?.Count)
        {
            Owner = this,
        };

        _codexResuming = true;
        if (forScreenshot)
        {
            _shotDialog = dialog;
            if (screenshotSummary) dialog.Loaded += (_, _) => dialog.MakeSummaryForScreenshot();
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
            // The folder is made again first if it is gone, before anything runs in it (owner, 2026-11-22). The full resume would otherwise fall back
            // to the app's own folder, so a folder that cannot be made refuses.
            if (!EnsureSessionFolder(folder, out var folderRefusal))
            {
                SetStatus(folderRefusal!);
                return;
            }
            if (choice.Mode == CodexResumeMode.Full)
            {
                await ResumeCodexAsync(session);
            }
            else if (choice.Target != ProviderId.Codex)
            {
                // A start in another platform (S3b-B): the cross text the preview showed goes to that platform's start.
                await StartCrossAsync(ProviderId.Codex, choice.Target, RememberChoice.ModeOf(choice.Mode), folder, dialog.PreviewText, title);
            }
            else
            {
                await StartCodexFromTextAsync(session, folder, choice, dialog.PreviewText);
            }
            if (choice.Remember) RememberResumeMode(ProviderId.Codex, session.Key.SessionId, RememberChoice.ModeOf(choice.Mode));
        }
        finally
        {
            _codexResuming = false;
        }
    }

    /// <summary>
    /// Deletes the start files that are more than a day old (R-D8, R3). A folder or file that cannot be read is logged, and the screen opens all the same.
    /// </summary>
    private void RemoveOldStartsQuietly() => ResumeStartFiles.RemoveOldStartsQuietly(
        _options.StatePath, "codex", CodexStartLauncher.StartsFolderBeside,
        folder => CodexStartLauncher.RemoveOldStarts(folder, TimeSpan.FromDays(1), DateTimeOffset.Now));

    /// <summary>
    /// The start text of a Codex resume for a choice, built by one method for the preview and for the start. The messages are read off the UI thread.
    /// For a summary, the summary is made (one Codex call) unless the kept one is for the level; it is saved as a file. The last turns are copied
    /// (<see cref="CodexResumeText.Tail"/>) and the text is composed (<see cref="CodexResumeText.Compose"/>). A session with no messages gives an empty
    /// text and makes no call. The launch check comes first (R4c): a refused launch gives the refusal as the preview, with no message read and no summary
    /// call, because the start would be refused too. A failure is logged by type and message only, shown in the screen, and never escapes this method.
    /// A cancellation passes through to the screen, which is quiet about it.
    /// With a <paramref name="target"/> other than Codex (null is Codex), the text is the cross-platform start text (S3b-A): the target's checks run
    /// before the summary call (RS-D13), and the summary is the one Codex makes, as it does for its own start.
    /// </summary>
    private async Task<CodexPreview> BuildCodexStartTextAsync(
        SessionSummary session, string folder, CodexResumeChoice choice, KeptSummary? kept, bool writeByTarget, CancellationToken ct, ProviderId? target = null)
    {
        var provider = _codex ??= new CodexProvider();
        var sessionId = session.Key.SessionId;
        var to = target ?? ProviderId.Codex;
        // RS-D28: the target writes the summary when the owner asked it to after the source's failed; Codex otherwise.
        var writer = SummaryWriterRules.WriterOf(ProviderId.Codex, to, writeByTarget);
        try
        {
            var launch = await CodexLaunchCheckAsync(ct);
            if (launch.Refusal is { } refusal)
            {
                AppLog.Write("codex resume preview: launch refused, no summary");
                return new CodexPreview(string.Empty, null, refusal);
            }
            if (to != ProviderId.Codex)
            {
                var crossRefusal = await CrossPlatformStartText.RefusalBeforeSummaryAsync(
                    ProviderId.Codex, to, RememberChoice.ModeOf(choice.Mode), () => CrossTargetCheckAsync(ProviderId.Codex, to, folder, ct));
                if (crossRefusal is not null)
                {
                    AppLog.Write("codex cross start refused: no summary");
                    return new CodexPreview(string.Empty, null, crossRefusal);
                }
            }

            var lines = await Task.Run(() => CodexTurns.ReadSession(provider.Paths, sessionId, ct), ct);
            AppLog.Write($"codex start text: {lines.Count} messages read");
            if (lines.Count == 0) return new CodexPreview(string.Empty, null, Lines: lines.Count);

            KeptSummary? summary = null;
            if (choice.Level is { } level)
            {
                // A kept summary is reused only when it was made from this transcript (RS-D21, FIX 1 of the S6c review). One made for the setting from an
                // earlier read of a transcript that has since changed is out of date: no call is made for it here, and the screen is told.
                var sameSetting = kept is not null && kept.MatchesChoice(level, choice.Turns, kept.Lines);
                if (kept is not null && kept.MatchesChoice(level, choice.Turns, lines.Count))
                {
                    summary = kept;
                }
                else if (sameSetting)
                {
                    return new CodexPreview(string.Empty, null, Lines: lines.Count, Stale: true);
                }
                else
                {
                    // Codex runs its summary in the session's folder, so the folder is made again first if it is gone (owner, 2026-11-22). A folder that
                    // cannot be made refuses: the refusal is reported as a failed summary is, in the status line and the preview.
                    if (!EnsureSessionFolder(folder, out var folderRefusal))
                    {
                        SetStatus(folderRefusal!);
                        return new CodexPreview(string.Empty, null, folderRefusal);
                    }
                    // The summary reads only the messages before the tail (RS-D20, RS-D23); the tail is the rest.
                    var part = ResumeText.SummaryPart(lines, choice.Turns, ResumeText.TailTokenCap, turn => ProviderResumeText.TurnTokens(turn, "codex"));
                    var made = writer == ProviderId.Codex
                        ? await new CodexSummarizer(new ProcessCliRunner("cmd.exe")).SummariseLinesAsync(part, level, folder, ct)
                        : await WriteSummaryAsync(writer, ProviderId.Codex, part, null, level, folder, ct);
                    AppLog.Write($"{writer.Value} summary made: {made.Length:N0} characters");
                    var saved = await SaveCodexSummaryFileAsync(session, level, made);
                    summary = new KeptSummary(level, made, Saved: saved, Turns: choice.Turns, Lines: lines.Count, WrittenBy: SummaryWriterRules.WrittenByOf(ProviderId.Codex, writer));
                }
            }

            if (to != ProviderId.Codex)
            {
                var cross = CrossStartTextFor(ProviderId.Codex, to, folder, summary, choice.Level, lines, choice.Turns);
                if (!cross.Fits) return new CodexPreview(string.Empty, null, "Not started: " + CrossPlatformStartText.TooLongMessage, lines.Count);
                AppLog.Write($"codex cross start text built: {cross.Text.Length:N0} characters");
                return new CodexPreview(cross.Text, summary, Lines: lines.Count);
            }

            var tail = CodexResumeText.Tail(lines, choice.Turns);
            var start = CodexResumeText.Compose(summary?.Text, choice.Level, tail, choice.Turns, sessionId);
            AppLog.Write($"codex start text built: {start.Length:N0} characters");
            return new CodexPreview(start, summary, Lines: lines.Count);
        }
        catch (Exception ex) when (ex is CodexSummaryException or ClaudeCliException or CopilotSummaryException or ArgumentException or FileNotFoundException
                                       or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            AppLog.Write($"codex start text not built: {ex.GetType().Name}");
            var summaryFailed = ex is CodexSummaryException or ClaudeCliException or CopilotSummaryException;
            var problem = summaryFailed
                ? $"The summary was not made: {ex.Message}."
                : $"The start text could not be built: {ex.Message}.";
            SetStatus($"Not started: {problem}");
            return new CodexPreview(string.Empty, null, problem, SummaryFailed: summaryFailed);
        }
    }

    /// <summary>
    /// The refusal when a Codex start cannot run here, or null: PowerShell 7 (pwsh.exe) must be on PATH, and the Codex shim must be present. The Codex
    /// start and a cross-platform start into Codex both ask it, before anything is written. The start runs codex.ps1 (the launcher's script); the terminal's
    /// own codex may be codex.exe, so the start file is not written when the shim is missing.
    /// </summary>
    private static string? CodexStartToolRefusal()
    {
        if (CodexStartLauncher.FindPwsh() is null)
            return "Not started: PowerShell 7 (pwsh.exe) is not on PATH.";
        return CodexStartLauncher.FindShim() is null ? CodexResumeRules.NoShimOnPath : null;
    }

    /// <summary>
    /// Keeps a summary as a file in the Summaries folder beside the state file, as Claude's summaries are kept (<see cref="SummaryFiles"/>). The file's
    /// name and path are not logged: the session's title can hold the owner's words. A file that cannot be written is logged by type, and the
    /// summary is still used. Returns true when the file was written.
    /// </summary>
    private Task<bool> SaveCodexSummaryFileAsync(SessionSummary session, SummaryLevel level, string summary) =>
        ResumeStartFiles.SaveSummaryAsync(
            _options.StatePath, "codex", CodexRowTitle(MarkOf(ProviderId.Codex, session.Key.SessionId)?.Name, session),
            level, session.Key.SessionId, summary);

    /// <summary>
    /// Starts the text the preview showed, in a new Codex terminal tab (R6). The text is <paramref name="text"/>, as shown: it is not built again. The tab
    /// runs PowerShell 7 (<c>pwsh.exe</c>) with the text in a file in the Starts folder (R3), in the session's folder. Without PowerShell 7 nothing starts;
    /// the preview's Copy is the way out. The new session makes a model turn when it starts, so the status line says so.
    /// </summary>
    private async Task StartCodexFromTextAsync(SessionSummary session, string folder, CodexResumeChoice choice, string? text)
    {
        var title = CodexRowTitle(MarkOf(ProviderId.Codex, session.Key.SessionId)?.Name, session);
        var start = await StartCodexFromTextAsync(folder, choice.Model, text, "Codex: resume of " + title);
        if (start is null) return;
        var how = choice.Level is { } level
            ? $"a Codex summary ({level.ToString().ToLowerInvariant()}) and the last {choice.Turns} turns"
            : $"the last {choice.Turns} turns";
        SetStatus(WithWarning(start.Warning, $"Started a Codex session from {how}. It starts with a model turn."));
    }

    /// <summary>
    /// What a Codex tab start gave on success: the launch check's warning (null for none), which the caller puts in its status line.
    /// </summary>
    private sealed record CodexTabStart(string? Warning);

    /// <summary>
    /// The start of a Codex terminal tab with <paramref name="text"/> in <paramref name="folder"/>, titled <paramref name="tabTitle"/>, at <paramref name="model"/>
    /// (null for Codex's own default). The same checks as the session start, in the same order, before the start file is written (P9, R4). A refusal sets
    /// the status line and returns null. A cross-platform start (S3b-B1) calls this with no Codex session: its title is the cross title and its model is the
    /// default (RS-D12). The caller sets the success status line.
    /// </summary>
    private async Task<CodexTabStart?> StartCodexFromTextAsync(string folder, string? model, string? text, string tabTitle)
    {
        if (text is not { Length: > 0 })
        {
            SetStatus("Not started: there is no start text to send.");
            return null;
        }
        var toolRefusal = CodexStartToolRefusal();
        if (toolRefusal is not null)
        {
            SetStatus(toolRefusal);
            return null;
        }
        // The model is checked before the start file is written (P9, M-D5): a refused model leaves no text on disk.
        if (model is { } chosen && !CodexModelList.IsValidModelId(chosen))
        {
            SetStatus("Not started: the model name is not valid.");
            return null;
        }

        // The version check comes before the start file is written, so a refused start leaves no text on disk and no tab (R4).
        var launch = await CodexLaunchCheckAsync();
        if (launch.Refusal is { } refusal)
        {
            SetStatus(refusal);
            return null;
        }

        string command;
        try
        {
            var path = CodexStartLauncher.WriteStart(CodexStartLauncher.StartsFolderBeside(_options.StatePath), text, DateTimeOffset.Now);
            command = CodexStartLauncher.CommandLine(folder, path, model);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Write($"codex start file not written: {ex.GetType().Name}");
            SetStatus($"Not started: the start text could not be written ({ex.GetType().Name}).");
            return null;
        }

        OpenCodexTab("new-" + Guid.NewGuid().ToString("N"), tabTitle, command, folder);
        return new CodexTabStart(launch.Warning);
    }

    /// <summary>
    /// The screenshot option (<c>--codex-resume</c>, test only): once the Codex history has loaded, the resume screen opens on its first session, and
    /// <c>--shot</c> takes the picture of it. With <c>summary</c>, the screen opens in the summary mode and presses Make summary once. Nothing else
    /// opens it.
    /// </summary>
    private void OpenCodexResumeForShot()
    {
        if (!_options.CodexResume || _codexResumeShotOpened) return;
        _codexResumeShotOpened = true;
        if (_codexSessions.Count == 0)
        {
            AppLog.Write("codex resume screenshot: no Codex session in the history");
            return;
        }
        ShowCodexResume(_codexSessions[0], forScreenshot: true, screenshotSummary: _options.CodexResumeSummary);
    }
}
