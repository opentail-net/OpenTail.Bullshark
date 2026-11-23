using System;
using System.IO;
using OpenTail.Bullshark.App;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.OpenCode;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// OC2d (the pure parts of the opencode marks, the rail's view menu and the "+" under opencode): the mark view each provider reads, an opencode mark
/// written under its own key and read back by its view, the views the opencode rail offers, and the command line of the "+". The library is in a
/// temporary folder with synthetic entries. No window, no process, no model call.
/// </summary>
public sealed class OpenCodeMarksAndMenusTests : IDisposable
{
    private const string Id = "ses_0123456789abcdefABCDEFGHIJ";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-opencode-marks-" + Guid.NewGuid().ToString("N"));

    public OpenCodeMarksAndMenusTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string LibraryPath => Path.Combine(_dir, "library.json");

    [Fact]
    public void EachProviderReadsItsOwnMarkView()
    {
        Assert.Equal(ProviderId.Claude, SessionWindow.MarkViewOf(ProviderId.Claude));
        Assert.Equal(ProviderId.Codex, SessionWindow.MarkViewOf(ProviderId.Codex));
        Assert.Equal(ProviderId.Copilot, SessionWindow.MarkViewOf(ProviderId.Copilot));
        Assert.Equal(ProviderId.OpenCode, SessionWindow.MarkViewOf(ProviderId.OpenCode));
    }

    [Fact]
    public void TheOpenCodeViewIsNamedByTheOpenCodeKey()
    {
        Assert.Equal("opencode", SessionWindow.MarkViewOf(ProviderId.OpenCode).Value);
    }

    [Fact]
    public void AProviderWithNoViewOfItsOwnReadsClaudes()
    {
        Assert.Equal(ProviderId.Claude, SessionWindow.MarkViewOf(new ProviderId("unknown")));
    }

    [Fact]
    public void AnOpenCodeMarkIsWrittenUnderItsKeyAndReadBackByItsViewAndClaudesStays()
    {
        var library = new SessionLibrary(LibraryPath);
        SessionWindow.ApplyMark(library, ProviderId.Claude, Id, mark => mark with { Name = "Claude name" });

        SessionWindow.ApplyMark(library, ProviderId.OpenCode, Id, mark => mark with { Name = "opencode name", Tags = ["review"] });

        var all = library.LoadAll(TimeSpan.FromSeconds(5));
        var openCode = SessionLibrary.ProviderView(all, SessionWindow.MarkViewOf(ProviderId.OpenCode));
        var claude = SessionLibrary.ProviderView(all, SessionWindow.MarkViewOf(ProviderId.Claude));
        Assert.Equal("opencode name", openCode[Id].Name);
        Assert.Equal(new[] { "review" }, openCode[Id].Tags);
        Assert.Equal("Claude name", claude[Id].Name);
        Assert.DoesNotContain("review", claude[Id].Tags ?? Array.Empty<string>());
        Assert.Contains("\"opencode:" + Id + "\"", File.ReadAllText(LibraryPath));
    }

    [Fact]
    public void TheOpenCodeRailViewsAreAllHistoryAndArchived()
    {
        Assert.True(SessionWindow.IsOpenCodeView(RailFilter.All));
        Assert.True(SessionWindow.IsOpenCodeView(RailFilter.History));
        Assert.True(SessionWindow.IsOpenCodeView(RailFilter.Archived));
        Assert.False(SessionWindow.IsOpenCodeView(RailFilter.Running));
        Assert.False(SessionWindow.IsOpenCodeView(RailFilter.Temporary));
    }

    [Fact]
    public void ThePlusUnderOpenCodeBuildsTheOpenCodeNewCommandForTheFolder()
    {
        const string folder = @"C:\Work\app";
        var line = SessionWindow.OpenCodeNewCommandLine(folder);
        Assert.Equal(new OpenCodeTerminalOpener().NewCommandLine(folder), line);
        Assert.Contains("opencode", line);
        Assert.DoesNotContain("copilot", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("claude", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ThePlusRefusesAFolderTheCommandBuilderRefuses()
    {
        Assert.Throws<ArgumentException>(() => SessionWindow.OpenCodeNewCommandLine("C:\\Work\\a\"b"));
        Assert.Throws<ArgumentException>(() => SessionWindow.OpenCodeNewCommandLine("C:\\Work\\100%"));
    }
}
