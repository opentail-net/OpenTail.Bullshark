using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Library;

/// <summary>
/// Stage D1 (Core): one library file holds Claude's and Codex's marks under their own keys. A session id under both providers is two entries; a
/// Codex view or write leaves Claude's entries alone, and the reverse holds too; the file keeps the <c>codex:</c> key text. All data is synthetic,
/// in a temporary folder.
/// </summary>
public sealed class CodexLibraryViewTests : IDisposable
{
    private const string SharedId = "shared-session-1";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-codex-library-" + Guid.NewGuid().ToString("N")[..8]);

    public CodexLibraryViewTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string PathIn(string name = "library.json") => Path.Combine(_dir, name);

    private static readonly SessionKey CodexKey = new(ProviderId.Codex, SharedId);
    private static readonly SessionKey ClaudeKey = new(ProviderId.Claude, SharedId);

    [Fact]
    public void AClaudeEntryAndACodexEntrySharingAnIdStaySeparate()
    {
        var library = new SessionLibrary(PathIn());
        library.UpdateAll(entries =>
        {
            entries[ClaudeKey] = new LibraryEntry(Name: "Claude name");
            entries[CodexKey] = new LibraryEntry(Name: "Codex name");
        });

        var all = library.LoadAll(TimeSpan.FromSeconds(5));

        Assert.Equal(2, all.Count);
        Assert.Equal("Claude name", all[ClaudeKey].Name);
        Assert.Equal("Codex name", all[CodexKey].Name);
        Assert.Equal("Claude name", SessionLibrary.ProviderView(all, ProviderId.Claude)[SharedId].Name);
        Assert.Equal("Codex name", SessionLibrary.ProviderView(all, ProviderId.Codex)[SharedId].Name);
    }

    [Fact]
    public void AProviderViewHoldsOnlyThatProvidersEntriesByBareId()
    {
        var library = new SessionLibrary(PathIn());
        library.UpdateAll(entries =>
        {
            entries[ClaudeKey] = new LibraryEntry(Pinned: true);
            entries[new SessionKey(ProviderId.Claude, "claude-only")] = new LibraryEntry(Note: "claude note");
            entries[CodexKey] = new LibraryEntry(Archived: true);
            entries[new SessionKey(ProviderId.Codex, "codex-only")] = new LibraryEntry(Tags: ["codex"]);
        });

        var all = library.LoadAll(TimeSpan.FromSeconds(5));
        var codex = SessionLibrary.ProviderView(all, ProviderId.Codex);
        var claude = SessionLibrary.ProviderView(all, ProviderId.Claude);

        Assert.Equal(new[] { "codex-only", SharedId }, codex.Keys.Order());
        Assert.Equal(new[] { "claude-only", SharedId }, claude.Keys.Order());
        Assert.True(codex[SharedId].Archived);
        Assert.False(claude[SharedId].Archived);
        Assert.True(claude[SharedId].Pinned);
    }

    [Fact]
    public void TheClaudeLoadIsTheClaudeProviderView()
    {
        var library = new SessionLibrary(PathIn());
        library.UpdateAll(entries =>
        {
            entries[ClaudeKey] = new LibraryEntry(Name: "Claude");
            entries[CodexKey] = new LibraryEntry(Name: "Codex");
        });

        var bare = library.Load();

        Assert.Single(bare);
        Assert.Equal("Claude", bare[SharedId].Name);
    }

    [Fact]
    public void ACodexUpdateKeepsTheClaudeEntryForTheSameId()
    {
        var library = new SessionLibrary(PathIn());
        library.Update(claude => claude[SharedId] = new LibraryEntry(Name: "Claude name", Pinned: true));

        library.UpdateProvider(ProviderId.Codex, codex => codex[SharedId] = new LibraryEntry(Name: "Codex name", Tags: ["review"]));

        var all = library.LoadAll(TimeSpan.FromSeconds(5));
        Assert.Equal("Claude name", all[ClaudeKey].Name);
        Assert.True(all[ClaudeKey].Pinned);
        Assert.Null(all[ClaudeKey].Tags);
        Assert.Equal("Codex name", all[CodexKey].Name);
        Assert.Equal(new[] { "review" }, all[CodexKey].Tags);
    }

    [Fact]
    public void AClaudeUpdateKeepsTheCodexEntryForTheSameId()
    {
        var library = new SessionLibrary(PathIn());
        library.UpdateProvider(ProviderId.Codex, codex => codex[SharedId] = new LibraryEntry(Note: "codex note"));

        library.Update(claude => claude[SharedId] = new LibraryEntry(Name: "Claude name"));

        var all = library.LoadAll(TimeSpan.FromSeconds(5));
        Assert.Equal("codex note", all[CodexKey].Note);
        Assert.Equal("Claude name", all[ClaudeKey].Name);
    }

    [Fact]
    public void ACodexEntryLeftWithNothingSetIsRemovedAndClaudesStays()
    {
        var library = new SessionLibrary(PathIn());
        library.Update(claude => claude[SharedId] = new LibraryEntry(Name: "Claude name"));
        library.UpdateProvider(ProviderId.Codex, codex => codex[SharedId] = new LibraryEntry(Archived: true));

        library.UpdateProvider(ProviderId.Codex, codex => codex[SharedId] = new LibraryEntry(Archived: false));

        var all = library.LoadAll(TimeSpan.FromSeconds(5));
        Assert.DoesNotContain(CodexKey, all.Keys);
        Assert.Equal("Claude name", all[ClaudeKey].Name);
        Assert.DoesNotContain("codex:" + SharedId, File.ReadAllText(PathIn()), StringComparison.Ordinal);
    }

    [Fact]
    public void TheFileKeepsTheCodexKeyTextAndTheClaudeKeyText()
    {
        var library = new SessionLibrary(PathIn());
        library.Update(claude => claude[SharedId] = new LibraryEntry(Name: "Claude name"));
        library.UpdateProvider(ProviderId.Codex, codex => codex[SharedId] = new LibraryEntry(Name: "Codex name"));

        var text = File.ReadAllText(PathIn());

        Assert.Contains("\"codex:" + SharedId + "\"", text);
        Assert.Contains("\"claude:" + SharedId + "\"", text);
        Assert.DoesNotContain("\"" + SharedId + "\"", text);
    }

    [Fact]
    public void AProviderViewIsACopyNotTheSource()
    {
        var library = new SessionLibrary(PathIn());
        library.UpdateProvider(ProviderId.Codex, codex => codex[SharedId] = new LibraryEntry(Name: "Codex name"));
        var all = library.LoadAll(TimeSpan.FromSeconds(5));

        var view = SessionLibrary.ProviderView(all, ProviderId.Codex);
        view.Remove(SharedId);

        Assert.Equal("Codex name", library.LoadAll(TimeSpan.FromSeconds(5))[CodexKey].Name);
    }

    [Fact]
    public void AnEmptyProviderViewWritesNoCodexEntries()
    {
        var library = new SessionLibrary(PathIn());
        library.Update(claude => claude[SharedId] = new LibraryEntry(Name: "Claude name"));

        library.UpdateProvider(ProviderId.Codex, _ => { });

        var all = library.LoadAll(TimeSpan.FromSeconds(5));
        Assert.Single(all);
        Assert.Equal("Claude name", all[ClaudeKey].Name);
    }
}
