using OpenTail.Bullshark.Core.Codex;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Live;

/// <summary>
/// P9 live check: reads the model list of the owner's Codex account once, through <c>codex app-server</c>. That is a read-only call: no model turn and no
/// session starts. It runs only when OPENTAIL_CODEX_LIVE=1 is set. It writes a report of the problem type, the counts and the model ids (and which one is the
/// default) to the temp folder. The reply itself is never written or printed.
/// </summary>
[Trait("Category", "Live")]
public sealed class CodexModelListLiveTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("OPENTAIL_CODEX_LIVE") == "1";

    [Fact]
    public async Task TheAccountsModelListIsReadAndHasOneDefault()
    {
        if (!Enabled) return;

        var reading = await new CodexModelList().ReadAsync(CancellationToken.None);

        var lines = new List<string>
        {
            $"problem: {reading.Problem}",
            $"visible: {reading.Models.Count}",
            $"hidden: {reading.Hidden}",
            $"dropped: {reading.Dropped}",
        };
        lines.AddRange(reading.Models.Select(model => $"model: {model.Id} default={model.IsDefault}"));
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "opentail-codex-model-list-live.txt"), string.Join(Environment.NewLine, lines) + Environment.NewLine);

        Assert.Equal(CodexModelProblem.None, reading.Problem);
        Assert.Single(reading.Models, model => model.IsDefault);
    }
}
