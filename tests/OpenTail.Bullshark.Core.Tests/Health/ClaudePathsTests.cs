using OpenTail.Bullshark.Core.Health;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Health;

/// <summary>Reliability batch R2: every Claude path comes from one helper, and CLAUDE_CONFIG_DIR moves them together.</summary>
public sealed class ClaudePathsTests
{
    private static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>An environment in which only CLAUDE_CONFIG_DIR has the given value (null means not set).</summary>
    private static Func<string, string?> With(string? value) =>
        name => name == ClaudePaths.ConfigDirVariable ? value : null;

    [Fact]
    public void WithoutTheVariableEverythingIsUnderTheUserProfile()
    {
        var none = With(null);

        Assert.Equal(Path.Combine(UserProfile, ".claude"), ClaudePaths.ConfigDir(none));
        Assert.Equal(Path.Combine(UserProfile, ".claude.json"), ClaudePaths.ClaudeJson(none));
        Assert.Equal(Path.Combine(UserProfile, ".claude", "projects"), ClaudePaths.ProjectsRoot(none));
        Assert.Equal(Path.Combine(UserProfile, ".claude", "bullshark-archive"), ClaudePaths.ArchiveRoot(none));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyOrBlankVariableIsNotSet(string value)
    {
        var blank = With(value);

        Assert.Equal(Path.Combine(UserProfile, ".claude"), ClaudePaths.ConfigDir(blank));
        Assert.Equal(Path.Combine(UserProfile, ".claude.json"), ClaudePaths.ClaudeJson(blank));
    }

    [Fact]
    public void WithTheVariableClaudeJsonIsInsideTheConfigFolder()
    {
        var set = With(@"D:\cfg");

        Assert.Equal(@"D:\cfg", ClaudePaths.ConfigDir(set));
        Assert.Equal(@"D:\cfg\.claude.json", ClaudePaths.ClaudeJson(set));
        Assert.Equal(@"D:\cfg\projects", ClaudePaths.ProjectsRoot(set));
        Assert.Equal(@"D:\cfg\bullshark-archive", ClaudePaths.ArchiveRoot(set));
    }

    [Fact]
    public void ATrailingSeparatorDoesNotDoubleTheJoin()
    {
        var set = With(@"D:\cfg\");

        Assert.Equal(@"D:\cfg\.claude.json", ClaudePaths.ClaudeJson(set));
        Assert.Equal(@"D:\cfg\projects", ClaudePaths.ProjectsRoot(set));
    }

    [Fact]
    public void SurroundingSpacesAreTrimmed()
    {
        Assert.Equal(@"D:\cfg", ClaudePaths.ConfigDir(With("  D:\\cfg  ")));
    }

    [Fact]
    public void ARelativeValueIsLeftRelative()
    {
        var relative = With("cfg");

        Assert.Equal("cfg", ClaudePaths.ConfigDir(relative));
        Assert.Equal(Path.Combine("cfg", ".claude.json"), ClaudePaths.ClaudeJson(relative));
    }

    [Fact]
    public void TheEnvironmentIsReadForTheVariableOnly()
    {
        var asked = new List<string>();
        string? Env(string name)
        {
            asked.Add(name);
            return null;
        }

        ClaudePaths.ClaudeJson(Env);

        Assert.Equal([ClaudePaths.ConfigDirVariable], asked);
    }
}
