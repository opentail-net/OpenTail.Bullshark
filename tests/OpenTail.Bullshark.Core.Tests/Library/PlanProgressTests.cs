using System.Text;
using OpenTail.Bullshark.Core.Library;

namespace OpenTail.Bullshark.Core.Tests.Library;

/// <summary>The checkboxes of a plan file, shown as progress on a session row (plan progress, 2026-11-18). Each test writes into a temp folder it removes.</summary>
public sealed class PlanProgressTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-plan-" + Guid.NewGuid().ToString("N")[..8]);

    public PlanProgressTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static PlanProgressCount Parse(params string[] lines) => PlanProgress.Parse(lines);

    [Fact]
    public void DoneAndOpenItemsAreCounted()
    {
        var count = Parse("# Plan", "- [x] first", "- [ ] second", "- [X] third", "- [ ] fourth");

        Assert.Equal(new PlanProgressCount(Done: 2, Total: 4, Next: "second"), count);
    }

    [Fact]
    public void ANotDoneItemIsCountedButIsNotDone()
    {
        var count = Parse("- [!] blocked on the owner", "- [x] done");

        Assert.Equal(new PlanProgressCount(1, 2, "blocked on the owner"), count);
    }

    [Fact]
    public void NestedItemsAtAnyIndentationCount()
    {
        var count = Parse("- [x] top", "  - [ ] nested", "\t- [x] tabbed", "        - [ ] deep");

        Assert.Equal(new PlanProgressCount(2, 4, "nested"), count);
    }

    [Fact]
    public void ALineInsideAFencedBlockDoesNotCount()
    {
        var count = Parse("- [ ] real", "```", "- [x] inside a code sample", "- [ ] also inside", "```", "- [x] after the block");

        Assert.Equal(new PlanProgressCount(1, 2, "real"), count);
    }

    [Fact]
    public void ATildeFenceIsHonouredAndAShorterFenceDoesNotClose()
    {
        var count = Parse("~~~~", "- [ ] inside", "~~~", "- [ ] still inside", "~~~~", "- [ ] outside");

        Assert.Equal(new PlanProgressCount(0, 1, "outside"), count);
    }

    [Fact]
    public void AnEmptyFileHasNothingToShow()
    {
        Assert.Equal(new PlanProgressCount(0, 0, null), PlanProgress.Parse(Array.Empty<string>()));
    }

    [Fact]
    public void AFileWithoutCheckboxesHasTotalZero()
    {
        var count = Parse("# Plan", "- a plain bullet", "1. [ ] a numbered line is not a box", "text [x] inside a sentence");

        Assert.Equal(0, count.Total);
        Assert.Equal(0, count.Done);
        Assert.Null(count.Next);
    }

    [Fact]
    public void AHundredPercentFileHasNoNext()
    {
        var count = Parse("- [x] one", "- [X] two");

        Assert.Equal(new PlanProgressCount(2, 2, null), count);
    }

    [Fact]
    public void NextIsCutToEightyCharactersWithAnEllipsis()
    {
        var text = new string('w', 120);

        var count = Parse("- [ ] " + text);

        Assert.Equal(PlanProgress.MaxNextLength, count.Next!.Length);
        Assert.Equal(new string('w', 79) + "…", count.Next);
    }

    [Fact]
    public void NextOfExactlyEightyCharactersIsShownWhole()
    {
        var text = new string('v', PlanProgress.MaxNextLength);

        Assert.Equal(text, Parse("- [ ] " + text).Next);
    }

    [Fact]
    public void AnOpenItemWithNoTextIsSkippedForNext()
    {
        var count = Parse("- [ ]", "- [ ] the words");

        Assert.Equal(new PlanProgressCount(0, 2, "the words"), count);
    }

    [Fact]
    public void CrLfLinesCountTheSame()
    {
        var count = Parse("- [x] one\r", "- [ ] two\r", "x\r");

        Assert.Equal(new PlanProgressCount(1, 2, "two"), count);
    }

    [Fact]
    public void ReadFileGivesTheCountsOfAFile()
    {
        var path = Write("plan.md", "- [x] one\r\n- [ ] two\r\n- [ ] three\r\n");

        Assert.Equal(new PlanProgressCount(1, 3, "two"), PlanProgress.ReadFile(path));
    }

    [Fact]
    public void ReadFileDropsAByteOrderMark()
    {
        var path = Write("bom.md", "\uFEFF- [x] first line\n- [ ] second\n");

        Assert.Equal(new PlanProgressCount(1, 2, "second"), PlanProgress.ReadFile(path));
    }

    [Fact]
    public void AMissingFileGivesNullAndNeverThrows()
    {
        Assert.Null(PlanProgress.ReadFile(Path.Combine(_dir, "does-not-exist.md")));
        Assert.Null(PlanProgress.ReadFile(_dir));
        Assert.Null(PlanProgress.ReadFile(string.Empty));
        Assert.Null(PlanProgress.ReadFile("bad\0name.md"));
    }

    [Fact]
    public void OnlyTheFirstPartOfALargeFileIsCounted()
    {
        // 14,000 open items of 40 bytes make about 560 KB. The first 512 KB hold about 13,100 of them; the done item at the end is past the cap.
        var open = string.Concat(Enumerable.Repeat("- [ ] open item padding padding padding\n", 14_000));
        var path = Write("big.md", open + "- [x] past the cap\n");
        Assert.True(new FileInfo(path).Length > PlanProgress.MaxBytes);

        var count = PlanProgress.ReadFile(path)!;

        Assert.Equal(0, count.Done);
        Assert.InRange(count.Total, 13_000, 13_200);
    }

    [Fact]
    public void PathOfTrimsSpacesAndQuotesAndBlankIsNoPlan()
    {
        Assert.Equal(@"C:\work\plan.md", PlanProgress.PathOf(@"  ""C:\work\plan.md""  "));
        Assert.Equal(@"C:\work\plan.md", PlanProgress.PathOf(@" C:\work\plan.md "));
        Assert.Null(PlanProgress.PathOf("   "));
        Assert.Null(PlanProgress.PathOf(string.Empty));
        Assert.Null(PlanProgress.PathOf(null));
    }

    [Theory]
    [InlineData(@"C:\work\plan.md", true)]
    [InlineData(@"\\server\share\plan.md", true)]
    [InlineData("plan.md", false)]
    [InlineData(@"..\plan.md", false)]
    [InlineData(@"C:plan.md", false)]
    public void OnlyAFullPathIsAccepted(string path, bool full)
    {
        Assert.Equal(full, PlanProgress.IsFullPath(path));
    }

    [Fact]
    public void TheCacheReadsAFileAgainOnlyWhenItChanges()
    {
        var path = Write("cached.md", "- [ ] one\n");
        var cache = new PlanProgressCache();
        Assert.Equal(new PlanProgressCount(0, 1, "one"), cache.Get(path));

        // The same length and the same write time: the cached counts stay, even though the text differs.
        var written = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, "- [x] one\n");
        File.SetLastWriteTimeUtc(path, written);
        Assert.Equal(new PlanProgressCount(0, 1, "one"), cache.Get(path));

        // A change of length is read again.
        File.WriteAllText(path, "- [x] one\n- [ ] two\n");
        Assert.Equal(new PlanProgressCount(1, 2, "two"), cache.Get(path));
    }

    [Fact]
    public void TheCacheForgetsAMissingFileWithoutThrowing()
    {
        var path = Write("gone.md", "- [ ] one\n");
        var cache = new PlanProgressCache();
        Assert.NotNull(cache.Get(path));

        File.Delete(path);

        Assert.Null(cache.Get(path));
        Assert.Null(cache.Get(Path.Combine(_dir, "never-there.md")));
    }

    private string Write(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }
}
