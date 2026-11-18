using OpenTail.Bullshark.Core.Library;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Library;

/// <summary>
/// The text the magic rename gives the model (magic rename, D4): labelled messages in order, capped, boilerplate and tool traffic left out,
/// and a long session kept at its top and its end. All lines are invented; no real transcript is used.
/// </summary>
public sealed class NameExtractTests
{
    private static string User(string text) =>
        "{\"type\":\"user\",\"timestamp\":\"2026-11-18T10:00:00Z\",\"message\":{\"role\":\"user\",\"content\":" + Quote(text) + "}}";

    private static string UserBlocks(params string[] texts) =>
        "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[" + string.Join(',', texts.Select(t => "{\"type\":\"text\",\"text\":" + Quote(t) + "}")) + "]}}";

    private static string Assistant(params string[] blocks) =>
        "{\"type\":\"assistant\",\"message\":{\"role\":\"assistant\",\"content\":[" + string.Join(',', blocks) + "]}}";

    private static string AssistantText(string text) => Assistant("{\"type\":\"text\",\"text\":" + Quote(text) + "}");

    private static string ToolUse => Assistant("{\"type\":\"tool_use\",\"id\":\"t1\",\"name\":\"Bash\",\"input\":{\"command\":\"ls\"}}");

    private static string ToolResult =>
        "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t1\",\"content\":\"BIGSECRET listing\"}]}}";

    private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    [Fact]
    public void AShortSessionIsSentWhole()
    {
        var extract = NameExtract.Build([User("Fix the login page"), AssistantText("Done, the form posts now.")]);

        Assert.Equal("You: Fix the login page\nClaude: Done, the form posts now.", extract);
    }

    [Fact]
    public void TextBlocksOfAUserMessageAreKept()
    {
        var extract = NameExtract.Build([UserBlocks("first part", "second part")]);

        Assert.Equal("You: first part\nYou: second part", extract);
    }

    [Fact]
    public void ALongSessionKeepsItsTopAndItsEndWithTheMarker()
    {
        var lines = Enumerable.Range(0, 100)
            .Select(i => User($"msg-{i:000} " + new string('w', 480)))
            .ToList();

        var extract = NameExtract.Build(lines)!;

        Assert.True(extract.Length <= NameExtract.DefaultMaxChars, $"length {extract.Length}");
        Assert.Contains(NameExtract.Marker.Trim(), extract, StringComparison.Ordinal);
        Assert.Contains("msg-000", extract, StringComparison.Ordinal);
        Assert.Contains("msg-099", extract, StringComparison.Ordinal);
        Assert.DoesNotContain("msg-050", extract, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTopIsAboutAFortyPercentShareAndTheEndTheRest()
    {
        var lines = Enumerable.Range(0, 100)
            .Select(i => User($"msg-{i:000} " + new string('w', 480)))
            .ToList();

        var extract = NameExtract.Build(lines)!;
        var marker = extract.IndexOf(NameExtract.Marker.Trim(), StringComparison.Ordinal);

        Assert.InRange(marker, 5_500, 7_000);
        Assert.InRange(extract.Length - marker, 8_500, 10_500);
    }

    [Fact]
    public void ToolResultsAndToolCallsAreDropped()
    {
        var extract = NameExtract.Build([User("Run the tests"), ToolUse, ToolResult, AssistantText("All green.")]);

        Assert.Equal("You: Run the tests\nClaude: All green.", extract);
        Assert.DoesNotContain("BIGSECRET", extract, StringComparison.Ordinal);
    }

    [Fact]
    public void ThinkingIsDropped()
    {
        var extract = NameExtract.Build([Assistant(
            "{\"type\":\"thinking\",\"thinking\":\"hidden plan\",\"signature\":\"x\"}",
            "{\"type\":\"text\",\"text\":\"Visible answer\"}")]);

        Assert.Equal("Claude: Visible answer", extract);
    }

    [Fact]
    public void AMessageLongerThanTheCapIsCut()
    {
        var extract = NameExtract.Build([User(new string('a', 3000))])!;

        Assert.Equal("You: " + new string('a', NameExtract.MessageCap - 1) + "…", extract);
    }

    [Fact]
    public void CommandAndSystemBoilerplateIsDropped()
    {
        var extract = NameExtract.Build([
            User("<command-name>/clear</command-name>"),
            User("<system-reminder>Some reminder</system-reminder>"),
            User("/help"),
            User("Continues session: 2a3b4c5d-0000-0000-0000-000000000000\nold text"),
            User("The real question"),
        ]);

        Assert.Equal("You: The real question", extract);
    }

    [Fact]
    public void MetaRecordsAreDropped()
    {
        var meta = "{\"type\":\"user\",\"isMeta\":true,\"message\":{\"role\":\"user\",\"content\":\"Caveat text\"}}";

        Assert.Equal("You: Keep this", NameExtract.Build([meta, User("Keep this")]));
    }

    [Fact]
    public void AnEmptyTranscriptGivesNull()
    {
        Assert.Null(NameExtract.Build([]));
        Assert.Null(NameExtract.Build(["", "   "]));
    }

    [Fact]
    public void ATranscriptWithOnlyToolTrafficGivesNull()
    {
        Assert.Null(NameExtract.Build([ToolUse, ToolResult, ToolUse]));
    }

    [Fact]
    public void BadLinesAreSkipped()
    {
        var extract = NameExtract.Build(["not json at all", "{", User("Valid one"), "[1,2", AssistantText("Valid two")]);

        Assert.Equal("You: Valid one\nClaude: Valid two", extract);
    }

    [Fact]
    public void ARecordWhoseValuesCannotBeReadCountsForNothing()
    {
        var extract = NameExtract.Build([
            "{\"type\":\"user\",\"message\":\"just a string\"}",
            "{\"type\":\"assistant\",\"message\":{\"content\":5}}",
            "{\"type\":\"user\",\"message\":{\"content\":[\"not an object\"]}}",
            User("Still counted"),
        ]);

        Assert.Equal("You: Still counted", extract);
    }

    [Fact]
    public void ACapTooSmallForTheMarkerIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NameExtract.Build([User("x")], maxChars: 10));
    }
}
