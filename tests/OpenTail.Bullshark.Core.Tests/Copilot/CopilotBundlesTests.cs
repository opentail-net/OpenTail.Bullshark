using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Copilot;

/// <summary>
/// PP-D2 (2026-11-23): the downloaded Copilot bundles under a folder the test owns. <see cref="CopilotBundles.NewestVersion"/> picks the newest version
/// folder with a finished-extraction marker, and the setup check accepts the terminal row when that bundle has the flags. No copilot program is run:
/// both version reads go through fake runners.
/// </summary>
public sealed class CopilotBundlesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-copilot-bundles-" + Guid.NewGuid().ToString("N"));

    public CopilotBundlesTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A bundle folder under the root; with <paramref name="finished"/>, the extraction marker is written too.</summary>
    private string Bundle(string root, string version, bool finished = true)
    {
        var folder = Path.Combine(root, version);
        Directory.CreateDirectory(folder);
        if (finished) File.WriteAllText(Path.Combine(folder, ".extraction-complete"), string.Empty);
        return folder;
    }

    /// <summary>A fake read that prints <paramref name="stdout"/> and exits with 0.</summary>
    private static FakeCliRunner Printing(string stdout) => new(_ => new CliResult(0, stdout, string.Empty, TimedOut: false));

    /// <summary>A folder under the test folder that holds one empty copilot.exe (the terminal row finds it on PATH).</summary>
    private string Launcher()
    {
        var bin = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "copilot.exe"), string.Empty);
        return bin;
    }

    [Fact]
    public void TheNewestFinishedVersionWins()
    {
        var root = Path.Combine(_dir, "pkg");
        Bundle(root, "1.0.34");
        Bundle(root, "1.0.104-3");
        Bundle(root, "1.0.103");
        Bundle(root, "1.0.200", finished: false);
        Directory.CreateDirectory(Path.Combine(root, "tmp"));
        File.WriteAllText(Path.Combine(root, "tmp", ".extraction-complete"), string.Empty);

        Assert.Equal("1.0.104-3", CopilotBundles.NewestVersion(root));
    }

    [Fact]
    public void NoBundleOrNoFolderIsNull()
    {
        Assert.Null(CopilotBundles.NewestVersion(Path.Combine(_dir, "missing")));
        Assert.Null(CopilotBundles.NewestVersion(Path.Combine(_dir, "empty")));
    }

    [Fact]
    public void VersionsCompareNumerically()
    {
        Assert.True(CopilotFlagsVersion.Compare("1.0.104-3", "1.0.103") > 0);
        Assert.True(CopilotFlagsVersion.Compare("1.0.9", "1.0.10") < 0);
        Assert.Equal(0, CopilotFlagsVersion.Compare("junk", "tmp"));
        Assert.True(CopilotFlagsVersion.Compare("1.0.34", "junk") > 0);
    }

    [Fact]
    public async Task AnOlderLauncherCopyIsOkWhenTheDownloadedBundleHasTheFlags()
    {
        var root = Path.Combine(_dir, "pkg");
        Bundle(root, "1.0.104-3");

        var rows = await CopilotSetupCheck.RunAsync(
            Printing("GitHub Copilot CLI 1.0.34."), Printing("unused"), Launcher(), bundleRoot: root);

        var terminal = rows.Single(row => row.Name == "copilot");
        Assert.Equal(SetupState.Ok, terminal.State);
        Assert.Contains("downloaded bundle 1.0.104-3", terminal.Detail);
    }

    [Fact]
    public async Task AnOlderLauncherCopyStaysAProblemWithNoDownloadedBundle()
    {
        var root = Path.Combine(_dir, "pkg");
        Bundle(root, "1.0.51", finished: false);

        var rows = await CopilotSetupCheck.RunAsync(
            Printing("GitHub Copilot CLI 1.0.34."), Printing("unused"), Launcher(), bundleRoot: root);

        Assert.Equal(SetupState.Problem, rows.Single(row => row.Name == "copilot").State);
    }

    [Fact]
    public async Task ADownloadedBundleOlderThanTheFlagsDoesNotHelp()
    {
        var root = Path.Combine(_dir, "pkg");
        Bundle(root, "1.0.51");

        var rows = await CopilotSetupCheck.RunAsync(
            Printing("GitHub Copilot CLI 1.0.34."), Printing("unused"), Launcher(), bundleRoot: root);

        Assert.Equal(SetupState.Problem, rows.Single(row => row.Name == "copilot").State);
    }
}
