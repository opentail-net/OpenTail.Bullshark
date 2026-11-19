using System.Globalization;
using OpenTail.Bullshark.Core.Diagnostics;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Diagnostics;

/// <summary>The crash file text and its retention (firstrun batch B3). Files are written only in a unique temp folder, which is then deleted.</summary>
public sealed class CrashReportTests : IDisposable
{
    private const string Profile = @"C:\Users\CANARYUSER";
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bullshark-fr-" + Guid.NewGuid().ToString("N")[..8]);

    public CrashReportTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static CrashInput Input(Exception ex, IReadOnlyList<string> lines, DateTimeOffset? at = null) =>
        new(at ?? new DateTimeOffset(2026, 11, 19, 3, 12, 45, TimeSpan.Zero), "0.1.0+abc1234", "Windows 10.0.26200", ".NET 10.0.1", ex, lines, Profile);

    /// <summary>An exception with a real stack, thrown here, so the stack lines name this test file.</summary>
    private static Exception Thrown(string message)
    {
        try
        {
            throw new InvalidOperationException(message);
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    [Fact]
    public void TheReportNamesTheVersionTheSystemAndTheException()
    {
        var text = CrashReport.Build(Input(new InvalidOperationException("the pass broke"), ["a line"]));

        Assert.Contains("App: 0.1.0+abc1234", text);
        Assert.Contains("OS: Windows 10.0.26200", text);
        Assert.Contains("Runtime: .NET 10.0.1", text);
        var at = new DateTimeOffset(2026, 11, 19, 3, 12, 45, TimeSpan.Zero);
        Assert.Contains($"When: {at.ToLocalTime():yyyy-MM-dd HH:mm:ss}", text); // local time, as the owner reads it
        Assert.Contains("System.InvalidOperationException: the pass broke", text);
    }

    [Fact]
    public void TheExceptionAndTheLogLinesGoThroughTheRedactor()
    {
        var ex = Thrown(@"token sk-ant-api03-CANARYCRASH0123456789 read C:\Users\CANARYUSER\state.json");
        var text = CrashReport.Build(Input(ex, [
            "2026-11-19 10:00:00 prompt: CANARYPROMPT line",
            @"2026-11-19 10:00:01 pass failed: C:\Users\CANARYUSER\runs.json",
        ]));

        Assert.DoesNotContain("CANARYCRASH", text);
        Assert.DoesNotContain("CANARYUSER", text);
        Assert.DoesNotContain("CANARYPROMPT", text);
        Assert.Contains(Redactor.PromptMark, text);
        Assert.Contains(@"pass failed: ~\runs.json", text);
    }

    [Fact]
    public void AUrlWithATokenInTheExceptionOrTheLogNeverReachesTheFile()
    {
        var ex = Thrown("connect failed: https://CANARYCRASHUSER:CANARYCRASHPASS@mcp.example.invalid/sse?api_key=CANARYCRASHURL");
        var text = CrashReport.Build(Input(ex, [
            "2026-11-19 10:00:01 remote https://mcp.example.invalid/mcp?key=CANARYCRASHLOG connected",
        ]));

        Assert.DoesNotContain("CANARY", text);
        Assert.Contains("https://mcp.example.invalid/sse", text);
        Assert.Contains("https://mcp.example.invalid/mcp connected", text);
    }

    [Fact]
    public void TheStackKeepsItsFileNameAndLineButNotItsFolders()
    {
        var text = CrashReport.Build(Input(Thrown("x"), []));

        Assert.Contains("CrashReportTests.cs:line", text);
        Assert.DoesNotContain("C:\\Git", text);
    }

    [Fact]
    public void OnlyTheLast200LogLinesAreKept()
    {
        var lines = Enumerable.Range(1, 250).Select(n => $"log line {n:000}").ToList();

        var text = CrashReport.Build(Input(Thrown("x"), lines));

        Assert.DoesNotContain("log line 050", text);
        Assert.Contains("log line 051", text);
        Assert.Contains("log line 250", text);
        Assert.Contains("Last 200 lines of app.log", text);
    }

    [Fact]
    public void TheFileNameIsCrashWithLocalTimeToTheSecond()
    {
        var at = new DateTimeOffset(2026, 11, 19, 3, 12, 45, TimeSpan.Zero);

        Assert.Equal($"crash-{at.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.txt", CrashReport.FileNameFor(at));
    }

    [Fact]
    public void TheNewestTenCrashFilesAreKept()
    {
        for (var day = 1; day <= 12; day++)
        {
            File.WriteAllText(Path.Combine(_folder, $"crash-202611{day:00}-031245.txt"), "x");
        }
        File.WriteAllText(Path.Combine(_folder, "other.txt"), "not a crash file");

        KeptFiles.KeepNewest(_folder, CrashReport.Pattern, CrashReport.KeepNewest);

        var kept = Directory.GetFiles(_folder, CrashReport.Pattern).Select(Path.GetFileName).ToList();
        Assert.Equal(10, kept.Count);
        Assert.Contains("crash-20261112-031245.txt", kept);
        Assert.Contains("crash-20261103-031245.txt", kept);
        Assert.DoesNotContain("crash-20261102-031245.txt", kept); // the two oldest are the ones deleted
        Assert.DoesNotContain("crash-20261101-031245.txt", kept);
        Assert.True(File.Exists(Path.Combine(_folder, "other.txt")));
    }

    [Fact]
    public void AUniqueNameAddsANumberWhenTheNameIsTaken()
    {
        File.WriteAllText(Path.Combine(_folder, "crash-20261119-031245.txt"), "x");

        Assert.Equal("crash-20261119-031245-2.txt", KeptFiles.UniqueName(_folder, "crash-20261119-031245.txt"));
        Assert.Equal("crash-20261119-031246.txt", KeptFiles.UniqueName(_folder, "crash-20261119-031246.txt"));
    }

    private string WriteAt(string name, DateTime writtenUtc)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "x");
        File.SetLastWriteTimeUtc(path, writtenUtc);
        return name;
    }

    [Fact]
    public void ASameSecondDuplicateIsNewerThanItsOriginal()
    {
        // "-2" sorts before ".txt" by name, so a name order would keep the original and delete the newer copy (review R8).
        var first = DateTime.UtcNow.AddMinutes(-10);
        WriteAt("crash-20261119-031245.txt", first);
        WriteAt("crash-20261119-031245-2.txt", first.AddSeconds(1));

        KeptFiles.KeepNewest(_folder, CrashReport.Pattern, 1);

        Assert.Equal(new[] { "crash-20261119-031245-2.txt" }, Directory.GetFiles(_folder, CrashReport.Pattern).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void TheWriteTimeDecidesNotTheNameWhenTheTwoDisagree()
    {
        // A local-time name can go backwards at the DST fall-back: the file written later is the newer one, whatever its name says.
        var now = DateTime.UtcNow;
        WriteAt("crash-20261025-020000.txt", now.AddHours(-3));
        WriteAt("crash-20261025-013000.txt", now.AddHours(-1));

        KeptFiles.KeepNewest(_folder, CrashReport.Pattern, 1);

        Assert.Equal(new[] { "crash-20261025-013000.txt" }, Directory.GetFiles(_folder, CrashReport.Pattern).Select(Path.GetFileName).ToArray());
    }

    [Fact]
    public void TheFileWrittenInThisCallIsNeverDeleted()
    {
        // The file just written has the oldest time of the three and the lowest name: it is still kept, and it counts toward the limit.
        var now = DateTime.UtcNow;
        WriteAt("crash-20261119-030000.txt", now.AddHours(-3));
        WriteAt("crash-20261119-040000.txt", now.AddHours(-2));
        WriteAt("crash-19990101-000000.txt", now.AddHours(-5));

        KeptFiles.KeepNewest(_folder, CrashReport.Pattern, 2, keepFile: "crash-19990101-000000.txt");

        var kept = Directory.GetFiles(_folder, CrashReport.Pattern).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "crash-19990101-000000.txt", "crash-20261119-040000.txt" }, kept);
    }
}
