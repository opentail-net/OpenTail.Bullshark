using System.Globalization;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Codex;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>
/// The Codex usage reader (R8): the last valid running total of a rollout, the field mapping (cached input and reasoning inside their totals),
/// the <c>usage.reasoningAsOutput</c> setting, the model and project, and the lines that are left out. Synthetic lines only: no owner's rollout is read.
/// </summary>
public sealed class CodexUsageReaderTests : IDisposable
{
    /// <summary>A made-up rollout name, passed as the record's key.</summary>
    private const string RolloutName = "rollout-test.jsonl";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bullshark-codex-usage-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    /// <summary>A time on 18 November 2026 (UTC) at the given minute, in the form the rollout writes.</summary>
    private static string Stamp(int minute) =>
        new DateTimeOffset(2026, 11, 18, 10, minute, 0, TimeSpan.Zero).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static DateTimeOffset At(int minute) => new(2026, 11, 18, 10, minute, 0, TimeSpan.Zero);

    /// <summary>A JSON string literal for a value that may hold backslashes (a folder).</summary>
    private static string Json(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal) + "\"";

    private static string Meta(string cwd) =>
        "{\"timestamp\":\"" + Stamp(0) + "\",\"type\":\"session_meta\",\"payload\":{\"id\":\"synthetic\",\"cwd\":" + Json(cwd) + "}}";

    private static string Turn(int minute, string model) =>
        "{\"timestamp\":\"" + Stamp(minute) + "\",\"type\":\"turn_context\",\"payload\":{\"model\":" + Json(model) + ",\"cwd\":\"C:\\\\Work\\\\proj-x\"}}";

    /// <summary>A token-count event with a running total, in the shape the rollout writes it.</summary>
    private static string Total(int minute, long input, long cached, long output, long reasoning, long write = 0) =>
        "{\"timestamp\":\"" + Stamp(minute) + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{" +
        "\"total_token_usage\":{\"input_tokens\":" + input + ",\"cached_input_tokens\":" + cached + ",\"cache_write_input_tokens\":" + write +
        ",\"output_tokens\":" + output + ",\"reasoning_output_tokens\":" + reasoning + ",\"total_tokens\":" + (input + output) + "}," +
        "\"last_token_usage\":{\"input_tokens\":1,\"output_tokens\":1}},\"rate_limits\":null}}";

    /// <summary>A token-count event with no figures: the info is null, as the rollout writes it before any total.</summary>
    private static string EmptyInfo(int minute) =>
        "{\"timestamp\":\"" + Stamp(minute) + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":null,\"rate_limits\":null}}";

    private static CodexSessionUsage? Read(IEnumerable<string> lines, bool reasoningAsOutput = true) =>
        CodexUsageReader.FromLines(RolloutName, lines, reasoningAsOutput);

    [Fact]
    public void TheLastRunningTotalWinsAndTheEventsAreNeverSummed()
    {
        var record = Read(
        [
            Meta("C:\\Work\\proj-x"),
            Turn(0, "model-one"),
            Total(1, 100, 60, 10, 4),
            Total(2, 250, 200, 30, 9),
            Total(3, 400, 330, 55, 20),
        ]);

        var found = Assert.IsType<CodexSessionUsage>(record);
        // Input 400 less 330 cached is 70 uncached; cached 330 is the cache read; output 55 is kept as it is (reasoning inside it).
        Assert.Equal(new TokenUsage(70, 55, 0, 330), found.Usage);
        Assert.Equal(At(3), found.LastTotal);
        Assert.Equal(At(1), found.FirstTotal);
        Assert.Equal("model-one", found.Model);
        Assert.Equal("proj-x", found.Project);
        Assert.Equal(RolloutName, found.Path);
    }

    [Fact]
    public void CachedInputIsCountedOnceInsideInput()
    {
        var found = Assert.IsType<CodexSessionUsage>(Read([Meta("C:\\Work\\proj-x"), Total(1, 1000, 900, 40, 0)]));

        // The uncached part is 100 and the cached part 900: together they are the 1000 of input, never 1900.
        Assert.Equal(1000, found.Usage.Input + found.Usage.CacheRead);
        Assert.Equal(100, found.Usage.Input);
        Assert.Equal(900, found.Usage.CacheRead);
        Assert.Equal(0, found.Usage.CacheCreation);
    }

    [Fact]
    public void CacheWrittenIsCountedAsCacheCreationInsideInput()
    {
        // Assumption (R8 plan, decision 2): cache written sits inside input, as cached input does. Always zero on the owner's data.
        var found = Assert.IsType<CodexSessionUsage>(Read([Meta("C:\\Work\\proj-x"), Total(1, 1000, 600, 40, 0, write: 100)]));

        Assert.Equal(new TokenUsage(300, 40, 100, 600), found.Usage);
        Assert.Equal(1000, found.Usage.Input + found.Usage.CacheCreation + found.Usage.CacheRead);
    }

    [Fact]
    public void ReasoningIsCountedAsOutputWhenOn()
    {
        var found = Assert.IsType<CodexSessionUsage>(Read([Total(1, 100, 0, 50, 20)], reasoningAsOutput: true));

        // Output tokens already hold the reasoning (total = input + output on every event of the owner's files), so 50 is 50.
        Assert.Equal(50, found.Usage.Output);
    }

    [Fact]
    public void ReasoningIsTakenOutOfOutputWhenOff()
    {
        var found = Assert.IsType<CodexSessionUsage>(Read([Total(1, 100, 0, 50, 20)], reasoningAsOutput: false));

        Assert.Equal(30, found.Usage.Output);
    }

    [Fact]
    public void ReasoningTakenOutCannotMakeOutputNegative()
    {
        var found = Assert.IsType<CodexSessionUsage>(Read([Total(1, 100, 0, 5, 9)], reasoningAsOutput: false));

        Assert.Equal(0, found.Usage.Output);
    }

    [Fact]
    public void EventsWithNoFiguresAreSkippedAndTheLastValidTotalCounts()
    {
        var record = Read(
        [
            Meta("C:\\Work\\proj-x"),
            Total(1, 100, 0, 10, 0),
            EmptyInfo(2),
            "{\"timestamp\":\"" + Stamp(3) + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"last_token_usage\":{\"input_tokens\":5}}}}",
            "{\"timestamp\":\"" + Stamp(4) + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":\"many\",\"output_tokens\":1}}}}",
            "{\"timestamp\":\"" + Stamp(5) + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":9,\"output_tokens\":-1}}}}",
            "{\"timestamp\":\"" + Stamp(6) + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":9}}}}",
            "{\"timestamp\":\"" + Stamp(7) + "\",\"type\":\"event_msg\",\"payload\":{\"type\":\"agent_message\",\"info\":null}}",
        ]);

        var found = Assert.IsType<CodexSessionUsage>(record);
        Assert.Equal(new TokenUsage(100, 10, 0, 0), found.Usage);
        Assert.Equal(At(1), found.LastTotal);
    }

    [Fact]
    public void AFileWithNoValidTotalGivesNoRecord()
    {
        Assert.Null(Read([Meta("C:\\Work\\proj-x"), Turn(0, "m"), EmptyInfo(1), EmptyInfo(2)]));
        Assert.Null(Read([]));
        // A total that has no time at all cannot be placed, so it is skipped too.
        Assert.Null(Read(["{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":5,\"output_tokens\":5}}}}"]));
    }

    [Fact]
    public void APartlyWrittenLastLineIsSkippedAndTheRestCounts()
    {
        var cut = Total(2, 250, 200, 30, 9);
        var record = Read([Meta("C:\\Work\\proj-x"), "not json", "[1,2]", Total(1, 100, 60, 10, 4), cut[..(cut.Length - 25)]]);

        var found = Assert.IsType<CodexSessionUsage>(record);
        Assert.Equal(new TokenUsage(40, 10, 0, 60), found.Usage);
        Assert.Equal(At(1), found.LastTotal);
    }

    [Fact]
    public void TheModelInEffectAtTheLastTotalIsKept()
    {
        var found = Assert.IsType<CodexSessionUsage>(Read(
        [
            Turn(0, "model-one"),
            Total(1, 10, 0, 1, 0),
            Turn(2, "model-two"),
            Total(3, 20, 0, 2, 0),
            // A later turn names another model after the last total: the record keeps the model of its total.
            Turn(4, "model-three"),
        ]));

        Assert.Equal("model-two", found.Model);
        Assert.Equal(new TokenUsage(20, 2, 0, 0), found.Usage);
    }

    [Fact]
    public void NoTurnNamingAModelGivesUnknown()
    {
        Assert.Equal("unknown", Assert.IsType<CodexSessionUsage>(Read([Total(1, 10, 0, 1, 0)])).Model);
    }

    [Theory]
    [InlineData("C:\\Work\\proj-x\\", "proj-x")]
    [InlineData("C:\\Work\\proj-x", "proj-x")]
    [InlineData("C:\\", CodexUsageReader.NoProject)]
    [InlineData("", CodexUsageReader.NoProject)]
    public void TheProjectIsTheLastFolderSegment(string cwd, string project)
    {
        var lines = new List<string> { Total(1, 10, 0, 1, 0) };
        if (cwd.Length > 0) lines.Insert(0, Meta(cwd));

        Assert.Equal(project, Assert.IsType<CodexSessionUsage>(Read(lines)).Project);
    }

    [Fact]
    public void TheProjectFallsBackToTheTurnFolderWhenTheSessionNamesNone()
    {
        var found = Assert.IsType<CodexSessionUsage>(Read(
            ["{\"timestamp\":\"" + Stamp(0) + "\",\"type\":\"turn_context\",\"payload\":{\"model\":\"m\",\"cwd\":\"C:\\\\Work\\\\from-turn\"}}", Total(1, 10, 0, 1, 0)]));

        Assert.Equal("from-turn", found.Project);
    }

    [Fact]
    public void TheTimesAreTheFirstAndLastValidTotalsAndAMissingTimeTakesTheLastSeen()
    {
        var found = Assert.IsType<CodexSessionUsage>(Read(
        [
            Total(5, 10, 0, 1, 0),
            Total(7, 20, 0, 2, 0),
            // No time of its own: it takes the last time the file gave, which is the seventh minute.
            "{\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":30,\"output_tokens\":3}}}}",
        ]));

        Assert.Equal(At(5), found.FirstTotal);
        Assert.Equal(At(7), found.LastTotal);
        Assert.Equal(new TokenUsage(30, 3, 0, 0), found.Usage);
    }

    [Fact]
    public void ReadFileGivesWhatFromLinesGivesForTheSameLines()
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, RolloutName);
        File.WriteAllLines(path, [Meta("C:\\Work\\proj-x"), Turn(0, "m"), Total(1, 100, 60, 10, 4), Total(2, 250, 200, 30, 9)]);

        var fromFile = CodexUsageReader.ReadFile(path, reasoningAsOutput: true);
        var fromLines = CodexUsageReader.FromLines(path, File.ReadLines(path), reasoningAsOutput: true);

        Assert.NotNull(fromFile);
        Assert.Equal(fromLines, fromFile);
        Assert.Equal(new TokenUsage(50, 30, 0, 200), fromFile.Usage);
    }

    [Fact]
    public void ReadFileOfAMissingFileGivesNoRecord()
    {
        Assert.Null(CodexUsageReader.ReadFile(Path.Combine(_folder, "missing.jsonl"), reasoningAsOutput: true));
    }
}
