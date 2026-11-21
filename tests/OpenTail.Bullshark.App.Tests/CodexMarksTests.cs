using System;
using System.IO;
using OpenTail.Bullshark.App;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// Stage D1 (App part): a Codex row's title follows the owner's name first, then the history's title, then the short id; and a Codex row's mark is
/// written under its <c>codex:</c> key and leaves the Claude entry with the same id alone. The window is not built here (it starts supervision and
/// timers): the title rule and the write are the static members the window uses (<see cref="SessionWindow.CodexRowTitle"/> and
/// <see cref="SessionWindow.ApplyMark"/>). The library is in a temporary folder with synthetic entries.
/// </summary>
public sealed class CodexMarksTests : IDisposable
{
    private const string Id = "0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5b";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-codex-marks-" + Guid.NewGuid().ToString("N"));

    public CodexMarksTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string LibraryPath => Path.Combine(_dir, "library.json");

    private static SessionSummary Summary(string? title, string id = Id) =>
        new(new SessionKey(ProviderId.Codex, id), title, "C:\\synthetic\\folder", null, null, false, 1, 1, 0, "synthetic-model");

    [Fact]
    public void ACodexRowsNameWinsOverTheHistoryTitle()
    {
        Assert.Equal("The owner's name", SessionWindow.CodexRowTitle("  The owner's name  ", Summary("History title")));
    }

    [Fact]
    public void WithNoNameTheHistoryTitleIsUsed()
    {
        Assert.Equal("History title", SessionWindow.CodexRowTitle(null, Summary("History title")));
        Assert.Equal("History title", SessionWindow.CodexRowTitle("   ", Summary("History title")));
    }

    [Fact]
    public void WithNeitherTheShortIdIsUsed()
    {
        Assert.Equal("Codex session 0190a2b3", SessionWindow.CodexRowTitle(null, Summary(null)));
        Assert.Equal("Codex session 0190a2b3", SessionWindow.CodexRowTitle(string.Empty, Summary("  ")));
    }

    [Fact]
    public void ACodexMarkIsWrittenUnderItsKeyAndTheClaudeEntryStays()
    {
        var library = new SessionLibrary(LibraryPath);
        SessionWindow.ApplyMark(library, ProviderId.Claude, Id, mark => mark with { Name = "Claude name" });

        SessionWindow.ApplyMark(library, ProviderId.Codex, Id, mark => mark with { Name = "Codex name", Tags = ["review"] });

        var all = library.LoadAll(TimeSpan.FromSeconds(5));
        Assert.Equal("Claude name", all[new SessionKey(ProviderId.Claude, Id)].Name);
        Assert.Equal("Codex name", all[new SessionKey(ProviderId.Codex, Id)].Name);
        Assert.Equal(new[] { "review" }, all[new SessionKey(ProviderId.Codex, Id)].Tags);
        Assert.Contains("\"codex:" + Id + "\"", File.ReadAllText(LibraryPath));
    }

    [Fact]
    public void ACodexArchiveIsWrittenOnlyToTheCodexEntry()
    {
        var library = new SessionLibrary(LibraryPath);
        SessionWindow.ApplyMark(library, ProviderId.Claude, Id, mark => mark with { Pinned = true });

        SessionWindow.ApplyMark(library, ProviderId.Codex, Id, mark => mark with { Archived = true });

        var codex = SessionLibrary.ProviderView(library.LoadAll(TimeSpan.FromSeconds(5)), ProviderId.Codex);
        var claude = SessionLibrary.ProviderView(library.LoadAll(TimeSpan.FromSeconds(5)), ProviderId.Claude);
        Assert.True(codex[Id].Archived);
        Assert.False(claude[Id].Archived);
        Assert.True(claude[Id].Pinned);
    }

    [Fact]
    public void AnEmptyCodexMarkIsRemovedAndTheClaudeEntryStays()
    {
        var library = new SessionLibrary(LibraryPath);
        SessionWindow.ApplyMark(library, ProviderId.Claude, Id, mark => mark with { Name = "Claude name" });
        SessionWindow.ApplyMark(library, ProviderId.Codex, Id, mark => mark with { Archived = true });

        SessionWindow.ApplyMark(library, ProviderId.Codex, Id, mark => mark with { Archived = false });

        var all = library.LoadAll(TimeSpan.FromSeconds(5));
        Assert.Single(all);
        Assert.Equal("Claude name", all[new SessionKey(ProviderId.Claude, Id)].Name);
    }
}
