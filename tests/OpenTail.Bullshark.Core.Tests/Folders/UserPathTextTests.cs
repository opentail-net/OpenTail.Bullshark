using OpenTail.Bullshark.Core.Folders;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Folders;

/// <summary>The path shown on an empty page: the profile folder as ~ (firstrun batch B8).</summary>
public sealed class UserPathTextTests
{
    private const string Profile = @"C:\Users\Sample";

    [Fact]
    public void APathUnderTheProfileStartsWithATilde()
    {
        Assert.Equal(@"~\.claude\projects", UserPathText.Of(@"C:\Users\Sample\.claude\projects", Profile));
    }

    [Fact]
    public void TheProfileItselfIsATilde()
    {
        Assert.Equal("~", UserPathText.Of(Profile, Profile));
    }

    [Fact]
    public void AnotherUserWhoseNameStartsTheSameIsNotShortened()
    {
        Assert.Equal(@"C:\Users\Samplez\x", UserPathText.Of(@"C:\Users\Samplez\x", Profile));
    }

    [Fact]
    public void ThePrefixIsMatchedWithoutCaseAndForwardSlashesStayAsTheyAre()
    {
        Assert.Equal("~/.claude/projects", UserPathText.Of("c:/users/sample/.claude/projects", "C:/Users/Sample"));
    }

    [Fact]
    public void APathOutsideTheProfileAndAnEmptyProfileAreUnchanged()
    {
        Assert.Equal(@"D:\data\projects", UserPathText.Of(@"D:\data\projects", Profile));
        Assert.Equal(@"C:\Users\Sample\x", UserPathText.Of(@"C:\Users\Sample\x", string.Empty));
    }
}
