using OpenTail.Bullshark.Core.Codex;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>
/// Step R1: the messages of a rollout as transcript lines. Pure: the lines are synthetic records built by <see cref="CodexHome"/>, and no file is read.
/// </summary>
public sealed class CodexTurnsTests
{
    [Fact]
    public void TheStandardRolloutGivesItsThreeMessagesWithTheKindsAndTheOrder()
    {
        var turns = CodexTurns.FromRollout(CodexHome.Standard(CodexHome.SessionId));

        Assert.Equal(3, turns.Count);
        Assert.Equal(new[] { "you", "codex", "you" }, turns.Select(t => t.Kind).ToArray());
        Assert.Equal(new[] { CodexHome.FirstMessage, "Done", "Second question" }, turns.Select(t => t.Text).ToArray());
    }

    [Fact]
    public void NumbersRunFromOneOverTheKeptMessages()
    {
        var turns = CodexTurns.FromRollout(CodexHome.Standard(CodexHome.SessionId));

        Assert.Equal(new[] { 1, 2, 3 }, turns.Select(t => t.Number).ToArray());
        Assert.All(turns, t => Assert.False(t.Collapsed));
    }

    [Fact]
    public void InjectedContextAndEmptyTextAreSkippedAndTheNumbersStillRunFromOne()
    {
        var lines = new[]
        {
            CodexHome.Message("2026-11-20T10:00:01.000Z", "user", "<environment_context>skipped</environment_context>"),
            CodexHome.Record("2026-11-20T10:00:02.000Z", "response_item", "{\"type\":\"message\",\"role\":\"user\",\"content\":\"\"}"),
            CodexHome.Record("2026-11-20T10:00:03.000Z", "response_item", "{\"type\":\"message\",\"role\":\"assistant\",\"content\":[]}"),
            CodexHome.Message("2026-11-20T10:00:04.000Z", "user", "kept question"),
        };

        var turns = CodexTurns.FromRollout(lines);

        var line = Assert.Single(turns);
        Assert.Equal(1, line.Number);
        Assert.Equal("you", line.Kind);
        Assert.Equal("kept question", line.Text);
    }

    [Fact]
    public void BadLinesAndNonMessageRecordsAreSkipped()
    {
        var lines = new[]
        {
            "this is not json",
            "",
            "[1,2,3]",
            "{\"type\":\"response_item\",\"payload\":",
            CodexHome.Meta(CodexHome.SessionId),
            CodexHome.Record("2026-11-20T10:00:03.000Z", "response_item", "{\"type\":\"function_call\",\"name\":\"shell\",\"arguments\":\"x\"}"),
            CodexHome.Record("2026-11-20T10:00:03.000Z", "response_item", "{\"type\":\"message\",\"role\":\"developer\",\"content\":\"not kept\"}"),
            CodexHome.Message("2026-11-20T10:00:05.000Z", "assistant", "kept reply"),
        };

        var line = Assert.Single(CodexTurns.FromRollout(lines));
        Assert.Equal("codex", line.Kind);
        Assert.Equal("kept reply", line.Text);
    }

    [Fact]
    public void TheTimestampOfTheRecordIsReadWhenPresent()
    {
        var lines = new[] { CodexHome.Message("2026-11-20T10:00:02.000Z", "user", "timed") };

        var line = Assert.Single(CodexTurns.FromRollout(lines));

        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 0, 2, TimeSpan.Zero), line.Time);
    }

    [Fact]
    public void TheTimestampIsNullWhenTheRecordHasNone()
    {
        var lines = new[]
        {
            "{\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"user\",\"content\":\"no time here\"}}",
        };

        var line = Assert.Single(CodexTurns.FromRollout(lines));

        Assert.Null(line.Time);
        Assert.Equal("no time here", line.Text);
    }

    [Fact]
    public void AnUnreadableTimestampIsNull()
    {
        var lines = new[]
        {
            "{\"timestamp\":\"not a date\",\"type\":\"response_item\",\"payload\":{\"type\":\"message\",\"role\":\"user\",\"content\":\"text\"}}",
        };

        var line = Assert.Single(CodexTurns.FromRollout(lines));

        Assert.Null(line.Time);
    }

    [Fact]
    public void PartsOfAMessageAreJoinedWithANewline()
    {
        var lines = new[]
        {
            CodexHome.Record("2026-11-20T10:00:03.000Z", "response_item",
                "{\"type\":\"message\",\"role\":\"assistant\",\"content\":[{\"type\":\"output_text\",\"text\":\"first part\"},{\"type\":\"output_text\",\"text\":\"second part\"}]}"),
        };

        var line = Assert.Single(CodexTurns.FromRollout(lines));

        Assert.Equal("first part\nsecond part", line.Text);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 0, 3, TimeSpan.Zero), line.Time);
    }

    [Fact]
    public void AnEmptyRolloutGivesNoLines()
    {
        Assert.Empty(CodexTurns.FromRollout(Array.Empty<string>()));
    }
}
