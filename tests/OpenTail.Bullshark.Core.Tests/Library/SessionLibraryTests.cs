using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Library;

public sealed class SessionLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-library-" + Guid.NewGuid().ToString("N")[..8]);

    public SessionLibraryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string PathIn(string name = "library.json") => Path.Combine(_dir, name);

    private static SessionDigest Digest(string? aiTitle, string? firstUser) =>
        new(SessionId: "17a94483-99fc-4005-9fa9-dbd63958653d", FilePath: "unused", ProjectFolder: "unused", IsSubagent: false,
            FirstActivity: null, LastActivity: null, Turns: 0, UserMessages: 0, AssistantMessages: 0, ToolCalls: 0,
            ToolNames: new Dictionary<string, int>(), FilesWritten: [], LastAssistantText: null, LastModel: null,
            ContextTokensAtLastTurn: 0, Usage: TokenUsage.Zero, FirstUserText: firstUser, AiTitle: aiTitle);

    [Fact]
    public void AMissingFileIsAnEmptyLibrary()
    {
        var library = new SessionLibrary(PathIn());

        Assert.Empty(library.Load());
        Assert.False(File.Exists(PathIn()), "loading must not write a file");
    }

    [Fact]
    public void AnUnreadableFileThrowsAndIsNotOverwritten()
    {
        File.WriteAllText(PathIn(), "{ this is not json");
        var library = new SessionLibrary(PathIn());

        Assert.Throws<StoreUnreadableException>(() => _ = library.Load());
        Assert.Throws<StoreUnreadableException>(() => library.Update(entries => entries["a"] = new LibraryEntry(Pinned: true)));
        Assert.Equal("{ this is not json", File.ReadAllText(PathIn()));
    }

    [Fact]
    public void AnEmptyEntryIsRemoved()
    {
        var library = new SessionLibrary(PathIn());
        library.Update(entries => entries["s1"] = new LibraryEntry(Pinned: true));

        library.Update(entries => entries["s1"] = new LibraryEntry(Pinned: false));

        Assert.Empty(library.Load());
        Assert.DoesNotContain("s1", File.ReadAllText(PathIn()));
    }

    [Fact]
    public void TwoUpdatesBothLand()
    {
        // Each update reads the file again under the lock, so the second one keeps what the first one wrote.
        var first = new SessionLibrary(PathIn());
        var second = new SessionLibrary(PathIn());
        first.Update(entries => entries["a"] = new LibraryEntry(Name: "Alpha"));

        second.Update(entries => entries["b"] = new LibraryEntry(Name: "Beta"));

        var loaded = new SessionLibrary(PathIn()).Load();
        Assert.Equal("Alpha", loaded["a"].Name);
        Assert.Equal("Beta", loaded["b"].Name);
    }

    [Fact]
    public void TheEntryIsKeptAcrossALoad()
    {
        var library = new SessionLibrary(PathIn());
        library.Update(entries => entries["s1"] = new LibraryEntry(Name: "Plan", Pinned: true, Archived: false,
            Tags: ["plan", "review"], Note: "the first plan", Colour: "blue"));

        var loaded = new SessionLibrary(PathIn()).Load()["s1"];

        Assert.Equal("Plan", loaded.Name);
        Assert.True(loaded.Pinned);
        Assert.Equal(new[] { "plan", "review" }, loaded.Tags);
        Assert.Equal("the first plan", loaded.Note);
        Assert.Equal("blue", loaded.Colour);
    }

    [Fact]
    public void TheOwnersNameComesFirst()
    {
        var digest = Digest(aiTitle: "Claude's title", firstUser: "Write the plan");

        Assert.Equal("Owner's name", SessionLibrary.TitleFor(digest, new LibraryEntry(Name: "Owner's name")));
        Assert.Equal("Claude's title", SessionLibrary.TitleFor(digest, new LibraryEntry(Pinned: true)));
        Assert.Equal("Claude's title", SessionLibrary.TitleFor(digest, new LibraryEntry(Name: "   ")));
        Assert.Equal("Claude's title", SessionLibrary.TitleFor(digest, null));
    }

    [Fact]
    public void TagsAreSplitAndLowerCased()
    {
        Assert.Equal(new[] { "plan", "review", "a2a" }, SessionLibrary.TagsOf("Plan, REVIEW  a2a,plan"));
        Assert.Empty(SessionLibrary.TagsOf(null));
        Assert.Empty(SessionLibrary.TagsOf(" , "));
    }

    [Fact]
    public void BesideTheStateFileIsLibraryJsonInTheSameFolder()
    {
        var library = SessionLibrary.Beside(Path.Combine(_dir, "state.json"));

        Assert.Equal(Path.Combine(_dir, "library.json"), library.FilePath);
    }

    [Fact]
    public void ANullEntryIsLeftOutLoggedOnceAndDroppedBySaving()
    {
        var path = PathIn();
        File.WriteAllText(path, "{\"a\":null,\"b\":{\"Pinned\":true}}");
        var logged = new List<string>();
        var library = new SessionLibrary(path, logged.Add);

        Assert.Equal(new[] { "b" }, library.Load().Keys);
        Assert.Single(logged);

        library.Update(entries => entries["c"] = new LibraryEntry(Name: "named"));

        Assert.Equal(new[] { "b", "c" }, library.Load().Keys.OrderBy(key => key, StringComparer.Ordinal));
        Assert.Single(logged);
    }

    [Fact]
    public void AFileThatCannotBeReadIsAnErrorAndIsNotReadAsEmpty()
    {
        var path = PathIn();
        File.WriteAllText(path, "{\"b\":{\"Pinned\":true}}");
        var library = new SessionLibrary(path);

        // Another process holds the file without sharing it. Reading it as empty would let the next save overwrite it.
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<StoreUnreadableException>(() => library.Load());
            Assert.Throws<StoreUnreadableException>(() => library.Update(entries => entries["c"] = new LibraryEntry(Name: "x")));
        }

        Assert.Equal("{\"b\":{\"Pinned\":true}}", File.ReadAllText(path));
    }
}
