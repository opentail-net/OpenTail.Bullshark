using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>The name Save keeps when the edit box starts with the title shown now.</summary>
public sealed class LibraryEditNameTests
{
    [Theory]
    [InlineData("Fix the parser", null, "Fix the parser", "")]
    [InlineData("Fix the parser", "", "Fix the parser", "")]
    [InlineData("Fix the parser v2", null, "Fix the parser", "Fix the parser v2")]
    [InlineData("", null, "Fix the parser", "")]
    [InlineData("Planner", "Planner", "Fix the parser", "Planner")]
    [InlineData("Fix the parser", "Planner", "Fix the parser", "Fix the parser")]
    [InlineData("", "Planner", "Fix the parser", "")]
    public void NameToSave(string text, string? currentName, string fallback, string expected) =>
        Assert.Equal(expected, LibraryEditDialog.NameToSave(text, currentName, fallback));
}
