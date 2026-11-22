using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Providers;

/// <summary>
/// Provider kit R3: the rules for a folder and a session id in a <c>cmd.exe</c> command line (<see cref="CommandLineRules"/>). Only strings are built;
/// no program is started, and every folder is synthetic. The providers' own opener tests cover their lines.
/// </summary>
public sealed class CommandLineRulesTests
{
    private const string Uuid = "0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5b";

    [Theory]
    [InlineData("C:\\Work\\Synthetic Folder")]
    [InlineData("C:\\Work\\R&D ^ Synthetic")]
    [InlineData("\\\\server\\share\\folder")]
    public void AFolderThatCanSitInsideTheQuotesIsQuotedAndKept(string folder)
    {
        Assert.Equal($"-C \"{folder}\"", CommandLineRules.FolderOption(folder));
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   ", "empty")]
    [InlineData("C:\\Work\\a\"b", "double quote")]
    [InlineData("C:\\Work\\a\" & calc", "double quote")]
    [InlineData("C:\\Work\\hello!", "exclamation mark")]
    [InlineData("C:\\Work\\100%\\Synthetic", "percent sign")]
    [InlineData("C:\\Work\\%USERNAME%", "percent sign")]
    [InlineData("C:\\Work\\a\nb", "control character")]
    [InlineData("C:\\Work\\a\tb", "control character")]
    [InlineData("C:\\Work\\ends\\", "backslash")]
    public void AFolderThatWouldBreakTheLineIsRefusedForItsReason(string folder, string reason)
    {
        var error = Assert.Throws<ArgumentException>(() => CommandLineRules.RequireFolder(folder));
        Assert.Contains(reason, error.Message);
        Assert.Throws<ArgumentException>(() => CommandLineRules.FolderOption(folder));
    }

    [Fact]
    public void ANullFolderIsRefusedAsEmpty()
    {
        var error = Assert.Throws<ArgumentException>(() => CommandLineRules.RequireFolder(null!));
        Assert.Contains("empty", error.Message);
    }

    [Fact]
    public void CmdWrapPutsTheProgramAfterCmdAndJoinsTheArgumentsWithSpaces()
    {
        Assert.Equal("cmd.exe /d /c codex resume", CommandLineRules.CmdWrap("codex", ["resume"]));
        Assert.Equal($"cmd.exe /d /c codex resume {Uuid}", CommandLineRules.CmdWrap("codex", ["resume", Uuid]));
        Assert.Equal("cmd.exe /d /c copilot", CommandLineRules.CmdWrap("copilot", []));
    }

    [Fact]
    public void CmdWrapAddsNoQuotesOfItsOwnToItsArguments()
    {
        // The folder option carries its own quotes (FolderOption); CmdWrap only joins the tokens it is given.
        Assert.Equal(
            "cmd.exe /d /c copilot -C \"C:\\Fixture\\Demo\" --sync-session=off",
            CommandLineRules.CmdWrap("copilot", [CommandLineRules.FolderOption("C:\\Fixture\\Demo"), "--sync-session=off"]));
    }

    [Theory]
    [InlineData("0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5b")]
    [InlineData("3F2A9C10-1B2D-4E5F-8A9B-0C1D2E3F4A51")]
    public void AUuidInTheUsualFormHasTheRightShape(string id)
    {
        Assert.True(CommandLineRules.IsSessionIdShape(id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("0190a2b3c4d57e6f8a9b0c1d2e3f4a5b")]
    [InlineData("0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5")]
    [InlineData("0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5bb")]
    [InlineData("0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5g")]
    [InlineData("0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5b & calc")]
    [InlineData("0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5b --yolo")]
    [InlineData("0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5b\n")]
    [InlineData("name-of-a-session")]
    public void AnIdThatIsNotAUuidDoesNotHaveTheRightShape(string id)
    {
        Assert.False(CommandLineRules.IsSessionIdShape(id));
    }

    [Fact]
    public void ANullIdDoesNotHaveTheRightShape()
    {
        Assert.False(CommandLineRules.IsSessionIdShape(null!));
    }
}
