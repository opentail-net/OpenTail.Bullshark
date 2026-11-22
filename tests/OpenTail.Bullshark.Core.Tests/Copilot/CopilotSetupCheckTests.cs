using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Copilot;

/// <summary>
/// Stage G1.6 (G-D13, G-O5): the Copilot row of the setup check. Only a PATH given by the test is searched, and the version read goes through a
/// fake runner, so no copilot program is run.
/// </summary>
public sealed class CopilotSetupCheckTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-copilot-setup-" + Guid.NewGuid().ToString("N"));

    public CopilotSetupCheckTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task WithNoCopilotOnThePathTheRowIsAProblemAndNoVersionIsRead()
    {
        var empty = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(empty);
        var runner = new FakeCliRunner();

        var items = await CopilotSetupCheck.RunAsync(runner, empty);

        var item = Assert.Single(items);
        Assert.Equal("Copilot CLI", item.Area);
        Assert.Equal("copilot", item.Name);
        Assert.Equal(SetupState.Problem, item.State);
        Assert.StartsWith("not found on PATH: install GitHub Copilot CLI", item.Detail);
        Assert.Empty(runner.Calls);
    }

    [Theory]
    [InlineData("copilot.exe")]
    [InlineData("copilot.cmd")]
    [InlineData("copilot.ps1")]
    public async Task AProgramOnThePathGivesAnOkRowWithItsPathAndTheBundledVersion(string fileName)
    {
        var folder = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, fileName);
        File.WriteAllText(file, string.Empty);
        // A start from text needs copilot.ps1 on PATH, so the row is Ok only with it: every case has one.
        File.WriteAllText(Path.Combine(folder, "copilot.ps1"), string.Empty);
        var runner = new FakeCliRunner(_ => new CliResult(0, "GitHub Copilot CLI version 1.0.34.\n", string.Empty, TimedOut: false));

        var item = Assert.Single(await CopilotSetupCheck.RunAsync(runner, folder));

        Assert.Equal(SetupState.Ok, item.State);
        Assert.Contains("found: " + file, item.Detail);
        Assert.Contains("bundled version 1.0.34", item.Detail);
        Assert.Contains("bundled", item.Detail);
    }

    [Fact]
    public async Task TheVersionReadIsTheBundledOneWithAutoUpdateAndSyncOff()
    {
        var folder = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "copilot.cmd"), string.Empty);
        var runner = new FakeCliRunner(_ => new CliResult(0, "1.0.104-3", string.Empty, TimedOut: false));

        var item = Assert.Single(await CopilotSetupCheck.RunAsync(runner, folder));

        var call = Assert.Single(runner.Calls);
        Assert.Equal(new[] { "/d", "/c", "copilot", "--version", "--no-auto-update", "--sync-session=off" }, call.Args);
        Assert.Contains("bundled version 1.0.104-3", item.Detail);
    }

    [Fact]
    public async Task AVersionThatDoesNotParseIsSaidSoAndTheRowIsStillOk()
    {
        var folder = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "copilot.exe"), string.Empty);
        File.WriteAllText(Path.Combine(folder, "copilot.ps1"), string.Empty);
        var runner = new FakeCliRunner(_ => new CliResult(0, "no digits here", string.Empty, TimedOut: false));

        var item = Assert.Single(await CopilotSetupCheck.RunAsync(runner, folder));

        Assert.Equal(SetupState.Ok, item.State);
        Assert.Contains("bundled version not read", item.Detail);
    }

    [Fact]
    public async Task AFailedVersionReadIsAWarningNamingOnlyTheExitCode()
    {
        var folder = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "copilot.exe"), string.Empty);
        var runner = new FakeCliRunner(_ => new CliResult(1, "STDOUT SECRET", "STDERR SECRET", TimedOut: false));

        var item = Assert.Single(await CopilotSetupCheck.RunAsync(runner, folder));

        Assert.Equal(SetupState.Warning, item.State);
        Assert.Contains("(exit 1)", item.Detail);
        Assert.DoesNotContain("SECRET", item.Detail);
    }

    [Fact]
    public async Task ATimedOutVersionReadIsAWarning()
    {
        var folder = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "copilot.exe"), string.Empty);
        var runner = new FakeCliRunner(_ => new CliResult(-1, string.Empty, string.Empty, TimedOut: true));

        var item = Assert.Single(await CopilotSetupCheck.RunAsync(runner, folder));

        Assert.Equal(SetupState.Warning, item.State);
        Assert.Contains("timed out", item.Detail);
    }

    [Fact]
    public async Task ACopilotExeWithNoPs1OnThePathIsAWarningThatSaysAStartFromTextIsNotPossible()
    {
        var folder = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "copilot.exe"), string.Empty);
        var runner = new FakeCliRunner(_ => new CliResult(0, "GitHub Copilot CLI version 1.0.34.\n", string.Empty, TimedOut: false));

        var item = Assert.Single(await CopilotSetupCheck.RunAsync(runner, folder));

        Assert.Equal(SetupState.Warning, item.State);
        Assert.StartsWith("copilot.ps1 is not on PATH: a start from text is not possible; the version shown is that of copilot.exe", item.Detail);
        Assert.Contains("bundled version 1.0.34", item.Detail);
    }

    [Fact]
    public async Task WithCopilotPs1OnThePathBesideCopilotExeTheRowIsOkAndDoesNotWarn()
    {
        var folder = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "copilot.exe"), string.Empty);
        File.WriteAllText(Path.Combine(folder, "copilot.ps1"), string.Empty);
        var runner = new FakeCliRunner(_ => new CliResult(0, "GitHub Copilot CLI version 1.0.34.\n", string.Empty, TimedOut: false));

        var item = Assert.Single(await CopilotSetupCheck.RunAsync(runner, folder));

        Assert.Equal(SetupState.Ok, item.State);
        Assert.DoesNotContain("not on PATH", item.Detail);
        Assert.Contains("bundled version 1.0.34", item.Detail);
    }
}
