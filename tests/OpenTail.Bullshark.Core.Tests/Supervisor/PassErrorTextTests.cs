using System.ComponentModel;
using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

/// <summary>The owner's sentence for a failed pass (firstrun batch B2): each known cause, and the rest as type and first line.</summary>
public sealed class PassErrorTextTests
{
    [Fact]
    public void AClaudeProcessThatCannotBeStartedGivesTheInstallSentence()
    {
        var ex = new Win32Exception(2, "An error occurred trying to start process 'claude' with working directory 'C:\\Users\\Sample\\work'. The system cannot find the file specified.");

        Assert.Equal(PassErrorText.ClaudeNotStarted, PassErrorText.Of(ex));
    }

    [Fact]
    public void TheRunnersCouldNotStartMessageForClaudeGivesTheSameSentence()
    {
        Assert.Equal(PassErrorText.ClaudeNotStarted, PassErrorText.Of(new InvalidOperationException("Could not start 'claude'.")));
    }

    [Fact]
    public void AClaudeSentenceNamesTheNextStep()
    {
        Assert.Contains("on PATH", PassErrorText.ClaudeNotStarted);
        Assert.Contains("Settings > Claude > Check setup", PassErrorText.ClaudeNotStarted);
    }

    [Fact]
    public void AMissingGitIsNotTheClaudeSentence()
    {
        var text = PassErrorText.Of(new Win32Exception(2, "An error occurred trying to start process 'git' with working directory 'C:\\x'."));

        Assert.NotEqual(PassErrorText.ClaudeNotStarted, text);
        Assert.StartsWith("Win32Exception: ", text);
    }

    [Fact]
    public void AnUnreadableStoreGivesItsFileNameAndTheReasonOnly()
    {
        var ex = new StoreUnreadableException(@"C:\Users\Someone\AppData\Local\OpenTail.Bullshark\runs.json", "unexpected end of data");

        var text = PassErrorText.Of(ex);

        Assert.Equal("runs.json cannot be read: unexpected end of data", text);
        Assert.DoesNotContain("Someone", text);
    }

    [Fact]
    public void AFileErrorThatNamesAJsonFileGivesItsNameWithoutTheFolder()
    {
        var ex = new IOException(@"The process cannot access the file 'C:\Users\Someone\.claude.json' because it is being used by another process.");

        var text = PassErrorText.Of(ex);

        Assert.StartsWith(".claude.json cannot be read: ", text);
        Assert.DoesNotContain("Someone", text);
        Assert.DoesNotContain("C:\\", text);
    }

    [Fact]
    public void AnUnknownFailureGivesTypeAndFirstLineWithNoStackAndNoFolders()
    {
        var ex = new InvalidOperationException(
            "boom reading C:\\Users\\Someone\\secret\\notes.txt\n   at OpenTail.Bullshark.Core.Foo() in C:\\Git\\x.cs:line 3");

        var text = PassErrorText.Of(ex);

        Assert.Equal("InvalidOperationException: boom reading notes.txt", text);
        Assert.DoesNotContain("Someone", text);
        Assert.DoesNotContain("   at ", text);
    }

    [Fact]
    public void AnUnknownFailureIsKeptToTwoHundredCharacters()
    {
        var text = PassErrorText.Of(new InvalidOperationException(new string('x', 500)));

        Assert.Equal(PassErrorText.MaxLength + 1, text.Length); // the 200 characters and the ellipsis
        Assert.EndsWith("…", text);
    }

    [Fact]
    public void AnUnknownFailureWithNoMessageGivesTheTypeOnly()
    {
        Assert.Equal("InvalidOperationException: ", PassErrorText.Of(new InvalidOperationException(string.Empty)));
    }
}
