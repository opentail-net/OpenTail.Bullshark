using OpenTail.Bullshark.Core.Health;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Health;

/// <summary>The supervisor heartbeat (night batch N1): the file, its tolerant read, the age and the wording.</summary>
public sealed class HeartbeatTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "bullshark-heartbeat-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string NewPath(string name = "heartbeat.json")
    {
        Directory.CreateDirectory(_folder);
        return Path.Combine(_folder, name);
    }

    [Fact]
    public void WriteThenReadGivesTheSameInstant()
    {
        var at = new DateTimeOffset(2026, 11, 19, 2, 15, 30, TimeSpan.FromHours(1));
        var path = NewPath();

        Heartbeat.Write(path, at, passNumber: 42);

        Assert.Equal(at, Heartbeat.Read(path));
    }

    [Fact]
    public void AMissingFileIsNoPass()
    {
        Assert.Null(Heartbeat.Read(NewPath("nothere.json")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[1,2]")]
    [InlineData("\"text\"")]
    [InlineData("{\"at\":\"not a time\"}")]
    [InlineData("{\"at\":12}")]
    [InlineData("{\"when\":\"2026-11-19T02:00:00+00:00\"}")]
    public void ADamagedFileIsNoPassAndNeverThrows(string text)
    {
        var path = NewPath();
        System.IO.File.WriteAllText(path, text);

        Assert.Null(Heartbeat.Read(path));
    }

    [Fact]
    public void WriteReplacesTheOldTime()
    {
        var path = NewPath();
        Heartbeat.Write(path, new DateTimeOffset(2026, 11, 18, 22, 0, 0, TimeSpan.Zero));

        Heartbeat.Write(path, new DateTimeOffset(2026, 11, 19, 2, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 11, 19, 2, 0, 0, TimeSpan.Zero), Heartbeat.Read(path));
    }

    [Fact]
    public void WriteLeavesNoTemporaryFileBehind()
    {
        var path = NewPath();

        Heartbeat.Write(path, DateTimeOffset.UtcNow);

        Assert.Equal(["heartbeat.json"], Directory.GetFiles(_folder).Select(name => Path.GetFileName(name)).ToArray());
    }

    [Fact]
    public void AgeIsNullWithoutAPass()
    {
        Assert.Null(Heartbeat.Age(null, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void AgeIsTheGapAndZeroWhenTheClockWentBack()
    {
        var now = new DateTimeOffset(2026, 11, 19, 3, 0, 0, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(12), Heartbeat.Age(now.AddSeconds(-12), now));
        Assert.Equal(TimeSpan.Zero, Heartbeat.Age(now.AddMinutes(5), now));
    }

    [Theory]
    [InlineData(0, "0 s ago")]
    [InlineData(12, "12 s ago")]
    [InlineData(59, "59 s ago")]
    [InlineData(60, "1 min ago")]
    [InlineData(3 * 60 + 40, "3 min ago")]
    [InlineData(59 * 60 + 59, "59 min ago")]
    [InlineData(2 * 3600, "2 h ago")]
    [InlineData(23 * 3600 + 59 * 60, "23 h ago")]
    [InlineData(3 * 86400, "3 d ago")]
    public void DescribeReadsTheAgeAsTheOwnerSeesIt(int seconds, string expected)
    {
        Assert.Equal(expected, Heartbeat.Describe(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void DescribeOfANegativeAgeIsZeroSeconds()
    {
        Assert.Equal("0 s ago", Heartbeat.Describe(TimeSpan.FromSeconds(-5)));
    }

    [Fact]
    public void AWriteIsDueWhenNoneWasMadeOrTheLastIsTenSecondsOld()
    {
        var now = new DateTimeOffset(2026, 11, 19, 3, 0, 0, TimeSpan.Zero);

        Assert.True(Heartbeat.WriteDue(null, now));
        Assert.False(Heartbeat.WriteDue(now.AddSeconds(-9), now));
        Assert.True(Heartbeat.WriteDue(now.AddSeconds(-10), now));
        Assert.True(Heartbeat.WriteDue(now.AddMinutes(5), now));
    }

    [Fact]
    public void PathIsBesideTheStateFile()
    {
        var state = Path.Combine(_folder, "state.json");

        Assert.Equal(Path.Combine(_folder, "heartbeat.json"), Heartbeat.PathFor(state));
    }
}
