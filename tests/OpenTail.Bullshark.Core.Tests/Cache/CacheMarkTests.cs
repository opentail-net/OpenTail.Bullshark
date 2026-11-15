using OpenTail.Bullshark.Core.Cache;

namespace OpenTail.Bullshark.Core.Tests.Cache;

public sealed class CacheMarkTests
{
    [Fact]
    public void WarmIsFireColdIsSnowflakeAndRunningIsARocket()
    {
        Assert.Equal("🔥", CacheRule.Mark(CacheState.Warm, running: false));
        Assert.Equal("🌡️", CacheRule.Mark(CacheState.Cooling, running: false));
        Assert.Equal("❄️", CacheRule.Mark(CacheState.Cold, running: false));
        Assert.Equal("🚀", CacheRule.Mark(CacheState.Cold, running: true));
        Assert.Equal(string.Empty, CacheRule.Mark(CacheState.Unknown, running: false));
    }
}