using System.Windows.Controls;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// How a WPF TextBox keeps line breaks (the supervisor plan, item 14: the instructions save keeps a lone carriage return as it is, and this checks the
/// editor does not change it). Checked 2026-11-23 on net10.0 and net11.0; the test pins what was seen.
/// </summary>
public sealed class TextBoxLineBreaksTests
{
    [Fact]
    public void ATextBoxKeepsEveryLineBreakAsItWasSet()
    {
        UiThread.Run(() =>
        {
            var box = new TextBox { AcceptsReturn = true, Text = "one\rtwo\r\nthree\nfour" };

            Assert.Equal("one\rtwo\r\nthree\nfour", box.Text);
        });
    }

    [Fact]
    public void ATypedOrInsertedCarriageReturnIsOnlyACarriageReturn()
    {
        UiThread.Run(() =>
        {
            var box = new TextBox { AcceptsReturn = true, Text = "x" };
            box.SelectionStart = 1;
            box.SelectedText = "\r";

            Assert.Equal("x\r", box.Text);
        });
    }

    [Fact]
    public void ALoneCarriageReturnStaysALoneCarriageReturn()
    {
        UiThread.Run(() =>
        {
            var box = new TextBox { AcceptsReturn = true, Text = "a\rb" };

            Assert.Equal("a\rb", box.Text);
        });
    }
}
