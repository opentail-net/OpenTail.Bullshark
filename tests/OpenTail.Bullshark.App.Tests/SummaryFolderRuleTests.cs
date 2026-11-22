using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The folder rule of a Codex summary (<see cref="SummaryFolderRule"/>): no summary is made while the session's folder is missing. Pure: no window and
/// no process is started.
/// </summary>
public sealed class SummaryFolderRuleTests
{
    [Fact]
    public void ASummaryIsRefusedWithTheCodexFolderTextWhileTheFolderIsMissing()
    {
        Assert.Equal(
            "The summary was not made: the session's folder is missing, and Codex runs its summary in that folder.",
            SummaryFolderRule.Refusal(folderExists: false));
    }

    [Fact]
    public void ASummaryMayBeMadeWhileTheFolderExists()
    {
        Assert.Null(SummaryFolderRule.Refusal(folderExists: true));
    }
}
