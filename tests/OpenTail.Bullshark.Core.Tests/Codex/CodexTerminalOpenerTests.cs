using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Providers;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>
/// Stage C3 (Core part): the command lines of a Codex tab, and what the Codex provider offers. Only strings are built here; no codex
/// program is started, and the folders are synthetic.
/// </summary>
public sealed class CodexTerminalOpenerTests
{
    private const string Id = "0190a2b3-c4d5-7e6f-8a9b-0c1d2e3f4a5b";

    private readonly CodexTerminalOpener _opener = new();

    [Fact]
    public void ANewSessionRunsCodexThroughCmdInTheFolderInQuotes()
    {
        // codex is a .cmd shim on this machine, so the line goes through cmd.exe (no codex.exe to start directly).
        Assert.Equal("cmd.exe /d /c codex -C \"C:\\Work\\Synthetic Folder\"", _opener.NewCommandLine("C:\\Work\\Synthetic Folder"));
    }

    [Fact]
    public void AFolderWithAnAmpersandAndACaretIsQuotedAndKept()
    {
        // Inside the double quotes cmd.exe takes & and ^ as text, so the folder is accepted and carried unchanged.
        Assert.Equal("cmd.exe /d /c codex -C \"C:\\Work\\R&D ^ Synthetic\"", _opener.NewCommandLine("C:\\Work\\R&D ^ Synthetic"));
    }

    [Fact]
    public void AFolderWithAPercentSignIsRefused()
    {
        // cmd.exe expands %NAME% even inside quotes, so a percent sign is refused rather than passed on.
        Assert.Throws<ArgumentException>(() => _opener.NewCommandLine("C:\\Work\\100%\\Synthetic"));
        Assert.Throws<ArgumentException>(() => _opener.NewCommandLine("C:\\Work\\%USERNAME%"));
    }

    [Fact]
    public void AFolderWithAnExclamationMarkIsRefused()
    {
        // cmd.exe expands !NAME! when delayed expansion is on (a registry setting on the machine), so an exclamation mark is refused too.
        Assert.Throws<ArgumentException>(() => _opener.NewCommandLine("C:\\Work\\hello!"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnEmptyFolderIsRefused(string? folder)
    {
        Assert.Throws<ArgumentException>(() => _opener.NewCommandLine(folder!));
    }

    [Theory]
    [InlineData("C:\\Work\\a\"b")]
    [InlineData("C:\\Work\\a\" & calc")]
    [InlineData("C:\\Work\\a\nb")]
    [InlineData("C:\\Work\\a\tb")]
    [InlineData("C:\\Work\\ends\\")]
    public void AFolderThatWouldBreakTheQuotesIsRefused(string folder)
    {
        Assert.Throws<ArgumentException>(() => _opener.NewCommandLine(folder));
    }

    [Fact]
    public void AResumeRunsCodexResumeThroughCmdWithTheId()
    {
        Assert.Equal($"cmd.exe /d /c codex resume {Id}", _opener.ResumeCommandLine(Id));
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
    public void AnIdThatIsNotAUuidIsRefused(string sessionId)
    {
        Assert.Throws<ArgumentException>(() => _opener.ResumeCommandLine(sessionId));
    }

    [Fact]
    public void ANullIdIsRefused()
    {
        Assert.Throws<ArgumentException>(() => _opener.ResumeCommandLine(null!));
    }

    [Fact]
    public void TheCodexProviderOpensTerminalsButHasNoSupervisorOrLiveList()
    {
        var provider = new CodexProvider(new ProviderPaths("C:\\synthetic-codex-home", "C:\\synthetic-codex-home\\sessions", null));

        Assert.Equal(ProviderId.Codex, provider.Id);
        Assert.Equal("Codex", provider.DisplayName);
        Assert.True(ProviderFeatures.Has<ITerminalOpener>(provider));
        Assert.True(ProviderFeatures.Has<IHistorySource>(provider));
        Assert.False(ProviderFeatures.Has<ISupervisedLauncher>(provider));
        Assert.False(ProviderFeatures.Has<ILiveSource>(provider));
        Assert.Equal($"cmd.exe /d /c codex resume {Id}", provider.ResumeCommandLine(Id));
    }
}
