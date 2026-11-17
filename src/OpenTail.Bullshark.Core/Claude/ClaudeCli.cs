using System.Text;
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
    private readonly TimeProvider _clock;

    /// <param name="clock">
    /// The clock the session-id wait reads and waits on. The default is the system clock; a test passes a clock it moves itself,
    /// so the wait can be checked without real seconds passing.
    /// </param>
    public ClaudeCli(ICliRunner runner, TimeProvider? clock = null)
    {
        _runner = runner;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>The sessions `claude agents --json` lists now: the active ones, interactive and background.</summary>
    public Task<IReadOnlyList<AgentInfo>> ListAgentsAsync(CancellationToken cancellationToken = default) =>
        ListAgentsAsync(includeCompleted: false, cancellationToken);

    /// <summary>
    /// The sessions `claude agents --json` lists. With <paramref name="includeCompleted"/>, `--all` adds the completed background
    /// sessions, so one that finished or was stopped is still listed, with its state (done, stopped, failed). The supervisor's
    /// passes use it; the rail and the name checks use the active list.
    /// </summary>
    public Task<IReadOnlyList<AgentInfo>> ListAgentsAsync(bool includeCompleted, CancellationToken cancellationToken = default) =>
        ListAgentsAsync(null, includeCompleted, cancellationToken);

    /// <summary>
    /// The sessions `claude agents --json` lists, limited to those started under <paramref name="cwd"/> when it is given (the
    /// `--cwd` option, additions item 6). A null or blank folder gives the same listing as the overloads without one. Nothing calls
    /// this with a folder yet: the item waits on its gate (the additions plan, D6.2).
    /// </summary>
    public async Task<IReadOnlyList<AgentInfo>> ListAgentsAsync(string? cwd, bool includeCompleted, CancellationToken cancellationToken = default)
    {
        var args = new List<string> { "agents", "--json" };
        if (includeCompleted) args.Add("--all");
        if (!string.IsNullOrWhiteSpace(cwd)) args.AddRange(["--cwd", cwd]);
        var result = await RunAsync(args, null, cancellationToken);
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
    /// The most characters the joined launch arguments may take, counted as they are written on the command line (see
    /// <see cref="EscapeArgument"/>). Windows caps a command line at 32,767 characters. The margin is for the executable's path.
    /// </summary>
    public const int MaxLaunchCharacters = 30_000;

    /// <summary>
    /// An argument as the runtime writes it on the Windows command line (the rules of <c>ProcessStartInfo.ArgumentList</c>). An argument with
    /// a space, a tab or a quote is wrapped in quotes; inside them each quote is written as <c>\"</c>, and a run of backslashes before a
    /// quote is doubled, with the quote's own backslash, so that the program reads the argument back as it was. Any other argument is
    /// written as it is. Round 5 item 12: a brief full of quotes and backslashes is longer on the command line than its characters.
    /// </summary>
    public static string EscapeArgument(string argument)
    {
        // An empty argument is written as two quotes, so that the program still gets an argument there.
        if (argument.Length == 0) return "\"\"";
        if (!argument.Any(c => char.IsWhiteSpace(c) || c == '"')) return argument;

        var text = new StringBuilder("\"");
        for (var i = 0; i < argument.Length;)
        {
            var backslashes = 0;
            while (i < argument.Length && argument[i] == '\\')
            {
                i++;
                backslashes++;
            }

            if (i == argument.Length)
            {
                // Backslashes at the end are doubled, so they do not escape the closing quote.
                text.Append('\\', backslashes * 2);
            }
            else if (argument[i] == '"')
            {
                // The backslashes before a quote are doubled, and the quote itself is escaped.
                text.Append('\\', backslashes * 2 + 1);
                text.Append('"');
                i++;
            }
            else
            {
                text.Append('\\', backslashes);
                text.Append(argument[i]);
                i++;
            }
        }
        return text.Append('"').ToString();
    }

    /// <summary>The joined arguments as they are written on the command line, each one escaped by <see cref="EscapeArgument"/>.</summary>
    private static string CommandLineOf(IEnumerable<string> args) => string.Join(' ', args.Select(EscapeArgument));

    /// <summary>Starts a background session and returns its 8-character id.</summary>
    public async Task<string> StartBackgroundAsync(BackgroundLaunch launch, CancellationToken cancellationToken = default)
    {
        var args = ArgumentsOf(launch);

        RequireCarriable(launch, args);
        var result = await RunAsync(args, launch.WorkingDirectory, cancellationToken);
        return ParseBackgroundId(result.StdOut)
            ?? throw new ClaudeCliException($"`claude --bg` did not print an id: {result.StdOut.Trim()}");
    }

    /// <summary>The arguments a launch passes to `claude --bg`, in order. The prompt, when there is one, is the last.</summary>
    private static List<string> ArgumentsOf(BackgroundLaunch launch)
    {
        var args = new List<string> { "--bg", "--model", launch.Model, "--effort", launch.Effort, "--name", launch.Name };
        if (launch.PermissionMode is not null) args.AddRange(["--permission-mode", launch.PermissionMode]);
        if (launch.ResumeSessionId is not null) args.AddRange(["--resume", launch.ResumeSessionId]);
        if (launch.Prompt is not null) args.Add(launch.Prompt);
        return args;
    }

    /// <summary>
    /// The length of the command line a launch makes, counted as <see cref="StartBackgroundAsync"/> counts it against
    /// <see cref="MaxLaunchCharacters"/>. A caller checks a launch before it writes the run that starts it.
    /// </summary>
    public static int CommandLineLength(BackgroundLaunch launch) => CommandLineOf(ArgumentsOf(launch)).Length;

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

        var joined = CommandLineOf(args).Length;
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
        var deadline = _clock.GetUtcNow() + timeout;
        while (true)
        {
            var agent = (await ListAgentsAsync(cancellationToken)).FirstOrDefault(a => a.Id == shortId);
            if (agent?.SessionId is { } sessionId) return sessionId;
            if (_clock.GetUtcNow() >= deadline)
                throw new ClaudeCliException($"session {shortId} did not appear in `claude agents --json` within {timeout.TotalSeconds:0}s");
            await Task.Delay(TimeSpan.FromSeconds(1), _clock, cancellationToken);
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
