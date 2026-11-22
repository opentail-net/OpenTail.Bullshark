using OpenTail.Bullshark.Core.Copilot;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Copilot;

/// <summary>The version rule of the Copilot setup check: which builds carry the flags the app passes (CopilotFlagsVersion).</summary>
public sealed class CopilotFlagsVersionTests
{
    [Theory]
    [InlineData("1.0.34", false)]
    [InlineData("1.0.51", false)]
    [InlineData("1.0.79", false)]
    [InlineData("1.0.99", false)]
    [InlineData("1.0.102", false)]
    [InlineData("1.0.103", true)]
    [InlineData("1.0.104-3", true)]
    [InlineData("1.0.104", true)]
    [InlineData("1.1.0", true)]
    [InlineData("2.0.0", true)]
    [InlineData("", false)]
    [InlineData("junk", false)]
    public void HasFlagsIsTrueFromTheFirstBuildWithTheFlagsOn(string version, bool expected) =>
        Assert.Equal(expected, CopilotFlagsVersion.HasFlags(version));

    [Fact]
    public void TheFirstBuildWithTheFlagsIsVersion1_0_103() =>
        Assert.Equal("1.0.103", CopilotFlagsVersion.FirstVersionWithFlags);
}
