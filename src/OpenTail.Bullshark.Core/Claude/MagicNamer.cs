using OpenTail.Bullshark.Core.Library;

namespace OpenTail.Bullshark.Core.Claude;

/// <summary>The model that names a past session (magic rename, step 6). The window knows only this seam, not which model answers.</summary>
public interface IMagicNameModel
{
    /// <summary>
    /// The model's reply to <paramref name="prompt"/> (the user message; the instructions are the model's own). Null or empty when there is no
    /// reply. Throws <see cref="MagicLimitException"/> when a usage limit stopped the call, and any other exception when the call failed.
    /// </summary>
    Task<string?> NameAsync(string prompt, CancellationToken cancellationToken);
}

/// <summary>A usage limit stopped a name call. The batch stops at it: no further call is made in that run.</summary>
public sealed class MagicLimitException : Exception
{
    public MagicLimitException(string message) : base(message) { }
}

/// <summary>
/// Haiku through the `claude` command, as the gate ran it (docs/plans/Done/magic-rename-2026-11-18.md, Gate): print mode, no session saved to
/// disk, no tools, the naming instructions as the system prompt, and the user message on standard input. The call runs in the given folder
/// (beside the state file), never in a project. Its output is capped; a timeout or a non-zero exit is an error, and the error text never
/// contains the transcript.
/// </summary>
public sealed class HaikuNameModel : IMagicNameModel
{
    /// <summary>
    /// The instructions the model gets (plan decision D5, and tags plan Part B): two lines, the name, then the tags line. The reply is read by
    /// <see cref="MagicName.ParseReply"/>.
    /// </summary>
    public const string SystemPrompt =
        "You name past coding sessions. Reply with ONLY two lines and nothing else. " +
        "Line 1 is a name of at most 5 words (max 40 characters) that says what the session was about and tells it apart from similar sessions. " +
        "No quotes, no emoji, no punctuation at the end. Do not describe yourself. " +
        "Line 2 starts with 'tags:' and lists 0 to 2 tags for the session, separated by commas. A tag is one lower-case word of 2 to 20 " +
        "letters, digits or hyphens, for example login, billing or cache-warmup. When a tag in the list of tags already in use fits the session, " +
        "reuse that exact tag; invent a new tag only when none of them fits. If no tag fits, write 'tags:' alone.";

    /// <summary>How long one call may take before it is stopped (plan step 6).</summary>
    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(90);

    /// <summary>The most characters of a reply that are read (plan step 6).</summary>
    public const int MaxReplyCharacters = 4_096;

    /// <summary>The arguments of the call, in the order the gate ran them.</summary>
    public static IReadOnlyList<string> Arguments { get; } =
        ["-p", "--model", "haiku", "--no-session-persistence", "--tools", "", "--output-format", "text", "--system-prompt", SystemPrompt];

    private readonly ICliRunner _runner;
    private readonly string _workingDirectory;

    public HaikuNameModel(ICliRunner runner, string workingDirectory)
    {
        _runner = runner;
        _workingDirectory = workingDirectory;
    }

    public async Task<string?> NameAsync(string prompt, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(Arguments, _workingDirectory, CallTimeout, cancellationToken, prompt);
        if (result.TimedOut) throw new TimeoutException("the name call timed out");
        if (result.ExitCode != 0)
        {
            // The text of the error is not kept: it may hold part of the transcript. Only the outcome is reported.
            if (LooksLikeLimit(result.StdOut) || LooksLikeLimit(result.StdErr)) throw new MagicLimitException("a usage limit stopped the name call");
            throw new ClaudeCliException($"the name call failed (exit {result.ExitCode})");
        }
        return result.StdOut.Length > MaxReplyCharacters ? result.StdOut[..MaxReplyCharacters] : result.StdOut;
    }

    private static bool LooksLikeLimit(string text) =>
        text.Contains("usage limit", StringComparison.OrdinalIgnoreCase)
        || text.Contains("limit reached", StringComparison.OrdinalIgnoreCase)
        || text.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// One session to name: its id, its last message (the snowflake mark), its extract (null when it has no text), the names already used in its
/// folder, whether it may replace the owner's own name (a confirmed single rename), the tags in use in the library that the prompt shows
/// (null for none; tags plan, Part B), and for a re-roll the session's current magic name (null for a fresh name), which the new name must not
/// repeat (<see cref="MagicNamer.RunAsync"/>).
/// </summary>
public sealed record MagicJob(
    string SessionId,
    DateTimeOffset LastActivity,
    string? Extract,
    IReadOnlyList<string> UsedNames,
    bool ReplaceManual = false,
    IReadOnlyList<string>? KnownTags = null,
    string? CurrentName = null);

/// <summary>
/// A name the batch chose, for the caller to store. <see cref="MagicAt"/> is the session's last message; <see cref="MagicNamedAt"/> is now.
/// <see cref="Tags"/> are the tags the reply offered (null for none); the caller adds them to the session's own tags, never replaces them.
/// </summary>
public sealed record MagicNamed(
    string SessionId,
    string Name,
    DateTimeOffset MagicAt,
    DateTimeOffset MagicNamedAt,
    bool ReplaceManual,
    IReadOnlyList<string>? Tags = null);

/// <summary>What a batch did. <see cref="Calls"/> counts the model calls made, including failed ones.</summary>
public sealed record MagicBatchResult(int Requested, int Named, int Refused, int Failed, int NoText, int SkippedAfterFailures, int Calls, bool StoppedAtLimit);

/// <summary>
/// Names sessions one call at a time (magic rename, D7 and D8). For each job: the batch stops at a usage limit (checked before every call,
/// and a limit error stops it too); a session with no text is not sent; a call that fails, times out or gives an unusable reply leaves the
/// session unnamed and is counted in <paramref name="failures"/>, and a session that failed <see cref="MaxFailuresPerWindowRun"/> times is
/// skipped. A name is made unique against <paramref name="takenNames"/> (<see cref="MagicName.Distinct"/>) and handed to
/// <c>store</c>, which writes it (through SessionLibrary.Update) and returns false when the owner's name was there and was kept.
/// Nothing is ever sent to a session, and nothing about a transcript is logged: only ids, counts and outcomes.
/// </summary>
public sealed class MagicNamer
{
    /// <summary>The most model calls one run makes (D7b).</summary>
    public const int MaxPerRun = 10;

    /// <summary>A session that fails this many times in one window run is skipped until the next run of the window (D8).</summary>
    public const int MaxFailuresPerWindowRun = 2;

    private readonly IMagicNameModel _model;
    private readonly Func<bool> _limitInForce;
    private readonly TimeProvider _clock;
    private readonly Action<string> _log;

    /// <param name="limitInForce">True while a usage limit is in force. Checked before every call, so no call is made while one is.</param>
    public MagicNamer(IMagicNameModel model, Func<bool> limitInForce, TimeProvider? clock = null, Action<string>? log = null)
    {
        _model = model;
        _limitInForce = limitInForce;
        _clock = clock ?? TimeProvider.System;
        _log = log ?? (_ => { });
    }

    /// <param name="jobs">The sessions, in the order they are named.</param>
    /// <param name="takenNames">The names in use, compared without case. A name chosen and stored is added to it.</param>
    /// <param name="failures">The failures counted per session id for this window run. Updated here.</param>
    /// <param name="store">Writes one name. Returns false when the name was not written (the owner's name is there and was kept).</param>
    /// <param name="maxCalls">The most model calls this run makes, at most <see cref="MaxPerRun"/>.</param>
    /// <param name="onCall">Called just before each model call with the number of this call and the most calls the run can make, so the window can
    /// say "naming 2 of 5" while a call takes its 5 to 20 seconds (a call with no sign of life looks like a broken window).</param>
    public async Task<MagicBatchResult> RunAsync(
        IReadOnlyList<MagicJob> jobs,
        ISet<string> takenNames,
        IDictionary<string, int> failures,
        Func<MagicNamed, bool> store,
        int maxCalls = MaxPerRun,
        CancellationToken cancellationToken = default,
        Action<int, int>? onCall = null)
    {
        var limit = Math.Clamp(maxCalls, 0, MaxPerRun);
        int named = 0, refused = 0, failed = 0, noText = 0, skipped = 0, calls = 0;
        var stoppedAtLimit = false;

        foreach (var job in jobs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (failures.TryGetValue(job.SessionId, out var failureCount) && failureCount >= MaxFailuresPerWindowRun)
            {
                skipped++;
                continue;
            }
            if (job.Extract is null)
            {
                // Counted like a failed call, so a session with nothing to name is not picked again on every run (D8).
                noText++;
                failures[job.SessionId] = failures.TryGetValue(job.SessionId, out var noTextCount) ? noTextCount + 1 : 1;
                _log("magic rename: a session has no text to name");
                continue;
            }
            if (calls >= limit) break;
            if (_limitInForce())
            {
                stoppedAtLimit = true;
                break;
            }

            calls++;
            onCall?.Invoke(calls, Math.Min(limit, jobs.Count));
            string? reply;
            try
            {
                reply = await _model.NameAsync(MagicName.PromptFor(job.UsedNames, job.Extract, job.KnownTags ?? [], job.CurrentName), cancellationToken);
            }
            catch (MagicLimitException)
            {
                stoppedAtLimit = true;
                _log("magic rename: a usage limit stopped the batch; no further call is made");
                break;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                failed++;
                Fail(job.SessionId, failures, $"the call failed ({ex.GetType().Name})");
                continue;
            }

            // The name is what decides a failure. Unusable tags are dropped by the parser and do not fail the reply.
            var (clean, tags) = MagicName.ParseReply(reply);
            if (clean is null)
            {
                failed++;
                Fail(job.SessionId, failures, "the reply was empty or unusable");
                continue;
            }

            // A re-roll must not hand the owner the same name again: the session's current name counts as taken for this call only. It is not
            // added to takenNames, so it stays free for the other sessions (the name is free once this session has a new one).
            var scope = takenNames;
            if (!string.IsNullOrWhiteSpace(job.CurrentName))
            {
                scope = new HashSet<string>(takenNames, StringComparer.OrdinalIgnoreCase) { job.CurrentName.Trim() };
            }
            var name = MagicName.Distinct(clean, scope, _clock.GetLocalNow());
            var written = store(new MagicNamed(job.SessionId, name, job.LastActivity, _clock.GetUtcNow(), job.ReplaceManual, tags));
            if (written)
            {
                takenNames.Add(name);
                named++;
                _log("magic rename: a session named");
            }
            else
            {
                refused++;
                _log("magic rename: a session kept its own name");
            }
        }

        var result = new MagicBatchResult(jobs.Count, named, refused, failed, noText, skipped, calls, stoppedAtLimit);
        _log($"magic rename: {named} named, {refused} kept their own name, {failed} failed, {noText} with no text, " +
             $"{skipped} skipped after failures, {calls} calls{(stoppedAtLimit ? ", stopped at a usage limit" : string.Empty)}");
        return result;
    }

    private void Fail(string sessionId, IDictionary<string, int> failures, string reason)
    {
        failures[sessionId] = failures.TryGetValue(sessionId, out var count) ? count + 1 : 1;
        _log($"magic rename: a session not named: {reason}");
    }
}
