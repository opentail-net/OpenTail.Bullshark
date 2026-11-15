using OpenTail.Bullshark.Core.Claude;

namespace OpenTail.Bullshark.Core.Tests.Support;

/// <summary>A scripted stand-in for the `claude` executable. Records every call and answers from a queue or a rule.</summary>
public sealed class FakeCliRunner : ICliRunner
{
    private readonly Queue<CliResult> _scripted = new();
    private readonly Func<IReadOnlyList<string>, CliResult>? _rule;

    public List<(IReadOnlyList<string> Args, string? WorkingDirectory)> Calls { get; } = new();

    /// <summary>What each call was given on standard input, in order (null when nothing was).</summary>
    public List<string?> Inputs { get; } = new();

    public FakeCliRunner() { }

    public FakeCliRunner(Func<IReadOnlyList<string>, CliResult> rule)
    {
        _rule = rule;
    }

    public void Enqueue(CliResult result) => _scripted.Enqueue(result);

    public static CliResult Ok(string stdout = "") => new(0, stdout, string.Empty, TimedOut: false);

    public static CliResult Fail(string stderr, int exitCode = 1) => new(exitCode, string.Empty, stderr, TimedOut: false);

    public Task<CliResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        string? standardInput = null)
    {
        Calls.Add((arguments.ToArray(), workingDirectory));
        Inputs.Add(standardInput);
        if (_scripted.Count > 0) return Task.FromResult(_scripted.Dequeue());
        if (_rule is not null) return Task.FromResult(_rule(arguments));
        return Task.FromResult(Ok());
    }
}
