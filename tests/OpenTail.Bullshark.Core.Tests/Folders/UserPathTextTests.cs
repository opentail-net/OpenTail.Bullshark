using OpenTail.Bullshark.Core.Folders;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Folders;

/// <summary>The path shown on an empty page: the profile folder as ~ (firstrun batch B8).</summary>
public sealed class UserPathTextTests
{
    private const string Profile = @"C:\Users\Dmitri";

    [Fact]
    public void APathUnderTheProfileStartsWithATilde()
    {
        Assert.Equal(@"~\.claude\projects", UserPathText.Of(@"C:\Users\Dmitri\.claude\projects", Profile));
    }

    [Fact]
    public void TheProfileItselfIsATilde()
    {
        Assert.Equal("~", UserPathText.Of(Profile, Profile));
    }

    [Fact]
    public void AnotherUserWhoseNameStartsTheSameIsNotShortened()
    {
        Assert.Equal(@"C:\Users\Dmitriy\x", UserPathText.Of(@"C:\Users\Dmitriy\x", Profile));
    }

    [Fact]
    public void ThePrefixIsMatchedWithoutCaseAndForwardSlashesStayAsTheyAre()
    {
        Assert.Equal("~/.claude/projects", UserPathText.Of("c:/users/dmitri/.claude/projects", "C:/Users/Dmitri"));
    }

    [Fact]
    public void APathOutsideTheProfileAndAnEmptyProfileAreUnchanged()
    {
        Assert.Equal(@"D:\data\projects", UserPathText.Of(@"D:\data\projects", Profile));
        Assert.Equal(@"C:\Users\Dmitri\x", UserPathText.Of(@"C:\Users\Dmitri\x", string.Empty));
    }
}
