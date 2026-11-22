using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Resume;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Copilot;

/// <summary>
/// Stage G1.4 (G-D1, G-D2, G-D15): the summary call, with a fake runner, so no Copilot process runs. Checks the exact arguments and the
/// <c>--sync-session=off</c> flag, the scratch folder (its marker, its work, config and log folders, its removal), the timeout, the exit codes, the
/// cancellation, the cap, and that no transcript text reaches an error. The state file is a temp path whose folder the test deletes.
/// </summary>
public sealed class CopilotSummarizerTests : IDisposable
{
    private readonly CopilotHome _home = new();
    private readonly string _state;

    public CopilotSummarizerTests()
    {
        _state = Path.Combine(_home.Home, "state", "state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_state)!);
    }

    public void Dispose() => _home.Dispose();

    private static CopilotSummarizer Summarizer(ICliRunner runner) => new(runner);

    private string ScratchParent => CopilotScratchFolder.ParentBeside(_state);

    /// <summary>A session with one long old message and a short newest one, for the cap test.</summary>
    private void WriteLongSession(string oldText, string newText) => _home.WriteEvents(CopilotHome.SessionId,
        CopilotHome.SessionStart(CopilotHome.SessionId),
        CopilotHome.UserMessage("2026-11-20T10:00:02.000Z", oldText),
        CopilotHome.AssistantMessage("2026-11-20T10:00:03.000Z", "reply"),
        CopilotHome.UserMessage("2026-11-20T10:00:04.000Z", newText));

    [Fact]
    public void TheArgumentsAreTheExactCallWithSyncOffAndTheViewToolOnly()
    {
        var config = Path.Combine("C:\\Fixture\\State", "CopilotScratch", "abc", "config");
        var log = Path.Combine("C:\\Fixture\\State", "CopilotScratch", "abc", "log");

        var args = CopilotSummarizer.Arguments(config, log);

        Assert.Equal(
            new[]
            {
                "/d", "/c", "copilot", "-p", CopilotSummarizer.SummaryPrompt,
                "--config-dir", config,
                "--log-dir", log,
                "--sync-session=off",
                "--output-format", "text",
                "--silent",
                "--no-ask-user",
                "--no-custom-instructions",
                "--disable-builtin-mcps",
                "--available-tools=view",
            },
            args);
        Assert.Contains("--sync-session=off", args);
    }

    [Fact]
    public void TheShortPromptHoldsNoCmdSpecialCharacterAndNamesBothFiles()
    {
        // The prompt sits inside the quotes of a cmd.exe line: a double quote, a percent sign, an ampersand or an exclamation mark would break it.
        foreach (var character in new[] { "\"", "%", "&", "!" })
        {
            Assert.DoesNotContain(character, CopilotSummarizer.SummaryPrompt);
        }
        Assert.Contains(CopilotSummarizer.TranscriptFileName, CopilotSummarizer.SummaryPrompt);
        Assert.Contains(CopilotSummarizer.InstructionsFileName, CopilotSummarizer.SummaryPrompt);
        Assert.Contains("data", CopilotSummarizer.SummaryPrompt);
    }

    [Fact]
    public void AFolderThatCannotSitInACmdLineIsRefusedBeforeAnyCall()
    {
        foreach (var bad in new[] { "C:\\Fixture\\100%", "C:\\Fixture\\hi!", "C:\\Fixture\\a\"b" })
        {
            Assert.Throws<ArgumentException>(() => CopilotSummarizer.Arguments(bad, "C:\\Fixture\\log"));
            Assert.Throws<ArgumentException>(() => CopilotSummarizer.Arguments("C:\\Fixture\\config", bad));
        }
    }

    [Fact]
    public async Task TheCallRunsInAScratchFolderWithTheMarkerAndTheWorkFilesAndTheFolderIsRemovedAfter()
    {
        WriteLongSession("Old question", "Newest question");
        string? folder = null;
        var markerAtCall = false;
        string? transcriptAtCall = null;
        string? instructionsAtCall = null;
        var runner = new FakeCliRunner(args =>
        {
            folder = ScratchFolderOf(args);
            var work = Path.Combine(folder, CopilotScratchFolder.WorkFolderName);
            markerAtCall = File.Exists(Path.Combine(folder, CopilotScratchFolder.MarkerName));
            transcriptAtCall = File.ReadAllText(Path.Combine(work, CopilotSummarizer.TranscriptFileName));
            instructionsAtCall = File.ReadAllText(Path.Combine(work, CopilotSummarizer.InstructionsFileName));
            return FakeCliRunner.Ok("A summary.");
        });

        var summary = await Summarizer(runner).SummariseSessionAsync(_home.Paths(), CopilotHome.SessionId, _state, SummaryLevel.Normal, CancellationToken.None);

        Assert.Equal("A summary.", summary);
        var call = Assert.Single(runner.Calls);
        Assert.NotNull(folder);
        Assert.Equal(Path.Combine(folder, CopilotScratchFolder.WorkFolderName), call.WorkingDirectory);
        Assert.Equal(ScratchParent, Path.GetDirectoryName(folder));
        Assert.True(markerAtCall, "the marker was not there when the call ran");
        Assert.Contains("Newest question", transcriptAtCall);
        Assert.Contains("copilot: reply", transcriptAtCall);
        Assert.StartsWith(CopilotSummaryText.Instructions(SummaryLevel.Normal), instructionsAtCall);
        Assert.Contains(CopilotSummaryText.DataLine, instructionsAtCall);
        Assert.False(Directory.Exists(folder), "the scratch folder was left behind");
    }

    [Fact]
    public async Task TheConfigAndLogFoldersAreTheScratchSubfoldersAndNeverTheWorkingFolderOrInsideIt()
    {
        WriteLongSession("Old question", "Newest question");
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("A summary."));

        await Summarizer(runner).SummariseSessionAsync(_home.Paths(), CopilotHome.SessionId, _state, SummaryLevel.Normal, CancellationToken.None);

        var call = Assert.Single(runner.Calls);
        var scratch = Path.GetDirectoryName(Path.GetFullPath(call.WorkingDirectory!))!;
        Assert.Equal(Path.Combine(scratch, CopilotScratchFolder.ConfigFolderName), ConfigFolderOf(call.Args));
        Assert.Equal(Path.Combine(scratch, CopilotScratchFolder.LogFolderName), LogFolderOf(call.Args));

        var work = Path.GetFullPath(call.WorkingDirectory!);
        var workWithSeparator = work + Path.DirectorySeparatorChar;
        foreach (var folder in new[] { ConfigFolderOf(call.Args), LogFolderOf(call.Args) })
        {
            var full = Path.GetFullPath(folder);
            Assert.NotEqual(work, full, StringComparer.OrdinalIgnoreCase);
            Assert.False(full.StartsWith(workWithSeparator, StringComparison.OrdinalIgnoreCase), $"{full} is inside the working folder {work}");
        }
    }

    [Fact]
    public async Task TheTranscriptIsAFileNotPartOfTheCommandLine()
    {
        WriteLongSession("Old question", "Newest question");
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("A summary."));

        await Summarizer(runner).SummariseSessionAsync(_home.Paths(), CopilotHome.SessionId, _state, SummaryLevel.Minimal, CancellationToken.None);

        var args = Assert.Single(runner.Calls).Args;
        Assert.DoesNotContain(args, argument => argument.Contains("Newest question", StringComparison.Ordinal));
        Assert.Null(Assert.Single(runner.Inputs));
    }

    [Fact]
    public async Task ATimeoutSaysSoNamesNoOutputAndRemovesTheFolder()
    {
        WriteLongSession("Old question", "Newest question");
        var runner = new FakeCliRunner(_ => new CliResult(-1, "PARTIAL SECRET", string.Empty, TimedOut: true));

        var error = await Assert.ThrowsAsync<CopilotSummaryException>(() =>
            Summarizer(runner).SummariseSessionAsync(_home.Paths(), CopilotHome.SessionId, _state, SummaryLevel.Normal, CancellationToken.None));

        Assert.Equal("the summary timed out after five minutes", error.Message);
        Assert.Equal(TimeSpan.FromMinutes(5), CopilotSummarizer.Timeout);
        Assert.Empty(ScratchFoldersLeft());
    }

    [Fact]
    public async Task ANonZeroExitNamesTheExitCodeOnlyAndRemovesTheFolder()
    {
        WriteLongSession("Old question", "Newest question");
        var runner = new FakeCliRunner(_ => new CliResult(2, "STDOUT SECRET", "STDERR SECRET Newest question", TimedOut: false));

        var error = await Assert.ThrowsAsync<CopilotSummaryException>(() =>
            Summarizer(runner).SummariseSessionAsync(_home.Paths(), CopilotHome.SessionId, _state, SummaryLevel.Normal, CancellationToken.None));

        Assert.Equal("the summary failed (exit 2)", error.Message);
        Assert.Empty(ScratchFoldersLeft());
    }

    [Fact]
    public async Task AnEmptyReplyIsAnError()
    {
        WriteLongSession("Old question", "Newest question");
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("  \n \t\n"));

        var error = await Assert.ThrowsAsync<CopilotSummaryException>(() =>
            Summarizer(runner).SummariseSessionAsync(_home.Paths(), CopilotHome.SessionId, _state, SummaryLevel.Normal, CancellationToken.None));

        Assert.Equal("the summary came back empty", error.Message);
    }

    [Fact]
    public async Task TheReplyIsReturnedTrimmed()
    {
        WriteLongSession("Old question", "Newest question");
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("  \nSummary line one.\n\nLine two.\n\n"));

        var summary = await Summarizer(runner).SummariseSessionAsync(_home.Paths(), CopilotHome.SessionId, _state, SummaryLevel.Normal, CancellationToken.None);

        Assert.Equal("Summary line one.\n\nLine two.", summary);
    }

    [Fact]
    public async Task ACancelledCallMakesNoFolderAndACancelDuringTheCallRemovesTheFolder()
    {
        WriteLongSession("Old question", "Newest question");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Summarizer(new FakeCliRunner()).SummariseSessionAsync(_home.Paths(), CopilotHome.SessionId, _state, SummaryLevel.Normal, cancelled.Token));
        Assert.Empty(ScratchFoldersLeft());

        // The token is cancelled while the call runs, as a cancelled process run is: the caller sees the cancel and the folder is still removed.
        using var during = new CancellationTokenSource();
        var runner = new CancelDuringCallRunner(during);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Summarizer(runner).SummariseSessionAsync(_home.Paths(), CopilotHome.SessionId, _state, SummaryLevel.Normal, during.Token));
        Assert.Single(runner.Calls);
        Assert.Empty(ScratchFoldersLeft());
    }

    [Fact]
    public async Task ASessionWithNoEventsFileIsNotRunAndMakesNoScratchFolder()
    {
        var runner = new FakeCliRunner();

        await Assert.ThrowsAsync<FileNotFoundException>(() => Summarizer(runner).SummariseSessionAsync(
            _home.Paths(), CopilotHome.MissingId, _state, SummaryLevel.Minimal, CancellationToken.None));

        Assert.Empty(runner.Calls);
        Assert.Empty(ScratchFoldersLeft());
    }

    [Theory]
    [InlineData("*")]
    [InlineData("..")]
    [InlineData("a/b")]
    public async Task AnIdThatCouldWidenTheSearchIsRefusedBeforeAnyFileIsRead(string sessionId)
    {
        var runner = new FakeCliRunner();

        await Assert.ThrowsAsync<ArgumentException>(() => Summarizer(runner).SummariseSessionAsync(
            _home.Paths(), sessionId, _state, SummaryLevel.Minimal, CancellationToken.None));

        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task AStatePathThatCannotSitInACmdLineIsRefusedBeforeAnyFolderIsMade()
    {
        WriteLongSession("Old question", "Newest question");
        var runner = new FakeCliRunner();
        var badState = Path.Combine(_home.Home, "100%", "state.json");

        await Assert.ThrowsAsync<ArgumentException>(() => Summarizer(runner).SummariseSessionAsync(
            _home.Paths(), CopilotHome.SessionId, badState, SummaryLevel.Minimal, CancellationToken.None));

        Assert.Empty(runner.Calls);
        Assert.False(Directory.Exists(Path.Combine(_home.Home, "100%")));
    }

    [Fact]
    public async Task TheInputKeepsTheNewestTextWhenTheSessionIsOverTheCap()
    {
        // The old message alone is over the cap; the newest message fits and must be kept, with the cut notice before it.
        var oldText = new string('o', CopilotSummaryInput.MaxInputChars + 5_000);
        var newText = "NEWEST message at the end";
        WriteLongSession(oldText, newText);
        string? transcript = null;
        var runner = new FakeCliRunner(args =>
        {
            var work = Path.Combine(ScratchFolderOf(args), CopilotScratchFolder.WorkFolderName);
            transcript = File.ReadAllText(Path.Combine(work, CopilotSummarizer.TranscriptFileName));
            return FakeCliRunner.Ok("A summary.");
        });

        await Summarizer(runner).SummariseSessionAsync(_home.Paths(), CopilotHome.SessionId, _state, SummaryLevel.Normal, CancellationToken.None);

        Assert.NotNull(transcript);
        Assert.True(transcript!.Length <= CopilotSummaryInput.MaxInputChars, $"{transcript.Length} characters for a cap of {CopilotSummaryInput.MaxInputChars}");
        Assert.StartsWith(CopilotSummaryInput.CutNotice, transcript);
        Assert.EndsWith(newText, transcript);
        Assert.DoesNotContain(new string('o', 1000), transcript);
    }

    [Fact]
    public void TheLevelsHaveTheCodexWordLimitsAndTheCopilotWording()
    {
        Assert.Contains("at most 120 words", CopilotSummaryText.Instructions(SummaryLevel.Minimal));
        Assert.Contains("at most 400 words", CopilotSummaryText.Instructions(SummaryLevel.Normal));
        Assert.Contains("at most 1,200 words", CopilotSummaryText.Instructions(SummaryLevel.Complete));
        Assert.Contains("GitHub Copilot CLI session", CopilotSummaryText.Instructions(SummaryLevel.Normal));
        Assert.Equal(120_000, CopilotSummaryInput.MaxInputChars);
    }

    /// <summary>The value after <c>--config-dir</c>: the config folder of a call.</summary>
    private static string ConfigFolderOf(IReadOnlyList<string> args) => ValueAfter(args, "--config-dir");

    /// <summary>The value after <c>--log-dir</c>: the log folder of a call.</summary>
    private static string LogFolderOf(IReadOnlyList<string> args) => ValueAfter(args, "--log-dir");

    /// <summary>The scratch folder of a call: the folder that holds its config folder.</summary>
    private static string ScratchFolderOf(IReadOnlyList<string> args) => Path.GetDirectoryName(ConfigFolderOf(args))!;

    private static string ValueAfter(IReadOnlyList<string> args, string flag) =>
        args[Enumerable.Range(0, args.Count).First(i => args[i] == flag) + 1];

    private IReadOnlyList<string> ScratchFoldersLeft() =>
        Directory.Exists(ScratchParent)
            ? Directory.GetDirectories(ScratchParent)
            : Array.Empty<string>();

    /// <summary>A runner that cancels the token while the call runs, then stops, as a cancelled process run does.</summary>
    private sealed class CancelDuringCallRunner(CancellationTokenSource source) : ICliRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = new();

        public Task<CliResult> RunAsync(
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            string? standardInput = null)
        {
            Calls.Add(arguments);
            source.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(FakeCliRunner.Ok());
        }
    }
}
