using OpenTail.Bullshark.Core.Activity;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Activity;

/// <summary>
/// The signature of the session list (R9) says what the rail shows. A redraw happens only when it changes, so it must change for each thing the
/// rail draws from a session, and must not change for the same list.
/// </summary>
public sealed class SessionListSignatureTests
{
    private static readonly DateTimeOffset Activity = new(2026, 11, 22, 9, 30, 0, TimeSpan.Zero);

    private static SessionDigest Digest(
        string id,
        DateTimeOffset? last = null,
        int turns = 3,
        int messages = 0,
        string? aiTitle = null,
        string folder = "C--Git-alpha",
        string? cwd = "C:\\Git\\alpha") =>
        new(id, "C:\\projects\\" + folder + "\\" + id + ".jsonl", folder, false, null, last ?? Activity, turns, 0, messages, 0,
            new Dictionary<string, int>(), [], null, null, 0, TokenUsage.Zero,
            FirstUserText: "prompt of " + id, AiTitle: aiTitle, Cwd: cwd);

    private static List<SessionDigest> Sample() =>
    [
        Digest("aaaaaaaa-0000-0000-0000-000000000001", aiTitle: "First"),
        Digest("aaaaaaaa-0000-0000-0000-000000000002", last: Activity.AddMinutes(-5), turns: 7),
    ];

    [Fact]
    public void TheSameListGivesTheSameSignature()
    {
        var first = SessionListSignature.Of(Sample());
        var second = SessionListSignature.Of(Sample());

        Assert.Equal(first, second);
    }

    [Fact]
    public void AChangedLastActivityChangesTheSignature()
    {
        var before = SessionListSignature.Of(Sample());
        var list = Sample();
        list[1] = list[1] with { LastActivity = Activity.AddMinutes(1) };

        Assert.NotEqual(before, SessionListSignature.Of(list));
    }

    [Fact]
    public void AChangedMessageCountChangesTheSignature()
    {
        // The row shows the assistant message count ("N msgs"), so a change in it must redraw the rail.
        var before = SessionListSignature.Of(Sample());
        var list = Sample();
        list[1] = list[1] with { AssistantMessages = 9 };

        Assert.NotEqual(before, SessionListSignature.Of(list));
    }

    [Fact]
    public void AChangedTitleChangesTheSignature()
    {
        var before = SessionListSignature.Of(Sample());
        var list = Sample();
        list[0] = list[0] with { AiTitle = "Renamed" };

        Assert.NotEqual(before, SessionListSignature.Of(list));
    }

    [Fact]
    public void AChangedFolderOrWorkingFolderChangesTheSignature()
    {
        var before = SessionListSignature.Of(Sample());
        var moved = Sample();
        moved[0] = moved[0] with { ProjectFolder = "C--Git-beta" };
        var renamed = Sample();
        renamed[0] = renamed[0] with { Cwd = "C:\\Git\\beta" };

        Assert.NotEqual(before, SessionListSignature.Of(moved));
        Assert.NotEqual(before, SessionListSignature.Of(renamed));
    }

    [Fact]
    public void AnAddedOrRemovedSessionChangesTheSignature()
    {
        var before = SessionListSignature.Of(Sample());
        var added = Sample();
        added.Add(Digest("aaaaaaaa-0000-0000-0000-000000000003"));
        var removed = Sample();
        removed.RemoveAt(0);

        Assert.NotEqual(before, SessionListSignature.Of(added));
        Assert.NotEqual(before, SessionListSignature.Of(removed));
    }

    /// <summary>
    /// The order is part of the signature (R9, decision 7): the rail's stable sort keeps the index order for sessions with equal activity, so a
    /// reordering can move a row. A brief example said the opposite; this is the coder's decision, stated in the plan.
    /// </summary>
    [Fact]
    public void AReorderingChangesTheSignatureBecauseTheRailShowsTheIndexOrder()
    {
        var before = SessionListSignature.Of(Sample());
        var reversed = Sample();
        reversed.Reverse();

        Assert.NotEqual(before, SessionListSignature.Of(reversed));
    }

    [Fact]
    public void TextsThatMoveAcrossFieldsDoNotGiveTheSameSignature()
    {
        // The id "ab" with the title "c", and the id "a" with the title "bc", have the same characters in the same order.
        var first = SignatureOf(Digest("ab", aiTitle: "c"));
        var second = SignatureOf(Digest("a", aiTitle: "bc"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ANullLastActivityIsNotAnActivityTime()
    {
        var none = SignatureOf(Digest("aaaaaaaa-0000-0000-0000-000000000009") with { LastActivity = null });
        var some = SignatureOf(Digest("aaaaaaaa-0000-0000-0000-000000000009", last: Activity));

        Assert.NotEqual(none, some);
    }

    private static string SignatureOf(SessionDigest digest) => SessionListSignature.Of([digest]);
}
