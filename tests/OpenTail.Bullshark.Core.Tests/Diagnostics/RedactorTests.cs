using OpenTail.Bullshark.Core.Diagnostics;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Diagnostics;

/// <summary>The redaction rules (firstrun batch B3). The canaries are fake values that must never come out of the redactor.</summary>
public sealed class RedactorTests
{
    private const string Profile = @"C:\Users\CANARYUSER";

    [Fact]
    public void AnApiKeyWithAKnownPrefixIsRemoved()
    {
        var text = Redactor.Line("key sk-ant-api03-CANARYTOKEN0123456789abcdef is in use", Profile);

        Assert.DoesNotContain("CANARYTOKEN", text);
        Assert.Contains(Redactor.TokenMark, text);
    }

    [Theory]
    [InlineData("token ghp_CANARYGITHUB12345678")]
    [InlineData("token github_pat_CANARYPAT_0123456789")]
    [InlineData("token xoxb-CANARYSLACK-0123456789")]
    public void OtherKnownKeyPrefixesAreRemoved(string line)
    {
        var text = Redactor.Line(line, Profile);

        Assert.DoesNotContain("CANARY", text);
        Assert.Contains(Redactor.TokenMark, text);
    }

    [Fact]
    public void ABearerTokenIsRemoved()
    {
        var text = Redactor.Line("Authorization: Bearer CANARYBEARER.part.two", Profile);

        Assert.DoesNotContain("CANARYBEARER", text);
        Assert.Contains("Bearer " + Redactor.TokenMark, text);
    }

    [Fact]
    public void ALongBase64OrHexRunIsRemoved()
    {
        var run = "CANARYRUN" + new string('a', 32); // 41 letters, digits and +/ in a row
        var text = Redactor.Line($"secret {run} end", Profile);

        Assert.DoesNotContain("CANARYRUN", text);
        Assert.Contains(Redactor.TokenMark, text);
    }

    [Fact]
    public void AShortRunIsKept()
    {
        Assert.Equal("value abc123 kept", Redactor.Line("value abc123 kept", Profile));
    }

    [Fact]
    public void TheProfileFolderIsWrittenAsATildeInBothSlashForms()
    {
        Assert.Equal(@"~\AppData\Local\OpenTail.Bullshark\state.json",
            Redactor.Line(@"C:\Users\CANARYUSER\AppData\Local\OpenTail.Bullshark\state.json", Profile));
        Assert.Equal("~/Documents/notes.md", Redactor.Line("C:/Users/CANARYUSER/Documents/notes.md", Profile));
        Assert.DoesNotContain("CANARYUSER", Redactor.Line(@"c:\users\canaryuser\x.txt", Profile));
    }

    [Fact]
    public void OtherDrivePathsAreCutToTheirFileNameAndKeepTheLine()
    {
        var text = Redactor.Line(@"   at Foo.Bar() in C:\Git\split\OpenTail.Bullshark\src\SessionWindow.xaml.cs:line 435", Profile);

        Assert.Equal("   at Foo.Bar() in SessionWindow.xaml.cs:line 435", text);
    }

    [Fact]
    public void ASessionGuidIsCutToItsFirstEightCharacters()
    {
        Assert.Equal("session 0a1b2c3d ended", Redactor.Line("session 0a1b2c3d-1111-2222-3333-444455556666 ended", Profile));
    }

    [Theory]
    [InlineData("2026-11-19 10:00:00 brief: CANARYPROMPT please refactor everything")]
    [InlineData("prompt: CANARYPROMPT do the work")]
    [InlineData("transcript: CANARYPROMPT")]
    [InlineData("{\"role\":\"user\",\"content\":\"CANARYPROMPT\"}")]
    public void ALineThatMayCarryPromptOrTranscriptTextIsReplaced(string line)
    {
        Assert.Equal(Redactor.PromptMark, Redactor.Line(line, Profile));
    }

    [Fact]
    public void OutputThatAFailedCommandPrintedIsCutAfterItsLead()
    {
        var text = Redactor.Line("`claude --bg` did not print an id: CANARYBRIEF please do the work", Profile);

        Assert.DoesNotContain("CANARYBRIEF", text);
        Assert.Equal("`claude --bg` did not print an id: " + Redactor.OutputMark, text);
    }

    [Fact]
    public void ALongLineIsCutAtFourHundredCharacters()
    {
        // Words with spaces: a single unbroken run would be a token-like run and be replaced before the length rule.
        var text = Redactor.Line(string.Join(' ', Enumerable.Repeat("word", 250)), Profile);

        Assert.Equal(Redactor.MaxLineLength + 1, text.Length);
        Assert.EndsWith("…", text);
    }

    [Fact]
    public void TextIsRedactedLineByLineAndKeepsItsLines()
    {
        var text = Redactor.Text("one C:\\Users\\CANARYUSER\\a.txt\r\nprompt: CANARYPROMPT\nthree", Profile);

        Assert.Equal(3, text.Split(Environment.NewLine).Length);
        Assert.DoesNotContain("CANARY", text);
    }

    [Fact]
    public void AnEmptyProfileLeavesAPlainLineAsItIs()
    {
        Assert.Equal("nothing to hide here", Redactor.Line("nothing to hide here", string.Empty));
    }

    [Fact]
    public void CanaryStringsDoNotSurviveAMixedLine()
    {
        // The profile prefix is written as ~ (the plan's rule); the folders after it stay, so only the user's name and the token are asserted gone.
        var line = @"sk-ant-api03-CANARYMIX0123456789 at C:\Users\CANARYUSER\Desktop\project\file.txt; Bearer CANARYB";

        var text = Redactor.Line(line, Profile);

        Assert.DoesNotContain("CANARYMIX", text);
        Assert.DoesNotContain("CANARYUSER", text);
        Assert.DoesNotContain("CANARYB", text);
        Assert.Contains(@"~\Desktop\project\file.txt", text);
    }
}
