using System.IO;
using System.Text;
using OpenTail.Bullshark.Core.Continue;
using OpenTail.Bullshark.Core.OpenCode;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The rules of the opencode resume screen (OC3): the cost lines (OC-D9), the summary's cost and start lines (OC-O4, decided 2026-11-23), the labels, the Start
/// rule, the Remember mapping, the status lines, and the start command (<see cref="OpenCodeStartLauncher"/>). Pure: no window, no process, no file is read
/// or written.
/// </summary>
public sealed class OpenCodeResumeRulesTests
{
    [Fact]
    public void TheInFullCostLineIsTheOpencodeLine()
    {
        Assert.Equal("Opens the session in opencode's terminal. Nothing is sent until you type.", OpenCodeResumeRules.CostLine(OpenCodeResumeMode.Full));
    }

    [Fact]
    public void TheLastTurnsCostLineIsTheOpencodeLine()
    {
        Assert.Equal(
            "Start makes one model turn on your opencode login: opencode submits the prompt at once. Nothing else is sent.",
            OpenCodeResumeRules.CostLine(OpenCodeResumeMode.LastTurns));
    }

    [Fact]
    public void TheLastTurnsCostLineSaysOneModelTurn()
    {
        Assert.Contains("one model turn", OpenCodeResumeRules.CostLine(OpenCodeResumeMode.LastTurns));
    }

    [Theory]
    [InlineData(OpenCodeResumeMode.Full)]
    [InlineData(OpenCodeResumeMode.LastTurns)]
    [InlineData(OpenCodeResumeMode.Summary)]
    public void NoCostLineOfOpencodeSaysNoModelCall(OpenCodeResumeMode mode)
    {
        Assert.DoesNotContain("no model call", OpenCodeResumeRules.CostLine(mode), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSummaryCostLineIsTheOwnersWordingAndNamesOneModelCallAndTheSessionRow()
    {
        Assert.Equal(
            "Making the summary is one model call on your opencode login, and opencode writes a session row into your opencode database.",
            OpenCodeResumeRules.SummaryCostLine);
        Assert.Equal(OpenCodeResumeRules.SummaryCostLine, OpenCodeResumeRules.CostLine(OpenCodeResumeMode.Summary));
    }

    [Fact]
    public void TheSummaryStartLineSaysOneModelTurnAndNamesTheSummaryAndTheLastTurns()
    {
        Assert.Equal(
            "Started opencode with the summary and the last turns of Example title (one model turn).",
            OpenCodeResumeRules.StartedSummaryLine("Example title"));
    }

    [Fact]
    public void TheTurnsBoxAcceptsTheWholeRangeAndNothingElse()
    {
        Assert.True(OpenCodeResumeRules.TryTurns("0", out var zero));
        Assert.Equal(0, zero);
        Assert.True(OpenCodeResumeRules.TryTurns(" 7 ", out var seven));
        Assert.Equal(7, seven);
        Assert.True(OpenCodeResumeRules.TryTurns(OpenCodeResumeRules.MaxTurns.ToString(), out var max));
        Assert.Equal(OpenCodeResumeRules.MaxTurns, max);
        Assert.False(OpenCodeResumeRules.TryTurns((OpenCodeResumeRules.MaxTurns + 1).ToString(), out var over));
        Assert.Equal(OpenCodeResumeRules.DefaultTurns, over);
        Assert.False(OpenCodeResumeRules.TryTurns("-1", out _));
        Assert.False(OpenCodeResumeRules.TryTurns("many", out _));
    }

    [Fact]
    public void TheRememberedModeIsTheContinueModeOfTheChoice()
    {
        Assert.Equal(ContinueMode.Full, OpenCodeResumeRules.ContinueModeOf(OpenCodeResumeMode.Full));
        Assert.Equal(ContinueMode.LastTurns, OpenCodeResumeRules.ContinueModeOf(OpenCodeResumeMode.LastTurns));
        Assert.Equal(ContinueMode.Summary, OpenCodeResumeRules.ContinueModeOf(OpenCodeResumeMode.Summary));
    }

    [Fact]
    public void TheStatusLineAfterALastTurnsStartNamesTheTitleAndTheOneModelTurn()
    {
        Assert.Equal("Started opencode with the last turns of Example title (one model turn).", OpenCodeResumeRules.StartedLine("Example title"));
    }

    [Fact]
    public void TheStatusLineAfterAFullResumeNamesTheTitle()
    {
        Assert.Equal("Opened Example title in opencode.", OpenCodeResumeRules.OpenedLine("Example title"));
    }

    [Fact]
    public void TheNoShimLineNamesOpencodePs1AndNoOtherCli()
    {
        Assert.Contains("opencode.ps1", OpenCodeResumeRules.NoShimOnPath);
        Assert.DoesNotContain("copilot", OpenCodeResumeRules.NoShimOnPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("codex", OpenCodeResumeRules.NoShimOnPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheFinalCallRunsTheShimWithTheFolderAndThePromptVariable()
    {
        Assert.Equal("& $shim.Source 'C:\\Work\\Fixture' --prompt $prompt", OpenCodeStartLauncher.FinalCall("C:\\Work\\Fixture"));
    }

    [Fact]
    public void TheFinalCallDoublesAnApostropheInTheFolder()
    {
        Assert.Equal("& $shim.Source 'C:\\Work\\O''Brien' --prompt $prompt", OpenCodeStartLauncher.FinalCall("C:\\Work\\O'Brien"));
    }

    [Fact]
    public void AFolderWithALineBreakIsRefusedBeforeAnyScriptIsMade()
    {
        Assert.Throws<ArgumentException>(() => OpenCodeStartLauncher.FinalCall("C:\\Work\\one\ntwo"));
    }

    [Fact]
    public void TheCommandLineRunsTheOpencodeScriptAndKeepsTheTextOutOfIt()
    {
        const string folder = "C:\\Work\\Fixture";
        const string promptPath = "C:\\Temp\\Starts\\opencode\\fixture-start.txt";
        var command = OpenCodeStartLauncher.CommandLine(folder, promptPath);

        Assert.StartsWith(CliStartLauncher.Prefix, command, StringComparison.Ordinal);
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(command[CliStartLauncher.Prefix.Length..]));
        Assert.Contains("Get-Command opencode -CommandType ExternalScript", script, StringComparison.Ordinal);
        Assert.Contains("opencode PowerShell shim (opencode.ps1) not found.", script, StringComparison.Ordinal);
        Assert.Contains("[System.IO.File]::ReadAllText('C:\\Temp\\Starts\\opencode\\fixture-start.txt'", script, StringComparison.Ordinal);
        Assert.Contains(OpenCodeStartLauncher.FinalCall(folder), script, StringComparison.Ordinal);
        Assert.DoesNotContain("copilot", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheOpencodeStartsFolderIsAFolderNamedForOpencodeBesideTheStateFile()
    {
        var statePath = Path.Combine(Path.GetTempPath(), "opentail-test", "state.json");
        var beside = Path.GetDirectoryName(Path.GetFullPath(statePath))!;
        Assert.Equal(Path.Combine(beside, "Starts", "opencode"), OpenCodeStartLauncher.StartsFolderBeside(statePath));
    }
}
