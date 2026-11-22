using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The rule a resume screen uses when a summary is not made (<see cref="ResumeDefaults"/>): its truth table and its line. Pure: no window and no file.
/// </summary>
public sealed class ResumeDefaultsTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void AFailedSummaryFallsBackToInFullUnlessTheRequestWasCancelled(bool wasSummary, bool cancelled, bool expected)
    {
        Assert.Equal(expected, ResumeDefaults.FallBackToFull(wasSummary, cancelled));
    }

    [Fact]
    public void TheLineAddedAfterAFallbackSaysInFullIsSelected()
    {
        Assert.Equal("In full is selected instead.", ResumeDefaults.FullFallbackLine);
    }
}
