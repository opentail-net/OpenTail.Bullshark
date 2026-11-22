using OpenTail.Bullshark.Core.Codex;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>
/// The start time that a Codex session id carries (note A): a UUIDv7's first 48 bits are Unix milliseconds. A pure check of the id's text; no file
/// is read. Plain fixtures run on both targets.
/// </summary>
public sealed class CodexSessionTimeTests
{
    /// <summary>
    /// CodexSessionTime is internal and Core has no InternalsVisibleTo, so it is reached by reflection (as <see cref="CodexHistorySourceTests"/> does
    /// for RolloutReader).
    /// </summary>
    private static bool TryStartFromId(string id, out DateTimeOffset start)
    {
        var type = typeof(CodexHistorySource).Assembly.GetType("OpenTail.Bullshark.Core.Codex.CodexSessionTime", throwOnError: true)!;
        var args = new object?[] { id, null };
        var found = (bool)type.GetMethod("TryStartFromId")!.Invoke(null, args)!;
        start = (DateTimeOffset)args[1]!;
        return found;
    }

    [Fact]
    public void AKnownUuidV7GivesItsStartInUtc()
    {
        // The first 48 bits of this id are 0x017F22E279B0 ms, which is 2022-02-22T19:22:22.000Z.
        Assert.True(TryStartFromId("017f22e2-79b0-7cc3-98c4-dc0c0c07398f", out var start));
        Assert.Equal(new DateTimeOffset(2022, 2, 22, 19, 22, 22, TimeSpan.Zero), start);
        Assert.Equal(TimeSpan.Zero, start.Offset);
    }

    [Fact]
    public void AnUpperCaseUuidV7IsAcceptedWithTheSameStart()
    {
        // Decision: hex digits are accepted in either case, as the file-name UUID test already accepts both.
        Assert.True(TryStartFromId("017F22E2-79B0-7CC3-98C4-DC0C0C07398F", out var start));
        Assert.Equal(new DateTimeOffset(2022, 2, 22, 19, 22, 22, TimeSpan.Zero), start);
    }

    [Fact]
    public void TheLastInstantADateTimeOffsetHoldsIsAccepted()
    {
        var last = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

        Assert.True(TryStartFromId(CodexHome.UuidV7(last), out var start));
        Assert.Equal(last, start.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void ATimeBeyondTheDateTimeOffsetRangeCarriesNoTime()
    {
        // 0xFFFFFFFFFFFF ms is far past 9999-12-31, the last instant a DateTimeOffset holds, so the id is refused rather than thrown on.
        Assert.False(TryStartFromId("ffffffff-ffff-7000-8000-000000000001", out _));
    }

    [Theory]
    [InlineData("550e8400-e29b-41d4-a716-446655440000")] // version 4
    [InlineData("017f22e2-79b0-6cc3-98c4-dc0c0c07398f")] // version 6: the version nibble is 6, not 7
    public void AnIdThatIsNotVersion7CarriesNoTime(string id)
    {
        Assert.False(TryStartFromId(id, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    [InlineData("017f22e2-79b0-7cc3-98c4-dc0c0c07398")] // one character short
    [InlineData("017f22e2-79b0-7cc3-98c4-dc0c0c07398f0")] // one character long
    [InlineData("017f22e-279b0-7cc3-98c4-dc0c0c07398f")] // hyphens not at the usual places
    [InlineData("017f22e2_79b0-7cc3-98c4-dc0c0c07398f")] // an underscore where the first hyphen belongs
    [InlineData("017f22e2-79b0-7cc3-98c4-dc0c0c07398g")] // a character that is not hex
    [InlineData("017f22e2-79b0-7cc3-98c4-dc0c0c07398*")] // a wildcard
    [InlineData("017f22e2-79b0-7cc3-98c4-dc0c0c0 7398f")] // a space
    public void AMalformedIdCarriesNoTime(string id)
    {
        Assert.False(TryStartFromId(id, out _));
    }
}
