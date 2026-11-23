using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.OpenCode;
using OpenTail.Bullshark.Core.Providers.Summary;
using OpenTail.Bullshark.Core.Resume;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.OpenCode;

/// <summary>
/// The opencode summary call (OC3 summary; OC-O4 decided 2026-11-23), with a fake environment runner, so no process runs. Checks the command line, the
/// standard input, the configuration that denies every tool (and that it is written before the call), the environment, the empty working folder, the
/// removal of the scratch folder after success, failure and cancellation, the failure messages, and that nothing is written beside the state file.
/// </summary>
public sealed class OpenCodeSummarizerTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "opentail-oc-summary-" + Guid.NewGuid().ToString("N"));
    private readonly string _state;

    public OpenCodeSummarizerTests()
    {
        _state = Path.Combine(_home, "state", "state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_state)!);
    }

    public void Dispose()
    {
        if (Directory.Exists(_home)) Directory.Delete(_home, recursive: true);
    }

    private string ScratchParent => Path.Combine(Path.GetDirectoryName(_state)!, OpenCodeSummarizer.ParentName);

    private IReadOnlyList<string> ScratchFoldersLeft() =>
        Directory.Exists(ScratchParent) ? Directory.GetDirectories(ScratchParent) : Array.Empty<string>();

    private static IReadOnlyList<TranscriptLine> Session(params string[] texts)
    {
        var lines = new List<TranscriptLine>();
        for (var i = 0; i < texts.Length; i++)
        {
            var kind = i % 2 == 0 ? "you" : "opencode";
            lines.Add(new TranscriptLine(i + 1, kind, null, texts[i], Collapsed: false));
        }
        return lines;
    }

    private static IReadOnlyList<TranscriptLine> SessionWithTool() =>
    [
        new TranscriptLine(1, "you", null, "fix the parser", Collapsed: false),
        new TranscriptLine(2, "tool", null, "read Parser.cs", Collapsed: false),
        new TranscriptLine(3, "opencode", null, "I fixed it", Collapsed: false),
    ];

    /// <summary>A fake that records each call and what was on disk during it (the configuration, the working folder, the marker).</summary>
    private sealed class FakeEnvRunner : ICliEnvRunner
    {
        public sealed record Call(
            IReadOnlyList<string> Args,
            string? WorkingDirectory,
            IReadOnlyDictionary<string, string> Environment,
            TimeSpan Timeout,
            string? StandardInput,
            string? ConfigDuringCall,
            bool WorkIsEmptyDuringCall,
            bool MarkerDuringCall);

        public List<Call> Calls { get; } = new();

        public Func<CliResult> Answer { get; set; } = () => FakeCliRunner.Ok("A short summary.");

        public Exception? Throw { get; set; }

        public Task<CliResult> RunAsync(
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            string? standardInput = null) =>
            throw new InvalidOperationException("The summary call always carries an environment.");

        public Task<CliResult> RunAsync(
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            IReadOnlyDictionary<string, string> environment,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            string? standardInput = null)
        {
            var config = environment.TryGetValue(OpenCodeSummaryCommand.ConfigEnvName, out var path) ? path : null;
            var configText = config is not null && File.Exists(config) ? File.ReadAllText(config) : null;
            var workEmpty = workingDirectory is not null && Directory.Exists(workingDirectory)
                && !Directory.EnumerateFileSystemEntries(workingDirectory).Any();
            var marker = workingDirectory is not null
                && File.Exists(Path.Combine(Path.GetDirectoryName(workingDirectory)!, OpenCodeSummarizer.MarkerName));
            Calls.Add(new Call(arguments, workingDirectory, environment, timeout, standardInput, configText, workEmpty, marker));
            cancellationToken.ThrowIfCancellationRequested();
            if (Throw is not null) throw Throw;
            return Task.FromResult(Answer());
        }
    }

    [Fact]
    public void TheCommandLineIsOpencodeRunWithTheFolderAndTheShortPromptOnly()
    {
        var arguments = OpenCodeSummaryCommand.Arguments(@"C:\state\OpenCodeScratch\abc\work");

        Assert.Equal(
            new[] { "/d", "/c", "opencode", "run", "--dir", @"C:\state\OpenCodeScratch\abc\work", OpenCodeSummaryCommand.SummaryPrompt },
            arguments);
    }

    [Fact]
    public void TheShortPromptHasNoQuotePercentAmpersandOrExclamationMark()
    {
        var prompt = OpenCodeSummaryCommand.SummaryPrompt;

        Assert.DoesNotContain("\"", prompt);
        Assert.DoesNotContain("%", prompt);
        Assert.DoesNotContain("&", prompt);
        Assert.DoesNotContain("!", prompt);
    }

    [Fact]
    public void AFolderThatCannotSitInACommandLineIsRefusedBeforeAnyCall()
    {
        Assert.Throws<ArgumentException>(() => OpenCodeSummaryCommand.Arguments(@"C:\a%b\work"));
        Assert.Throws<ArgumentException>(() => OpenCodeSummaryCommand.Arguments(@"C:\a!b\work"));
    }

    [Fact]
    public void TheConfigurationDeniesEveryToolAndTheDenyKeyIsInTheText()
    {
        using var document = JsonDocument.Parse(OpenCodeSummaryCommand.ConfigText);
        var permission = document.RootElement.GetProperty("permission");

        Assert.Equal("deny", permission.GetProperty("*").GetString());
        Assert.Contains("\"permission\"", OpenCodeSummaryCommand.ConfigText);
        Assert.Contains("\"*\":\"deny\"", OpenCodeSummaryCommand.ConfigText);
    }

    [Fact]
    public void TheEnvironmentNamesTheConfigurationFileAndSwitchesOffTheProjectConfiguration()
    {
        var environment = OpenCodeSummaryCommand.EnvironmentFor(@"C:\scratch\config\opencode.json");

        Assert.Equal(@"C:\scratch\config\opencode.json", environment["OPENCODE_CONFIG"]);
        Assert.Equal("1", environment["OPENCODE_DISABLE_PROJECT_CONFIG"]);
    }

    [Fact]
    public async Task TheCallRunsWithTheConfigurationWrittenBeforeAndTheEmptyWorkingFolderAsDir()
    {
        var runner = new FakeEnvRunner();

        await new OpenCodeSummarizer(runner).SummariseAsync(_state, Session("question", "answer"), SummaryLevel.Normal, CancellationToken.None);

        var call = Assert.Single(runner.Calls);
        Assert.Equal(OpenCodeSummaryCommand.ConfigText, call.ConfigDuringCall);
        Assert.True(call.WorkIsEmptyDuringCall);
        Assert.True(call.MarkerDuringCall);
        Assert.Equal(call.WorkingDirectory, call.Args[5]);
        Assert.Equal(OpenCodeSummarizer.WorkFolderName, Path.GetFileName(call.WorkingDirectory));
        var scratch = Path.GetDirectoryName(call.WorkingDirectory)!;
        Assert.Equal(Path.Combine(scratch, OpenCodeSummarizer.ConfigFolderName, OpenCodeSummaryCommand.ConfigFileName), call.Environment["OPENCODE_CONFIG"]);
        Assert.Equal("1", call.Environment["OPENCODE_DISABLE_PROJECT_CONFIG"]);
        Assert.Equal(TimeSpan.FromMinutes(5), call.Timeout);
    }

    [Fact]
    public async Task TheInstructionAndTheTranscriptGoOnStandardInputAndTheToolLinesDoNot()
    {
        var runner = new FakeEnvRunner();

        await new OpenCodeSummarizer(runner).SummariseAsync(_state, SessionWithTool(), SummaryLevel.Normal, CancellationToken.None);

        var input = Assert.Single(runner.Calls).StandardInput!;
        Assert.StartsWith("Summarise this opencode session in at most 400 words", input);
        Assert.Contains(OpenCodeSummaryInput.DataLine, input);
        Assert.Contains("you: fix the parser", input);
        Assert.Contains("opencode: I fixed it", input);
        Assert.DoesNotContain("read Parser.cs", input);
        var commandLine = string.Join(" ", Assert.Single(runner.Calls).Args);
        Assert.DoesNotContain("fix the parser", commandLine);
        Assert.DoesNotContain("read Parser.cs", commandLine);
    }

    [Theory]
    [InlineData(SummaryLevel.Minimal, "at most 120 words")]
    [InlineData(SummaryLevel.Normal, "at most 400 words")]
    [InlineData(SummaryLevel.Complete, "at most 1,200 words")]
    public async Task TheLevelSetsTheWordLimitOnStandardInput(SummaryLevel level, string limit)
    {
        var runner = new FakeEnvRunner();

        await new OpenCodeSummarizer(runner).SummariseAsync(_state, Session("question", "answer"), level, CancellationToken.None);

        Assert.Contains(limit, Assert.Single(runner.Calls).StandardInput!);
    }

    [Fact]
    public void TheStandardInputStaysWithinTheCapAndKeepsTheNewestMessage()
    {
        // Two long messages are over the cap together, so the oldest goes; the newest message is always kept.
        var lines = Session(new string('a', 70_000), new string('b', 70_000), "the newest answer");

        var input = OpenCodeSummaryInput.StdinText(lines, SummaryLevel.Normal);

        Assert.True(input.Length <= SummaryInputBuilder.MaxInputChars);
        Assert.Contains("the newest answer", input);
        Assert.Contains(SummaryInputBuilder.CutNotice, input);
    }

    [Fact]
    public async Task ASummaryIsReturnedTrimmedAndNothingIsWrittenBesideTheStateFile()
    {
        var runner = new FakeEnvRunner { Answer = () => FakeCliRunner.Ok("\n  The summary.  \n") };

        var text = await new OpenCodeSummarizer(runner).SummariseAsync(_state, Session("question", "answer"), SummaryLevel.Minimal, CancellationToken.None);

        Assert.Equal("The summary.", text);
        Assert.Empty(ScratchFoldersLeft());
        Assert.Equal(new[] { OpenCodeSummarizer.ParentName }, Directory.GetFileSystemEntries(Path.GetDirectoryName(_state)!).Select(p => Path.GetFileName(p)));
    }

    [Fact]
    public async Task TheScratchFolderIsRemovedAfterASuccessfulCall()
    {
        var runner = new FakeEnvRunner();

        await new OpenCodeSummarizer(runner).SummariseAsync(_state, Session("question", "answer"), SummaryLevel.Normal, CancellationToken.None);

        Assert.Single(runner.Calls);
        Assert.Empty(ScratchFoldersLeft());
    }

    [Fact]
    public async Task EmptyOutputWithExitCodeZeroIsAFailure()
    {
        var runner = new FakeEnvRunner { Answer = () => FakeCliRunner.Ok("   \n") };

        var error = await Assert.ThrowsAsync<OpenCodeSummaryException>(() =>
            new OpenCodeSummarizer(runner).SummariseAsync(_state, Session("question", "answer"), SummaryLevel.Normal, CancellationToken.None));

        Assert.Equal(CliProcessRun.EmptyMessage, error.Message);
        Assert.Empty(ScratchFoldersLeft());
    }

    [Fact]
    public async Task ANonZeroExitIsAFailureThatNamesTheCodeAndNotTheOutput()
    {
        var runner = new FakeEnvRunner { Answer = () => new CliResult(3, "secret transcript text", "leaked stderr", TimedOut: false) };

        var error = await Assert.ThrowsAsync<OpenCodeSummaryException>(() =>
            new OpenCodeSummarizer(runner).SummariseAsync(_state, Session("question", "answer"), SummaryLevel.Normal, CancellationToken.None));

        Assert.Equal(CliProcessRun.FailedMessage(3), error.Message);
        Assert.DoesNotContain("secret", error.Message);
        Assert.DoesNotContain("leaked", error.Message);
        Assert.Empty(ScratchFoldersLeft());
    }

    [Fact]
    public async Task ATimeoutIsAFailureAndTheScratchFolderIsRemoved()
    {
        var runner = new FakeEnvRunner { Answer = () => new CliResult(-1, string.Empty, string.Empty, TimedOut: true) };

        var error = await Assert.ThrowsAsync<OpenCodeSummaryException>(() =>
            new OpenCodeSummarizer(runner).SummariseAsync(_state, Session("question", "answer"), SummaryLevel.Normal, CancellationToken.None));

        Assert.Equal(CliProcessRun.TimedOutMessage, error.Message);
        Assert.Empty(ScratchFoldersLeft());
    }

    [Fact]
    public async Task ACancelledCallReachesTheCallerAndTheScratchFolderIsRemoved()
    {
        var runner = new FakeEnvRunner { Throw = new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new OpenCodeSummarizer(runner).SummariseAsync(_state, Session("question", "answer"), SummaryLevel.Normal, CancellationToken.None));

        Assert.Empty(ScratchFoldersLeft());
    }

    [Fact]
    public async Task AStateFolderThatCannotSitInACommandLineIsRefusedBeforeAnyFolderIsMade()
    {
        var unsafeState = Path.Combine(_home, "a%b", "state.json");
        var runner = new FakeEnvRunner();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new OpenCodeSummarizer(runner).SummariseAsync(unsafeState, Session("question", "answer"), SummaryLevel.Normal, CancellationToken.None));

        Assert.Empty(runner.Calls);
        Assert.False(Directory.Exists(Path.Combine(_home, "a%b", OpenCodeSummarizer.ParentName)));
    }
}
