using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.Claude;

/// <summary>Thrown when a `claude` command fails or prints something OpenTail.Bullshark cannot read.</summary>
public sealed class ClaudeCliException : Exception
{
    public ClaudeCliException(string message) : base(message) { }
}

/// <summary>
/// What to start in the background. `claude --bg` manages the session id itself and ignores --session-id
/// (finding F18), so a new session's id is read back from `agents --json` after the launch.
/// To continue an existing session, set <see cref="ResumeSessionId"/>; it then runs with the same id.
/// </summary>
public sealed record BackgroundLaunch(
    string Name,
    string WorkingDirectory,
    string Model,
    string Effort,
    string? Prompt = null,
    string? PermissionMode = null,
    string? ResumeSessionId = null);

/// <summary>
/// The public `claude` commands the supervisor relies on. Each method maps to one command, and the
/// argument lists are the ones checked against `claude --help` on 2026-11-14.
/// </summary>
public sealed class ClaudeCli
{
    private static readonly TimeSpan ShortTimeout = TimeSpan.FromSeconds(60);
    private static readonly Regex BackgroundIdPattern =
        new(@"backgrounded\s*·\s*([0-9a-f]{8})\s*·", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ICliRunner _runner;

    public ClaudeCli(ICliRunner runner)
    {
        _runner = runner;
    }

    public async Task<IReadOnlyList<AgentInfo>> ListAgentsAsync(CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["agents", "--json"], null, cancellationToken);
        try
        {
            return AgentInfo.ParseList(result.StdOut);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException)
        {
            // Not a list: treated as a failed listing, so no session is judged gone (and resumed) because of it.
            throw new ClaudeCliException($"`claude agents --json` did not print a list: {ex.Message}");
        }
    }

    /// <summary>
    /// The most characters the joined launch arguments may take. Windows caps a command line at 32,767 characters. The margin is
    /// for the executable's path and for the quotes the runtime adds around arguments with spaces (the length is counted before that).
    /// </summary>
    public const int MaxLaunchCharacters = 30_000;

    /// <summary>Starts a background session and returns its 8-character id.</summary>
    public async Task<string> StartBackgroundAsync(BackgroundLaunch launch, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "--bg", "--model", launch.Model, "--effort", launch.Effort, "--name", launch.Name };
        if (launch.PermissionMode is not null) args.AddRange(["--permission-mode", launch.PermissionMode]);
        if (launch.ResumeSessionId is not null) args.AddRange(["--resume", launch.ResumeSessionId]);
        if (launch.Prompt is not null) args.Add(launch.Prompt);

        RequireCarriable(launch, args);
        var result = await RunAsync(args, launch.WorkingDirectory, cancellationToken);
        return ParseBackgroundId(result.StdOut)
            ?? throw new ClaudeCliException($"`claude --bg` did not print an id: {result.StdOut.Trim()}");
    }

    /// <summary>
    /// Refuses a launch the command line cannot carry, before `claude` runs. A brief that starts with '-' would be read as an
    /// option, and a command line over the limit is cut short or refused by Windows. The message says which and how long, so the
    /// owner can shorten the brief. The runner records the refusal as a failed start.
    /// </summary>
    private static void RequireCarriable(BackgroundLaunch launch, IReadOnlyList<string> args)
    {
        if (launch.Prompt is { Length: > 0 } prompt && prompt[0] == '-')
            throw new ClaudeCliException(
                $"The brief for '{launch.Name}' starts with '-', so `claude` would read it as an option. " +
                $"It was not started ({prompt.Length:N0} characters). Begin the brief with a word.");

        var joined = string.Join(' ', args).Length;
        if (joined > MaxLaunchCharacters)
            throw new ClaudeCliException(
                $"The launch for '{launch.Name}' is {joined:N0} characters ({launch.Prompt?.Length ?? 0:N0} of them the brief); " +
                $"the command line takes at most {MaxLaunchCharacters:N0} here, so it was not started. Shorten the brief.");
    }

    /// <summary>
    /// Reads back the full session id of a background session that was just started, by its short id.
    /// The session can take a moment to appear in the list, so this polls for a while before giving up.
    /// </summary>
    public async Task<string> ResolveSessionIdAsync(
        string shortId,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (true)
        {
            var agent = (await ListAgentsAsync(cancellationToken)).FirstOrDefault(a => a.Id == shortId);
            if (agent?.SessionId is { } sessionId) return sessionId;
            if (DateTimeOffset.UtcNow >= deadline)
                throw new ClaudeCliException($"session {shortId} did not appear in `claude agents --json` within {timeout.TotalSeconds:0}s");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    /// <summary>
    /// The 8-character id that `claude logs`, `attach` and `stop` take. In every sample so far it is the first
    /// 8 characters of the session id (checked 2026-11-14 on several sessions).
    /// </summary>
    public static string ShortIdOf(string sessionId) =>
        sessionId.Length >= 8 ? sessionId[..8] : sessionId;

    public static string? ParseBackgroundId(string output)
    {
        var match = BackgroundIdPattern.Match(output);
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>Prints recent terminal output of a background session (escape codes included).</summary>
    public async Task<string> LogsAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        var result = await RunAsync(["logs", idOrName], null, cancellationToken);
        return result.StdOut;
    }

    /// <summary>Stops a session. stop and rm take the id, not the name (finding F14).</summary>
    public Task<CliResult> StopAsync(string id, CancellationToken cancellationToken = default) =>
        RunAsync(["stop", id], null, cancellationToken, requireSuccess: false);

    public Task<CliResult> RemoveAsync(string id, CancellationToken cancellationToken = default) =>
        RunAsync(["rm", id], null, cancellationToken, requireSuccess: false);

    /// <summary>
    /// Asks the model for a summary of a transcript. The transcript goes in on standard input, because a long session does not
    /// fit on the command line. Haiku at low effort: the cheapest model that writes a usable summary (D-S11). The model is the
    /// owner's to change.
    /// </summary>
    public async Task<string> SummariseAsync(string instructions, string transcript, CancellationToken cancellationToken = default)
    {
        var result = await _runner.RunAsync(
            ["-p", instructions, "--model", "haiku", "--effort", "low"],
            null,
            TimeSpan.FromMinutes(5),
            cancellationToken,
            transcript);
        if (result.TimedOut) throw new ClaudeCliException("the summary timed out after five minutes");
        if (result.ExitCode != 0) throw new ClaudeCliException($"the summary failed (exit {result.ExitCode}): {result.StdErr.Trim()}");
        var text = result.StdOut.Trim();
        if (text.Length == 0) throw new ClaudeCliException("the summary came back empty");
        return text;
    }

    private async Task<CliResult> RunAsync(
        IReadOnlyList<string> args,
        string? workingDirectory,
        CancellationToken cancellationToken,
        bool requireSuccess = true)
    {
        var result = await _runner.RunAsync(args, workingDirectory, ShortTimeout, cancellationToken);
        if (requireSuccess && !result.Succeeded)
        {
            var reason = result.TimedOut ? "timed out" : $"exit {result.ExitCode}";
            throw new ClaudeCliException($"`claude {string.Join(' ', args.Take(2))}` failed ({reason}): {result.StdErr.Trim()} {result.StdOut.Trim()}");
        }
        return result;
    }
}
