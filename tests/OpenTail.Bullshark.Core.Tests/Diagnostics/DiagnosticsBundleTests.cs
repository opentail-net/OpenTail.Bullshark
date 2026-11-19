using System.IO.Compression;
using OpenTail.Bullshark.Core.Diagnostics;
using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Diagnostics;

/// <summary>
/// The diagnostics zip (firstrun batch B4). The fixture state folder holds a canary string in every file the zip could be built from, and in
/// the files it must never read (cache, library, runs, transcripts, a note). No canary may appear in any entry, and no forbidden entry may exist.
/// </summary>
public sealed class DiagnosticsBundleTests : IDisposable
{
    private const string Profile = @"C:\Users\CANARYUSER";

    /// <summary>Every canary the fixture plants. None may come out of the zip.</summary>
    private static readonly string[] Canaries =
    [
        "CANARYAPPLOG", "CANARYPROMPT", "CANARYEVENT", "CANARYUSER", "CANARYNOTE", "CANARYKEYREVIEW",
        "CANARYLIBRARY", "CANARYRUNS", "CANARYCACHE", "CANARYTRANSCRIPT", "CANARYNOTEFILE",
    ];

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bullshark-fr-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _state;

    public DiagnosticsBundleTests()
    {
        _state = Path.Combine(_root, "state");
        Directory.CreateDirectory(Path.Combine(_state, "logs"));
        Directory.CreateDirectory(Path.Combine(_state, "cache"));
        Directory.CreateDirectory(Path.Combine(_state, "projects", "C--work"));
        WriteFixture();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private void WriteFixture()
    {
        File.WriteAllLines(Path.Combine(_state, "logs", "app.log"),
        [
            @"2026-11-19T10:00:00 pass failed: C:\Users\CANARYUSER\AppData\Local\OpenTail.Bullshark\runs.json",
            "2026-11-19T10:00:01 token sk-ant-api03-CANARYAPPLOG0123456789 was used",
            "2026-11-19T10:00:02 prompt: CANARYPROMPT please refactor the whole repository",
            "2026-11-19T10:00:03 an ordinary line about the pass",
        ]);

        var events = new EventLog(Path.Combine(_state, "events.jsonl"));
        events.Write(new SupervisorEvent(new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero), "coder", "alert:Failed",
            @"token sk-ant-api03-CANARYEVENT0123456789 at C:\Users\CANARYUSER\work"));
        events.Write(new SupervisorEvent(new DateTimeOffset(2026, 11, 19, 10, 0, 5, TimeSpan.Zero), "coder", "state",
            "prompt: CANARYPROMPT in the detail"));
        events.Write(new SupervisorEvent(new DateTimeOffset(2026, 11, 19, 10, 0, 9, TimeSpan.Zero), "planner", "start", string.Empty));

        File.WriteAllText(Path.Combine(_state, "ui.json"),
            """{"railTheme":"Dark","note":"CANARYNOTE free text the owner typed","quietHours":"22:00-07:00","zoom":1.25,"notify":false,"reviewed":{"CANARYKEYREVIEW":"2026-11-19"}}""");
        File.WriteAllText(Path.Combine(_state, "library.json"), """{"CANARYLIBRARY":true}""");
        File.WriteAllText(Path.Combine(_state, "runs.json"), """[{"CANARYRUNS":1}]""");
        File.WriteAllText(Path.Combine(_state, "cache", "index.json"), "CANARYCACHE");
        File.WriteAllText(Path.Combine(_state, "projects", "C--work", "session.jsonl"), """{"message":"CANARYTRANSCRIPT"}""");
        File.WriteAllText(Path.Combine(_state, "notes.txt"), "CANARYNOTEFILE");
    }

    private DiagnosticsInput Input(IReadOnlyList<string>? appLog = null, IReadOnlyList<string>? events = null) => new(
        VersionsText: "App: 0.1.0 (abc1234)" + Environment.NewLine + "OS: Windows 10.0.26200",
        SetupText: DiagnosticsBundle.SetupText([
            new SetupItem("Claude Code", "claude", SetupState.Ok, @"found: C:\Users\CANARYUSER\.local\bin\claude.exe"),
            new SetupItem("Settings", "settings.json", SetupState.Warning, "not found"),
        ]),
        PreflightText: DiagnosticsBundle.PreflightText(new PreflightReport(
            [new PreflightRow("Runs", PreflightStatus.Ok, "none waiting")], "Ready for the night")),
        AppLogLines: appLog ?? DiagnosticsSources.LastLines(Path.Combine(_state, "logs", "app.log"), 500),
        EventLines: events ?? DiagnosticsSources.EventLines(Path.Combine(_state, "events.jsonl"), 200),
        SettingsText: DiagnosticsSources.SettingsText(Path.Combine(_state, "ui.json")));

    private static Dictionary<string, string> ReadZip(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var reader = new StreamReader(entry.Open());
            return reader.ReadToEnd();
        });
    }

    [Fact]
    public void NoCanaryInAnyEntryAndNoForbiddenEntry()
    {
        var zipPath = DiagnosticsBundle.Save(_state, new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero), Input(), Profile);

        var entries = ReadZip(zipPath);

        Assert.Equal(new[] { "app.log", "events.tail.txt", "preflight.txt", "settings.txt", "setup-check.txt", "versions.txt" },
            entries.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray());
        foreach (var (name, text) in entries)
        {
            foreach (var canary in Canaries) Assert.False(text.Contains(canary, StringComparison.Ordinal), $"{canary} survived in {name}");
            Assert.DoesNotContain("CANARY", text);
        }
        foreach (var forbidden in new[] { "cache", "library", "runs", "projects", "transcript", "notes" })
        {
            Assert.DoesNotContain(entries.Keys, name => name.Contains(forbidden, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void TheLogLinesAreRedactedAndTheStateFilesReadAreWhatTheZipShows()
    {
        var entries = ReadZip(DiagnosticsBundle.Save(_state, new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero), Input(), Profile));

        Assert.Contains("pass failed: ~\\AppData\\Local\\OpenTail.Bullshark\\runs.json", entries["app.log"]);
        Assert.Contains(Redactor.TokenMark, entries["app.log"]);
        Assert.Contains("an ordinary line about the pass", entries["app.log"]);
        Assert.Contains(Redactor.PromptMark, entries["app.log"]);
        Assert.Contains("found: ~\\.local\\bin\\claude.exe", entries["setup-check.txt"]);
        Assert.Contains("Ready for the night", entries["preflight.txt"]);
        Assert.Contains("App: 0.1.0 (abc1234)", entries["versions.txt"]);
    }

    [Fact]
    public void TheSettingsShowKeysAndNonTextValuesOnly()
    {
        var settings = DiagnosticsSources.SettingsText(Path.Combine(_state, "ui.json"));

        Assert.Contains("railTheme: Dark", settings);
        Assert.Contains("quietHours: 22:00-07:00", settings);
        Assert.Contains("zoom: 1.25", settings);
        Assert.Contains("notify: false", settings);
        Assert.Contains("note: (text, ", settings);
        Assert.Contains("reviewed: (object, 1 entries)", settings);
    }

    [Fact]
    public void ASettingsFileThatIsMissingOrDamagedSaysSo()
    {
        Assert.Equal("(no ui.json)", DiagnosticsSources.SettingsText(Path.Combine(_root, "missing.json")));

        var damaged = Path.Combine(_root, "damaged.json");
        File.WriteAllText(damaged, "{ not json");
        Assert.StartsWith("(ui.json could not be read: ", DiagnosticsSources.SettingsText(damaged));

        var array = Path.Combine(_root, "array.json");
        File.WriteAllText(array, "[1,2]");
        Assert.Equal("(ui.json is not an object)", DiagnosticsSources.SettingsText(array));
    }

    [Fact]
    public void AMissingLogIsNoLinesAndAnEventFileThatCannotBeReadSaysSo()
    {
        Assert.Empty(DiagnosticsSources.LastLines(Path.Combine(_root, "none.log"), 10));
        Assert.Empty(DiagnosticsSources.EventLines(Path.Combine(_root, "none.jsonl"), 10));
    }

    [Fact]
    public void TheAppLogIsCutToTheLast500LinesAndTheEventsTo200()
    {
        var log = Enumerable.Range(1, 600).Select(n => $"line {n:000}").ToList();
        var events = Enumerable.Range(1, 250).Select(n => $"event {n:000}").ToList();

        var entries = DiagnosticsBundle.Entries(Input(appLog: log, events: events), Profile).ToDictionary(e => e.Name, e => e.Text);

        Assert.DoesNotContain("line 100", entries["app.log"]);
        Assert.Contains("line 101", entries["app.log"]);
        Assert.Contains("line 600", entries["app.log"]);
        Assert.DoesNotContain("event 050", entries["events.tail.txt"]);
        Assert.Contains("event 051", entries["events.tail.txt"]);
    }

    [Fact]
    public void TheNewestFiveZipsAreKept()
    {
        var start = new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 7; i++)
        {
            DiagnosticsBundle.Save(_state, start.AddMinutes(i), Input(), Profile);
        }

        var folder = Path.Combine(_state, DiagnosticsBundle.FolderName);
        var kept = Directory.GetFiles(folder, DiagnosticsBundle.Pattern).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.Equal(5, kept.Count);
        Assert.Equal($"diagnostics-{start.AddMinutes(6).ToLocalTime():yyyyMMdd-HHmmss}.zip", kept[^1]);
        Assert.Equal($"diagnostics-{start.AddMinutes(2).ToLocalTime():yyyyMMdd-HHmmss}.zip", kept[0]);
    }

    [Fact]
    public void TheZipIsWrittenBesideTheStateFileInTheDiagnosticsFolder()
    {
        var path = DiagnosticsBundle.Save(_state, new DateTimeOffset(2026, 11, 19, 10, 0, 0, TimeSpan.Zero), Input(), Profile);

        Assert.Equal(Path.Combine(_state, "diagnostics"), Path.GetDirectoryName(path));
        Assert.StartsWith("diagnostics-", Path.GetFileName(path));
    }

    [Fact]
    public void TheVersionLineShowsTheHashAndTheClaudeVersionOnlyWhenKnown()
    {
        var (version, hash) = AppVersion.Split("0.1.0+abc1234");

        Assert.Equal("Version 0.1.0 (abc1234) · .NET 10 · Claude Code 2.1.326", AppVersion.Line(version, hash, 10, "2.1.326"));
        Assert.Equal("Version 0.1.0 · .NET 10", AppVersion.Line("0.1.0", null, 10, null));
    }

    [Theory]
    [InlineData("0.1.0+abc1234", "0.1.0", "abc1234")]
    [InlineData("0.1.0", "0.1.0", null)]
    [InlineData("0.1.0+", "0.1.0", null)]
    [InlineData("  0.2.0+f00ba11  ", "0.2.0", "f00ba11")]
    public void TheInformationalVersionIsSplitAtThePlus(string text, string version, string? hash)
    {
        Assert.Equal((version, hash), AppVersion.Split(text));
    }

    [Fact]
    public void AMissingInformationalVersionIsUnknown()
    {
        Assert.Equal(("unknown", null), AppVersion.Split(null));
    }
}
