using OpenTail.Bullshark.Core.Claude;

namespace OpenTail.Bullshark.Core.Providers.Summary;

/// <summary>
/// Runs one summary call through an <see cref="ICliRunner"/> and returns its trimmed output (provider kit R2). A timeout, a non-zero exit code or an
/// empty output is refused. The refusal is made by the caller's <paramref name="failure"/> factory, so each provider keeps its own exception type. The
/// messages name the exit code only, never the output. A cancellation is not wrapped: it reaches the caller as the runner throws it.
/// </summary>
public static class CliProcessRun
{
    /// <summary>The message of a call that ran past the timeout. Both providers use a five-minute timeout, so the text says so.</summary>
    public const string TimedOutMessage = "the summary timed out after five minutes";

    /// <summary>The message of a call whose output is empty once trimmed.</summary>
    public const string EmptyMessage = "the summary came back empty";

    /// <summary>The message of a call that exited with a non-zero code. It names the code and nothing else.</summary>
    public static string FailedMessage(int exitCode) => $"the summary failed (exit {exitCode})";

    /// <summary>
    /// Runs the command with <paramref name="standardInput"/> (null for none) and returns its trimmed standard output. A timed-out run, a non-zero exit
    /// or an empty output throws the exception <paramref name="failure"/> makes from the message.
    /// </summary>
    public static async Task<string> RunAsync(
        ICliRunner runner,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string? standardInput,
        Func<string, Exception> failure)
    {
        var result = await runner.RunAsync(arguments, workingDirectory, timeout, cancellationToken, standardInput);
        if (result.TimedOut) throw failure(TimedOutMessage);
        if (result.ExitCode != 0) throw failure(FailedMessage(result.ExitCode));
        var text = result.StdOut.Trim();
        if (text.Length == 0) throw failure(EmptyMessage);
        return text;
    }
}
