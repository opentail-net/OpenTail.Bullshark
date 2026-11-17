using OpenTail.Bullshark.Core.Ui;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Ui;

/// <summary>The screenshot options (round 4, item 5): --open-tab and --claude-home.</summary>
public sealed class TestScreensTests
{
    [Theory]
    [InlineData("storage", TestScreen.Storage)]
    [InlineData("Storage", TestScreen.Storage)]
    [InlineData("timeline", TestScreen.Timeline)]
    [InlineData(" TIMELINE ", TestScreen.Timeline)]
    public void TheTwoPlainScreensParse(string value, TestScreen screen)
    {
        var request = TestScreenRequest.Parse(value);

        Assert.Equal(screen, request?.Screen);
        Assert.Null(request?.SessionName);
    }

    [Fact]
    public void ChangesNamesTheSession()
    {
        var request = TestScreenRequest.Parse("changes:bullshark-coder-ab12-1");

        Assert.Equal(TestScreen.Changes, request?.Screen);
        Assert.Equal("bullshark-coder-ab12-1", request?.SessionName);
    }

    [Fact]
    public void ChangesKeepsColonsAfterTheFirstOne()
    {
        Assert.Equal("a:b", TestScreenRequest.Parse("changes:a:b")?.SessionName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("changes")]
    [InlineData("changes:")]
    [InlineData("changes:   ")]
    [InlineData("storage:extra")]
    [InlineData("timeline:extra")]
    [InlineData("storag")]
    [InlineData("setup")]
    public void AnythingElseOpensNothing(string? value)
    {
        Assert.Null(TestScreenRequest.Parse(value));
    }

    [Fact]
    public void TheTestHomeHoldsTheSameTwoFoldersAsTheOwnersClaudeFolder()
    {
        Assert.Equal(Path.Combine(@"C:\scratch\home", "projects"), TestHome.ProjectsRootUnder(@"C:\scratch\home"));
        Assert.Equal(Path.Combine(@"C:\scratch\home", "bullshark-archive"), TestHome.ArchiveRootUnder(@"C:\scratch\home"));
    }
}
