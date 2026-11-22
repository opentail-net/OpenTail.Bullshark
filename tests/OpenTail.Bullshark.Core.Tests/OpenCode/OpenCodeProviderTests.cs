using OpenTail.Bullshark.Core.OpenCode;
using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Tests.OpenCode;

/// <summary>
/// Stage OC1a (OC-D2, OC-D12, OC-D16): the opencode provider's identity, its paths (<c>XDG_DATA_HOME</c> or the profile), and its command lines. Paths are
/// computed from fake variables; no file is opened and no process is started.
/// </summary>
public sealed class OpenCodeProviderTests
{
    private const string Id = "ses_0123456789abAbCdEfGhIjKlMn";

    [Fact]
    public void TheIdAndTheDisplayNameAreOpenCode()
    {
        Assert.Equal("opencode", ProviderId.OpenCode.Value);

        var provider = new OpenCodeProvider(ProviderPaths.ForOpenCode(_ => null, "C:\\Fixture\\Profile"));
        IProvider asProvider = provider;

        Assert.Equal(ProviderId.OpenCode, asProvider.Id);
        Assert.Equal("opencode", asProvider.DisplayName);
    }

    [Fact]
    public void TheCommandLinesAreTheOpenerOnes()
    {
        var provider = new OpenCodeProvider(ProviderPaths.ForOpenCode(_ => null, "C:\\Fixture\\Profile"));

        Assert.Equal("cmd.exe /d /c opencode \"C:\\Fixture\\Demo\"", provider.NewCommandLine("C:\\Fixture\\Demo"));
        Assert.Equal($"cmd.exe /d /c opencode -s {Id}", provider.ResumeCommandLine(Id));
        Assert.Throws<ArgumentException>(() => provider.ResumeCommandLine("ses_BAD"));
    }

    [Fact]
    public void XdgDataHomeMovesTheDataFolderUnderOpenCode()
    {
        var paths = ProviderPaths.ForOpenCode(name => name == ProviderPaths.XdgDataHomeVariable ? "  D:\\Fixture\\xdg  " : null, "C:\\Fixture\\Profile");

        Assert.Equal("D:\\Fixture\\xdg\\opencode", paths.HomeRoot);
        Assert.Equal("D:\\Fixture\\xdg\\opencode", paths.HistoryRoot);
        Assert.Null(paths.ArchiveRoot);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithoutXdgDataHomeTheDataFolderIsUnderTheProfile(string? value)
    {
        var paths = ProviderPaths.ForOpenCode(name => name == ProviderPaths.XdgDataHomeVariable ? value : null, "C:\\Fixture\\Profile");

        Assert.Equal("C:\\Fixture\\Profile\\.local\\share\\opencode", paths.HomeRoot);
        Assert.Equal("C:\\Fixture\\Profile\\.local\\share\\opencode", paths.HistoryRoot);
        Assert.Null(paths.ArchiveRoot);
    }

    [Fact]
    public void TheProviderUsesThePathsItIsGiven()
    {
        var given = ProviderPaths.ForOpenCode(_ => "D:\\Fixture\\given", "C:\\Fixture\\Profile");
        var provider = new OpenCodeProvider(given);

        Assert.Same(given, provider.Paths);
    }
}
