using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Library;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Library;

/// <summary>The title of a resumed session (L5): "↪ " and the title of the session it continues, when it has no title of its own.</summary>
public sealed class LineageTitleTests
{
    private const string Earlier = "8aed1196-caaa-4c13-81ea-f2ef607248f5";
    private const string Resumed = "17a94483-99fc-4005-9fa9-dbd63958653d";

    private static SessionDigest Digest(string id, string? aiTitle = null, string? firstUser = null, string? continues = null) =>
        new(SessionId: id, FilePath: "unused", ProjectFolder: "unused", IsSubagent: false,
            FirstActivity: null, LastActivity: null, Turns: 0, UserMessages: 0, AssistantMessages: 0, ToolCalls: 0,
            ToolNames: new Dictionary<string, int>(), FilesWritten: [], LastAssistantText: null, LastModel: null,
            ContextTokensAtLastTurn: 0, Usage: TokenUsage.Zero, FirstUserText: firstUser, AiTitle: aiTitle,
            ContinuesSessionId: continues);

    [Fact]
    public void AContinuedSessionWithNoTitleTakesTheEarlierTitle()
    {
        var resumed = Digest(Resumed, continues: Earlier);
        var earlier = Digest(Earlier, aiTitle: "Earlier title");

        Assert.Equal("↪ Earlier title", SessionLibrary.TitleFor(resumed, null, earlier));
    }

    [Fact]
    public void WithNoEarlierSessionItShowsItsShortId()
    {
        var resumed = Digest(Resumed, continues: Earlier);

        Assert.Equal("↪ Session 8aed1196", SessionLibrary.TitleFor(resumed, null, null));
    }

    [Fact]
    public void ItsOwnTitleComesBeforeTheEarlierOne()
    {
        var resumed = Digest(Resumed, aiTitle: "Own title", continues: Earlier);

        Assert.Equal("Own title", SessionLibrary.TitleFor(resumed, null, Digest(Earlier, aiTitle: "Earlier title")));
    }

    [Fact]
    public void ItsFirstMessageComesBeforeTheEarlierOne()
    {
        var resumed = Digest(Resumed, firstUser: "Write the plan", continues: Earlier);

        Assert.Equal("Write the plan", SessionLibrary.TitleFor(resumed, null, Digest(Earlier, aiTitle: "Earlier title")));
    }

    [Fact]
    public void TheOwnersNameComesBeforeEverything()
    {
        var resumed = Digest(Resumed, aiTitle: "Own title", continues: Earlier);

        Assert.Equal("Named", SessionLibrary.TitleFor(resumed, new LibraryEntry(Name: "Named"), Digest(Earlier, aiTitle: "Earlier title")));
    }
}
