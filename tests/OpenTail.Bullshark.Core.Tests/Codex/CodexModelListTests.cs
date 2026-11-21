using System.Text;
using OpenTail.Bullshark.Core.Codex;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>
/// P9: the model list. The parser is tested on synthetic JSON lines (the shape the probe recorded: <c>id</c>, <c>hidden</c>, <c>isDefault</c>,
/// <c>displayName</c>). The conversation is tested with fake programs: a file of canned lines written by <c>type</c>, an exit with no output, a
/// process that never answers, and a missing program. The ids are synthetic. No Codex program runs.
/// </summary>
public sealed class CodexModelListTests
{
    private const string Reply =
        """{"jsonrpc":"2.0","id":2,"result":{"data":[""" +
        """{"id":"example-hidden","hidden":true,"isDefault":false,"displayName":"Example Hidden"},""" +
        """{"id":"example-default","hidden":false,"isDefault":true,"displayName":"Example Default"},""" +
        """{"id":"example-other","hidden":false,"isDefault":false,"displayName":"Example Other"},""" +
        """{"id":"Bad Id","hidden":false,"isDefault":false,"displayName":"Not Usable"}""" +
        """],"nextCursor":null}}""";

    [Fact]
    public void ParseKeepsTheVisibleValidEntriesInOrderAndMarksTheDefault()
    {
        var reading = CodexModelList.Parse(Reply);

        Assert.Equal(CodexModelProblem.None, reading.Problem);
        Assert.Equal(
            [
                new CodexModelEntry("example-default", "Example Default", IsDefault: true),
                new CodexModelEntry("example-other", "Example Other", IsDefault: false),
            ],
            reading.Models);
    }

    [Fact]
    public void ParseCountsTheHiddenEntriesAndTheDroppedOnesSeparately()
    {
        var reading = CodexModelList.Parse(Reply);

        Assert.Equal(1, reading.Hidden);
        Assert.Equal(1, reading.Dropped);
    }

    [Fact]
    public void WithNoIsDefaultTheFirstVisibleEntryIsTheDefault()
    {
        var line = """{"id":2,"result":{"data":[{"id":"example-a","hidden":false},{"id":"example-b","hidden":false,"isDefault":false}]}}""";

        var reading = CodexModelList.Parse(line);

        Assert.Equal(["example-a", "example-b"], reading.Models.Select(m => m.Id));
        Assert.Equal([true, false], reading.Models.Select(m => m.IsDefault));
    }

    [Fact]
    public void AHiddenDefaultIsNotOfferedSoTheFirstVisibleEntryIsTheDefault()
    {
        var line = """{"id":2,"result":{"data":[{"id":"example-hidden-default","hidden":true,"isDefault":true},{"id":"example-a","hidden":false,"isDefault":false}]}}""";

        var reading = CodexModelList.Parse(line);

        Assert.Equal(CodexModelProblem.None, reading.Problem);
        Assert.Equal("example-a", reading.Models.Single().Id);
        Assert.True(reading.Models.Single().IsDefault);
        Assert.Equal(1, reading.Hidden);
    }

    [Fact]
    public void ADisplayNameThatIsMissingOrBlankIsTheId()
    {
        var line = """{"id":2,"result":{"data":[{"id":"example-a","hidden":false,"isDefault":true},{"id":"example-b","displayName":"  ","hidden":false}]}}""";

        var reading = CodexModelList.Parse(line);

        Assert.Equal(["example-a", "example-b"], reading.Models.Select(m => m.DisplayName));
    }

    [Fact]
    public void AReplyWithNoOfferedModelGivesNoModels()
    {
        var line = """{"id":2,"result":{"data":[{"id":"example-hidden","hidden":true},{"id":"Bad Id","hidden":false}]}}""";

        var reading = CodexModelList.Parse(line);

        Assert.Equal(CodexModelProblem.NoModels, reading.Problem);
        Assert.Empty(reading.Models);
        Assert.Equal(1, reading.Hidden);
        Assert.Equal(1, reading.Dropped);
    }

    [Fact]
    public void AnEmptyDataArrayGivesNoModels()
    {
        Assert.Equal(CodexModelProblem.NoModels, CodexModelList.Parse("""{"id":2,"result":{"data":[]}}""").Problem);
    }

    [Fact]
    public void AnErrorReplyGivesAnAppServerError()
    {
        Assert.Equal(CodexModelProblem.AppServerError, CodexModelList.Parse("""{"id":2,"error":{"code":-32600,"message":"synthetic"}}""").Problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("""{"id":2}""")]
    [InlineData("""{"id":2,"result":{"items":[]}}""")]
    [InlineData("""{"id":2,"result":{"data":"example"}}""")]
    public void AReplyOfTheWrongShapeGivesABadReply(string line)
    {
        var reading = CodexModelList.Parse(line);

        Assert.Equal(CodexModelProblem.BadReply, reading.Problem);
        Assert.Empty(reading.Models);
    }

    [Theory]
    [InlineData("gpt-6-luna", true)]
    [InlineData("gpt-5.6-terra", true)]
    [InlineData("a", true)]
    [InlineData("0-x.y", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("-gpt", false)]
    [InlineData(".gpt", false)]
    [InlineData("GPT-6-Luna", false)]
    [InlineData("gpt 6", false)]
    [InlineData("gpt_6", false)]
    [InlineData("gpt-6'; calc", false)]
    [InlineData("gpt-6\n", false)]
    [InlineData("gpt-é", false)]
    public void TheModelIdPatternIsTheOneInDecisionD3(string? id, bool valid)
    {
        Assert.Equal(valid, CodexModelList.IsValidModelId(id));
    }

    [Fact]
    public async Task AMissingProgramGivesNotStarted()
    {
        var reading = await new CodexModelList("opentail-no-such-program-for-a-test.exe", ["/c", "x"], TimeSpan.FromSeconds(5))
            .ReadAsync(CancellationToken.None);

        Assert.Equal(CodexModelProblem.NotStarted, reading.Problem);
        Assert.Empty(reading.Models);
    }

    [Fact]
    public async Task AProcessThatEndsWithoutAReplyGivesExitedWithoutReply()
    {
        var reading = await new CodexModelList("cmd.exe", ["/d", "/c", "exit", "0"], TimeSpan.FromSeconds(20))
            .ReadAsync(CancellationToken.None);

        Assert.Equal(CodexModelProblem.ExitedWithoutReply, reading.Problem);
        Assert.Empty(reading.Models);
    }

    [Fact]
    public async Task AProcessThatNeverAnswersIsStoppedAtTheTimeout()
    {
        // ping prints lines that are not JSON, so none is a reply, and it runs for about 30 seconds: the one-second limit stops it.
        var started = DateTimeOffset.UtcNow;

        var reading = await new CodexModelList("cmd.exe", ["/d", "/c", "ping", "-n", "30", "127.0.0.1"], TimeSpan.FromSeconds(1))
            .ReadAsync(CancellationToken.None);

        Assert.Equal(CodexModelProblem.TimedOut, reading.Problem);
        Assert.True(DateTimeOffset.UtcNow - started < TimeSpan.FromSeconds(25), "the process was not stopped at the timeout");
    }

    [Fact]
    public async Task TheReplyWithIdTwoIsReadPastOtherLinesAndTheDefaultIsTaken()
    {
        using var temp = new TempFile();
        File.WriteAllText(temp.Path, string.Join(
            "\r\n",
            """{"jsonrpc":"2.0","id":1,"result":{"userAgent":"synthetic"}}""",
            "a line that is not JSON",
            """{"jsonrpc":"2.0","method":"synthetic/notification","params":{}}""",
            """{"jsonrpc":"2.0","id":3,"result":{}}""",
            Reply) + "\r\n", new UTF8Encoding(false));

        var reading = await new CodexModelList("cmd.exe", ["/d", "/c", "type", temp.Path], TimeSpan.FromSeconds(20))
            .ReadAsync(CancellationToken.None);

        Assert.Equal(CodexModelProblem.None, reading.Problem);
        Assert.Equal("example-default", reading.Models.Single(m => m.IsDefault).Id);
        Assert.Equal(2, reading.Models.Count);
    }

    [Fact]
    public async Task ACancelledReadIsThrownToTheCaller()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CodexModelList("cmd.exe", ["/d", "/c", "ping", "-n", "30", "127.0.0.1"], TimeSpan.FromSeconds(20)).ReadAsync(cancel.Token));
    }

    /// <summary>A temp file for one test, deleted when the test ends. Its name is synthetic.</summary>
    private sealed class TempFile : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bullshark-models-" + Guid.NewGuid().ToString("N") + ".txt");

        public void Dispose()
        {
            try { File.Delete(Path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
