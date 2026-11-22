using System.IO;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Folders;
using OpenTail.Bullshark.Core.Providers;
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
            AppLog.Write($"patterns not read for a resume: {ex.GetType().Name}");
            return PatternStore.Defaults("solo");
        }
    }

    /// <summary>True while a resume runs. A request that comes meanwhile (a second press, or the rail's mark) is refused until it ends.</summary>
    private bool _resuming;

    /// <summary>
    /// Runs one resume at a time. The resume button is off while one runs, and a request that comes meanwhile is refused with a status line.
    /// </summary>
    private async Task ResumeAsync(SessionDigest digest, ContinueMode? initialMode = null)
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
            await ResumeOnceAsync(digest, initialMode);
        }
        catch (Exception ex)
        {
            // The callers do not await a resume, so an exception here would be lost: it is logged and the status line says so.
            AppLog.Write($"resume failed: {ex.GetType().Name}");
            SetStatus($"Not resumed: {ex.Message}");
        }
        finally
        {
            _resuming = false;
            _transcript.SetResumeBusy(false);
        }
    }

    /// <summary>
    /// Opens the resume prompt for a past session, then does what the owner chose. <paramref name="initialMode"/> is the mode the prompt opens on, from
    /// Continue (CN-D9); null keeps the prompt's own recommendation, as before. Opening on a mode makes no summary (CN-D3).
    /// </summary>
    private async Task ResumeOnceAsync(SessionDigest digest, ContinueMode? initialMode = null)
    {
        SetStatus($"Reading session {ShortId(digest.SessionId)}…");
        var (lines, transcriptFolder) = await Task.Run(() => ResumeSource.Load(digest.FilePath));
        // A transcript with no folder gives an empty folder. The screen opens with the notice that the session has no folder on record, and Start is
        // blocked. A missing folder is made again before the start runs in it (EnsureSessionFolder).
        var folder = transcriptFolder ?? string.Empty;

        var facts = $"{digest.Turns} {(digest.Turns == 1 ? "turn" : "turns")} · last active {AgeOf(digest.LastActivity)} · {FolderFact.For(folder)}";
        var defaults = SavedSolo();
        // The folder is checked when the screen opens, as the Codex and Copilot screens check theirs: a missing folder is shown on the screen and blocks
        // Start (the Start-time checks still run). A folder that is not trusted is not shown here: its Start refuses, as before.
        var check = FolderCheck.Check(folder, TrustCheck.DefaultClaudeJsonPath);
        var prompt = new ResumePrompt($"Resume: {DisplayTitle(digest)}", facts, digest.ContextTokensAtLastTurn, Palette,
            defaults.PlannerModel, defaults.PlannerEffort,
            (choice, kept, cancellationToken) => PreviewAsync(digest, folder, lines, choice, kept, cancellationToken), initialMode,
            folder, folderExists: check.Status != FolderStatus.Missing)
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
        if (choice.Remember) RememberResumeMode(ProviderId.Claude, digest.SessionId, RememberChoice.ModeOf(choice.Mode));
    }

    /// <summary>
    /// Writes the mode the owner ticked "Remember for this session" as the session's remembered mode (CN-D6, stage C6), under the provider's own key. It goes
    /// through <see cref="ChangeMarkOf"/>, the one mark path the rail's edits use: a library that cannot be read is not written, and the status line says so.
    /// The log holds the provider and the mode only.
    /// </summary>
    private void RememberResumeMode(ProviderId provider, string sessionId, ContinueMode mode)
    {
        if (!ChangeMarkOf(provider, sessionId, existing => RememberChoice.Apply(existing, mode))) return;
        AppLog.Write($"resume mode remembered: {provider.Value} {ContinueModeText.ToText(mode)}");
    }

    /// <summary>
    /// The prompt's preview (and its copy): the start text the choice would send, built by the same method the start uses, so the two cannot
    /// drift. It starts nothing and types into no session. A summary is made once here and the prompt keeps it for the start. The launch check
    /// comes first (R4d): a refused launch gives the refusal as the preview, with no message read and no summary call, because the start would be
    /// refused too. The start keeps its own check in <see cref="StartRunAsync"/>.
    /// </summary>
    private async Task<ResumePromptPreview> PreviewAsync(
        SessionDigest digest, string folder, IReadOnlyList<TranscriptLine> lines, ResumeChoice choice, KeptSummary? kept, CancellationToken cancellationToken)
    {
        if (choice.Mode == ResumeMode.Full) throw new InvalidOperationException("A full resume sends no prompt, so there is none to preview.");
        var launch = await ClaudeLaunchCheckAsync(cancellationToken);
        if (launch.Refusal is { } refusal)
        {
            AppLog.Write("claude resume preview: launch refused, no summary");
            return new ResumePromptPreview(null, refusal);
        }
        var start = await BuildStartTextAsync(digest, folder, lines, choice, kept, cancellationToken, target: choice.Target);
        if (start.Refusal is { } targetRefusal)
        {
            AppLog.Write("claude resume preview: target refused, no summary");
            return new ResumePromptPreview(null, targetRefusal);
        }
        // The text itself is the owner's transcript, so only its size is logged.
        AppLog.Write($"preview built, {start.Fitted.Text.Length:N0} chars");
        return new ResumePromptPreview(new ResumePreview(start.Fitted.Text, start.Fitted.CutCharacters, start.Fitted.Fits, start.Summary));
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

        // The folder is made again first if it is gone (owner, 2026-11-22). A folder that cannot be made refuses.
        var madeNow = !FolderExists(folder);
        if (!EnsureSessionFolder(folder, out var folderRefusal)) return new FullResume(folderRefusal!);
        var check = FolderCheck.Check(folder, TrustCheck.DefaultClaudeJsonPath);
        if (check.Status is FolderStatus.Missing or FolderStatus.NotTrusted)
        {
            return new FullResume(madeNow && check.Status == FolderStatus.NotTrusted
                ? SessionFolder.ClaudeNotTrustedAfterMake
                : $"Not resumed: {check.Message}");
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
            AppLog.Write($"resume in full failed: {ex.GetType().Name}");
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
            AppLog.Write($"resumed in full; its id was not read back in time: {ex.GetType().Name}");
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
            AppLog.Write($"resumed in full but it is not kept alive: {ex.GetType().Name}");
            return new FullResume(null, name, shortId, fullId, Recorded: false, RecordProblem: ex.Message, Warning: launch.Warning);
        }

        return new FullResume(null, name, shortId, fullId, Warning: launch.Warning);
    }

    /// <summary>
    /// The start text of a resume: the fitted text, the request that launches it, and the summary it carries (null for none). A start in another
    /// platform has no request of Claude's (<paramref name="Request"/> is null), and a refused one (RS-D13) has a <paramref name="Refusal"/> instead.
    /// </summary>
    private sealed record StartText(ResumeText.FittedStart Fitted, SetupRequest? Request, KeptSummary? Summary, string? Refusal = null);

    /// <summary>
    /// The start text of a resume, built once for the preview and for the start alike: the summary (one Haiku call when the owner chose a
    /// level, or none when the kept summary of that level is reused), then the last turns, fitted to the command line the session is
    /// launched with. The request is the one the start launches, for the fitted text. It starts nothing and shows nothing. A summary that is
    /// not made throws ClaudeCliException; a cancelled one throws OperationCanceledException. With a <paramref name="target"/> other than Claude
    /// (null is Claude), the target's checks run before the summary call (RS-D13), and the text is the cross-platform start text (S3b-A).
    /// </summary>
    private async Task<StartText> BuildStartTextAsync(
        SessionDigest digest, string folder, IReadOnlyList<TranscriptLine> lines, ResumeChoice choice, KeptSummary? kept,
        CancellationToken cancellationToken = default, ProviderId? target = null)
    {
        var to = target ?? ProviderId.Claude;
        if (to != ProviderId.Claude)
        {
            var refusal = await CrossPlatformStartText.RefusalBeforeSummaryAsync(
                ProviderId.Claude, to, RememberChoice.ModeOf(choice.Mode),
                () => CrossTargetCheckAsync(ProviderId.Claude, to, folder, cancellationToken));
            if (refusal is not null) return new StartText(new ResumeText.FittedStart(string.Empty, 0, Fits: false), null, null, refusal);
            var crossSummary = await SummaryForAsync(digest, lines, choice, kept, cancellationToken);
            return new StartText(CrossStartTextFor(ProviderId.Claude, to, folder, crossSummary, choice.Level, lines, choice.Turns), null, crossSummary);
        }

        var summary = await SummaryForAsync(digest, lines, choice, kept, cancellationToken);

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
    /// The summary of a resume at the chosen level: the kept one when it is for that level (no call), else one model call of the session's own CLI,
    /// which is saved as a file. Null when the choice has no level. The call is the one the same-platform start has always made (RS-D9).
    /// </summary>
    private async Task<KeptSummary?> SummaryForAsync(
        SessionDigest digest, IReadOnlyList<TranscriptLine> lines, ResumeChoice choice, KeptSummary? kept, CancellationToken cancellationToken)
    {
        if (choice.Level is not { } level) return null;
        if (kept is not null && kept.MatchesLevel(level)) return kept;
        var input = await Task.Run(() => ResumeText.SummaryInput(lines, digest.FilesWritten), cancellationToken);
        var text = await _cli.SummariseAsync(ResumeText.Instructions(level), input, cancellationToken);
        await SaveSummaryFileAsync(digest, level, text);
        return new KeptSummary(level, text, Saved: true);
    }

    /// <summary>
    /// The start text of a cross-platform start (S3b-A), built from the source's summary by <see cref="CrossPlatformStartText.Build"/>. A Claude target is
    /// fitted to the Claude command line of the request it would launch: the Claude screen's default planner model and effort (RS-D12) and the session's
    /// folder (RS-D5). Codex and Copilot targets take no fit.
    /// </summary>
    private ResumeText.FittedStart CrossStartTextFor(
        ProviderId source, ProviderId target, string folder, KeptSummary? summary, SummaryLevel? level, IReadOnlyList<TranscriptLine> lines, int turns)
    {
        if (target != ProviderId.Claude)
            return CrossPlatformStartText.Build(source, target, summary?.Text, level, lines, turns);

        var solo = SavedSolo();
        return CrossPlatformStartText.Build(
            source, target, summary?.Text, level, lines, turns, text => SetupPlan.LongestCommandLine(CrossClaudeRequestOf(folder, text, solo)));
    }

    /// <summary>
    /// The Claude request of a cross-platform start into Claude (S3b-B1, RS-D5, RS-D12), for <paramref name="text"/> in <paramref name="folder"/>: the
    /// solo pattern, one coder, the planner at the Claude screen's default model and effort, and the coder at the saved solo's. The fit of
    /// <see cref="CrossStartTextFor"/> and the start of <c>StartCrossAsync</c> both build it here, so they measure the same command line.
    /// </summary>
    internal SetupRequest CrossClaudeRequest(string folder, string text) => CrossClaudeRequestOf(folder, text, SavedSolo());

    /// <summary>
    /// <see cref="CrossClaudeRequest"/> for a given solo pattern, with no file read. A test reaches the request through it, since no window is made in a test.
    /// </summary>
    internal static SetupRequest CrossClaudeRequestOf(string folder, string text, PatternSettings solo)
    {
        var (model, effort) = CrossPlatformStartText.ClaudeDefaults(solo);
        return new SetupRequest(
            PatternKey: "solo",
            Folder: folder,
            Task: text,
            Coders: 1,
            PlannerModel: model,
            PlannerEffort: effort,
            CoderModel: solo.CoderModel,
            CoderEffort: solo.CoderEffort);
    }

    /// <summary>
    /// The target's checks for a cross-platform start, run before the summary call (RS-D5, RS-D13), with the checks the target's own start makes. A
    /// missing folder refuses. A Claude target: the folder's trust (<see cref="FolderCheck"/>, Missing and NotTrusted refuse) and the launch check. A Codex
    /// target: PowerShell 7 and the shim, then the launch check. A Copilot target has no launch check here yet (RS-D8): it is refused with
    /// <see cref="ResumeTarget"/>'s reason, even if the enabling constant were turned on. Returns the refusal, or null when the start may go on.
    /// </summary>
    private async Task<string?> CrossTargetCheckAsync(ProviderId source, ProviderId target, string folder, CancellationToken cancellationToken)
    {
        // The folder is made again first if it is gone (owner, 2026-11-22). A folder that cannot be made refuses.
        var madeNow = !FolderExists(folder);
        if (!EnsureSessionFolder(folder, out var folderRefusal)) return folderRefusal;
        if (target == ProviderId.Claude)
        {
            var check = FolderCheck.Check(folder, TrustCheck.DefaultClaudeJsonPath);
            if (check.Status is FolderStatus.Missing or FolderStatus.NotTrusted)
                return madeNow && check.Status == FolderStatus.NotTrusted
                    ? SessionFolder.ClaudeNotTrustedAfterMake
                    : $"Not started: {check.Message}";
            return (await ClaudeLaunchCheckAsync(cancellationToken)).Refusal;
        }
        if (target == ProviderId.Codex)
            return CodexStartToolRefusal() ?? (await CodexLaunchCheckAsync(cancellationToken)).Refusal;
        return ResumeTarget.RefusalReason(source, ContinueMode.Summary, target, copilotEnabled: false);
    }

    /// <summary>
    /// A new session starts from the chosen text: the summary (one Haiku call, when the owner chose a level and no kept summary of that
    /// level exists), then the last turns word for word. The new session runs at the chosen model and effort. It is a solo run in the
    /// session's own folder, so it is written, supervised and shown like any run.
    /// </summary>
    private async Task StartFromTextAsync(
        SessionDigest digest, string folder, IReadOnlyList<TranscriptLine> lines, ResumeChoice choice, KeptSummary? kept)
    {
        // The launch check comes before the summary call (R4d, the Start path too): a refused launch makes no model call. StartRunAsync checks again
        // before it writes the run, as before; a Supported reading is cached, and a refused one is kept, so the second check costs nothing.
        var early = await ClaudeLaunchCheckAsync();
        if (early.Refusal is { } earlyRefusal)
        {
            AppLog.Write("claude resume start: launch refused, no summary");
            SetStatus(earlyRefusal);
            return;
        }

        if (choice.Level is { } level && !(kept is not null && kept.MatchesLevel(level)))
            SetStatus($"Summarising {ShortId(digest.SessionId)} ({level.ToString().ToLowerInvariant()}): one model call…");

        StartText start;
        try
        {
            start = await BuildStartTextAsync(digest, folder, lines, choice, kept, target: choice.Target);
        }
        catch (ClaudeCliException ex)
        {
            AppLog.Write($"summary not made: {ex.GetType().Name}");
            SetStatus($"Not started: {ex.Message}");
            return;
        }
        catch (ArgumentException ex)
        {
            // ProviderResumeText.ComposeFrom throws for a blank (whitespace) summary: no start text is made, and nothing starts.
            AppLog.Write($"summary not used: {ex.GetType().Name}");
            SetStatus("Not started: the summary could not be used.");
            return;
        }
        if (start.Refusal is { } targetRefusal)
        {
            SetStatus(targetRefusal);
            return;
        }

        var fitted = start.Fitted;
        if (!fitted.Fits)
        {
            SetStatus("Not started: the start text is too long for the command line, even with its latest turn cut.");
            return;
        }
        // A start in another platform (S3b-B) goes to that platform's start with the cross text built above. Only a start in Claude has a request of Claude's.
        if (choice.Target != ProviderId.Claude)
        {
            await StartCrossAsync(ProviderId.Claude, choice.Target, RememberChoice.ModeOf(choice.Mode), folder, fitted.Text, DisplayTitle(digest));
            return;
        }
        var request = start.Request ?? throw new InvalidOperationException("A Claude start has a request.");

        // The folder is made again first if it is gone, before the setup is checked (owner, 2026-11-22). A folder that cannot be made refuses.
        var madeNow = !FolderExists(folder);
        if (!EnsureSessionFolder(folder, out var folderRefusal))
        {
            SetStatus(folderRefusal!);
            return;
        }
        var check = SetupPlan.Validate(request, TrustCheck.DefaultClaudeJsonPath);
        if (!check.IsValid)
        {
            SetStatus("Not started: " + string.Join("  ", check.Errors));
            return;
        }
        if (check.Folder?.Status == FolderStatus.NotTrusted)
        {
            SetStatus(madeNow ? SessionFolder.ClaudeNotTrustedAfterMake : $"Not started: {check.Folder.Message}");
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
            await Task.Run(() => SummaryFiles.Save(folder, name, level, digest.SessionId, summary, DateTimeOffset.Now));
            AppLog.Write($"summary saved as a file: {summary.Length:N0} characters");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"summary not saved as a file: {ex.GetType().Name}");
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
