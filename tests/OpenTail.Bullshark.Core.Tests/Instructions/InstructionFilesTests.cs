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

    [Fact]
    public void ALoneCarriageReturnIsNotMadeALineBreak()
    {
        // A lone CR is not a line break in the file, so a save leaves it as it is (round 5 item 14).
        Assert.Equal("a\rb", InstructionFiles.WithLineEnding("a\rb", "\r\n"));
        Assert.Equal("a\rb\nc", InstructionFiles.WithLineEnding("a\rb\r\nc", "\n"));
    }

    [Fact]
    public void AMixedFileIsDetectedAndAnEditGivesEveryBreakTheMajorityEnding()
    {
        var file = "one\r\ntwo\nthree\r\n";

        Assert.True(InstructionFiles.HasMixedLineEndings(file));
        Assert.False(InstructionFiles.HasMixedLineEndings("a\r\nb\r\n"));
        Assert.False(InstructionFiles.HasMixedLineEndings("a\nb\n"));

        // The owner changes one word. The majority of this file is CRLF, so every break is CRLF after the save.
        var edited = "one\r\ntwo\r\nthree\r\nfour";
        Assert.Equal("one\r\ntwo\r\nthree\r\nfour", InstructionFiles.TextToSave(file, edited));
    }

    [Fact]
    public void AnUneditedSaveWritesAMixedFileBackExactly()
    {
        // The editor shows every break as CRLF. With no edit, the file's own text goes back, with its LF line break still LF.
        var file = "one\r\ntwo\nthree\r\n";
        var editorView = file.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n");

        Assert.Equal(file, InstructionFiles.TextToSave(file, editorView));
    }

    [Fact]
    public void ASaveThatChangesTheTextUsesTheFilesMajorityEnding()
    {
        var file = "one\nlf\nthen crlf\r\n";

        Assert.Equal("one\nlf\nthen crlf\nchanged", InstructionFiles.TextToSave(file, "one\r\nlf\r\nthen crlf\r\nchanged"));
    }
}
