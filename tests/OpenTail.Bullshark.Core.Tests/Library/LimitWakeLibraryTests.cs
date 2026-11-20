using OpenTail.Bullshark.Core.Library;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Library;

/// <summary>
/// The wake fields of a library entry (limit wake, 2026-11-20): an old file loads with the defaults, the fields round-trip through the file, and an
/// entry with only the flag, or only the record of a wake, is kept rather than dropped. Each test has its own folder under the temp folder.
/// </summary>
public sealed class LimitWakeLibraryTests : IDisposable
{
    private static readonly DateTimeOffset Hit = new(2026, 11, 20, 10, 0, 7, TimeSpan.Zero);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-limitwake-lib-" + Guid.NewGuid().ToString("N")[..8]);

    public LimitWakeLibraryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string PathIn(string name = "library.json") => Path.Combine(_dir, name);

    [Fact]
    public void AFileWrittenBeforeTheFieldsLoadsWithTheDefaults()
    {
        // The library's keys are the property names as they are (PascalCase), as the file has always been written.
        File.WriteAllText(PathIn(), "{\"11111111-2222-3333-4444-555555555555\":{\"Name\":\"old\",\"Pinned\":true}}");

        var entry = new SessionLibrary(PathIn()).Load()["11111111-2222-3333-4444-555555555555"];

        Assert.Equal("old", entry.Name);
        Assert.True(entry.Pinned);
        Assert.False(entry.AutoWake);
        Assert.Null(entry.AutoWokenHitAt);
        Assert.Null(entry.AutoWakeTimes);
    }

    [Fact]
    public void TheWakeFieldsRoundTripThroughTheFile()
    {
        const string id = "22222222-3333-4444-5555-666666666666";
        var library = new SessionLibrary(PathIn());
        var wakes = new[] { Hit.AddHours(1), Hit.AddHours(2) };

        library.Update(entries => entries[id] = new LibraryEntry(AutoWake: true, AutoWokenHitAt: Hit, AutoWakeTimes: wakes));
        var entry = new SessionLibrary(PathIn()).Load()[id];

        Assert.True(entry.AutoWake);
        Assert.Equal(Hit, entry.AutoWokenHitAt);
        Assert.Equal(wakes, entry.AutoWakeTimes);
    }

    [Fact]
    public void AnEntryWithOnlyTheFlagIsKept()
    {
        const string id = "33333333-4444-5555-6666-777777777777";
        var library = new SessionLibrary(PathIn());

        library.Update(entries => entries[id] = new LibraryEntry(AutoWake: true));

        Assert.True(new SessionLibrary(PathIn()).Load().ContainsKey(id));
    }

    [Fact]
    public void AnEntryWithOnlyTheRecordOfAWakeIsKept()
    {
        const string id = "44444444-5555-6666-7777-888888888888";
        var library = new SessionLibrary(PathIn());

        library.Update(entries => entries[id] = new LibraryEntry(AutoWokenHitAt: Hit));

        Assert.True(new SessionLibrary(PathIn()).Load().ContainsKey(id));
    }

    [Fact]
    public void AnEntryWithNothingSetIsStillDropped()
    {
        const string id = "55555555-6666-7777-8888-999999999999";
        var library = new SessionLibrary(PathIn());

        library.Update(entries => entries[id] = new LibraryEntry(AutoWake: false, AutoWakeTimes: []));

        Assert.False(new SessionLibrary(PathIn()).Load().ContainsKey(id));
    }

    [Fact]
    public void IsEmptyCountsEachWakeField()
    {
        Assert.True(new LibraryEntry().IsEmpty);
        Assert.False(new LibraryEntry(AutoWake: true).IsEmpty);
        Assert.False(new LibraryEntry(AutoWokenHitAt: Hit).IsEmpty);
        Assert.False(new LibraryEntry(AutoWakeTimes: [Hit]).IsEmpty);
    }
}
