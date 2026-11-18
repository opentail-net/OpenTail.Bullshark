using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Claude;

/// <summary>
/// The magic rename's batch and its model call (magic rename, step 6, D7 and D8), with a fake model and a fake `claude` runner. No real
/// model is called: the transcripts are invented.
/// </summary>
public sealed class MagicNamerTests
{
    private static readonly DateTimeOffset Eighteenth = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastMessage = new(2026, 11, 18, 8, 0, 0, TimeSpan.Zero);

    /// <summary>A clock that stays put, in UTC, so the dated part of a name is the same on every machine.</summary>
    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Eighteenth;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    /// <summary>A model that answers from a rule, and records each prompt it was given.</summary>
    private sealed class ScriptedModel : IMagicNameModel
    {
        private readonly Func<int, string, string?> _answer;

        public ScriptedModel(Func<int, string, string?> answer) => _answer = answer;

        public List<string> Prompts { get; } = [];

        public Task<string?> NameAsync(string prompt, CancellationToken cancellationToken)
        {
            Prompts.Add(prompt);
            return Task.FromResult(_answer(Prompts.Count, prompt));
        }
    }

    private static MagicJob Job(string id, string? extract = "You: Fix the login page\nClaude: Done.", bool replace = false) =>
        new(id, LastMessage, extract, [], replace);

    private static MagicNamer Namer(IMagicNameModel model, Func<bool>? limit = null) =>
        new(model, limit ?? (() => false), new FixedClock());

    [Fact]
    public async Task AReplyBecomesAStoredNameWithItsMarks()
    {
        var stored = new List<MagicNamed>();
        var model = new ScriptedModel((_, _) => "fix login page");

        var result = await Namer(model).RunAsync([Job("s1")], new HashSet<string>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, int>(),
            named => { stored.Add(named); return true; });

        var named = Assert.Single(stored);
        Assert.Equal("s1", named.SessionId);
        Assert.Equal("Fix login page", named.Name);
        Assert.Equal(LastMessage, named.MagicAt);
        Assert.Equal(Eighteenth, named.MagicNamedAt);
        Assert.Equal(1, result.Named);
        Assert.Equal(1, result.Calls);
    }

    [Fact]
    public async Task EveryCallIsAnnouncedBeforeItIsMade()
    {
        var seen = new List<(int Call, int Total, int PromptsSoFar)>();
        var model = new ScriptedModel((_, _) => "some name");
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await Namer(model).RunAsync([Job("a"), Job("b"), Job("c")], taken, new Dictionary<string, int>(), _ => true, maxCalls: 2,
            onCall: (call, total) => seen.Add((call, total, model.Prompts.Count)));

        // Two calls are allowed of three sessions: each is announced as "n of 2", before its prompt is sent.
        Assert.Equal([(1, 2, 0), (2, 2, 1)], seen);
    }

    [Fact]
    public async Task ASessionWithNoTextIsNotAnnounced()
    {
        var seen = new List<int>();
        var model = new ScriptedModel((_, _) => "some name");

        await Namer(model).RunAsync([Job("a", extract: null)], new HashSet<string>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, int>(),
            _ => true, onCall: (call, _) => seen.Add(call));

        Assert.Empty(seen);
    }

    [Fact]
    public async Task ThePromptCarriesTheExtract()
    {
        var model = new ScriptedModel((_, _) => "Fix login");

        await Namer(model).RunAsync([Job("s1", "You: Fix the login page")], new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(), _ => true);

        Assert.Contains("You: Fix the login page", Assert.Single(model.Prompts), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyReplyLeavesTheSessionUnnamedAndCountsAFailure()
    {
        var failures = new Dictionary<string, int>(StringComparer.Ordinal);
        var stored = 0;

        var result = await Namer(new ScriptedModel((_, _) => "  ")).RunAsync([Job("s1")],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase), failures, _ => { stored++; return true; });

        Assert.Equal(0, stored);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, failures["s1"]);
    }

    [Fact]
    public async Task ATimeoutIsAFailure()
    {
        var failures = new Dictionary<string, int>(StringComparer.Ordinal);
        var model = new ScriptedModel((_, _) => throw new TimeoutException("the name call timed out"));

        var result = await Namer(model).RunAsync([Job("s1")], new HashSet<string>(StringComparer.OrdinalIgnoreCase), failures, _ => true);

        Assert.Equal(1, result.Failed);
        Assert.Equal(1, failures["s1"]);
    }

    [Fact]
    public async Task AnExceptionIsAFailureAndTheBatchGoesOn()
    {
        var model = new ScriptedModel((call, _) => call == 1 ? throw new InvalidOperationException("boom") : "Second session");
        var stored = new List<string>();

        var result = await Namer(model).RunAsync([Job("s1"), Job("s2")], new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(), named => { stored.Add(named.SessionId); return true; });

        Assert.Equal(1, result.Failed);
        Assert.Equal(1, result.Named);
        Assert.Equal(["s2"], stored);
    }

    [Fact]
    public async Task ASessionWithNoTextIsNotSent()
    {
        var model = new ScriptedModel((_, _) => "Never asked");

        var result = await Namer(model).RunAsync([Job("s1", extract: null)], new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(), _ => true);

        Assert.Empty(model.Prompts);
        Assert.Equal(1, result.NoText);
        Assert.Equal(0, result.Calls);
    }

    [Fact]
    public async Task ASessionWithNoTextIsSkippedAfterTwoRuns()
    {
        var failures = new Dictionary<string, int>(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var model = new ScriptedModel((_, _) => "Never");

        var first = await Namer(model).RunAsync([Job("s1", extract: null)], taken, failures, _ => true);
        var second = await Namer(model).RunAsync([Job("s1", extract: null)], taken, failures, _ => true);
        var third = await Namer(model).RunAsync([Job("s1", extract: null)], taken, failures, _ => true);

        Assert.Equal(1, first.NoText);
        Assert.Equal(1, second.NoText);
        Assert.Equal(1, third.SkippedAfterFailures);
        Assert.Equal(0, third.NoText);
        Assert.Empty(model.Prompts);
    }

    [Fact]
    public async Task ABatchMakesAtMostTenCalls()
    {
        var model = new ScriptedModel((call, _) => "Name " + call);
        var jobs = Enumerable.Range(0, 12).Select(i => Job("s" + i)).ToList();

        var result = await Namer(model).RunAsync(jobs, new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(), _ => true);

        Assert.Equal(MagicNamer.MaxPerRun, result.Calls);
        Assert.Equal(10, result.Named);
        Assert.Equal(12, result.Requested);
        Assert.Equal(10, model.Prompts.Count);
    }

    [Fact]
    public async Task ARunMayMakeFewerCalls()
    {
        var model = new ScriptedModel((call, _) => "Name " + call);
        var jobs = Enumerable.Range(0, 5).Select(i => Job("s" + i)).ToList();

        var result = await Namer(model).RunAsync(jobs, new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(), _ => true, maxCalls: 3);

        Assert.Equal(3, result.Calls);
        Assert.Equal(3, model.Prompts.Count);
    }

    [Fact]
    public async Task NoCallIsMadeWhileALimitIsInForce()
    {
        var model = new ScriptedModel((_, _) => "Never");

        var result = await Namer(model, limit: () => true).RunAsync([Job("s1"), Job("s2")],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, int>(), _ => true);

        Assert.Empty(model.Prompts);
        Assert.Equal(0, result.Calls);
        Assert.True(result.StoppedAtLimit);
    }

    [Fact]
    public async Task ALimitErrorStopsTheBatchAtOnce()
    {
        var model = new ScriptedModel((call, _) => call == 2 ? throw new MagicLimitException("limit") : "Name " + call);
        var stored = new List<string>();

        var result = await Namer(model).RunAsync([Job("s1"), Job("s2"), Job("s3")], new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(), named => { stored.Add(named.SessionId); return true; });

        Assert.True(result.StoppedAtLimit);
        Assert.Equal(["s1"], stored);
        Assert.Equal(2, model.Prompts.Count);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public async Task ASessionThatFailedTwiceIsSkippedInThisRun()
    {
        var failures = new Dictionary<string, int> { ["s1"] = 2 };
        var model = new ScriptedModel((_, _) => "Never");

        var result = await Namer(model).RunAsync([Job("s1")], new HashSet<string>(StringComparer.OrdinalIgnoreCase), failures, _ => true);

        Assert.Empty(model.Prompts);
        Assert.Equal(1, result.SkippedAfterFailures);
    }

    [Fact]
    public async Task AFailureOnceIsRetriedOnTheNextRun()
    {
        var failures = new Dictionary<string, int>(StringComparer.Ordinal);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var first = await Namer(new ScriptedModel((_, _) => null)).RunAsync([Job("s1")], taken, failures, _ => true);
        var second = await Namer(new ScriptedModel((_, _) => "Fix login")).RunAsync([Job("s1")], taken, failures, _ => true);

        Assert.Equal(1, first.Failed);
        Assert.Equal(1, second.Named);
    }

    [Fact]
    public async Task ANameTakenInTheLibraryGetsTheDate()
    {
        var stored = new List<MagicNamed>();
        var taken = new HashSet<string>(["Fix login"], StringComparer.OrdinalIgnoreCase);

        await Namer(new ScriptedModel((_, _) => "fix login")).RunAsync([Job("s1")], taken, new Dictionary<string, int>(),
            named => { stored.Add(named); return true; });

        Assert.Equal("Fix login - 18 Nov", Assert.Single(stored).Name);
    }

    [Fact]
    public async Task TwoNamesInOneBatchAreDistinct()
    {
        var stored = new List<string>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await Namer(new ScriptedModel((_, _) => "Same name")).RunAsync([Job("s1"), Job("s2"), Job("s3")], taken,
            new Dictionary<string, int>(), named => { stored.Add(named.Name); return true; });

        Assert.Equal(["Same name", "Same name - 18 Nov", "Same name 2"], stored);
        Assert.Equal(3, taken.Count);
    }

    [Fact]
    public async Task AKeptOwnersNameIsNotCountedAsNamed()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var result = await Namer(new ScriptedModel((_, _) => "Fix login")).RunAsync([Job("s1")], taken,
            new Dictionary<string, int>(), _ => false);

        Assert.Equal(0, result.Named);
        Assert.Equal(1, result.Refused);
        Assert.Empty(taken);
    }

    [Fact]
    public async Task AReplaceFlagIsPassedToTheStore()
    {
        var flags = new List<bool>();

        await Namer(new ScriptedModel((_, _) => "Fix login")).RunAsync([Job("s1", replace: true)], new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, int>(), named => { flags.Add(named.ReplaceManual); return true; });

        Assert.Equal([true], flags);
    }

    [Fact]
    public async Task AReplyOfOnlyEmojiIsAFailure()
    {
        var result = await Namer(new ScriptedModel((_, _) => "\U0001FA84")).RunAsync([Job("s1")],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, int>(), _ => true);

        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Named);
    }

    [Fact]
    public async Task ACancelledBatchStops()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Namer(new ScriptedModel((_, _) => "x")).RunAsync(
            [Job("s1")], new HashSet<string>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, int>(), _ => true,
            cancellationToken: cancel.Token));
    }

    [Fact]
    public async Task TheHaikuCallUsesTheArgumentsOfTheGate()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok("Fix login"));
        var folder = Path.Combine(Path.GetTempPath(), "bullshark-magic-" + Guid.NewGuid().ToString("N")[..8]);
        var model = new HaikuNameModel(runner, folder);

        var reply = await model.NameAsync("The session:\nYou: hello", CancellationToken.None);

        Assert.Equal("Fix login", reply);
        var call = Assert.Single(runner.Calls);
        Assert.Equal(HaikuNameModel.Arguments, call.Args);
        Assert.Equal(folder, call.WorkingDirectory);
        Assert.Equal("The session:\nYou: hello", Assert.Single(runner.Inputs));
        Assert.Contains("--no-session-persistence", call.Args);
        Assert.Contains("haiku", call.Args);
    }

    [Fact]
    public async Task ATimedOutCallIsATimeout()
    {
        var runner = new FakeCliRunner(_ => new CliResult(-1, string.Empty, string.Empty, TimedOut: true));

        await Assert.ThrowsAsync<TimeoutException>(() => new HaikuNameModel(runner, "x").NameAsync("p", CancellationToken.None));
    }

    [Fact]
    public async Task ALimitMessageIsALimitError()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Fail("Claude usage limit reached. Your limit will reset at 13:10."));

        await Assert.ThrowsAsync<MagicLimitException>(() => new HaikuNameModel(runner, "x").NameAsync("p", CancellationToken.None));
    }

    [Fact]
    public async Task AFailedCallDoesNotCarryItsTextIntoTheError()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Fail("SECRET transcript fragment"));

        var error = await Assert.ThrowsAsync<ClaudeCliException>(() => new HaikuNameModel(runner, "x").NameAsync("p", CancellationToken.None));

        Assert.DoesNotContain("SECRET", error.Message, StringComparison.Ordinal);
        Assert.Contains("exit 1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVeryLongReplyIsCutToTheCap()
    {
        var runner = new FakeCliRunner(_ => FakeCliRunner.Ok(new string('x', HaikuNameModel.MaxReplyCharacters + 500)));

        var reply = await new HaikuNameModel(runner, "x").NameAsync("p", CancellationToken.None);

        Assert.Equal(HaikuNameModel.MaxReplyCharacters, reply!.Length);
    }

    [Fact]
    public async Task AReplyStoresItsTagsWithTheName()
    {
        var stored = new List<MagicNamed>();

        await Namer(new ScriptedModel((_, _) => "Fix login\ntags: auth, login")).RunAsync([Job("s1")],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, int>(), named => { stored.Add(named); return true; });

        Assert.Equal("Fix login", Assert.Single(stored).Name);
        Assert.Equal(["auth", "login"], Assert.Single(stored).Tags);
    }

    [Fact]
    public async Task ANameOnlyReplyStoresNoTags()
    {
        var stored = new List<MagicNamed>();

        await Namer(new ScriptedModel((_, _) => "Fix login")).RunAsync([Job("s1")],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, int>(), named => { stored.Add(named); return true; });

        var tags = Assert.Single(stored).Tags;
        Assert.True(tags is null || tags.Count == 0);
    }

    [Fact]
    public async Task AnUnusableTagsLineStillStoresTheName()
    {
        var stored = new List<MagicNamed>();

        var result = await Namer(new ScriptedModel((_, _) => "Fix login\ntags: !!! ~~~")).RunAsync([Job("s1")],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, int>(), named => { stored.Add(named); return true; });

        Assert.Equal(1, result.Named);
        Assert.Equal("Fix login", Assert.Single(stored).Name);
        Assert.True(Assert.Single(stored).Tags is null or { Count: 0 });
    }

    [Fact]
    public async Task AFailingNameWithGoodTagsIsAFailureAndStoresNothing()
    {
        var stored = new List<MagicNamed>();
        var failures = new Dictionary<string, int>(StringComparer.Ordinal);

        var result = await Namer(new ScriptedModel((_, _) => "\U0001FA84\ntags: auth, login")).RunAsync([Job("s1")],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase), failures, named => { stored.Add(named); return true; });

        Assert.Empty(stored);
        Assert.Equal(1, result.Failed);
        Assert.Equal(1, failures["s1"]);
    }

    [Fact]
    public async Task TheTagsInUseAreInThePrompt()
    {
        var model = new ScriptedModel((_, _) => "Fix login");
        var job = new MagicJob("s1", LastMessage, "You: Fix the login page", [], false, ["auth", "billing"]);

        await Namer(model).RunAsync([job], new HashSet<string>(StringComparer.OrdinalIgnoreCase), new Dictionary<string, int>(), _ => true);

        Assert.Contains("auth, billing", Assert.Single(model.Prompts), StringComparison.Ordinal);
    }

    [Fact]
    public void TheModelIsAskedForTwoLinesWithATagsLine()
    {
        Assert.Contains("Reply with ONLY two lines", HaikuNameModel.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("'tags:'", HaikuNameModel.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("reuse that exact tag", HaikuNameModel.SystemPrompt, StringComparison.Ordinal);
    }
}
