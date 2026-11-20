using OpenTail.Bullshark.Core.Terminal;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Terminal;

public sealed class PasteTextTests
{
    [Fact]
    public void ImagePasteSendsAltV()
    {
        // Alt+V is ESC then v. Only the key goes to the terminal; Claude Code reads the clipboard itself.
        Assert.Equal(new[] { '\u001b', 'v' }, PasteText.ImagePasteKeys.ToCharArray());
    }

    [Fact]
    public void AnEscapeThatEndsTheBracketedPasteIsRemoved()
    {
        // The hostile clipboard text: it would close the paste and type "rm -rf x" into the session.
        var clean = PasteText.Clean("safe\u001b[201~rm -rf x\r");

        Assert.Equal("safe[201~rm -rf x\r", clean);
        Assert.DoesNotContain('\u001b', clean);
    }

    [Fact]
    public void EveryC0ControlExceptTabAndTheLineBreaksIsRemoved()
    {
        var controls = new System.Text.StringBuilder();
        for (var c = 0; c < 0x20; c++)
        {
            if (c is 0x09 or 0x0A or 0x0D) continue;
            controls.Append((char)c);
        }
        controls.Append('\u007f');

        var clean = PasteText.Clean("a" + controls.ToString() + "b");

        Assert.Equal("ab", clean);
    }

    [Fact]
    public void NulBellBackspaceAndFormFeedAreRemoved()
    {
        Assert.Equal("ab", PasteText.Clean("a\0b"));
        Assert.Equal("ab", PasteText.Clean("a\u0007b"));
        Assert.Equal("ab", PasteText.Clean("a\u0008b"));
        Assert.Equal("ab", PasteText.Clean("a\u000Bb"));
        Assert.Equal("ab", PasteText.Clean("a\u000Cb"));
    }

    [Fact]
    public void DeleteAndTheC1ControlsAreRemoved()
    {
        Assert.Equal("ab", PasteText.Clean("a\u007fb"));
        // U+009B is the 8-bit CSI, which some terminals read as ESC [.
        Assert.Equal("a201~b", PasteText.Clean("a\u009b201~b"));
        Assert.Equal("ab", PasteText.Clean("a\u0080\u009f" + "b"));
    }

    [Fact]
    public void TabIsKept()
    {
        Assert.Equal("a\tb", PasteText.Clean("a\tb"));
    }

    [Fact]
    public void LineBreaksBecomeCarriageReturns()
    {
        Assert.Equal("a\rb", PasteText.Clean("a\r\nb"));
        Assert.Equal("a\rb", PasteText.Clean("a\nb"));
        Assert.Equal("a\rb", PasteText.Clean("a\rb"));
        Assert.Equal("a\r\rb", PasteText.Clean("a\r\n\nb"));
        Assert.Equal("a\r", PasteText.Clean("a\r"));
    }

    [Fact]
    public void NonAsciiTextAndSurrogatePairsAreKept()
    {
        const string text = "Grüße 日本語 🙂 café";

        Assert.Equal(text, PasteText.Clean(text));
    }

    [Fact]
    public void AnEmptyStringStaysEmpty()
    {
        Assert.Equal(string.Empty, PasteText.Clean(string.Empty));
    }

    [Fact]
    public void AVeryLongStringIsCleanedWholeAndKeepsItsLength()
    {
        var text = string.Concat(Enumerable.Repeat("line of text \u001b[0m\r\n", 50_000));

        var clean = PasteText.Clean(text);

        Assert.Equal(50_000 * "line of text [0m\r".Length, clean.Length);
        Assert.DoesNotContain('\u001b', clean);
        Assert.DoesNotContain('\n', clean);
    }

    [Fact]
    public void ANullInputIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => PasteText.Clean(null!));
    }
}
