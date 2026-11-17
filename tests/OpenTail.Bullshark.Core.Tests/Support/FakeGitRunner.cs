using OpenTail.Bullshark.Core.Git;

namespace OpenTail.Bullshark.Core.Tests.Support;

/// <summary>A scripted `git`. Records every call and answers from a rule on the arguments.</summary>
public sealed class FakeGitRunner : IGitRunner
{
    private readonly Func<IReadOnlyList<string>, GitOutput> _rule;

    public FakeGitRunner(Func<IReadOnlyList<string>, GitOutput> rule)
    {
        _rule = rule;
    }

    public List<(IReadOnlyList<string> Args, string Folder, TimeSpan Timeout, int MaxBytes)> Calls { get; } = new();

    public Task<GitOutput> RunAsync(
        IReadOnlyList<string> arguments,
        string folder,
        TimeSpan timeout,
        int maxOutputBytes,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((arguments.ToArray(), folder, timeout, maxOutputBytes));
        return Task.FromResult(_rule(arguments));
    }

    public static GitOutput Ok(string stdout = "") => new(0, stdout, string.Empty, TimedOut: false, OutputTruncated: false);

    public static GitOutput Fail(string stderr, int exitCode = 1) => new(exitCode, string.Empty, stderr, TimedOut: false, OutputTruncated: false);
}
