using OpenTail.Bullshark.Core.Codex;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>Stage S1: the text a Codex summary reads. Pure: the lines are synthetic records, and no file is read.</summary>
public sealed class CodexSummaryInputTests
{
    private const string Notice = "(The start of this session is left out: it is longer than the summary reads.)";

    private static string User(string text) => CodexHome.Message("2026-11-20T10:00:02.000Z", "user", text);

    private static string Assistant(string text) => CodexHome.Message("2026-11-20T10:00:04.000Z", "assistant", text);

    [Fact]
    public void MessagesKeepTheirOrderWithALabelEach()
    {
        var lines = new[] { User("one"), Assistant("two"), User("three") };

        var text = CodexSummaryInput.Build(lines);

        Assert.Equal("you: one\n\ncodex: two\n\nyou: three", text);
    }

    [Fact]
    public void OtherRolesAndRecordsAreSkipped()
    {
        var lines = new[]
        {
            CodexHome.Meta(CodexHome.SessionId),
            CodexHome.Turn(),
            CodexHome.Record("2026-11-20T10:00:03.000Z", "response_item", "{\"type\":\"message\",\"role\":\"developer\",\"content\":[{\"type\":\"input_text\",\"text\":\"DEVELOPER TEXT\"}]}"),
            CodexHome.Record("2026-11-20T10:00:03.000Z", "response_item", "{\"type\":\"message\",\"role\":\"tool\",\"content\":\"TOOL TEXT\"}"),
            CodexHome.Record("2026-11-20T10:00:03.000Z", "response_item", "{\"type\":\"function_call\",\"name\":\"shell\",\"arguments\":\"CALL TEXT\"}"),
            CodexHome.Record("2026-11-20T10:00:03.000Z", "future_record_type", "{\"role\":\"user\",\"content\":\"FUTURE TEXT\"}"),
            User("kept"),
        };

        var text = CodexSummaryInput.Build(lines);

        Assert.Equal("you: kept", text);
    }

    [Fact]
    public void EmptyTextIsSkipped()
    {
        var lines = new[]
        {
            CodexHome.Record("2026-11-20T10:00:03.000Z", "response_item", "{\"type\":\"message\",\"role\":\"user\",\"content\":\"\"}"),
            CodexHome.Record("2026-11-20T10:00:03.000Z", "response_item", "{\"type\":\"message\",\"role\":\"assistant\",\"content\":\"   \"}"),
            CodexHome.Record("2026-11-20T10:00:03.000Z", "response_item", "{\"type\":\"message\",\"role\":\"user\",\"content\":[]}"),
            CodexHome.Record("2026-11-20T10:00:03.000Z", "response_item", "{\"type\":\"message\",\"role\":\"user\"}"),
            User("kept"),
        };

        var text = CodexSummaryInput.Build(lines);

        Assert.Equal("you: kept", text);
    }

    [Fact]
    public void BadLinesAreSkipped()
    {
        var lines = new[]
        {
            "this is not json",
            "[1,2,3]",
            "",
            "{\"type\":\"response_item\",\"payload\":",
            User("kept"),
            "   ",
        };

        var text = CodexSummaryInput.Build(lines);

        Assert.Equal("you: kept", text);
    }

    [Fact]
    public void ContentAsAStringAndAsPartsBothRead()
    {
        var lines = new[]
        {
            CodexHome.Record("2026-11-20T10:00:03.000Z", "response_item", "{\"type\":\"message\",\"role\":\"user\",\"content\":\"plain text\"}"),
            CodexHome.Record("2026-11-20T10:00:04.000Z", "response_item",
                "{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"first part\"},{\"type\":\"other\"},{\"type\":\"output_text\",\"text\":\"second part\"}]}"),
        };

        var text = CodexSummaryInput.Build(lines);

        Assert.Equal("you: plain text\n\ncodex: first part\nsecond part", text);
    }

    [Fact]
    public void WhenTheTextIsCutTheStartIsLeftOutAndTheNoticeSaysSo()
    {
        var pad = new string('-', 80);
        var lines = new[] { User("OLDEST " + pad), Assistant("MIDDLE " + pad), User("NEWEST " + pad) };
        var whole = CodexSummaryInput.Build(lines);

        // The cap is one character short of the whole text: the oldest message goes, the middle and the newest stay, and the notice is put at the top.
        var text = CodexSummaryInput.Build(lines, whole.Length - 1);

        Assert.StartsWith(Notice + "\n\n", text);
        Assert.DoesNotContain("OLDEST", text);
        Assert.Contains("codex: MIDDLE " + pad, text);
        Assert.EndsWith("you: NEWEST " + pad, text);
        Assert.True(text.Length <= whole.Length - 1);
    }

    [Fact]
    public void NoNoticeWhenTheTextFitsExactly()
    {
        var lines = new[] { User(new string('a', 995)) };

        var whole = CodexSummaryInput.Build(lines, 1000);

        Assert.Equal(1000, whole.Length);
        Assert.Equal(whole, CodexSummaryInput.Build(lines));
        Assert.StartsWith("you: aaa", whole);
        Assert.DoesNotContain(Notice, CodexSummaryInput.Build(lines, 1000));
        Assert.StartsWith(Notice, CodexSummaryInput.Build(lines, 999));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(500)]
    [InlineData(1000)]
    [InlineData(5000)]
    public void TheCapIsNeverExceededAndTheNewestMessageIsKept(int maxChars)
    {
        var lines = Enumerable.Range(1, 200)
            .Select(i => i % 3 == 0 ? Assistant($"reply {i}" + new string('r', i % 40)) : User($"question {i}" + new string('q', i % 25)))
            .ToList();

        var text = CodexSummaryInput.Build(lines, maxChars);

        Assert.True(text.Length <= maxChars, $"{text.Length} characters for a cap of {maxChars}");
        Assert.EndsWith("you: question 200", text);
    }

    [Fact]
    public void ACapTooSmallForTheNoticeIsRefused()
    {
        var lines = new[] { User("hello") };

        Assert.Throws<ArgumentOutOfRangeException>(() => CodexSummaryInput.Build(lines, 50));
    }

    [Fact]
    public void TheNewestMessageIsKeptWhenOlderOnesGo()
    {
        var lines = Enumerable.Range(1, 60).Select(i => User($"message number {i:000} with some padding text")).ToList();

        var text = CodexSummaryInput.Build(lines, 600);

        Assert.Contains("you: message number 060 with some padding text", text);
        Assert.DoesNotContain("message number 001", text);
        Assert.StartsWith(Notice, text);
        Assert.True(text.Length <= 600);
    }

    [Fact]
    public void ANewestMessageOverTheCapIsCutInItsMiddleAndItsEndStays()
    {
        var longText = "start-" + new string('x', 500) + "-the-end";
        var lines = new[] { User("older"), User(longText) };

        var text = CodexSummaryInput.Build(lines, 300);

        Assert.StartsWith(Notice + "\n\n", text);
        Assert.Contains("you: start-", text);
        Assert.EndsWith("-the-end", text);
        Assert.DoesNotContain("older", text);
        Assert.True(text.Length <= 300);
    }

    [Fact]
    public void ANewlineInsideAMessageKeepsTheMessageOnOneLabelledBlock()
    {
        var lines = new[]
        {
            CodexHome.Record("2026-11-20T10:00:03.000Z", "response_item",
                "{\"type\":\"message\",\"role\":\"user\",\"content\":[{\"type\":\"input_text\",\"text\":\"  first line  \"},{\"type\":\"input_text\",\"text\":\"   \"},{\"type\":\"input_text\",\"text\":\"second line\"}]}"),
        };

        var text = CodexSummaryInput.Build(lines);

        Assert.Equal("you: first line\nsecond line", text);
    }
}
