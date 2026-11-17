using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using OpenTail.Bullshark.Core.Claude;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Claude;

/// <summary>
/// How a launch's arguments are written on the command line (round 5 item 12): the escaping follows the Windows rules, so the length the
/// launch is checked against is the length the program receives. Checked by the expected text, and by reading the line back with the
/// operating system's own parser.
/// </summary>
public sealed class EscapeArgumentTests
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(string commandLine, out int argc);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    [SupportedOSPlatform("windows")]
    private static string[] ParseLikeWindows(string commandLine)
    {
        var argv = CommandLineToArgvW(commandLine, out var argc);
        try
        {
            var args = new string[argc];
            for (var i = 0; i < argc; i++)
                args[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!;
            return args;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [Fact]
    public void AnArgumentWithNoSpaceOrQuoteIsWrittenAsItIs()
    {
        Assert.Equal("plain", ClaudeCli.EscapeArgument("plain"));
        Assert.Equal(@"C:\work\a\b\", ClaudeCli.EscapeArgument(@"C:\work\a\b\"));
    }

    [Fact]
    public void AnArgumentWithASpaceIsWrappedInQuotes()
    {
        Assert.Equal("\"two words\"", ClaudeCli.EscapeArgument("two words"));
    }

    [Fact]
    public void AQuoteInsideIsEscapedAndTheWholeArgumentIsWrapped()
    {
        // say "hi"  becomes  "say \"hi\""
        Assert.Equal("\"say \\\"hi\\\"\"", ClaudeCli.EscapeArgument("say \"hi\""));
    }

    [Fact]
    public void BackslashesBeforeAQuoteAreDoubledAndTheQuoteIsEscaped()
    {
        // a\"b (no space) is wrapped, and the one backslash before the quote becomes three: a\\\"b
        Assert.Equal("\"a\\\\\\\"b\"", ClaudeCli.EscapeArgument("a\\\"b"));
    }

    [Fact]
    public void TrailingBackslashesOfAWrappedArgumentAreDoubled()
    {
        // "a b\" would close the quote, so the backslash is doubled: "a b\\"
        Assert.Equal("\"a b\\\\\"", ClaudeCli.EscapeArgument("a b\\"));
    }

    [Fact]
    public void AnEmptyArgumentIsTwoQuotes()
    {
        Assert.Equal("\"\"", ClaudeCli.EscapeArgument(string.Empty));
    }

    [Fact]
    public void AQuoteHeavyLaunchIsLongerOnTheCommandLineThanItsCharacters()
    {
        var brief = "He said \"go\" and the path was C:\\tmp\\\"x\\\" again";
        var launch = new BackgroundLaunch("bullshark-coder-abcd", @"C:\work", "haiku", "medium", brief, "auto");

        var written = string.Join(' ', new[]
        {
            "--bg", "--model", "haiku", "--effort", "medium", "--name", "bullshark-coder-abcd", "--permission-mode", "auto",
            ClaudeCli.EscapeArgument(brief),
        });

        Assert.Equal(written.Length, ClaudeCli.CommandLineLength(launch));
        Assert.True(ClaudeCli.CommandLineLength(launch) > "--bg --model haiku --effort medium --name bullshark-coder-abcd --permission-mode auto ".Length + brief.Length);
    }

    [Fact]
    public void WindowsReadsTheWrittenLineBackAsTheSameArguments()
    {
        if (!OperatingSystem.IsWindows()) return;

        string[] args =
        [
            "plain", "two words", "say \"hi\"", "back\\slash", "trailing\\", "a b\\", "a\\\"b", "x\\\\\"y",
            "tab\there", "", "--flag", "C:\\Program Files\\x\\", "\\\\server\\share\\\"quoted\"\\",
        ];
        var line = "prog " + string.Join(' ', args.Select(ClaudeCli.EscapeArgument));

        var parsed = ParseLikeWindows(line);

        Assert.Equal(args, parsed.Skip(1));
    }
}
