using OpenTail.Bullshark.Core.Ui;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Ui;

/// <summary>
/// The rail's signature (perf and testability batch, Q5): equal inputs give equal text, every input changes it, the parts cannot run together, and the
/// clock is kept to the minute.
/// </summary>
public sealed class RailSignatureTests
{
    private static string Of(string? theme = "dark", string? filter = "all", string? records = "r1", string? runs = null, string? minute = "m1")
    {
        var signature = new RailSignature()
            .Add("theme", theme)
            .Add("filter", filter)
            .Add("records", records)
            .Add("runs", runs)
            .Add("minute", minute);
        return signature.Build();
    }

    [Fact]
    public void EqualPartsGiveEqualText()
    {
        Assert.Equal(Of(), Of());
    }

    [Theory]
    [InlineData("light", null, null, null, null)]
    [InlineData(null, "running", null, null, null)]
    [InlineData(null, null, "r2", null, null)]
    [InlineData(null, null, null, "run", null)]
    [InlineData(null, null, null, null, "m2")]
    public void EachPartChangesTheText(string? theme, string? filter, string? records, string? runs, string? minute)
    {
        var changed = Of(theme ?? "dark", filter ?? "all", records ?? "r1", runs, minute ?? "m1");

        Assert.NotEqual(Of(), changed);
    }

    [Fact]
    public void ANullPartIsNotAnEmptyPart()
    {
        Assert.NotEqual(Of(runs: null), Of(runs: string.Empty));
    }

    [Fact]
    public void PartsCannotRunTogether()
    {
        // Without the length prefix, these two would give the same text.
        var one = new RailSignature().Add("a", "bc").Add("d", "e").Build();
        var two = new RailSignature().Add("a", "b").Add("d", "c").Add("d", "e").Build();

        Assert.NotEqual(one, two);
        var splitAcross = new RailSignature().Add("a", "b;d=1:c").Build();
        var split = new RailSignature().Add("a", "b").Add("d", "c").Build();
        Assert.NotEqual(splitAcross, split);
    }

    [Fact]
    public void JsonPartsAreComparedByTheirContent()
    {
        var same = new RailSignature().AddJson("list", new[] { "a", "b" }).Build();
        var again = new RailSignature().AddJson("list", new[] { "a", "b" }).Build();
        var other = new RailSignature().AddJson("list", new[] { "a", "c" }).Build();

        Assert.Equal(same, again);
        Assert.NotEqual(same, other);
    }

    [Fact]
    public void TheMinuteBucketHoldsWithinAMinuteAndChangesAfterIt()
    {
        var start = new DateTimeOffset(2026, 11, 19, 8, 14, 5, TimeSpan.Zero);

        Assert.Equal(RailSignature.MinuteOf(start), RailSignature.MinuteOf(start.AddSeconds(54)));
        Assert.NotEqual(RailSignature.MinuteOf(start), RailSignature.MinuteOf(start.AddSeconds(55)));
        Assert.Equal("202611190814", RailSignature.MinuteOf(start));
    }

    [Fact]
    public void TheMinuteIsTakenInUtc()
    {
        // The same instant in two offsets is the same minute.
        var utc = new DateTimeOffset(2026, 11, 19, 8, 14, 0, TimeSpan.Zero);
        var plusOne = utc.ToOffset(TimeSpan.FromHours(1));

        Assert.Equal(RailSignature.MinuteOf(utc), RailSignature.MinuteOf(plusOne));
    }
}
