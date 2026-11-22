using System.ComponentModel;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Copilot;

/// <summary>
/// Stage G1.6 (G-D13, G-O5): the Copilot rows of the setup check, with the version rule of CopilotFlagsVersion. Only a PATH given by the test is searched,
/// and both version reads go through fake runners (the terminal copy's through cmd.exe, the start copy's through pwsh), so no copilot program is run.
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

    /// <summary>A fake read that prints <paramref name="stdout"/> and exits with 0.</summary>
    private static FakeCliRunner Printing(string stdout) => new(_ => new CliResult(0, stdout, string.Empty, TimedOut: false));

    /// <summary>A bin folder under the test folder, holding empty files of the given names.</summary>
    private string Bin(params string[] files)
    {
        var folder = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(folder);
        foreach (var file in files) File.WriteAllText(Path.Combine(folder, file), string.Empty);
        return folder;
    }

    /// <summary>The exact detail of a Problem row for an older copy: the sentence of the check, then the path it is about.</summary>
    private static string ProblemDetail(string copy, string version, string file) =>
        $"{copy} is version {version}, older than 1.0.103, the first build with the flags this app passes. Update it, or remove the older copy from PATH.; found: {file}";

    [Fact]
    public async Task WithNoCopilotOnThePathTheRowIsAProblemAndNoVersionIsRead()
    {
        var empty = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(empty);
        var terminal = new FakeCliRunner();
        var start = new FakeCliRunner();

        var items = await CopilotSetupCheck.RunAsync(terminal, start, empty);

        var item = Assert.Single(items);
        Assert.Equal("Copilot CLI", item.Area);
        Assert.Equal("copilot", item.Name);
        Assert.Equal(SetupState.Problem, item.State);
        Assert.StartsWith("not found on PATH: install GitHub Copilot CLI", item.Detail);
        Assert.Empty(terminal.Calls);
        Assert.Empty(start.Calls);
    }

    [Theory]
    [InlineData("copilot.exe")]
    [InlineData("copilot.cmd")]
    [InlineData("copilot.ps1")]
    public async Task AnOlderTerminalCopyIsAProblemNamingItsPathAndAnUpToDateStartCopyIsOk(string fileName)
    {
        // A start from text needs copilot.ps1 on PATH, so every case has one: the start row is read in each.
        var folder = Bin(fileName, "copilot.ps1");
        var file = Path.Combine(folder, fileName);
        var terminal = Printing("GitHub Copilot CLI version 1.0.34.\n");
        var start = Printing("GitHub Copilot CLI version 1.0.103.\n");

        var items = await CopilotSetupCheck.RunAsync(terminal, start, folder);

        Assert.Equal(2, items.Count);
        Assert.Equal("copilot", items[0].Name);
        Assert.Equal(SetupState.Problem, items[0].State);
        Assert.Equal(ProblemDetail("The terminal's copilot", "1.0.34", file), items[0].Detail);
        Assert.Equal("copilot.ps1", items[1].Name);
        Assert.Equal(SetupState.Ok, items[1].State);
        Assert.Equal("copilot.ps1 version 1.0.103 (read with auto-update off); found: " + Path.Combine(folder, "copilot.ps1"), items[1].Detail);
    }

    [Fact]
    public async Task TheTerminalReadIsTheBundledOneWithAutoUpdateAndSyncOffAndAFlagsVersionIsOk()
    {
        var folder = Bin("copilot.cmd");
        var terminal = Printing("1.0.104-3");

        var items = await CopilotSetupCheck.RunAsync(terminal, Printing("1.0.104-3"), folder);

        var call = Assert.Single(terminal.Calls);
        Assert.Equal(new[] { "/d", "/c", "copilot", "--version", "--no-auto-update", "--sync-session=off" }, call.Args);
        // The start row is a Warning here: copilot.ps1 is not on PATH, so the start copy is not read.
        Assert.Equal(2, items.Count);
        Assert.Equal(SetupState.Ok, items[0].State);
        Assert.Contains("bundled version 1.0.104-3", items[0].Detail);
        Assert.Equal(SetupState.Warning, items[1].State);
    }

    [Fact]
    public async Task AVersionThatDoesNotParseIsSaidSoAndTheRowIsAWarning()
    {
        var folder = Bin("copilot.exe", "copilot.ps1");
        var terminal = Printing("no digits here");
        var start = Printing("no digits here");

        var items = await CopilotSetupCheck.RunAsync(terminal, start, folder);

        Assert.Equal(2, items.Count);
        Assert.Equal(SetupState.Warning, items[0].State);
        Assert.Contains("bundled version not read (no version in the output)", items[0].Detail);
        Assert.Equal(SetupState.Warning, items[1].State);
        Assert.Contains("copilot.ps1 version not read (no version in the output)", items[1].Detail);
    }

    [Fact]
    public async Task AFailedVersionReadIsAWarningNamingOnlyTheExitCode()
    {
        var folder = Bin("copilot.exe");
        var terminal = new FakeCliRunner(_ => new CliResult(1, "STDOUT SECRET", "STDERR SECRET", TimedOut: false));
        var start = Printing("1.0.103");

        var items = await CopilotSetupCheck.RunAsync(terminal, start, folder);

        Assert.Equal(2, items.Count);
        Assert.Equal(SetupState.Warning, items[0].State);
        Assert.Contains("(exit 1)", items[0].Detail);
        Assert.DoesNotContain("SECRET", items[0].Detail);
        Assert.Equal("copilot.ps1", items[1].Name);
        Assert.Equal(SetupState.Warning, items[1].State);
        Assert.Equal("copilot.ps1 not found on PATH: a start from text is not possible", items[1].Detail);
        Assert.Empty(start.Calls);
    }

    [Fact]
    public async Task ATimedOutVersionReadIsAWarning()
    {
        var folder = Bin("copilot.exe");
        var terminal = new FakeCliRunner(_ => new CliResult(-1, string.Empty, string.Empty, TimedOut: true));
        var start = Printing("1.0.103");

        var items = await CopilotSetupCheck.RunAsync(terminal, start, folder);

        Assert.Equal(2, items.Count);
        Assert.Equal(SetupState.Warning, items[0].State);
        Assert.Contains("timed out", items[0].Detail);
        Assert.Equal(SetupState.Warning, items[1].State);
        Assert.Empty(start.Calls);
    }

    [Fact]
    public async Task ACopilotExeWithNoPs1OnThePathGivesAProblemForTheTerminalCopyAndAWarningForTheStartRow()
    {
        var folder = Bin("copilot.exe");
        var terminal = Printing("GitHub Copilot CLI version 1.0.34.\n");
        var start = Printing("1.0.103");

        var items = await CopilotSetupCheck.RunAsync(terminal, start, folder);

        Assert.Equal(2, items.Count);
        Assert.Equal(SetupState.Problem, items[0].State);
        Assert.Equal(ProblemDetail("The terminal's copilot", "1.0.34", Path.Combine(folder, "copilot.exe")), items[0].Detail);
        Assert.Equal("copilot.ps1", items[1].Name);
        Assert.Equal(SetupState.Warning, items[1].State);
        Assert.Equal("copilot.ps1 not found on PATH: a start from text is not possible", items[1].Detail);
        Assert.Empty(start.Calls);
    }

    [Fact]
    public async Task WithCopilotPs1OnThePathBesideCopilotExeTheStartRowIsOkAndDoesNotWarn()
    {
        var folder = Bin("copilot.exe", "copilot.ps1");
        var terminal = Printing("GitHub Copilot CLI version 1.0.34.\n");
        var start = Printing("GitHub Copilot CLI version 1.0.103.\n");

        var items = await CopilotSetupCheck.RunAsync(terminal, start, folder);

        Assert.Equal(2, items.Count);
        Assert.Equal(SetupState.Problem, items[0].State);
        Assert.Equal(SetupState.Ok, items[1].State);
        Assert.DoesNotContain("not found", items[1].Detail);
        Assert.Contains("copilot.ps1 version 1.0.103", items[1].Detail);
    }

    [Fact]
    public async Task BothCopiesAtAFlagsVersionAreOkWithNoProblem()
    {
        var folder = Bin("copilot.exe", "copilot.ps1");

        var items = await CopilotSetupCheck.RunAsync(Printing("1.0.103"), Printing("1.0.104-3"), folder);

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal(SetupState.Ok, item.State));
        Assert.DoesNotContain(items, item => item.State == SetupState.Problem);
    }

    [Fact]
    public async Task AnOlderStartCopyIsAProblemNamingItsPathAndTheTerminalCopyIsOk()
    {
        var folder = Bin("copilot.exe", "copilot.ps1");

        var items = await CopilotSetupCheck.RunAsync(Printing("1.0.104-3"), Printing("1.0.34"), folder);

        Assert.Equal(2, items.Count);
        Assert.Equal(SetupState.Ok, items[0].State);
        Assert.Equal(SetupState.Problem, items[1].State);
        Assert.Equal(ProblemDetail("The start's copilot.ps1", "1.0.34", Path.Combine(folder, "copilot.ps1")), items[1].Detail);
    }

    [Fact]
    public async Task TheStartReadRunsTheShimThroughPowerShellWithTheSameFlags()
    {
        var folder = Bin("copilot.exe", "copilot.ps1");
        var shim = Path.Combine(folder, "copilot.ps1");
        var start = Printing("1.0.103");

        await CopilotSetupCheck.RunAsync(Printing("1.0.103"), start, folder);

        var call = Assert.Single(start.Calls);
        Assert.Equal(
            new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", $"& '{shim}' --version --no-auto-update --sync-session=off" },
            call.Args);
    }

    [Fact]
    public async Task ASingleQuoteInTheShimPathIsDoubledForPowerShell()
    {
        var folder = Path.Combine(_dir, "O'Brien");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "copilot.exe"), string.Empty);
        File.WriteAllText(Path.Combine(folder, "copilot.ps1"), string.Empty);
        var start = Printing("1.0.103");

        await CopilotSetupCheck.RunAsync(Printing("1.0.103"), start, folder);

        var call = Assert.Single(start.Calls);
        var shim = Path.Combine(folder, "copilot.ps1").Replace("'", "''");
        Assert.Equal($"& '{shim}' --version --no-auto-update --sync-session=off", call.Args[4]);
    }

    [Fact]
    public async Task WithOnlyCopilotPs1OnThePathAFailedTerminalReadIsAWarningAndTheStartRowIsStillRead()
    {
        // cmd.exe does not run copilot.ps1, so the terminal read fails here and the row warns, as before this rule.
        var folder = Bin("copilot.ps1");
        var terminal = new FakeCliRunner(_ => new CliResult(1, string.Empty, "not recognized", TimedOut: false));
        var start = Printing("1.0.103");

        var items = await CopilotSetupCheck.RunAsync(terminal, start, folder);

        Assert.Equal(2, items.Count);
        Assert.Equal("copilot", items[0].Name);
        Assert.Equal(SetupState.Warning, items[0].State);
        Assert.Contains("(exit 1)", items[0].Detail);
        Assert.Equal("copilot.ps1", items[1].Name);
        Assert.Equal(SetupState.Ok, items[1].State);
        Assert.Single(start.Calls);
    }

    [Fact]
    public async Task AStartReadThatCannotBeStartedIsAWarningNotAnException()
    {
        var folder = Bin("copilot.exe", "copilot.ps1");
        var start = new FakeCliRunner(_ => throw new Win32Exception(2, "missing"));

        var items = await CopilotSetupCheck.RunAsync(Printing("1.0.103"), start, folder);

        Assert.Equal(2, items.Count);
        Assert.Equal(SetupState.Ok, items[0].State);
        Assert.Equal("copilot.ps1", items[1].Name);
        Assert.Equal(SetupState.Warning, items[1].State);
        Assert.StartsWith("copilot.ps1 version not read (the program could not be started)", items[1].Detail);
    }

    [Fact]
    public async Task TheLogLineNamesTheCopyAndItsVersionButNeverThePath()
    {
        var folder = Bin("copilot.exe", "copilot.ps1");
        var lines = new List<string>();

        await CopilotSetupCheck.RunAsync(Printing("1.0.34"), Printing("1.0.103"), folder, log: lines.Add);

        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, line => line.Contains("The terminal's copilot") && line.Contains("1.0.34"));
        Assert.Contains(lines, line => line.Contains("The start's copilot.ps1") && line.Contains("1.0.103"));
        Assert.All(lines, line => Assert.DoesNotContain(folder, line));
    }
}
