using OpenTail.Bullshark.Core.OpenCode;

namespace OpenTail.Bullshark.Core.Tests.OpenCode;

/// <summary>Stage OC1a (OC-D1): the version read from the npm package file. Pure string functions; no file and no process.</summary>
public sealed class OpenCodeVersionTests
{
    [Fact]
    public void TheBuildTheAppWasReadAgainstIsOneFourX()
    {
        Assert.Equal("1.4", OpenCodeVersion.Line);
    }

    [Fact]
    public void AVersionInAPackageJsonIsReturned()
    {
        const string json = "{\"name\": \"opencode-ai\", \"version\": \"1.4.3\", \"bin\": {\"opencode\": \"./bin/opencode\"}}";

        Assert.Equal("1.4.3", OpenCodeVersion.FromPackageJson(json));
    }

    [Fact]
    public void AJsonWithoutAVersionGivesNull()
    {
        Assert.Null(OpenCodeVersion.FromPackageJson("{\"name\": \"opencode-ai\"}"));
    }

    [Theory]
    [InlineData("{\"version\": 143}")]
    [InlineData("{\"version\": null}")]
    [InlineData("{\"version\": \"  \"}")]
    [InlineData("[\"version\", \"1.4.3\"]")]
    [InlineData("\"1.4.3\"")]
    public void AVersionThatIsNotAStringOrIsBlankGivesNull(string json)
    {
        Assert.Null(OpenCodeVersion.FromPackageJson(json));
    }

    [Theory]
    [InlineData("{ \"version\": \"1.4.3\"")]
    [InlineData("not json")]
    [InlineData("")]
    public void InvalidJsonGivesNullAndDoesNotThrow(string json)
    {
        // The choice (OC-D1 review): a damaged package file is "version not read", a Warning in the setup check, not an exception.
        Assert.Null(OpenCodeVersion.FromPackageJson(json));
    }

    [Fact]
    public void ThePackageFileIsNamedBesideTheShim()
    {
        Assert.Equal(
            "C:\\Fixture\\npm\\node_modules\\opencode-ai\\package.json",
            OpenCodeVersion.PackagePathBeside("C:\\Fixture\\npm\\opencode.cmd"));
    }

    [Fact]
    public void ThePackageFileIsNamedBesideAShimInARootFolder()
    {
        Assert.Equal(
            "C:\\node_modules\\opencode-ai\\package.json",
            OpenCodeVersion.PackagePathBeside("C:\\opencode.ps1"));
    }
}
