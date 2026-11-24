using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Folders;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Trust;

namespace OpenTail.Bullshark.App;

/// <summary>
/// The dispatch of a start in another platform than its source (resume source and target plan, S3b-B1, RS-D5). The source screen builds the cross start
/// text (S3b-A) and the target's checks have run before its one summary call (RS-D13); this starts the target's CLI with that text and nothing else. It
/// writes no library entry under the source's key, marks no source as resumed, and links no tab to the source's id (RS-D5). The remembered mode is the
/// source's own, written by the caller as it is for a same-platform start (RS-D7).
/// </summary>
public partial class SessionWindow
{
    /// <summary>
    /// Starts the cross-platform text in <paramref name="target"/>, for a summary of <paramref name="source"/> in <paramref name="mode"/>. A refusal (the
    /// route, or the empty text) sets the status line and starts nothing. A start in opencode opens a plain terminal tab (S6h). A start in Codex opens a terminal tab titled
    /// <see cref="CrossStartRoute.TabTitleOf"/> in <paramref name="folder"/>, at Codex's own default model (RS-D12). A start in Claude runs the request of
    /// <see cref="CrossClaudeRequest"/> through <see cref="StartRunAsync"/>: a supervised run of the solo pattern, linked to no session (RS-D5, RS-D15).
    /// The status line of a success is <see cref="CrossStartRoute.StartedLine"/> (D-STATUS). The log holds the two provider names only: the text and the
    /// titles are the owner's words.
    /// </summary>
    private async Task StartCrossAsync(
        ProviderId source, ProviderId target, ContinueMode mode, string folder, string? text, string sourceTitle)
    {
        var route = CrossStartRoute.For(source, target, mode);
        if (route.Refusal is { } refusal)
        {
            AppLog.Write($"cross start refused: {ContinueDecision.NameOf(source)} to {ContinueDecision.NameOf(target)}");
            SetStatus(refusal);
            return;
        }
        if (text is not { Length: > 0 })
        {
            SetStatus("Not started: there is no start text to send.");
            return;
        }

        // The folder is made again first if it is gone, before anything runs in it (owner, 2026-11-22).
        var madeNow = !FolderExists(folder);
        if (!EnsureSessionFolder(folder, out var folderRefusal))
        {
            SetStatus(folderRefusal!);
            return;
        }

        // A start in opencode (S6h-6): the same tab start as the same-platform text start, with the cross text. opencode runs at its own default model.
        if (route.Kind == CrossStartKind.OpenCode)
        {
            if (!StartOpenCodeTabFromText(text, folder, CrossStartRoute.TabTitleOf(target, sourceTitle))) return;
            SetStatus(CrossStartRoute.StartedLine(source, target));
            return;
        }

        if (route.Kind == CrossStartKind.Codex)
        {
            var start = await StartCodexFromTextAsync(folder, model: null, text, CrossStartRoute.TabTitleOf(target, sourceTitle));
            if (start is null) return;
            SetStatus(WithWarning(start.Warning, CrossStartRoute.StartedLine(source, target)));
            return;
        }

        // A start in Claude: the text is the one the summary built and fitted, so the summary is not called again (RS-D13). The request is the solo pattern
        // with that text and the folder only: no session is linked to it (RS-D5). The checks are the same-platform start's, in the same order.
        var request = CrossClaudeRequest(folder, text);
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

        var launch = await StartRunAsync(request);
        if (launch is null)
        {
            SetStatus("Not started: the Claude run was refused (see the setup page).");
            return;
        }
        SetStatus(WithWarning(launch.Warning, CrossStartRoute.StartedLine(source, target)));
    }
}
