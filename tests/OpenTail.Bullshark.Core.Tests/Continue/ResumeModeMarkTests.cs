using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.Library;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Tests.Continue;

/// <summary>
/// The resume mode a session remembers (Continue plan, C3, CN-D6): each mode round-trips through library.json as its setting text, an entry without
/// the field loads as before, an unknown stored text loads as no mark without failing the file, and an entry whose only mark is a mode is kept.
/// Each test makes its own folder and removes only that folder.
/// </summary>
public sealed class ResumeModeMarkTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bullshark-resumemode-" + Guid.NewGuid().ToString("N"));

    public ResumeModeMarkTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private string PathIn(string name = "library.json") => Path.Combine(_folder, name);

    [Theory]
    [InlineData(ContinueMode.Full, "full")]
    [InlineData(ContinueMode.LastTurns, "lastTurns")]
    [InlineData(ContinueMode.Summary, "summary")]
    public void EachModeRoundTripsThroughTheLibraryFileAsItsSettingText(ContinueMode mode, string text)
    {
        new SessionLibrary(PathIn()).Update(entries => entries["s1"] = new LibraryEntry(ResumeMode: mode));

        var loaded = new SessionLibrary(PathIn()).Load()["s1"];

        Assert.Equal<ContinueMode?>(mode, loaded.ResumeMode);
        Assert.Contains($"\"ResumeMode\": \"{text}\"", File.ReadAllText(PathIn()));
    }

    [Fact]
    public void TheModeOfACodexSessionIsKeptUnderItsOwnProvider()
    {
        new SessionLibrary(PathIn()).UpdateProvider(ProviderId.Codex, entries => entries["c1"] = new LibraryEntry(ResumeMode: ContinueMode.Full));

        var codex = SessionLibrary.ProviderView(new SessionLibrary(PathIn()).LoadAll(TimeSpan.FromSeconds(5)), ProviderId.Codex);

        Assert.Equal<ContinueMode?>(ContinueMode.Full, codex["c1"].ResumeMode);
    }

    [Fact]
    public void AnOlderEntryWithoutTheFieldLoadsWithNoMarkAsBefore()
    {
        // A library.json written before the field existed: the entry has the fields it had, and no ResumeMode.
        File.WriteAllText(PathIn(), """{ "claude:s1": { "Name": "Old", "Pinned": true } }""");

        var loaded = new SessionLibrary(PathIn()).Load()["s1"];

        Assert.Equal("Old", loaded.Name);
        Assert.True(loaded.Pinned);
        Assert.Null(loaded.ResumeMode);
    }

    [Theory]
    [InlineData("\"tail\"")]
    [InlineData("\"Full\"")]
    [InlineData("\"last-turns\"")]
    [InlineData("\"\"")]
    [InlineData("2")]
    [InlineData("true")]
    [InlineData("null")]
    [InlineData("{ \"mode\": \"full\" }")]
    [InlineData("[\"full\"]")]
    public void AnUnknownStoredModeLoadsAsNoMarkAndTheRestOfTheLibraryLoads(string json)
    {
        File.WriteAllText(PathIn(), "{ \"claude:s1\": { \"Name\": \"Kept\", \"ResumeMode\": " + json + " }, \"claude:s2\": { \"Pinned\": true } }");

        var loaded = new SessionLibrary(PathIn()).Load();

        Assert.Null(loaded["s1"].ResumeMode);
        Assert.Equal("Kept", loaded["s1"].Name);
        Assert.True(loaded["s2"].Pinned);
    }

    [Fact]
    public void AnEntryWhoseOnlyMarkIsAModeIsNotEmpty()
    {
        Assert.False(new LibraryEntry(ResumeMode: ContinueMode.Summary).IsEmpty);
        Assert.True(new LibraryEntry().IsEmpty);
    }

    [Fact]
    public void AnEntryWithOnlyAModeIsKeptByTheFile()
    {
        new SessionLibrary(PathIn()).Update(entries => entries["s1"] = new LibraryEntry(ResumeMode: ContinueMode.Full));

        Assert.Equal<ContinueMode?>(ContinueMode.Full, new SessionLibrary(PathIn()).Load()["s1"].ResumeMode);
    }

    [Fact]
    public void ClearingAModeRemovesAnEntryWithNothingElseSet()
    {
        var library = new SessionLibrary(PathIn());
        library.Update(entries => entries["s1"] = new LibraryEntry(ResumeMode: ContinueMode.LastTurns));

        library.Update(entries => entries["s1"] = entries["s1"] with { ResumeMode = null });

        Assert.False(new SessionLibrary(PathIn()).Load().ContainsKey("s1"));
    }
}
