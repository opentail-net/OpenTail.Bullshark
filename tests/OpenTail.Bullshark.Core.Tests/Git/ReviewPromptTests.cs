using OpenTail.Bullshark.Core.Git;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Git;

/// <summary>The review prompt (batch B1): with and without a note, the file cap and length limit, no files, and names that are not plain.</summary>
public sealed class ReviewPromptTests
{
    private static IReadOnlyList<string> Files(int count, string prefix = "src/File") =>
        Enumerable.Range(1, count).Select(i => $"{prefix}{i}.cs").ToList();

    /// <summary>The file lines of a prompt: the "- " lines, except the "- and N more" count line.</summary>
    private static List<string> FileLinesOf(string prompt) =>
        prompt.Split(Environment.NewLine)
            .Where(line => line.StartsWith("- ", StringComparison.Ordinal) && !line.StartsWith("- and ", StringComparison.Ordinal))
            .ToList();

    [Fact]
    public void NoFilesGivesNullWhetherOrNotThereIsANote()
    {
        Assert.Null(ReviewPrompt.Build("coder-1", [], null));
        Assert.Null(ReviewPrompt.Build("coder-1", [], "check the tests"));
    }

    [Fact]
    public void WithANoteTheCoderIsAskedToAddressIt()
    {
        var prompt = ReviewPrompt.Build("coder-1", Files(2), "the retry loop never stops");

        Assert.NotNull(prompt);
        Assert.Contains("address this note from the owner: the retry loop never stops", prompt);
        Assert.DoesNotContain("leftover debug code", prompt);
        Assert.Equal(["- src/File1.cs", "- src/File2.cs"], FileLinesOf(prompt));
    }

    [Fact]
    public void WithoutANoteTheCoderIsAskedForASelfReview()
    {
        var prompt = ReviewPrompt.Build("coder-1", Files(1), null);

        Assert.NotNull(prompt);
        Assert.Contains("bugs", prompt);
        Assert.Contains("missing tests", prompt);
        Assert.Contains("leftover debug code", prompt);
        Assert.Contains("coder-1", prompt);
    }

    [Fact]
    public void ABlankNoteCountsAsNoNote()
    {
        var blank = ReviewPrompt.Build("coder-1", Files(1), "   \t ");
        var none = ReviewPrompt.Build("coder-1", Files(1), null);

        Assert.Equal(none, blank);
    }

    [Fact]
    public void TheNoteIsTrimmedAndCutToItsLimit()
    {
        var prompt = ReviewPrompt.Build("coder-1", Files(1), "  " + new string('n', ReviewPrompt.MaxNoteLength + 50) + "  ");

        Assert.NotNull(prompt);
        Assert.Contains(new string('n', ReviewPrompt.MaxNoteLength), prompt);
        Assert.DoesNotContain(new string('n', ReviewPrompt.MaxNoteLength + 1), prompt);
    }

    [Fact]
    public void ThirtyFilesAreAllNamedWithNoMoreLine()
    {
        var prompt = ReviewPrompt.Build("coder-1", Files(30), null);

        Assert.NotNull(prompt);
        Assert.Equal(30, FileLinesOf(prompt).Count);
        Assert.DoesNotContain("and 0 more", prompt);
        Assert.DoesNotContain("more", prompt.Split(Environment.NewLine).Last());
    }

    [Fact]
    public void MoreThanThirtyFilesNamesThirtyAndCountsTheRest()
    {
        var prompt = ReviewPrompt.Build("coder-1", Files(33), "fix it");

        Assert.NotNull(prompt);
        Assert.Equal(30, FileLinesOf(prompt).Count);
        Assert.Contains("- and 3 more", prompt);
        Assert.DoesNotContain("src/File31.cs", prompt);
    }

    [Fact]
    public void LongNamesStayUnderTheLengthLimitAndTheCountStillAdds()
    {
        var longFiles = Enumerable.Range(1, 30).Select(i => "deep/" + new string('d', 90) + $"/file{i}.cs").ToList();

        var prompt = ReviewPrompt.Build("coder-1", longFiles, "look at the paths");

        Assert.NotNull(prompt);
        Assert.True(prompt.Length <= ReviewPrompt.MaxLength, $"length {prompt.Length}");
        var listed = FileLinesOf(prompt).Count;
        Assert.True(listed < 30);
        Assert.Contains($"- and {30 - listed} more", prompt);
        Assert.Equal(longFiles.Take(listed), FileLinesOf(prompt).Select(line => line[2..]));
    }

    [Fact]
    public void SpecialCharactersInNamesAreKept()
    {
        string[] names =
        [
            "src/a&b (1).cs",
            "src/it's \"quoted\".md",
            "src/ünïcode 文件.txt",
            "src/<angle>|pipe`tick`.json",
            "src/100%$HOME.cs",
        ];

        var prompt = ReviewPrompt.Build("coder-1", names, null);

        Assert.NotNull(prompt);
        Assert.Equal(names.Select(name => "- " + name), FileLinesOf(prompt));
    }

    [Fact]
    public void NamesWithSpacesAreNotQuoted()
    {
        var prompt = ReviewPrompt.Build("coder-1", ["My Folder/file name.cs"], null);

        Assert.NotNull(prompt);
        Assert.Contains("- My Folder/file name.cs", prompt);
    }

    [Fact]
    public void AnEmptySessionNameReadsAsThisSession()
    {
        var prompt = ReviewPrompt.Build(" ", Files(1), null);

        Assert.NotNull(prompt);
        Assert.Contains("the changes in this session.", prompt);
    }
}
