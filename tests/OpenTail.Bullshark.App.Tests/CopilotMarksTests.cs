using System;
using System.IO;
using OpenTail.Bullshark.App;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// Stage D1 (App part): a Copilot row's title follows the owner's name first, then the history's title, then the short id; and a Copilot row's mark is
/// written under its <c>copilot:</c> key and leaves the Claude entry with the same id alone. The window is not built here (it starts supervision and
/// timers): the title rule and the write are the static members the window uses (<see cref="SessionWindow.CopilotRowTitle"/> and
/// <see cref="SessionWindow.ApplyMark"/>). The library is in a temporary folder with synthetic entries.
/// </summary>
public sealed class CopilotMarksTests : IDisposable
{
    private const string Id = "0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5b";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-copilot-marks-" + Guid.NewGuid().ToString("N"));

    public CopilotMarksTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string LibraryPath => Path.Combine(_dir, "library.json");

    private static SessionSummary Summary(string? title, string id = Id) =>
        new(new SessionKey(ProviderId.Copilot, id), title, "C:\\synthetic\\folder", null, null, false, 1, 1, 0, "synthetic-model");

    [Fact]
    public void ACopilotRowsNameWinsOverTheHistoryTitle()
    {
        Assert.Equal("The owner's name", SessionWindow.CopilotRowTitle("  The owner's name  ", Summary("History title")));
    }

    [Fact]
    public void WithNoNameTheHistoryTitleIsUsed()
    {
        Assert.Equal("History title", SessionWindow.CopilotRowTitle(null, Summary("History title")));
        Assert.Equal("History title", SessionWindow.CopilotRowTitle("   ", Summary("History title")));
    }

    [Fact]
    public void WithNeitherTheShortIdIsUsed()
    {
        Assert.Equal("Copilot session 0190a2b3", SessionWindow.CopilotRowTitle(null, Summary(null)));
        Assert.Equal("Copilot session 0190a2b3", SessionWindow.CopilotRowTitle(string.Empty, Summary("  ")));
    }

    [Fact]
    public void ACopilotMarkIsWrittenUnderItsKeyAndTheClaudeEntryStays()
    {
        var library = new SessionLibrary(LibraryPath);
        SessionWindow.ApplyMark(library, ProviderId.Claude, Id, mark => mark with { Name = "Claude name" });

        SessionWindow.ApplyMark(library, ProviderId.Copilot, Id, mark => mark with { Name = "Copilot name", Tags = ["review"] });

        var all = library.LoadAll(TimeSpan.FromSeconds(5));
        Assert.Equal("Claude name", all[new SessionKey(ProviderId.Claude, Id)].Name);
        Assert.Equal("Copilot name", all[new SessionKey(ProviderId.Copilot, Id)].Name);
        Assert.Equal(new[] { "review" }, all[new SessionKey(ProviderId.Copilot, Id)].Tags);
        Assert.Contains("\"copilot:" + Id + "\"", File.ReadAllText(LibraryPath));
    }

    [Fact]
    public void ACopilotArchiveIsWrittenOnlyToTheCopilotEntry()
    {
        var library = new SessionLibrary(LibraryPath);
        SessionWindow.ApplyMark(library, ProviderId.Claude, Id, mark => mark with { Pinned = true });

        SessionWindow.ApplyMark(library, ProviderId.Copilot, Id, mark => mark with { Archived = true });

        var copilot = SessionLibrary.ProviderView(library.LoadAll(TimeSpan.FromSeconds(5)), ProviderId.Copilot);
        var claude = SessionLibrary.ProviderView(library.LoadAll(TimeSpan.FromSeconds(5)), ProviderId.Claude);
        Assert.True(copilot[Id].Archived);
        Assert.False(claude[Id].Archived);
        Assert.True(claude[Id].Pinned);
    }

    [Fact]
    public void AnEmptyCopilotMarkIsRemovedAndTheClaudeEntryStays()
    {
        var library = new SessionLibrary(LibraryPath);
        SessionWindow.ApplyMark(library, ProviderId.Claude, Id, mark => mark with { Name = "Claude name" });
        SessionWindow.ApplyMark(library, ProviderId.Copilot, Id, mark => mark with { Archived = true });

        SessionWindow.ApplyMark(library, ProviderId.Copilot, Id, mark => mark with { Archived = false });

        var all = library.LoadAll(TimeSpan.FromSeconds(5));
        Assert.Single(all);
        Assert.Equal("Claude name", all[new SessionKey(ProviderId.Claude, Id)].Name);
    }
}
