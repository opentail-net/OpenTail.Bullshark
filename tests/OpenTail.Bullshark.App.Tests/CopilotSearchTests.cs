using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The matching of a search of past Copilot sessions (parity P6, decisions S6-D1): each field can match a word (the title, the owner's name, a tag with
/// and without its '#', the note), every word must match somewhere, the match is case-insensitive, and an empty query matches nothing. Synthetic fields
/// only; no window and no rollout is read (see <see cref="CopilotSearch"/>).
/// </summary>
public sealed class CopilotSearchTests
{
    [Fact]
    public void ATitleWordMatches()
    {
        Assert.True(CopilotSearch.Matches("notes", "Tidy the notes in notes.md", null, null, null));
    }

    [Fact]
    public void AnOwnersNameMatches()
    {
        Assert.True(CopilotSearch.Matches("release", "Synthetic title", "Release plan", null, null));
    }

    [Fact]
    public void ANoteMatches()
    {
        Assert.True(CopilotSearch.Matches("fixture", "Synthetic title", null, null, "Check the screenshot fixture first."));
    }

    [Fact]
    public void ATagMatchesWithoutItsHash()
    {
        Assert.True(CopilotSearch.Matches("auth", "Synthetic title", null, ["auth", "login"], null));
    }

    [Fact]
    public void ATagMatchesWithItsHash()
    {
        Assert.True(CopilotSearch.Matches("#auth", "Synthetic title", null, ["auth"], null));
    }

    [Fact]
    public void AStoredTagWithItsHashMatchesWithAndWithoutTheHash()
    {
        Assert.True(CopilotSearch.Matches("auth", "Synthetic title", null, ["#auth"], null));
        Assert.True(CopilotSearch.Matches("#auth", "Synthetic title", null, ["#auth"], null));
    }

    [Fact]
    public void EveryWordMustMatchSomewhere()
    {
        // "tidy" is in the title and "fixture" in the note: both words are found across the fields.
        Assert.True(CopilotSearch.Matches("tidy fixture", "Tidy the notes", null, null, "screenshot fixture"));
        // "missing" is in no field, so the session does not match.
        Assert.False(CopilotSearch.Matches("tidy missing", "Tidy the notes", null, null, "screenshot fixture"));
    }

    [Fact]
    public void TheMatchIgnoresCase()
    {
        Assert.True(CopilotSearch.Matches("TIDY", "tidy the notes", null, null, null));
        Assert.True(CopilotSearch.Matches("tidy", "TIDY THE NOTES", null, null, null));
        Assert.True(CopilotSearch.Matches("AUTH", null, null, ["Auth"], null));
    }

    [Fact]
    public void AnEmptyQueryMatchesNothing()
    {
        Assert.False(CopilotSearch.Matches(string.Empty, "Tidy the notes", "Release plan", ["auth"], "a note"));
        Assert.False(CopilotSearch.Matches("   ", "Tidy the notes", "Release plan", ["auth"], "a note"));
        Assert.False(CopilotSearch.Matches(null, "Tidy the notes", "Release plan", ["auth"], "a note"));
    }

    [Fact]
    public void AQueryThatIsInNoFieldMatchesNothing()
    {
        Assert.False(CopilotSearch.Matches("zebra", "Tidy the notes", "Release plan", ["auth"], "a note"));
    }

    [Fact]
    public void MissingFieldsDoNotMatchOrThrow()
    {
        Assert.False(CopilotSearch.Matches("tidy", null, null, null, null));
        Assert.False(CopilotSearch.Matches("tidy", null, null, [], null));
    }

    [Fact]
    public void ABlankTagIsNoMatch()
    {
        // A blank tag and a null tag hold nothing to find; a query made of '#' alone is not a word of any tag.
        Assert.False(CopilotSearch.Matches("auth", null, null, ["  ", null], null));
        Assert.False(CopilotSearch.Matches("#", null, null, ["  "], null));
    }

    [Fact]
    public void WordsAreSplitAtWhiteSpace()
    {
        Assert.True(CopilotSearch.Matches("  tidy\tnotes  ", "Tidy the notes", null, null, null));
    }
}
