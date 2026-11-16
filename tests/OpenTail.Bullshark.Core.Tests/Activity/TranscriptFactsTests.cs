using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// The facts Claude Code writes into a transcript for free (session-library L1): the title, the custom title, the recap and
/// the compactions. Synthetic records with the shape seen on 2026-11-16 (CLI 2.1.325). No real content is used.
/// </summary>
public sealed class TranscriptFactsTests
{
    private const string SessionId = "17a94483-99fc-4005-9fa9-dbd63958653d";

    private const string User =
        """{"type":"user","timestamp":"2026-11-14T10:00:00.000Z","message":{"role":"user","content":"Write the plan"}}""";

    private static string AiTitle(string title) => $$"""{"type":"ai-title","aiTitle":"{{title}}","sessionId":"{{SessionId}}"}""";

    private static string Recap(string content, string timestamp) =>
        $$"""{"type":"system","subtype":"away_summary","content":"{{content}}","timestamp":"{{timestamp}}"}""";

    private const string Compaction =
        """{"type":"system","subtype":"compact_boundary","content":"Conversation compacted","level":"info","compactMetadata":{"trigger":"auto","preTokens":967530,"postTokens":34807}}""";

    private static SessionDigest Digest(string? aiTitle, string? firstUser, string? customTitle = null) =>
        new(SessionId: SessionId, FilePath: "unused", ProjectFolder: "unused", IsSubagent: false,
            FirstActivity: null, LastActivity: null, Turns: 0, UserMessages: 0, AssistantMessages: 0, ToolCalls: 0,
            ToolNames: new Dictionary<string, int>(), FilesWritten: [], LastAssistantText: null, LastModel: null,
            ContextTokensAtLastTurn: 0, Usage: TokenUsage.Zero, FirstUserText: firstUser, AiTitle: aiTitle,
            CustomTitle: customTitle);

    [Fact]
    public void TheLastAiTitleWins()
    {
        // Claude Code repeats the title in one file, so the last one is the current one.
        var summary = TranscriptReader.Summarize([AiTitle("First title"), User, AiTitle("Second title"), AiTitle("Third title")]);

        Assert.Equal("Third title", summary.AiTitle);
    }

    [Fact]
    public void ATranscriptWithNoAiTitleKeepsTheFirstMessage()
    {
        var summary = TranscriptReader.Summarize([User]);

        Assert.Null(summary.AiTitle);
        Assert.Equal("Write the plan", summary.FirstUserText);
        Assert.Equal("Write the plan", Digest(null, summary.FirstUserText).Title);
    }

    [Fact]
    public void TheNewestRecapIsKept()
    {
        var summary = TranscriptReader.Summarize(
        [
            Recap("Goal one", "2026-11-14T10:05:00.000Z"),
            User,
            Recap("Goal two", "2026-11-14T11:05:00.000Z"),
        ]);

        Assert.Equal("Goal two", summary.LastRecap);
        Assert.Equal(DateTimeOffset.Parse("2026-11-14T11:05:00Z"), summary.LastRecapAt);
    }

    [Fact]
    public void CompactionsAreCounted()
    {
        var summary = TranscriptReader.Summarize([User, Compaction, User, Compaction]);

        Assert.Equal(2, summary.Compactions);
    }

    [Fact]
    public void AnAiTitleRecordWithNoTextIsIgnored()
    {
        var blank = TranscriptReader.Summarize([AiTitle("First title"), """{"type":"ai-title","aiTitle":"   "}""", User]);
        var missing = TranscriptReader.Summarize(["""{"type":"ai-title","sessionId":"x"}""", User]);

        Assert.Equal("First title", blank.AiTitle);
        Assert.Null(missing.AiTitle);
    }

    [Fact]
    public void TheTitleIsTheAiTitleThenTheFirstMessage()
    {
        Assert.Equal("Claude's title", Digest("Claude's title", "Write the plan").Title);
        Assert.Equal("Write the plan", Digest(null, "Write the plan").Title);
    }

    [Fact]
    public void TheCustomTitleIsNotTheTitle()
    {
        // A custom title is a role name (for example "ovc-coder"), so it is shown in the tooltip only.
        var summary = TranscriptReader.Summarize([User, """{"type":"custom-title","customTitle":"ovc-coder"}"""]);

        Assert.Equal("ovc-coder", summary.CustomTitle);
        Assert.Equal("Write the plan", Digest(null, summary.FirstUserText, summary.CustomTitle).Title);
    }

    [Fact]
    public void TheTitleFallsBackToTheShortId()
    {
        Assert.Equal("Session 17a94483", Digest(null, null).Title);
    }
}
