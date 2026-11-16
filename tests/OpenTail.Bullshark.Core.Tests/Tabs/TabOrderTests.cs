using OpenTail.Bullshark.Core.Tabs;

namespace OpenTail.Bullshark.Core.Tests.Tabs;

public class TabOrderTests
{
    [Fact]
    public void ATabMovesLeft()
    {
        var tabs = new List<string> { "a", "b", "c" };
        Assert.True(TabOrder.Move(tabs, 2, 0));
        Assert.Equal(["c", "a", "b"], tabs);
    }

    [Fact]
    public void ATabMovesRight()
    {
        var tabs = new List<string> { "a", "b", "c" };
        Assert.True(TabOrder.Move(tabs, 0, 2));
        Assert.Equal(["b", "c", "a"], tabs);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 3)]
    [InlineData(3, 0)]
    [InlineData(1, 1)]
    public void AMoveOutOfRangeIsIgnored(int from, int to)
    {
        var tabs = new List<string> { "a", "b", "c" };
        Assert.False(TabOrder.Move(tabs, from, to));
        Assert.Equal(["a", "b", "c"], tabs);
    }

    [Fact]
    public void SavedTabsComeFirstInTheSavedOrder()
    {
        var arranged = TabOrder.Arrange(["a", "b", "c", "d"], id => id, ["c", "a"]);
        Assert.Equal(["c", "a", "b", "d"], arranged);
    }

    [Fact]
    public void ASavedIdWithNoTabIsIgnored()
    {
        var arranged = TabOrder.Arrange(["a", "b"], id => id, ["gone", "b"]);
        Assert.Equal(["b", "a"], arranged);
    }

    [Fact]
    public void WithNothingSavedTheOrderIsKept()
    {
        var arranged = TabOrder.Arrange(["b", "a"], id => id, []);
        Assert.Equal(["b", "a"], arranged);
    }

    [Fact]
    public void ADragRightSwapsOnlyPastTheMiddle()
    {
        var spans = new List<(double, double)> { (0, 100), (100, 200) };
        Assert.Equal(-1, TabOrder.DropPlace(spans, dragged: 0, pointer: 140));
        Assert.Equal(1, TabOrder.DropPlace(spans, dragged: 0, pointer: 160));
    }

    [Fact]
    public void ADragLeftSwapsOnlyPastTheMiddle()
    {
        var spans = new List<(double, double)> { (0, 100), (100, 200) };
        Assert.Equal(-1, TabOrder.DropPlace(spans, dragged: 1, pointer: 60));
        Assert.Equal(0, TabOrder.DropPlace(spans, dragged: 1, pointer: 40));
    }

    [Fact]
    public void AWideAndANarrowTabDoNotSwapBack()
    {
        // The narrow tab (0..40) is dragged right over the wide one (40..240) and swaps at 150.
        var before = new List<(double, double)> { (0, 40), (40, 240) };
        Assert.Equal(1, TabOrder.DropPlace(before, dragged: 0, pointer: 150));

        // After the swap the wide tab is 0..200 and the dragged tab 200..240. The pointer has not moved and is over the wide tab.
        var after = new List<(double, double)> { (0, 200), (200, 240) };
        Assert.Equal(-1, TabOrder.DropPlace(after, dragged: 1, pointer: 150));
    }

    [Fact]
    public void APointerOverNoTabGivesNoPlace()
    {
        var spans = new List<(double, double)> { (0, 100), (100, 200) };
        Assert.Equal(-1, TabOrder.DropPlace(spans, dragged: 0, pointer: 500));
        Assert.Equal(-1, TabOrder.DropPlace(spans, dragged: 0, pointer: 50));
    }

    [Fact]
    public void TheLayoutIsSavedAndReadBack()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bullshark-tabs-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = TabOrder.PathBeside(Path.Combine(folder, "state.json"));
            Assert.Empty(TabOrder.Load(path).Order);
            Assert.Empty(TabOrder.Load(path).Right);

            TabOrder.Save(path, new TabLayout(["s2", "s1"], ["s1"]));
            var read = TabOrder.Load(path);
            Assert.Equal(["s2", "s1"], read.Order);
            Assert.Equal(["s1"], read.Right);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void AnUnreadableFileGivesNoLayout()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bullshark-tabs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "tabs.json");
            File.WriteAllText(path, "{ not a list");
            string? logged = null;
            Assert.Empty(TabOrder.Load(path, message => logged = message).Order);
            Assert.NotNull(logged);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
