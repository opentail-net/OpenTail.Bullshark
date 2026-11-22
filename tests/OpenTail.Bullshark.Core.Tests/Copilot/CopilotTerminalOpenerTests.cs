using OpenTail.Bullshark.Core.Copilot;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Tests.Support;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Copilot;

/// <summary>
/// Stage G1.5 and G1.6 (G-D8, G-D11, G-D12): the command lines of a Copilot tab, the folder rules, the provider's interface set, and the rule that
/// every command line the app builds carries <c>--sync-session=off</c>. Only strings are built; no copilot program is started.
/// </summary>
public sealed class CopilotTerminalOpenerTests
{
    private const string Id = "3f2a9c10-1b2d-4e5f-8a9b-0c1d2e3f4a51";

    private readonly CopilotTerminalOpener _opener = new();

    [Fact]
    public void ANewSessionRunsCopilotThroughCmdInTheFolderWithSyncOff()
    {
        Assert.Equal("cmd.exe /d /c copilot -C \"C:\\Fixture\\Demo Folder\" --sync-session=off", _opener.NewCommandLine("C:\\Fixture\\Demo Folder"));
    }

    [Fact]
    public void AFolderWithAnAmpersandAndACaretIsQuotedAndKept()
    {
        // Inside the double quotes cmd.exe takes & and ^ as text, so the folder is accepted and carried unchanged.
        Assert.Equal("cmd.exe /d /c copilot -C \"C:\\Fixture\\R&D ^ Demo\" --sync-session=off", _opener.NewCommandLine("C:\\Fixture\\R&D ^ Demo"));
    }

    [Theory]
    [InlineData("C:\\Fixture\\100%\\Demo")]
    [InlineData("C:\\Fixture\\%USERNAME%")]
    [InlineData("C:\\Fixture\\hello!")]
    [InlineData("C:\\Fixture\\a\"b")]
    [InlineData("C:\\Fixture\\a\" & calc")]
    [InlineData("C:\\Fixture\\a\nb")]
    [InlineData("C:\\Fixture\\a\tb")]
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
    public void AResumeRunsCopilotWithTheIdAndSyncOffAndNoFolderFlag()
    {
        var line = _opener.ResumeCommandLine(Id);

        Assert.Equal($"cmd.exe /d /c copilot --resume={Id} --sync-session=off", line);
        Assert.DoesNotContain(" -C ", line);
    }

    [Theory]
    [InlineData("")]
    [InlineData("3f2a9c10b1b2d4e5f8a9b0c1d2e3f4a51")]
    [InlineData("3f2a9c10-1b2d-4e5f-8a9b-0c1d2e3f4a5")]
    [InlineData("3f2a9c10-1b2d-4e5f-8a9b-0c1d2e3f4a51 & calc")]
    [InlineData("3f2a9c10-1b2d-4e5f-8a9b-0c1d2e3f4a51 --yolo")]
    [InlineData("3f2a9c10-1b2d-4e5f-8a9b-0c1d2e3f4a51\n")]
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
    public void EveryCommandLineTheAppBuildsCarriesSyncOff()
    {
        // G-D8: the terminal, the resume, the summary call and the setup check's version read, each checked as the string it builds.
        var commands = new List<string>
        {
            _opener.NewCommandLine("C:\\Fixture\\Demo"),
            _opener.ResumeCommandLine(Id),
            string.Join(' ', CopilotSummarizer.Arguments("C:\\Fixture\\Scratch\\config", "C:\\Fixture\\Scratch\\log")),
            string.Join(' ', CopilotSetupCheck.VersionArguments),
        };

        Assert.Equal(4, commands.Count);
        foreach (var command in commands)
        {
            Assert.Contains("--sync-session=off", command);
        }
    }

    [Fact]
    public void TheCopilotProviderOpensTerminalsAndReadsHistoryButHasNoSupervisorOrLiveList()
    {
        var provider = new CopilotProvider(ProviderPaths.ForCopilot(_ => "C:\\Fixture\\copilot-home", userProfile: "C:\\Fixture\\profile"));

        Assert.Equal(ProviderId.Copilot, provider.Id);
        Assert.Equal("Copilot", provider.DisplayName);
        Assert.True(ProviderFeatures.Has<ITerminalOpener>(provider));
        Assert.True(ProviderFeatures.Has<IHistorySource>(provider));
        Assert.True(ProviderFeatures.Has<ITranscriptReader>(provider));
        Assert.False(ProviderFeatures.Has<ISupervisedLauncher>(provider));
        Assert.False(ProviderFeatures.Has<ILiveSource>(provider));
        Assert.Equal($"cmd.exe /d /c copilot --resume={Id} --sync-session=off", provider.ResumeCommandLine(Id));
        Assert.Equal("cmd.exe /d /c copilot -C \"C:\\Fixture\\Demo\" --sync-session=off", provider.NewCommandLine("C:\\Fixture\\Demo"));
        Assert.Equal("C:\\Fixture\\copilot-home\\session-state", provider.Paths.HistoryRoot);
    }
}
