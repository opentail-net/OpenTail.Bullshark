using System.Globalization;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Reports;
using OpenTail.Bullshark.Core.Setup;
using OpenTail.Bullshark.Core.Supervisor;
using OpenTail.Bullshark.Core.Ui;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Ui;

/// <summary>
/// The night report file (night batch N5): the Markdown with its Plans and tokens lines, the file name, the names that never overwrite, the
/// exclusive save, and the cleanup that keeps the newest reports and touches nothing else.
/// </summary>
public sealed class NightReportTests : IDisposable
{
    private static readonly DateTimeOffset Since = new(2026, 11, 18, 22, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 6, 5, 0, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bullshark-reports-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static AwayDigest EmptyDigest() => new(Since, Now, [], [], [], []);

    private static UsageReport Usage(params (DateOnly Day, long New, long CacheRead)[] days) => new(
        DateOnly.FromDateTime(Since.LocalDateTime).AddDays(-1),
        DateOnly.FromDateTime(Now.LocalDateTime),
        days.Select(day => new UsageDay(day.Day, new TokenUsage(day.New, 0, 0, day.CacheRead), null)).ToList(),
        [],
        0,
        TokenUsage.Zero);

    [Fact]
    public void TheMarkdownIsTheAwayExportWithPlansAndTokensAfterIt()
    {
        var plans = new List<(string, int, int, string?)> { ("bullshark-coder-a", 2, 5, "Wire the *save* button"), ("bullshark-coder-b", 0, 0, null) };
        var usage = Usage((DateOnly.FromDateTime(Now.LocalDateTime), 1_234, 5_678), (DateOnly.FromDateTime(Now.LocalDateTime).AddDays(-5), 9, 9));

        var text = NightReport.Markdown(EmptyDigest(), Since, Now, plans, usage);

        Assert.StartsWith(AwayExport.Markdown(EmptyDigest(), Since, Now).TrimEnd(), text);
        Assert.Contains("\n## Plans\n\n- **bullshark-coder-a**: 2 of 5 boxes done; next: Wire the \\*save\\* button\n", text);
        Assert.DoesNotContain("bullshark-coder-b", text);
        Assert.EndsWith("**Tokens in this period:** New 1,234 and cache read 5,678, over the 1 usage day in the period.\n", text);
    }

    [Fact]
    public void ThePlansSectionIsLeftOutWhenNoPlanHasBoxes()
    {
        var text = NightReport.Markdown(EmptyDigest(), Since, Now, [("a", 0, 0, null)], null);

        Assert.DoesNotContain("## Plans", text);
        Assert.EndsWith("**Tokens in this period:** not read (the usage report could not be read).\n", text);
    }

    [Fact]
    public void APlanWithNoOpenItemSaysSo()
    {
        var text = NightReport.Markdown(EmptyDigest(), Since, Now, [("alpha", 3, 3, null)], null);

        Assert.Contains("- **alpha**: 3 of 3 boxes done; no open item", text);
    }

    [Fact]
    public void FileNameIsTheLocalTimeAndSortsByTime()
    {
        var at = new DateTimeOffset(2026, 11, 19, 9, 5, 0, DateTimeOffset.Now.Offset);

        Assert.Equal("away-2026-11-19-0905.md", NightReport.FileName(at));
    }

    [Fact]
    public void UniquePathAddsANumberInsteadOfOverwriting()
    {
        Directory.CreateDirectory(_folder);
        var first = NightReport.UniquePath(_folder, "away-2026-11-19-0905.md");
        Assert.Equal(Path.Combine(_folder, "away-2026-11-19-0905.md"), first);

        File.WriteAllText(first, "one");
        var second = NightReport.UniquePath(_folder, "away-2026-11-19-0905.md");
        Assert.Equal(Path.Combine(_folder, "away-2026-11-19-0905-2.md"), second);

        File.WriteAllText(second, "two");
        Assert.Equal(Path.Combine(_folder, "away-2026-11-19-0905-3.md"), NightReport.UniquePath(_folder, "away-2026-11-19-0905.md"));
        Assert.Equal("one", File.ReadAllText(first));
    }

    [Fact]
    public void SaveWritesNewFilesAndNeverOverwrites()
    {
        var at = new DateTimeOffset(2026, 11, 19, 9, 5, 0, DateTimeOffset.Now.Offset);

        var a = NightReport.Save(_folder, at, "first");
        var b = NightReport.Save(_folder, at, "second");

        Assert.Equal(Path.Combine(_folder, "away-2026-11-19-0905.md"), a);
        Assert.Equal(Path.Combine(_folder, "away-2026-11-19-0905-2.md"), b);
        Assert.Equal("first", File.ReadAllText(a));
        Assert.Equal("second", File.ReadAllText(b));
    }

    [Theory]
    [InlineData("away-2026-11-19-0905.md", true)]
    [InlineData("away-2026-11-19-0905-12.md", true)]
    [InlineData("away-notes.md", false)]
    [InlineData("away-2026-11-19-0905.txt", false)]
    [InlineData("my-away-2026-11-19-0905.md", false)]
    [InlineData("Away-2026-11-19-0905.md", false)]
    public void OnlyTheNamesThisFeatureMakesAreReports(string name, bool expected)
    {
        Assert.Equal(expected, NightReport.IsReportName(name));
    }

    [Fact]
    public void NamesToDeleteKeepsTheNewestByTimeThenSuffix()
    {
        var names = new[]
        {
            "away-2026-11-19-0906.md",
            "away-2026-11-19-0905-2.md",
            "away-2026-11-19-0905.md",
            "away-2026-11-18-2300.md",
            "notes.md",
        };

        var delete = NightReport.NamesToDelete(names, keep: 2);

        Assert.Equal(["away-2026-11-18-2300.md", "away-2026-11-19-0905.md"], delete);
    }

    [Fact]
    public void NothingIsDeletedWhileThereAreFewerThanTheKeepCount()
    {
        Assert.Empty(NightReport.NamesToDelete(["away-2026-11-19-0905.md"], keep: NightReport.KeepCount));
        Assert.Equal(NightReport.KeepCount, 60);
    }

    [Fact]
    public void PruneDeletesOnlyOldReportsAndLeavesOtherFiles()
    {
        Directory.CreateDirectory(_folder);
        for (var minute = 0; minute < 5; minute++)
        {
            File.WriteAllText(Path.Combine(_folder, $"away-2026-11-19-09{minute:00}.md"), "r");
        }
        File.WriteAllText(Path.Combine(_folder, "my-notes.md"), "keep me");
        File.WriteAllText(Path.Combine(_folder, "away-personal.md"), "keep me too");
        File.WriteAllText(Path.Combine(_folder, "away-2026-11-19-0900.txt"), "keep me three");

        var deleted = NightReport.Prune(_folder, keep: 2);

        Assert.Equal(3, deleted.Count);
        Assert.Equal(
            ["away-2026-11-19-0900.txt", "away-2026-11-19-0903.md", "away-2026-11-19-0904.md", "away-personal.md", "my-notes.md"],
            Directory.GetFiles(_folder).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void PruneOfAMissingFolderDeletesNothingAndDoesNotThrow()
    {
        Assert.Empty(NightReport.Prune(Path.Combine(_folder, "nothere")));
    }

    [Fact]
    public void TheNumberInTheCountIsFormattedAsInvariantText()
    {
        var text = NightReport.Markdown(EmptyDigest(), Since, Now, [], Usage((DateOnly.FromDateTime(Now.LocalDateTime), 1_000_000, 0)));

        Assert.Contains(1_000_000.ToString("N0", CultureInfo.InvariantCulture), text);
    }
}
