using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenTail.Bullshark.Core.Activity;
using OpenTail.Bullshark.Core.Claude;
using OpenTail.Bullshark.Core.OpenCode;
using OpenTail.Bullshark.Core.Providers;
using OpenTail.Bullshark.Core.Resume;
using Xunit;

namespace OpenTail.Bullshark.App.Tests;

/// <summary>
/// The opencode cross-platform start (resume source and target plan, S6h): the tool check that comes before any model call (S6h-5), the start file and the
/// command line that carry the cross text (S6h-6), the cross text's header for opencode (S6h-1, S6h-6) and the opencode writer's summary input (S6h-8). Nothing
/// here starts a process: the summary call runs through a fake runner, and the start file is written under a temporary folder that the test removes.
/// </summary>
public sealed class OpenCodeCrossStartTests
{
    private static TranscriptLine Line(int number, string kind, string text) => new(number, kind, null, text, false);

    /// <summary>A fake environment runner: it records the standard input of its one call and answers with a fixed reply. No process starts.</summary>
    private sealed class FakeEnvRunner(string reply) : ICliEnvRunner
    {
        public int Calls { get; private set; }

        public string? StandardInput { get; private set; }

        public Task<CliResult> RunAsync(
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            string? standardInput = null)
        {
            Calls++;
            StandardInput = standardInput;
            return Task.FromResult(new CliResult(0, reply, string.Empty, TimedOut: false));
        }

        public Task<CliResult> RunAsync(
            IReadOnlyList<string> arguments,
            string? workingDirectory,
            IReadOnlyDictionary<string, string> environment,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            string? standardInput = null) =>
            RunAsync(arguments, workingDirectory, timeout, cancellationToken, standardInput);
    }

    [Fact]
    public void TheToolRefusalNamesPwshFirstThenTheShimAndIsNullWhenBothAreOnPath()
    {
        Assert.Null(OpenCodeResumeRules.ToolRefusal(pwshOnPath: true, shimOnPath: true));
        Assert.Equal(OpenCodeResumeRules.NoShimOnPath, OpenCodeResumeRules.ToolRefusal(pwshOnPath: true, shimOnPath: false));
        Assert.Equal(OpenCodeResumeRules.NoPwshOnPath, OpenCodeResumeRules.ToolRefusal(pwshOnPath: false, shimOnPath: true));
        Assert.Equal(OpenCodeResumeRules.NoPwshOnPath, OpenCodeResumeRules.ToolRefusal(pwshOnPath: false, shimOnPath: false));
    }

    [Fact]
    public void ACrossStartIntoOpenCodeKeepsItsTextInTheStartFileAndNotOnTheCommandLine()
    {
        var root = Path.Combine(Path.GetTempPath(), "opentail-s6h-" + Guid.NewGuid().ToString("N"));
        try
        {
            var lines = new List<TranscriptLine> { Line(1, "you", "an example question"), Line(2, "claude", "an example answer") };
            var text = CrossPlatformStartText.Build(ProviderId.Claude, ProviderId.OpenCode, "An example summary.", SummaryLevel.Minimal, lines, 1).Text;

            var path = OpenCodeStartLauncher.WriteStart(Path.Combine(root, "starts"), text, DateTimeOffset.Now);
            var command = OpenCodeStartLauncher.CommandLine(Path.Combine(root, "work"), path);

            // The command is the PowerShell 7 line with its script encoded in base64 (the last word). The script names the start file and the prompt flag.
            var script = Encoding.Unicode.GetString(Convert.FromBase64String(command[(command.LastIndexOf(' ') + 1)..]));
            Assert.Equal(text, File.ReadAllText(path));
            Assert.Contains(Path.GetFileName(path), script);
            Assert.Contains("--prompt", script);
            Assert.DoesNotContain("example summary", command);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void TheOpenCodeTargetHeaderNamesTheSourceAndOpenCodeAndKeepsTheTail()
    {
        var lines = new List<TranscriptLine> { Line(1, "you", "an example question"), Line(2, "codex", "an example answer") };

        var text = CrossPlatformStartText.Build(ProviderId.Codex, ProviderId.OpenCode, "An example summary.", SummaryLevel.Minimal, lines, 1).Text;

        Assert.Contains("Summary of a Codex CLI session", text);
        Assert.Contains("This new opencode session starts from that summary. The Codex CLI session itself is not continued.", text);
        Assert.Contains("you: an example question\ncodex: an example answer", text);
    }

    [Fact]
    public void TheWriterLabelsTheSourceMessagesAsOpenCodesAndKeepsTheOwnersAndTheWords()
    {
        var part = new List<TranscriptLine> { Line(1, "you", "question"), Line(2, "codex", "answer"), Line(3, "copilot", "other") };

        var labelled = OpenCodeResumeRules.SummaryLines(part, "codex");

        Assert.Equal(new[] { "you", "opencode", "copilot" }, labelled.Select(line => line.Kind).ToArray());
        Assert.Equal(new[] { "question", "answer", "other" }, labelled.Select(line => line.Text).ToArray());
        Assert.Equal("codex", part[1].Kind);
    }

    [Fact]
    public async Task TheOpenCodeWriterReadsTheSourceMessagesUnderOpenCodesLabelInOneCall()
    {
        var root = Path.Combine(Path.GetTempPath(), "opentail-s6h-" + Guid.NewGuid().ToString("N"));
        try
        {
            var part = new List<TranscriptLine> { Line(1, "you", "an example question"), Line(2, "claude", "an example answer") };
            var runner = new FakeEnvRunner("An example summary.");

            var summary = await new OpenCodeSummarizer(runner).SummariseAsync(
                Path.Combine(root, "state.json"), OpenCodeResumeRules.SummaryLines(part, "claude"), SummaryLevel.Minimal, CancellationToken.None);

            Assert.Equal("An example summary.", summary);
            Assert.Equal(1, runner.Calls);
            Assert.Contains("you: an example question", runner.StandardInput ?? string.Empty);
            Assert.Contains("opencode: an example answer", runner.StandardInput ?? string.Empty);
            Assert.DoesNotContain("claude: an example answer", runner.StandardInput ?? string.Empty);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
