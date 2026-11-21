using System.Text.Json;
using OpenTail.Bullshark.Core.Codex;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>
/// Step 0 (decision R-D5): the injected context at the start of a session is left out of the summary input and of the transcript lines, by one
/// rule in <see cref="CodexMessages"/>. Every text here is synthetic; no rollout is read.
/// </summary>
public sealed class CodexMessagesTests
{
    private const string At = "2026-11-20T10:00:02.000Z";

    /// <summary>A message record whose text is escaped as JSON, so any text (quotes, newlines, backslashes) is valid.</summary>
    private static string Message(string role, string text)
    {
        var part = role == "user" ? "input_text" : "output_text";
        var payload = JsonSerializer.Serialize(new { type = "message", role, content = new[] { new { type = part, text } } });
        return CodexHome.Record(At, "response_item", payload);
    }

    [Theory]
    [InlineData("<environment_context>\n  <cwd>C:\\work\\demo</cwd>\n</environment_context>")]
    [InlineData("<user_instructions>Synthetic rules for the test.</user_instructions>")]
    [InlineData("<INSTRUCTIONS>Synthetic rules, upper case.</INSTRUCTIONS>")]
    [InlineData("# AGENTS.md instructions for C:\\work\\demo\n\nSynthetic text.")]
    [InlineData("   \n<environment_context>padded with white space first</environment_context>")]
    public void InjectedContextOfTheOwnerIsLeftOutOfTheSummaryInputAndTheTranscript(string injected)
    {
        var lines = new[] { Message("user", injected), Message("user", "a real question") };

        Assert.Equal("you: a real question", CodexSummaryInput.Build(lines));
        var turns = CodexTurns.FromRollout(lines);
        var line = Assert.Single(turns);
        Assert.Equal("you", line.Kind);
        Assert.Equal("a real question", line.Text);
        Assert.Equal(1, line.Number);
    }

    [Fact]
    public void InjectedContextOfCodexIsKept()
    {
        // The rule checks the owner's messages only (R-D5): a reply that begins with one of the prefixes is kept.
        var lines = new[] { Message("assistant", "<environment_context>a reply that quotes a tag</environment_context>") };

        Assert.Equal("codex: <environment_context>a reply that quotes a tag</environment_context>", CodexSummaryInput.Build(lines));
        var line = Assert.Single(CodexTurns.FromRollout(lines));
        Assert.Equal("codex", line.Kind);
    }

    [Fact]
    public void APrefixInTheMiddleOfAMessageDoesNotCountAsInjectedContext()
    {
        var lines = new[] { Message("user", "please explain what <user_instructions> does") };

        Assert.Equal("you: please explain what <user_instructions> does", CodexSummaryInput.Build(lines));
    }

    [Fact]
    public void TheSummaryInputAndTheTranscriptAgreeLineForLine()
    {
        var lines = new[]
        {
            Message("user", "<environment_context>skipped</environment_context>"),
            Message("user", "first question"),
            Message("assistant", "first answer"),
            "this is not json",
            Message("user", "second question with \"quotes\" and a\nnew line"),
        };

        var fromTranscript = string.Join("\n\n", CodexTurns.FromRollout(lines).Select(line => line.Kind + ": " + line.Text));

        Assert.Equal(CodexSummaryInput.Build(lines), fromTranscript);
    }
}
