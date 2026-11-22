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
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
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
            defaults.PlannerModel, defaults.PlannerEffort,
            (choice, kept, cancellationToken) => PreviewAsync(digest, folder, lines, choice, kept, cancellationToken))
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
        else await StartFromTextAsync(digest, folder, lines, choice, prompt.KeptSummary);
    }

    /// <summary>
    /// The prompt's preview (and its copy): the start text the choice would send, built by the same method the start uses, so the two cannot
    /// drift. It starts nothing and types into no session. A summary is made once here and the prompt keeps it for the start.
    /// </summary>
    private async Task<ResumePreview> PreviewAsync(
        SessionDigest digest, string folder, IReadOnlyList<TranscriptLine> lines, ResumeChoice choice, KeptSummary? kept, CancellationToken cancellationToken)
    {
        if (choice.Mode == ResumeMode.Full) throw new InvalidOperationException("A full resume sends no prompt, so there is none to preview.");
        var start = await BuildStartTextAsync(digest, folder, lines, choice, kept, cancellationToken);
        // The text itself is the owner's transcript, so only its size is logged.
        AppLog.Write($"preview built, {start.Fitted.Text.Length:N0} chars");
        return new ResumePreview(start.Fitted.Text, start.Fitted.CutCharacters, start.Fitted.Fits, start.Summary);
    }

    /// <summary>
    /// The same session continues, with its whole conversation as context: claude --bg --resume with the session's id, at the
    /// chosen model and effort. The session is recorded so the rail shows it, and it is kept alive: if it goes, the supervisor
    /// resumes it under the same id.
    /// </summary>
    private async Task ResumeInFullAsync(SessionDigest digest, string folder, ResumeChoice choice)
    {
        var start = await StartFullResumeAsync(digest.SessionId, folder, choice.Model, choice.Effort, prompt: null, includeCompleted: false, progress: SetStatus);
        if (start.Refusal is { } refusal)
        {
            SetStatus(refusal);
            return;
        }
        if (!start.Recorded)
        {
            SetStatus($"Resumed as {start.Name} but not recorded: {start.RecordProblem}. Check the log.");
            RefreshRail();
            return;
        }

        RefreshRail();
        SetStatus(WithWarning(start.Warning, start.SessionId is null
            ? $"Resume of {ShortId(digest.SessionId)} started as {start.Name}; the session id will be picked up on the next pass."
            : $"Resumed {ShortId(digest.SessionId)} in full as {start.Name}. It is kept alive: if it stops, the supervisor resumes it."));
    }

    /// <summary>
    /// What a full start gave: a refusal (the status text that says why, nothing was started), or a started session with its name, its short id, its
    /// full id (null when it was not read back in time) and whether its record and run were written (<see cref="RecordProblem"/> says why not).
    /// </summary>
    private sealed record FullResume(
        string? Refusal, string? Name = null, string? ShortId = null, string? SessionId = null, bool Recorded = true, string? RecordProblem = null,
        string? Warning = null);

    /// <summary>
    /// The guards and the start that continue a past session in full: a fresh listing, "already open in the rail", the folder check, a name, the start
    /// with the session's id (and the <paramref name="prompt"/>, when there is one), the id read back, and the record and the run that keep it alive.
    /// The owner's resume and the limit wake both use it, so the guards are the same. It shows nothing: the caller shows the result.
    /// <paramref name="includeCompleted"/> makes the listing include the finished and stopped sessions, so a wake refuses any session that is listed
    /// (a finished session is not started by the wake). The owner's resume leaves them out, since it may continue a finished session.
    /// </summary>
    private async Task<FullResume> StartFullResumeAsync(
        string sessionId, string folder, string model, string effort, string? prompt, bool includeCompleted, Action<string>? progress)
    {
        // A fresh listing decides: a session that is listed is already running, and a second start would be a second session.
        IReadOnlyList<AgentInfo> listing;
        try
        {
            listing = _daemon.IdleListing(await _cli.ListAgentsAsync(includeCompleted));
        }
        catch (Exception ex)
        {
            return new FullResume($"Not resumed: the sessions could not be read. {ex.Message}");
        }
        if (!includeCompleted) _agents = listing;

        if (listing.Any(a => string.Equals(a.SessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            || ReadRecords().Values.Any(r => SameId(r.SessionId, sessionId)))
        {
            return new FullResume($"Not resumed: session {ShortId(sessionId)} is already open in the rail.");
        }

        var check = FolderCheck.Check(folder, TrustCheck.DefaultClaudeJsonPath);
        if (check.Status is FolderStatus.Missing or FolderStatus.NotTrusted)
        {
            return new FullResume($"Not resumed: {check.Message}");
        }

        // The version check comes before the name is made and the start (R4): a refused launch starts nothing and writes no record.
        var launch = await ClaudeLaunchCheckAsync();
        if (launch.Refusal is { } refusal) return new FullResume(refusal);

        var taken = ReadRecords().Keys.Concat(listing.Concat(_allAgents).Select(a => a.Name)).ToList();
        var name = "bullshark-resume-" + RunNames.NewRunId(taken);
        progress?.Invoke($"Resuming {ShortId(sessionId)} in full as {name} ({model} {effort})…");

        string shortId;
        try
        {
            shortId = await _cli.StartBackgroundAsync(new BackgroundLaunch(
                Name: name,
                WorkingDirectory: folder,
                Model: model,
                Effort: effort,
                Prompt: prompt,
                PermissionMode: "auto",
                ResumeSessionId: sessionId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"resume in full failed: {ex.Message}");
            return new FullResume($"Not resumed: {ex.Message}");
        }

        // The session is running from here on, so a failure below is never reported as "not resumed". Its id is read back for the
        // record; if that takes longer than the wait, the record has no id and the next pass adopts the session by its name.
        string? fullId = null;
        try
        {
            fullId = await _cli.ResolveSessionIdAsync(shortId, TimeSpan.FromSeconds(30));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"resumed as {name} ({shortId}); its id was not read back in time: {ex.Message}");
        }

        // The record first, then the run that keeps it alive. If the record cannot be written the session is running but not kept
        // alive, and the log says so, so the owner can attach to it or stop it.
        try
        {
            _store.Update(records => records[name] = RunPlan.ResumedRecord(name, fullId, DateTimeOffset.UtcNow));
            _runStore.Update(runs => runs.Append(RunPlan.ResumedRun(name, folder, model, effort, DateTimeOffset.UtcNow)).ToList());
        }
        catch (Exception ex) when (StoreErrors.IsExpected(ex))
        {
            AppLog.Write($"resumed {fullId ?? shortId} as {name} but it is not kept alive: {ex.Message}");
            return new FullResume(null, name, shortId, fullId, Recorded: false, RecordProblem: ex.Message, Warning: launch.Warning);
        }

        return new FullResume(null, name, shortId, fullId, Warning: launch.Warning);
    }

    /// <summary>The start text of a resume: the fitted text, the request that launches it, and the summary it carries (null for none).</summary>
    private sealed record StartText(ResumeText.FittedStart Fitted, SetupRequest Request, KeptSummary? Summary);

    /// <summary>
    /// The start text of a resume, built once for the preview and for the start alike: the summary (one Haiku call when the owner chose a
    /// level, or none when the kept summary of that level is reused), then the last turns, fitted to the command line the session is
    /// launched with. The request is the one the start launches, for the fitted text. It starts nothing and shows nothing. A summary that is
    /// not made throws ClaudeCliException; a cancelled one throws OperationCanceledException.
    /// </summary>
    private async Task<StartText> BuildStartTextAsync(
        SessionDigest digest, string folder, IReadOnlyList<TranscriptLine> lines, ResumeChoice choice, KeptSummary? kept,
        CancellationToken cancellationToken = default)
    {
        KeptSummary? summary = null;
        if (choice.Level is { } level)
        {
            if (kept is not null && kept.MatchesLevel(level))
            {
                summary = kept;
            }
            else
            {
                var input = await Task.Run(() => ResumeText.SummaryInput(lines, digest.FilesWritten), cancellationToken);
                var text = await _cli.SummariseAsync(ResumeText.Instructions(level), input, cancellationToken);
                await SaveSummaryFileAsync(digest, level, text);
                summary = new KeptSummary(level, text, Saved: true);
            }
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
        var fitted = ResumeText.Fit(summary?.Text, choice.Level, lines, choice.Turns, digest.SessionId,
            text => SetupPlan.LongestCommandLine(RequestFor(text)));
        return new StartText(fitted, RequestFor(fitted.Text), summary);
    }

    /// <summary>
    /// A new session starts from the chosen text: the summary (one Haiku call, when the owner chose a level and no kept summary of that
    /// level exists), then the last turns word for word. The new session runs at the chosen model and effort. It is a solo run in the
    /// session's own folder, so it is written, supervised and shown like any run.
    /// </summary>
    private async Task StartFromTextAsync(
        SessionDigest digest, string folder, IReadOnlyList<TranscriptLine> lines, ResumeChoice choice, KeptSummary? kept)
    {
        if (choice.Level is { } level && !(kept is not null && kept.MatchesLevel(level)))
            SetStatus($"Summarising {ShortId(digest.SessionId)} ({level.ToString().ToLowerInvariant()}): one model call…");

        StartText start;
        try
        {
            start = await BuildStartTextAsync(digest, folder, lines, choice, kept);
        }
        catch (ClaudeCliException ex)
        {
            AppLog.Write($"summary not made: {ex.Message}");
            SetStatus($"Not started: {ex.Message}");
            return;
        }

        var fitted = start.Fitted;
        if (!fitted.Fits)
        {
            SetStatus("Not started: the start text is too long for the command line, even with its latest turn cut.");
            return;
        }
        var request = start.Request;

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
        var launch = await StartRunAsync(request);
        if (launch is null) return;
        SetStatus(WithWarning(launch.Warning, $"Resumed {ShortId(digest.SessionId)} from {how}, at {choice.Model} {choice.Effort}. The new session starts on the next pass.{cut}"));
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
