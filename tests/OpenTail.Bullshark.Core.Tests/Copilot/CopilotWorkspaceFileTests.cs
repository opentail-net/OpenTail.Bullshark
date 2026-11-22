using OpenTail.Bullshark.Core.Copilot;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Copilot;

/// <summary>Stage G1.2a: the flat <c>key: value</c> reader for <c>workspace.yaml</c>. Pure: the lines are synthetic.</summary>
public sealed class CopilotWorkspaceFileTests
{
    [Fact]
    public void PlainAndQuotedValuesAreReadAndTheQuotesStripped()
    {
        var read = CopilotWorkspaceFile.Parse(
        [
            "name: Plain value with spaces",
            "cwd: \"C:\\Fixture\\Demo\"",
            "title: 'It''s fixture text'",
            "note: \"a \\\"quoted\\\" word and a back\\\\slash\"",
        ]);

        Assert.Equal(CopilotWorkspaceState.Readable, read.State);
        Assert.Equal("Plain value with spaces", read.Values["name"]);
        Assert.Equal("C:\\Fixture\\Demo", read.Values["cwd"]);
        Assert.Equal("It's fixture text", read.Values["title"]);
        Assert.Equal("a \"quoted\" word and a back\\slash", read.Values["note"]);
    }

    [Fact]
    public void UnknownKeysAreKeptButTheFileStaysReadable()
    {
        var read = CopilotWorkspaceFile.Parse(["summary_count: 3", "host_type: fixture", "some_future_key: whatever"]);

        Assert.Equal(CopilotWorkspaceState.Readable, read.State);
        Assert.Equal("3", read.Values["summary_count"]);
        Assert.Equal("whatever", read.Values["some_future_key"]);
    }

    [Fact]
    public void AnEmptyFileIsReadableWithNoValues()
    {
        var read = CopilotWorkspaceFile.Parse(Array.Empty<string>());

        Assert.Equal(CopilotWorkspaceState.Readable, read.State);
        Assert.Empty(read.Values);
    }

    [Fact]
    public void BlankLinesAndCommentsAreSkippedAndALaterKeyReplacesAnEarlierOne()
    {
        var read = CopilotWorkspaceFile.Parse(["# a comment", "", "   ", "name: first", "name: second"]);

        Assert.Equal(CopilotWorkspaceState.Readable, read.State);
        Assert.Equal("second", read.Values["name"]);
    }

    [Fact]
    public void AKeyWithNoValueIsAnEmptyValue()
    {
        var read = CopilotWorkspaceFile.Parse(["name:", "branch:   "]);

        Assert.Equal(CopilotWorkspaceState.Readable, read.State);
        Assert.Equal(string.Empty, read.Values["name"]);
        Assert.Equal(string.Empty, read.Values["branch"]);
    }

    [Theory]
    [InlineData("  nested: value")]
    [InlineData("- list item")]
    [InlineData("key:value")]
    [InlineData("just words")]
    [InlineData("---")]
    public void ALineItDoesNotUnderstandMakesTheFileUnreadable(string bad)
    {
        var read = CopilotWorkspaceFile.Parse(["name: fine", bad, "cwd: C:\\Fixture"]);

        Assert.Equal(CopilotWorkspaceState.Unreadable, read.State);
        Assert.Empty(read.Values);
    }

    [Fact]
    public void ANestedLineUnderAKeyIsUnreadable()
    {
        // A mapping under a key is a nested line: the flat reader does not read it, so the file is counted as unreadable, not read partly.
        var read = CopilotWorkspaceFile.Parse(["context:", "  cwd: C:\\Fixture"]);

        Assert.Equal(CopilotWorkspaceState.Unreadable, read.State);
    }

    [Theory]
    [InlineData("name: \"open")]
    [InlineData("name: 'open")]
    public void AnUnterminatedQuoteIsUnreadable(string line)
    {
        Assert.Equal(CopilotWorkspaceState.Unreadable, CopilotWorkspaceFile.Parse([line]).State);
    }

    [Fact]
    public void AMissingFileIsMissingNotUnreadable()
    {
        using var home = new CopilotHome();

        var read = CopilotWorkspaceFile.Read(Path.Combine(home.Folder(CopilotHome.SessionId), CopilotWorkspaceFile.FileName));

        Assert.Equal(CopilotWorkspaceState.Missing, read.State);
        Assert.Empty(read.Values);
    }

    [Fact]
    public void AFileOnDiskIsReadWithItsValues()
    {
        using var home = new CopilotHome();
        var path = home.WriteWorkspace(CopilotHome.SessionId, CopilotHome.Workspace(CopilotHome.SessionId));

        var read = CopilotWorkspaceFile.Read(path);

        Assert.Equal(CopilotWorkspaceState.Readable, read.State);
        Assert.Equal("Fixture session name", read.Values["name"]);
        Assert.Equal("2026-11-20T10:05:00.000Z", read.Values["updated_at"]);
    }

    [Fact]
    public void AFileWithANestedLineOnDiskIsUnreadableAndCounted()
    {
        using var home = new CopilotHome();
        var path = home.WriteWorkspace(CopilotHome.SessionId, "name: fine", "  nested: value");

        Assert.Equal(CopilotWorkspaceState.Unreadable, CopilotWorkspaceFile.Read(path).State);
    }
}
