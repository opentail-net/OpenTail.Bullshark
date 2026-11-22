using OpenTail.Bullshark.Core.OpenCode;

namespace OpenTail.Bullshark.Core.Tests.OpenCode;

/// <summary>
/// Stage OC1a (OC-D8, OC-D11): the opencode command lines, the folder rules, and the id rule. Only strings are built; no opencode program is started.
/// </summary>
public sealed class OpenCodeTerminalOpenerTests
{
    private const string Id = "ses_0123456789abAbCdEfGhIjKlMn";

    private readonly OpenCodeTerminalOpener _opener = new();

    [Fact]
    public void ANewSessionRunsOpenCodeThroughCmdWithTheFolderAsAQuotedArgument()
    {
        Assert.Equal("cmd.exe /d /c opencode \"C:\\Fixture\\Demo Folder\"", _opener.NewCommandLine("C:\\Fixture\\Demo Folder"));
    }

    [Fact]
    public void ANewSessionNeverPassesTheSkipPermissionsFlag()
    {
        Assert.DoesNotContain("--dangerously-skip-permissions", _opener.NewCommandLine("C:\\Fixture\\Demo"));
    }

    [Fact]
    public void AFolderWithAnAmpersandAndACaretIsQuotedAndKept()
    {
        // Inside the double quotes cmd.exe takes & and ^ as text, so the folder is accepted and carried unchanged.
        Assert.Equal("cmd.exe /d /c opencode \"C:\\Fixture\\R&D ^ Demo\"", _opener.NewCommandLine("C:\\Fixture\\R&D ^ Demo"));
    }

    [Theory]
    [InlineData("C:\\Fixture\\100%\\Demo")]
    [InlineData("C:\\Fixture\\%USERNAME%")]
    [InlineData("C:\\Fixture\\hello!")]
    [InlineData("C:\\Fixture\\a\"b")]
    [InlineData("C:\\Fixture\\a\" & calc")]
    [InlineData("C:\\Fixture\\a\nb")]
    [InlineData("C:\\Fixture\\ends\\")]
    [InlineData("")]
    [InlineData("   ")]
    public void AFolderThatWouldBreakTheCmdLineIsRefused(string folder)
    {
        Assert.Throws<ArgumentException>(() => _opener.NewCommandLine(folder));
    }

    [Fact]
    public void ANullFolderIsRefused()
    {
        Assert.Throws<ArgumentException>(() => _opener.NewCommandLine(null!));
    }

    [Fact]
    public void AResumeRunsOpenCodeWithTheShortIdFlagAndNoFolder()
    {
        var line = _opener.ResumeCommandLine(Id);

        Assert.Equal($"cmd.exe /d /c opencode -s {Id}", line);
        Assert.DoesNotContain(" -C ", line);
        Assert.DoesNotContain("--dangerously-skip-permissions", line);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ses_0123456789abAbCdEfGhIjKlM")]
    [InlineData("-ses_0123456789abAbCdEfGhIjKlMn")]
    [InlineData("ses_0123456789abAbCdEfGhIjKlMn & calc")]
    [InlineData("ses_0123456789abAbCdEfGhIjKlMn --yolo")]
    [InlineData("ses_0123456789abAbCdEfGhIjKlMn\n")]
    [InlineData("3f2a9c10-1b2d-4e5f-8a9b-0c1d2e3f4a51")]
    public void AnIdOfAnotherShapeIsRefusedWithAMessageThatDoesNotCarryTheId(string sessionId)
    {
        var error = Assert.Throws<ArgumentException>(() => _opener.ResumeCommandLine(sessionId));

        if (sessionId.Length > 0)
        {
            Assert.DoesNotContain(sessionId.Trim(), error.Message);
        }
    }

    [Fact]
    public void ANullIdIsRefused()
    {
        Assert.Throws<ArgumentException>(() => _opener.ResumeCommandLine(null!));
    }
}
