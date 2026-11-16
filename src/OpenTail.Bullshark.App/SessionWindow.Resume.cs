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
    /// <summary>Opens the resume prompt for a past session, then does what the owner chose.</summary>
    private async Task ResumeAsync(SessionDigest digest)
    {
        SetStatus($"Reading session {ShortId(digest.SessionId)}…");
        var (lines, folder) = await Task.Run(() => ResumeSource.Load(digest.FilePath));
        if (folder is null)
        {
            SetStatus("Not resumed: the session's folder is not in its transcript.");
            return;
        }

        var facts = $"{digest.Turns} {(digest.Turns == 1 ? "turn" : "turns")} · last active {AgeOf(digest.LastActivity)} · in {folder}";
        var defaults = PatternStore.Beside(_options.StatePath).For("solo");
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

        var taken = ReadRecords().Keys.Concat(_agents.Select(a => a.Name)).ToList();
        var name = "bullshark-resume-" + RunNames.NewRunId(taken);
        SetStatus($"Resuming {ShortId(digest.SessionId)} in full as {name} ({choice.Model} {choice.Effort})…");

        string sessionId;
        try
        {
            var shortId = await _cli.StartBackgroundAsync(new BackgroundLaunch(
                Name: name,
                WorkingDirectory: folder,
                Model: choice.Model,
                Effort: choice.Effort,
                PermissionMode: "auto",
                ResumeSessionId: digest.SessionId));
            sessionId = await _cli.ResolveSessionIdAsync(shortId, TimeSpan.FromSeconds(30));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"resume in full failed: {ex.Message}");
            SetStatus($"Not resumed: {ex.Message}");
            return;
        }

        // The record first, then the run that keeps it alive. If the record cannot be written the session is running but not kept
        // alive, and the log and the status say so, so the owner can attach to it or stop it.
        try
        {
            _store.Update(records => records[name] = new SessionRecord(SessionRole.Planner, sessionId, name, DateTimeOffset.UtcNow, 0, [], null, null));
            _runStore.Update(runs => runs.Append(RunPlan.ResumedRun(name, folder, choice.Model, choice.Effort, DateTimeOffset.UtcNow)).ToList());
        }
        catch (Exception ex) when (ex is StoreUnreadableException or TimeoutException or IOException)
        {
            AppLog.Write($"resumed {sessionId} as {name} but it is not kept alive: {ex.Message}");
            SetStatus($"Resumed as {name} but not recorded: {ex.Message}. Check the log.");
            RefreshRail();
            return;
        }

        RefreshRail();
        SetStatus($"Resumed {ShortId(digest.SessionId)} in full as {name}. It is kept alive: if it stops, the supervisor resumes it.");
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
        }

        // The start text names the session it continues, on its first line (D-L8). The new session's transcript carries it.
        var text = ResumeText.Compose(summary, choice.Level, ResumeText.Tail(lines, choice.Turns), choice.Turns, digest.SessionId);
        var pattern = PatternStore.Beside(_options.StatePath).For("solo");
        var request = new SetupRequest(
            PatternKey: "solo",
            Folder: folder,
            Task: text,
            Coders: 1,
            PlannerModel: choice.Model,
            PlannerEffort: choice.Effort,
            CoderModel: pattern.CoderModel,
            CoderEffort: pattern.CoderEffort);

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
        await StartRunAsync(request);
        SetStatus($"Resumed {ShortId(digest.SessionId)} from {how}, at {choice.Model} {choice.Effort}. The new session starts on the next pass.");
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
