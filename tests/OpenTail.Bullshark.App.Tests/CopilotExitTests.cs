using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Copilot;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// G2.8 (App part): how the end of a Copilot terminal tab's process is classified and what the tab shows for it. Pure: no process is started, and no
/// title or session name is involved.
/// </summary>
public sealed class CopilotExitTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AnExitTheOwnerOrTheShutdownCausedIsExpectedAndSaysEnded(bool closedByOwner, bool appShuttingDown)
    {
        // Closing the tab or shutting the app ends the process on purpose, so it is not a failure, whatever the code.
        var record = CopilotExit.Classify(closedByOwner, appShuttingDown, 1);

        Assert.True(record.Expected);
        Assert.Equal(1, record.ExitCode);
        Assert.Equal("Ended.", record.Text);
    }

    [Fact]
    public void AnExitNobodyAskedForIsUnexpectedAndShowsItsCode()
    {
        var record = CopilotExit.Classify(closedByOwner: false, appShuttingDown: false, exitCode: 3);

        Assert.False(record.Expected);
        Assert.Equal(3, record.ExitCode);
        Assert.Equal("Ended with exit code 3.", record.Text);
    }

    [Fact]
    public void AZeroExitThatNobodyAskedForIsStillUnexpected()
    {
        // The process ended by itself (copilot quit on its own). Zero is a code, so the tab names it rather than hiding it.
        var record = CopilotExit.Classify(false, false, 0);

        Assert.False(record.Expected);
        Assert.Equal("Ended with exit code 0.", record.Text);
    }

    [Fact]
    public void ANegativeExitCodeIsShownWithItsSign()
    {
        // A Windows status such as STATUS_CONTROL_C_EXIT comes back as a negative int; it is shown as it was reported.
        var record = CopilotExit.Classify(false, false, -1073741510);

        Assert.False(record.Expected);
        Assert.Equal(-1073741510, record.ExitCode);
        Assert.Equal("Ended with exit code -1073741510.", record.Text);
    }

    [Fact]
    public void AnUnexpectedExitWithNoKnownCodeSaysSo()
    {
        var record = CopilotExit.Classify(false, false, null);

        Assert.False(record.Expected);
        Assert.Null(record.ExitCode);
        Assert.Equal("Ended; no exit code was reported.", record.Text);
    }

    [Fact]
    public void AnExpectedExitWithNoKnownCodeStillSaysEnded()
    {
        var record = CopilotExit.Classify(closedByOwner: true, appShuttingDown: false, exitCode: null);

        Assert.True(record.Expected);
        Assert.Null(record.ExitCode);
        Assert.Equal("Ended.", record.Text);
    }

    [Fact]
    public void AnEndedTitleGetsTheSuffixOnce()
    {
        Assert.Equal("Copilot: repo (ended)", CopilotExit.EndedTitle("Copilot: repo"));
    }

    [Fact]
    public void AnEndedTitleIsNotDoubledWhenItAlreadyHasTheSuffix()
    {
        // Each title refresh applies the suffix again, so it must be idempotent.
        var once = CopilotExit.EndedTitle("Copilot: repo");
        Assert.Equal("Copilot: repo (ended)", CopilotExit.EndedTitle(once));
        Assert.Equal(once, CopilotExit.EndedTitle(CopilotExit.EndedTitle(once)));
    }

    [Fact]
    public void ATitleThatOnlyContainsTheSuffixInTheMiddleStillGetsIt()
    {
        Assert.Equal("ended (ended) more (ended)", CopilotExit.EndedTitle("ended (ended) more"));
    }

    [Fact]
    public void AnEndedTitleOfNullIsRefused()
    {
        Assert.Throws<ArgumentNullException>(() => CopilotExit.EndedTitle(null!));
    }
}
