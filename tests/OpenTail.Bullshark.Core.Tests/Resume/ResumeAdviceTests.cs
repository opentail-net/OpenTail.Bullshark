using OpenTail.Bullshark.Core.Resume;

namespace OpenTail.Bullshark.Core.Tests.Resume;

public sealed class ResumeAdviceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(48_000)]
    [InlineData(49_999)]
    public void ASmallContextRecommendsAFullResume(long tokens) =>
        Assert.True(ResumeAdvice.FullIsRecommended(tokens));

    [Theory]
    [InlineData(50_000)]
    [InlineData(120_000)]
    [InlineData(942_000)]
    public void ALargeContextRecommendsASummary(long tokens) =>
        Assert.False(ResumeAdvice.FullIsRecommended(tokens));
}
