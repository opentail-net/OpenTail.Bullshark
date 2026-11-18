using System.Globalization;
using OpenTail.Bullshark.Core.Library;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Library;

/// <summary>The cleaning, the length cap and the distinguishing part of a magic name (magic rename, D3). Pure: no file, no model.</summary>
public sealed class MagicNameTests
{
    private static readonly DateTimeOffset Eighteenth = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private static ISet<string> Taken(params string[] names) => names.ToHashSet(StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("Fix the login bug", "Fix the login bug")]
    [InlineData("fix login", "Fix login")]
    [InlineData("\"Fix login\"", "Fix login")]
    [InlineData("“Fix login”", "Fix login")]
    [InlineData("'Fix login'", "Fix login")]
    [InlineData("`Fix login`", "Fix login")]
    [InlineData("Fix login.", "Fix login")]
    [InlineData("Fix login...", "Fix login")]
    [InlineData("Fix login!", "Fix login")]
    [InlineData("- Fix login", "Fix login")]
    [InlineData("#  Fix   login", "Fix login")]
    [InlineData("  Fix login  ", "Fix login")]
    public void CleaningGivesOneTidyLine(string reply, string expected) =>
        Assert.Equal(expected, MagicName.Clean(reply));

    [Fact]
    public void AnApostropheInsideAWordIsKept()
    {
        Assert.Equal("Fix the user's login", MagicName.Clean("Fix the user's login"));
        Assert.Equal("Fix the users login", MagicName.Clean("Fix the 'users' login"));
    }

    [Fact]
    public void EmojiAndSymbolsAreRemoved()
    {
        Assert.Equal("Fix login", MagicName.Clean("\U0001FA84 Fix login ✨"));
        Assert.Equal("Fix login", MagicName.Clean("Fix\U0001F680 login"));
        Assert.Equal("Fix login", MagicName.Clean("Fix login️"));
    }

    [Fact]
    public void OnlyTheFirstLineIsKept()
    {
        Assert.Equal("Fix login", MagicName.Clean("Fix login\nThe session was about the login page"));
        Assert.Equal("Fix login", MagicName.Clean("\r\n\r\nFix login\r\nmore"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"\"")]
    [InlineData("...")]
    [InlineData("\U0001FA84")]
    [InlineData("✨ ✨")]
    public void AnEmptyOrUnusableReplyGivesNothing(string? reply) =>
        Assert.Null(MagicName.Clean(reply));

    [Fact]
    public void AFortyCharacterNameIsKeptWhole()
    {
        var name = new string('a', 40);
        var cleaned = MagicName.Clean(name);

        Assert.Equal("A" + new string('a', 39), cleaned);
        Assert.Equal(40, cleaned!.Length);
    }

    [Fact]
    public void ALongerNameIsCutAtAWordBoundaryWithEllipsis()
    {
        var cleaned = MagicName.Clean("Refactor the session library storage and migrate old entries");

        Assert.Equal("Refactor the session library storage...", cleaned);
        Assert.True(cleaned!.Length <= MagicName.MaxLength);
    }

    [Fact]
    public void AWordlessLongNameIsCutInsideTheWordWithEllipsis()
    {
        var cleaned = MagicName.Clean(new string('x', 60));

        Assert.Equal(MagicName.MaxLength, cleaned!.Length);
        Assert.EndsWith("...", cleaned, StringComparison.Ordinal);
    }

    [Fact]
    public void UnicodeLettersAreKept()
    {
        Assert.Equal("Résumé parsing côté serveur", MagicName.Clean("résumé parsing côté serveur"));
        Assert.Equal("修复登录问题", MagicName.Clean("修复登录问题"));
    }

    [Fact]
    public void AnUnusedNameIsReturnedAsItIs()
    {
        Assert.Equal("Fix login", MagicName.Distinct("Fix login", Taken("Other"), Eighteenth));
    }

    [Fact]
    public void ATakenNameGetsTheDateWithoutCase()
    {
        Assert.Equal("Fix login - 18 Nov", MagicName.Distinct("Fix login", Taken("fix LOGIN"), Eighteenth));
    }

    [Fact]
    public void ATakenDatedNameGetsANumber()
    {
        var taken = Taken("Fix login", "Fix login - 18 Nov");

        Assert.Equal("Fix login 2", MagicName.Distinct("Fix login", taken, Eighteenth));
        Assert.Equal("Fix login 3", MagicName.Distinct("Fix login", Taken("Fix login", "Fix login - 18 Nov", "Fix login 2"), Eighteenth));
    }

    [Fact]
    public void AFortyCharacterNameStaysWithinTheCapWithItsSuffix()
    {
        var name = "Migrate the library file to the new schema now";
        var cleaned = MagicName.Clean(name)!;
        var distinct = MagicName.Distinct(cleaned, Taken(cleaned), Eighteenth);

        Assert.True(distinct.Length <= MagicName.MaxLength, distinct);
        Assert.EndsWith(" - 18 Nov", distinct, StringComparison.Ordinal);
    }

    [Fact]
    public void ACutNameGetsTheSuffixWithoutItsEllipsis()
    {
        var cleaned = MagicName.Clean(new string('x', 60))!;
        var distinct = MagicName.Distinct(cleaned, Taken(cleaned), Eighteenth);

        Assert.True(distinct.Length <= MagicName.MaxLength);
        Assert.EndsWith(" - 18 Nov", distinct, StringComparison.Ordinal);
        Assert.DoesNotContain("...", distinct, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePromptShowsTheNamesAndTheExtract()
    {
        var prompt = MagicName.PromptFor(["Fix login", "Review queue"], "You: hello");

        Assert.Contains("Names already used in this folder", prompt, StringComparison.Ordinal);
        Assert.Contains("- Fix login", prompt, StringComparison.Ordinal);
        Assert.Contains("- Review queue", prompt, StringComparison.Ordinal);
        Assert.EndsWith("You: hello", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePromptSaysSoWhenNoNameIsUsed()
    {
        var prompt = MagicName.PromptFor([], "You: hello");

        Assert.Contains("No name is used in this folder yet.", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("- ", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void ARerollPromptNamesTheCurrentNameToAvoid()
    {
        var prompt = MagicName.PromptFor(["Fix login"], "You: hello", [], "Fix login");

        Assert.Contains("Its current name, do not repeat it: Fix login.", prompt, StringComparison.Ordinal);
        Assert.EndsWith("You: hello", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void AFreshNamePromptHasNoCurrentNameLine()
    {
        var fresh = MagicName.PromptFor(["Fix login"], "You: hello", [], null);

        Assert.DoesNotContain("Its current name", fresh, StringComparison.Ordinal);
        Assert.Equal(MagicName.PromptFor(["Fix login"], "You: hello", []), fresh);
        Assert.Equal(fresh, MagicName.PromptFor(["Fix login"], "You: hello", [], "   "));
    }

    [Fact]
    public void TheDateIsInTheInvariantFormat()
    {
        Assert.Equal("18 Nov", Eighteenth.ToString("d MMM", CultureInfo.InvariantCulture));
    }
}
