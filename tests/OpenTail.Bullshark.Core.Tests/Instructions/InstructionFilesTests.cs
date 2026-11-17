using OpenTail.Bullshark.Core.Instructions;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Instructions;

public sealed class InstructionFilesTests
{
    [Theory]
    [InlineData("a\nb\n", "\n")]
    [InlineData("a\r\nb\r\n", "\r\n")]
    [InlineData("a\r\nb\n\n", "\n")]
    [InlineData("a\r\nb\r\nc\n", "\r\n")]
    [InlineData("no line break", "\r\n")]
    [InlineData("", "\r\n")]
    public void TheLineEndingIsTheOneMostBreaksUse(string text, string expected) =>
        Assert.Equal(expected, InstructionFiles.LineEndingOf(text));

    [Fact]
    public void AnLfFileKeepsItsEndingThroughAnEditorThatReportsCrlf()
    {
        // The editor reports each break as CRLF. A save with no edit writes the file back exactly as it was.
        var file = "# Rules\nBe brief.\n";
        var editorText = file.Replace("\n", "\r\n");

        Assert.Equal(file, InstructionFiles.WithLineEnding(editorText, InstructionFiles.LineEndingOf(file)));
    }

    [Fact]
    public void ACrlfFileKeepsItsEnding()
    {
        var file = "# Rules\r\nBe brief.\r\n";

        Assert.Equal(file, InstructionFiles.WithLineEnding(file, InstructionFiles.LineEndingOf(file)));
    }

    [Fact]
    public void AnEditWithNewLinesIsWrittenWithTheFilesEnding()
    {
        var file = "one\ntwo\n";
        var typed = "one\r\ntwo\r\nthree";

        Assert.Equal("one\ntwo\nthree", InstructionFiles.WithLineEnding(typed, InstructionFiles.LineEndingOf(file)));
    }

    [Fact]
    public void ConvertingAgainGivesTheSameText()
    {
        var once = InstructionFiles.WithLineEnding("a\r\nb\nc", "\r\n");

        Assert.Equal(once, InstructionFiles.WithLineEnding(once, "\r\n"));
        Assert.Equal("a\r\nb\r\nc", once);
    }
}
