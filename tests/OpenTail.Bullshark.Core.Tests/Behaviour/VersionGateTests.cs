using OpenTail.Bullshark.Core.Behaviour;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Behaviour;

/// <summary>
/// R4: the version in a tool's output, its comparison with the minimum, and the verdict. The outputs are the shapes the two CLIs print, and the minimums
/// are the defaults of behaviour.json (Claude 2.1.330, Codex 0.171.0).
/// </summary>
public sealed class VersionGateTests
{
    [Theory]
    [InlineData("2.1.330", "2.1.330")]
    [InlineData("2.1.330 (Claude Code)\n", "2.1.330")]
    [InlineData("codex-cli 0.171.0", "0.171.0")]
    [InlineData("v2.1.330", "2.1.330")]
    [InlineData("2.1.330-rc.1", "2.1.330")]
    [InlineData("Claude Code 2.1.330 (build 2026.11.01)", "2.1.330")]
    [InlineData("build 42", null)]
    [InlineData("no version here", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void TheFirstVersionNumberIsTheVersion(string? output, string? expected) =>
        Assert.Equal(expected, VersionGate.VersionIn(output));

    [Theory]
    [InlineData("2.1.4", "2.1.330", -1)]
    [InlineData("2.1.330", "2.1.330", 0)]
    [InlineData("3.0.0", "2.1.330", 1)]
    [InlineData("2.10.0", "2.9.9", 1)]
    [InlineData("2.1", "2.1.0", 0)]
    [InlineData("2.1.330-rc.1", "2.1.330", 0)]
    [InlineData("v2.1.330", "2.1.330", 0)]
    public void VersionsCompareByTheirNumericSegments(string installed, string minimum, int expected) =>
        Assert.Equal(expected, VersionGate.Compare(installed, minimum));

    [Theory]
    [InlineData("2.x.0", "2.1.330")]
    [InlineData("2.1.beta", "2.1.330")]
    [InlineData("2.1.330", "2.y")]
    [InlineData("99999999999.0", "2.1.330")]
    [InlineData("2..1", "2.1.330")]
    [InlineData("", "2.1.330")]
    [InlineData(null, "2.1.330")]
    public void AValueThatDoesNotParseHasNoOrder(string? installed, string minimum) =>
        Assert.Null(VersionGate.Compare(installed, minimum));

    [Fact]
    public void AVersionBelowTheMinimumIsTooOld()
    {
        var result = VersionGate.Check("2.1.4 (Claude Code)", "2.1.330");

        Assert.Equal(VersionVerdict.TooOld, result.Verdict);
        Assert.Equal("2.1.4", result.Installed);
        Assert.Equal("2.1.330", result.Minimum);
    }

    [Theory]
    [InlineData("2.1.330 (Claude Code)")]
    [InlineData("2.1.331")]
    [InlineData("3.0.0")]
    public void AVersionAtOrAboveTheMinimumIsSupported(string output)
    {
        var result = VersionGate.Check(output, "2.1.330");

        Assert.Equal(VersionVerdict.Supported, result.Verdict);
        Assert.Equal("2.1.330", result.Minimum);
    }

    [Theory]
    [InlineData("", "2.1.330")]
    [InlineData("command not found", "2.1.330")]
    [InlineData("2.1.330", "2.x")]
    public void AVersionThatCannotBeReadIsNeverTooOld(string output, string minimum)
    {
        var result = VersionGate.Check(output, minimum);

        Assert.Equal(VersionVerdict.CouldNotDetermine, result.Verdict);
        Assert.False(string.IsNullOrEmpty(result.Reason));
    }

    [Fact]
    public void TheReasonNeverCarriesTheOutputText()
    {
        // The output can hold a machine path; the reason, which the window shows and may log, must not.
        var result = VersionGate.Check(@"C:\Users\Someone\bin\claude: no version", "2.1.330");

        Assert.Equal(VersionVerdict.CouldNotDetermine, result.Verdict);
        Assert.DoesNotContain("Someone", result.Reason ?? string.Empty);
        Assert.Null(result.Installed);
    }
}
