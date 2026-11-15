using System.Text.Json;
using OpenTail.Bullshark.Core.Activity;

namespace OpenTail.Bullshark.Core.Tests.Activity;

public sealed class SessionTitleTests
{
    [Fact]
    public void SystemNoticesSlashCommandsAndBlanksGiveNoTitle()
    {
        Assert.Null(SessionTitle.Clean("<local-command-caveat>ignore this</local-command-caveat>"));
        Assert.Null(SessionTitle.Clean("/clear"));
        Assert.Null(SessionTitle.Clean("   "));
    }

    [Fact]
    public void TheTitleIsTheFirstLineInSingleSpaces()
    {
        Assert.Equal("Build the rail labels with spaces", SessionTitle.Clean("  Build the rail labels\twith   spaces\nmore text"));
    }

    [Fact]
    public void AppBriefsGiveTheirTaskOrNothing()
    {
        Assert.Null(SessionTitle.Clean("You are ovc-coder, the coder. Your peer is ovc-planner. Wait for batches."));
        Assert.Equal("Build the rail", SessionTitle.Clean("You are ovc-planner, the planner. Your peer is ovc-coder. Task: Build the rail\nmore"));
        Assert.Null(SessionTitle.Clean("You were restarted and are continuing where you left off."));
    }

    [Fact]
    public void AMessageFromAnotherSessionGivesItsBody()
    {
        Assert.Equal("Batch 1: write the file.",
            SessionTitle.Clean("Another Claude session sent a message:\n<cross-session-message from=\"x\">\nBatch 1: write the file."));
    }

    [Fact]
    public void ALongTitleIsShortenedWithAnEllipsis()
    {
        var title = SessionTitle.Clean(new string('a', 120));

        Assert.Equal(SessionTitle.MaxLength, title!.Length);
        Assert.EndsWith("…", title);
    }

    [Fact]
    public void AUserRecordWithATextBlockGivesItsText()
    {
        using var document = JsonDocument.Parse(
            "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"text\",\"text\":\"Fix the scrollbars\"}]}}");

        Assert.Equal("Fix the scrollbars", SessionTitle.FromUserRecord(document.RootElement));
    }

    [Fact]
    public void AToolResultHasNoTitle()
    {
        using var document = JsonDocument.Parse(
            "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":[{\"type\":\"tool_result\",\"tool_use_id\":\"t1\",\"content\":\"ok\"}]}}");

        Assert.Null(SessionTitle.FromUserRecord(document.RootElement));
    }

    [Fact]
    public void TheSummaryTakesTheFirstRealMessage()
    {
        var lines = new[]
        {
            "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"<local-command-caveat>x</local-command-caveat>\"}}",
            "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"Resume the planner\\nand the coder\"}}",
            "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"A later message\"}}",
        };

        Assert.Equal("Resume the planner", TranscriptReader.Summarize(lines).FirstUserText);
    }
}