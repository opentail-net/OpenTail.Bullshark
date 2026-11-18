using OpenTail.Bullshark.Core.Library;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Library;

/// <summary>
/// The tags part of a magic rename (tags plan, Part B): the reply's tags line, the prompt's list of tags in use, and the merge with a
/// session's own tags. Pure: no file, no model. The names are cleaned by the same rules as <see cref="MagicNameTests"/>.
/// </summary>
public sealed class MagicNameTagsTests
{
    [Fact]
    public void AReplyWithOnlyAName_GivesTheNameAndNoTags()
    {
        var (name, tags) = MagicName.ParseReply("Fix login");

        Assert.Equal("Fix login", name);
        Assert.Empty(tags);
    }

    [Fact]
    public void ANameAndATagsLine_GiveBoth()
    {
        var (name, tags) = MagicName.ParseReply("Fix login\ntags: auth, login");

        Assert.Equal("Fix login", name);
        Assert.Equal(["auth", "login"], tags);
    }

    [Fact]
    public void TheTagsLineIsFoundWhateverItsCase()
    {
        var (name, tags) = MagicName.ParseReply("Fix login\nTAGS: Auth, Login");

        Assert.Equal("Fix login", name);
        Assert.Equal(["auth", "login"], tags);
    }

    [Fact]
    public void ATagsLineFirst_IsNotTheName()
    {
        var (name, tags) = MagicName.ParseReply("tags: auth\nFix login");

        Assert.Equal("Fix login", name);
        Assert.Equal(["auth"], tags);
    }

    [Fact]
    public void ATagsLineWithABulletMark_IsStillTheTagsLine()
    {
        var (name, tags) = MagicName.ParseReply("Fix login\n- tags: auth");

        Assert.Equal("Fix login", name);
        Assert.Equal(["auth"], tags);
    }

    [Fact]
    public void TagsWithJunkCharactersOrOneLetterAreDropped()
    {
        // "auth!" has a mark, "ok_tag" an underscore and "x" is one letter: none of them is kept. The first two that pass are.
        var (_, tags) = MagicName.ParseReply("Fix login\ntags: auth!, ok_tag, x, good-tag, billing");

        Assert.Equal(["good-tag", "billing"], tags);
    }

    [Fact]
    public void AtMostTwoNewTagsAreKept()
    {
        var (_, tags) = MagicName.ParseReply("Fix login\ntags: alpha, beta, gamma");

        Assert.Equal(["alpha", "beta"], tags);
    }

    [Fact]
    public void ADuplicateTagIsKeptOnce()
    {
        var (_, tags) = MagicName.ParseReply("Fix login\ntags: auth, Auth, login, auth");

        Assert.Equal(["auth", "login"], tags);
    }

    [Fact]
    public void ACommaAndASpaceBothSeparateTags()
    {
        var (_, tags) = MagicName.ParseReply("Fix login\ntags: auth,login");

        Assert.Equal(["auth", "login"], tags);
    }

    [Fact]
    public void ATagsLineWithNothingAfterIt_GivesNoTags()
    {
        var (name, tags) = MagicName.ParseReply("Fix login\ntags:");

        Assert.Equal("Fix login", name);
        Assert.Empty(tags);

        var (spaced, none) = MagicName.ParseReply("Fix login\ntags:    ");
        Assert.Equal("Fix login", spaced);
        Assert.Empty(none);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AnEmptyReplyGivesNothing(string? reply)
    {
        var (name, tags) = MagicName.ParseReply(reply);

        Assert.Null(name);
        Assert.Empty(tags);
    }

    [Fact]
    public void ATagsLineWithNoName_GivesNoName()
    {
        // The caller fails such a reply: the name is the part that must be usable.
        var (name, _) = MagicName.ParseReply("tags: auth");

        Assert.Null(name);
    }

    [Fact]
    public void AReplyOfOnlyEmojiWithGoodTags_GivesNoName()
    {
        var (name, _) = MagicName.ParseReply("\U0001FA84\ntags: auth");

        Assert.Null(name);
    }

    [Fact]
    public void ThePromptListsTheTagsInUse()
    {
        var prompt = MagicName.PromptFor([], "You: hello", ["auth", "login"]);

        Assert.Contains("Tags already in use (reuse one when it fits): auth, login.", prompt, StringComparison.Ordinal);
        Assert.EndsWith("You: hello", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePromptShowsAtMostThirtyTags()
    {
        var known = Enumerable.Range(1, 40).Select(i => $"tag{i:00}").ToList();

        var prompt = MagicName.PromptFor([], "You: hello", known);

        Assert.Contains("tag30", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("tag31", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePromptSaysSoWhenNoTagIsInUse()
    {
        Assert.Contains("No tag is in use in the library yet.", MagicName.PromptFor([], "You: hello", []), StringComparison.Ordinal);
    }

    [Fact]
    public void TheOldPromptOverloadIsThePromptWithNoTags()
    {
        Assert.Equal(MagicName.PromptFor(["Fix login"], "You: hello", []), MagicName.PromptFor(["Fix login"], "You: hello"));
    }

    [Fact]
    public void TheMergeKeepsTheOwnersOrderThenAddsInOrder()
    {
        var merged = MagicName.MergeTags(["zeta", "alpha"], ["beta", "gamma"]);

        Assert.Equal(["zeta", "alpha", "beta", "gamma"], merged);
    }

    [Fact]
    public void TheMergeHasNoRepeatsWhateverTheCase()
    {
        var merged = MagicName.MergeTags(["Auth"], ["auth", "login"]);

        Assert.Equal(["Auth", "login"], merged);
    }

    [Fact]
    public void TheMergeIsCappedAtEightInAll()
    {
        var added = Enumerable.Range(1, 10).Select(i => $"tag{i:00}").ToList();

        var merged = MagicName.MergeTags([], added);

        Assert.Equal(MagicName.MaxTags, merged.Count);
        Assert.Equal(MagicName.MaxTags, 8);
        Assert.Equal(added.Take(8), merged);
    }

    [Fact]
    public void TheOwnersTagsFillTheCapFirst()
    {
        var existing = new[] { "a1", "b1", "c1", "d1", "e1", "f1" };

        var merged = MagicName.MergeTags(existing, ["g1", "h1", "i1"]);

        Assert.Equal(["a1", "b1", "c1", "d1", "e1", "f1", "g1", "h1"], merged);
    }

    [Fact]
    public void AnOwnersTagIsNeverDroppedEvenAboveTheCap()
    {
        var existing = Enumerable.Range(1, 10).Select(i => $"own{i:00}").ToList();

        var merged = MagicName.MergeTags(existing, ["new1"]);

        Assert.Equal(existing, merged);
    }

    [Fact]
    public void ANullOrBlankExistingTagIsHandled()
    {
        Assert.Equal(["a1"], MagicName.MergeTags(null, ["a1"]));
        Assert.Equal(["x"], MagicName.MergeTags([" ", "x"], [""]));
    }
}
