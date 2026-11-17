using System.IO;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Folders;
using OpenTail.Bullshark.Core.Resume;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Trust;

namespace OpenTail.Bullshark.App;

/// <summary>
/// Resuming a past session from the History view (D14, D-S11). Either the same session continues with its whole conversation,
/// or a new session starts from a summary, or from the last turns. The prompt shows what would be sent first, and nothing
/// runs until the owner presses Resume. The owner also chooses the model and effort of the resumed session. A new session is
/// started the way a setup run is: a solo run, so it is supervised.
/// </summary>
public partial class SessionWindow
{
    /// <summary>
    /// The solo pattern's saved models and efforts, for a resume. A patterns.json that cannot be read gives the defaults: a resume
    /// writes no pattern, so it need not stop for the file.
    /// </summary>
    private PatternSettings SavedSolo()
    {
        try
        {
            return PatternStore.Beside(_options.StatePath).For("solo");
        }
        catch (Exception ex) when (ex is StoreUnreadableException or TimeoutException or IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"patterns not read for a resume: {ex.Message}");
            return PatternStore.Defaults("solo");
        }
    }

    /// <summary>True while a resume runs. A request that comes meanwhile (a second press, or the rail's mark) is refused until it ends.</summary>
    private bool _resuming;

    /// <summary>
    /// Runs one resume at a time. The resume button is off while one runs, and a request that comes meanwhile is refused with a status line.
    /// </summary>
    private async Task ResumeAsync(SessionDigest digest)
    {
        if (_resuming)
        {
            SetStatus("A resume is already running. Wait for it to finish.");
            return;
        }
        _resuming = true;
        _transcript.SetResumeBusy(true);
        try
        {
            await ResumeOnceAsync(digest);
        }
        catch (Exception ex)
        {
            // The callers do not await a resume, so an exception here would be lost: it is logged and the status line says so.
            AppLog.Write($"resume failed: {ex}");
            SetStatus($"Not resumed: {ex.Message}");
        }
        finally
        {
            _resuming = false;
            _transcript.SetResumeBusy(false);
        }
    }

    /// <summary>Opens the resume prompt for a past session, then does what the owner chose.</summary>
    private async Task ResumeOnceAsync(SessionDigest digest)
    {
        SetStatus($"Reading session {ShortId(digest.SessionId)}…");
        var (lines, folder) = await Task.Run(() => ResumeSource.Load(digest.FilePath));
        if (folder is null)
        {
            SetStatus("Not resumed: the session's folder is not in its transcript.");
            return;
        }

        var facts = $"{digest.Turns} {(digest.Turns == 1 ? "turn" : "turns")} · last active {AgeOf(digest.LastActivity)} · in {folder}";
        var defaults = SavedSolo();
        var prompt = new ResumePrompt($"Resume: {DisplayTitle(digest)}", facts, digest.ContextTokensAtLastTurn, Palette,
            defaults.PlannerModel, defaults.PlannerEffort)
        {
            Owner = this,
        };
        SetStatus("Choose how to continue. Nothing runs until you press Resume.");
        prompt.ShowDialog();
        if (prompt.Choice is not { } choice)
        {
            SetStatus("Not resumed.");
            return;
        }

        if (choice.Mode == ResumeMode.Full) await ResumeInFullAsync(digest, folder, choice);
        else await StartFromTextAsync(digest, folder, lines, choice);
    }

    /// <summary>
    /// The same session continues, with its whole conversation as context: claude --bg --resume with the session's id, at the
    /// chosen model and effort. The session is recorded so the rail shows it, and it is kept alive: if it goes, the supervisor
    /// resumes it under the same id.
    /// </summary>
    private async Task ResumeInFullAsync(SessionDigest digest, string folder, ResumeChoice choice)
    {
        // A fresh listing decides: a session that is listed is already running, and a second start would be a second session.
        try
        {
            _agents = await _cli.ListAgentsAsync();
        }
        catch (Exception ex)
        {
            SetStatus($"Not resumed: the sessions could not be read. {ex.Message}");
            return;
        }

        if (_agents.Any(a => string.Equals(a.SessionId, digest.SessionId, StringComparison.OrdinalIgnoreCase))
            || ReadRecords().Values.Any(r => r.SessionId == digest.SessionId))
        {
            SetStatus($"Not resumed: session {ShortId(digest.SessionId)} is already open in the rail.");
            return;
        }

        var check = FolderCheck.Check(folder, TrustCheck.DefaultClaudeJsonPath);
        if (check.Status is FolderStatus.Missing or FolderStatus.NotTrusted)
        {
            SetStatus($"Not resumed: {check.Message}");
            return;
        }

        var taken = ReadRecords().Keys.Concat(_agents.Concat(_allAgents).Select(a => a.Name)).ToList();
        var name = "bullshark-resume-" + RunNames.NewRunId(taken);
        SetStatus($"Resuming {ShortId(digest.SessionId)} in full as {name} ({choice.Model} {choice.Effort})…");

        string shortId;
        try
        {
            shortId = await _cli.StartBackgroundAsync(new BackgroundLaunch(
                Name: name,
                WorkingDirectory: folder,
                Model: choice.Model,
                Effort: choice.Effort,
                PermissionMode: "auto",
                ResumeSessionId: digest.SessionId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"resume in full failed: {ex.Message}");
            SetStatus($"Not resumed: {ex.Message}");
            return;
        }

        // The session is running from here on, so a failure below is never reported as "not resumed". Its id is read back for the
        // record; if that takes longer than the wait, the record has no id and the next pass adopts the session by its name.
        string? sessionId = null;
        try
        {
            sessionId = await _cli.ResolveSessionIdAsync(shortId, TimeSpan.FromSeconds(30));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"resumed as {name} ({shortId}); its id was not read back in time: {ex.Message}");
        }

        // The record first, then the run that keeps it alive. If the record cannot be written the session is running but not kept
        // alive, and the log and the status say so, so the owner can attach to it or stop it.
        try
        {
            _store.Update(records => records[name] = RunPlan.ResumedRecord(name, sessionId, DateTimeOffset.UtcNow));
            _runStore.Update(runs => runs.Append(RunPlan.ResumedRun(name, folder, choice.Model, choice.Effort, DateTimeOffset.UtcNow)).ToList());
        }
        catch (Exception ex) when (ex is StoreUnreadableException or TimeoutException or IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"resumed {sessionId ?? shortId} as {name} but it is not kept alive: {ex.Message}");
            SetStatus($"Resumed as {name} but not recorded: {ex.Message}. Check the log.");
            RefreshRail();
            return;
        }

        RefreshRail();
        SetStatus(sessionId is null
            ? $"Resume of {ShortId(digest.SessionId)} started as {name}; the session id will be picked up on the next pass."
            : $"Resumed {ShortId(digest.SessionId)} in full as {name}. It is kept alive: if it stops, the supervisor resumes it.");
    }

    /// <summary>
    /// A new session starts from the chosen text: the summary (one Haiku call, when the owner chose a level), then the last turns
    /// word for word. The new session runs at the chosen model and effort. It is a solo run in the session's own folder, so it is
    /// written, supervised and shown like any run.
    /// </summary>
    private async Task StartFromTextAsync(SessionDigest digest, string folder, IReadOnlyList<TranscriptLine> lines, ResumeChoice choice)
    {
        string? summary = null;
        if (choice.Level is { } level)
        {
            SetStatus($"Summarising {ShortId(digest.SessionId)} ({level.ToString().ToLowerInvariant()}): one model call…");
            try
            {
                var input = await Task.Run(() => ResumeText.SummaryInput(lines, digest.FilesWritten));
                summary = await _cli.SummariseAsync(ResumeText.Instructions(level), input);
            }
            catch (ClaudeCliException ex)
            {
                AppLog.Write($"summary not made: {ex.Message}");
                SetStatus($"Not started: {ex.Message}");
                return;
            }
            await SaveSummaryFileAsync(digest, level, summary);
        }

        // The start text names the session it continues, on its first line (D-L8). It is fitted to the command line the session is
        // launched with, before the run is written: when the latest turn makes it too long, that turn is cut in its middle.
        var pattern = SavedSolo();
        SetupRequest RequestFor(string text) => new(
            PatternKey: "solo",
            Folder: folder,
            Task: text,
            Coders: 1,
            PlannerModel: choice.Model,
            PlannerEffort: choice.Effort,
            CoderModel: pattern.CoderModel,
            CoderEffort: pattern.CoderEffort);
        var fitted = ResumeText.Fit(summary, choice.Level, lines, choice.Turns, digest.SessionId,
            text => SetupPlan.LongestCommandLine(RequestFor(text)));
        if (!fitted.Fits)
        {
            SetStatus("Not started: the start text is too long for the command line, even with its latest turn cut.");
            return;
        }
        var request = RequestFor(fitted.Text);

        var check = SetupPlan.Validate(request, TrustCheck.DefaultClaudeJsonPath);
        if (!check.IsValid)
        {
            SetStatus("Not started: " + string.Join("  ", check.Errors));
            return;
        }
        if (check.Folder?.Status == FolderStatus.NotTrusted)
        {
            SetStatus($"Not started: {check.Folder.Message}");
            return;
        }

        var how = choice.Level is { } chosen
            ? $"a {chosen.ToString().ToLowerInvariant()} summary and the last {choice.Turns} turns"
            : $"the last {choice.Turns} turns";
        var cut = fitted.CutCharacters > 0
            ? $" {fitted.CutCharacters:N0} characters were cut from the start text to fit the command line."
            : string.Empty;
        await StartRunAsync(request);
        SetStatus($"Resumed {ShortId(digest.SessionId)} from {how}, at {choice.Model} {choice.Effort}. The new session starts on the next pass.{cut}");
    }

    /// <summary>
    /// Keeps a copy of a summary as a file in the Summaries folder beside the state file (review of 2026-11-15, Phase 2, step 1).
    /// The start text is unchanged: it still carries the summary. A file that cannot be written is logged, and the resume goes on.
    /// </summary>
    private async Task SaveSummaryFileAsync(SessionDigest digest, SummaryLevel level, string summary)
    {
        var folder = SummaryFiles.FolderBeside(_options.StatePath);
        var name = DisplayTitle(digest);
        try
        {
            var path = await Task.Run(() => SummaryFiles.Save(folder, name, level, digest.SessionId, summary, DateTimeOffset.Now));
            AppLog.Write($"summary saved: {path}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"summary not saved as a file: {ex.Message}");
        }
    }

    /// <summary>How long ago a session was last active, in words for the prompt.</summary>
    private static string AgeOf(DateTimeOffset? when)
    {
        if (when is not { } at) return "time unknown";
        var span = DateTimeOffset.UtcNow - at;
        if (span.TotalDays >= 1) return (int)span.TotalDays == 1 ? "1 day ago" : $"{(int)span.TotalDays} days ago";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours} h ago";
        return $"{Math.Max(0, (int)span.TotalMinutes)} min ago";
    }
}
