using OpenTail.Bullshark.Core.Reports;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Reports;

/// <summary>
/// The headless host's command line (round 4, item 4). A <c>--projects</c> with no folder after it is a problem, not a folder that
/// happens to be named like a flag.
/// </summary>
public sealed class HostCommandTests
{
    private const string Default = @"C:\Users\someone\.claude\projects";

    [Fact]
    public void AUsageReportWithoutProjectsUsesTheDefaultFolder()
    {
        var command = HostCommand.Parse(["--usage-report"], Default);

        Assert.Equal(HostReport.Usage, command.Report);
        Assert.Equal(Default, command.Projects);
        Assert.Null(command.Problem);
    }

    [Fact]
    public void ACacheReportTakesTheFolderAfterProjects()
    {
        var command = HostCommand.Parse(["--cache-report", "--projects", @"D:\transcripts"], Default);

        Assert.Equal(HostReport.Cache, command.Report);
        Assert.Equal(@"D:\transcripts", command.Projects);
        Assert.Null(command.Problem);
    }

    [Fact]
    public void TheFolderCanComeBeforeTheReportFlag()
    {
        var command = HostCommand.Parse(["--projects", @"D:\transcripts", "--usage-report"], Default);

        Assert.Equal(HostReport.Usage, command.Report);
        Assert.Equal(@"D:\transcripts", command.Projects);
        Assert.Null(command.Problem);
    }

    [Fact]
    public void ProjectsFollowedByAnotherFlagIsAProblemAndNoFolderIsTaken()
    {
        var command = HostCommand.Parse(["--usage-report", "--projects", "--cache-report"], Default);

        Assert.Equal(HostCommand.ProjectsNeedsFolder, command.Problem);
        Assert.Equal(Default, command.Projects);
    }

    [Fact]
    public void ProjectsAtTheEndIsAProblem()
    {
        var command = HostCommand.Parse(["--cache-report", "--projects"], Default);

        Assert.Equal(HostCommand.ProjectsNeedsFolder, command.Problem);
    }

    [Fact]
    public void AnEmptyFolderAfterProjectsIsAProblem()
    {
        var command = HostCommand.Parse(["--usage-report", "--projects", ""], Default);

        Assert.Equal(HostCommand.ProjectsNeedsFolder, command.Problem);
    }

    [Fact]
    public void ABlankFolderAfterProjectsIsAProblem()
    {
        var command = HostCommand.Parse(["--usage-report", "--projects", "   "], Default);

        Assert.Equal(HostCommand.ProjectsNeedsFolder, command.Problem);
    }

    [Fact]
    public void AProblemIsReportedEvenWithoutAReport()
    {
        var command = HostCommand.Parse(["--projects"], Default);

        Assert.Equal(HostReport.None, command.Report);
        Assert.Equal(HostCommand.ProjectsNeedsFolder, command.Problem);
    }

    [Fact]
    public void UsageWinsWhenBothReportsAreAsked()
    {
        var command = HostCommand.Parse(["--cache-report", "--usage-report"], Default);

        Assert.Equal(HostReport.Usage, command.Report);
    }

    [Fact]
    public void NoReportAskedIsNoneWithNoProblem()
    {
        var command = HostCommand.Parse([], Default);

        Assert.Equal(HostReport.None, command.Report);
        Assert.Null(command.Problem);
    }

    [Fact]
    public void UnknownArgumentsAreIgnored()
    {
        var command = HostCommand.Parse(["--usage-report", "--verbose"], Default);

        Assert.Equal(HostReport.Usage, command.Report);
        Assert.Equal(Default, command.Projects);
        Assert.Null(command.Problem);
    }
}
