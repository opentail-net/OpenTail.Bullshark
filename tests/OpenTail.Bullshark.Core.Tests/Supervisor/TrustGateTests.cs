using OpenTail.Bullshark.Core.Supervisor;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Supervisor;

public sealed class TrustGateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-gate-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _claudeJson;

    public TrustGateTests()
    {
        Directory.CreateDirectory(_dir);
        _claudeJson = Path.Combine(_dir, ".claude.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void AnUntrustedRunWaitsAndTheOtherRunsGoOn()
    {
        var trusted = Path.Combine(_dir, "trusted");
        var untrusted = Path.Combine(_dir, "untrusted");
        var plain = Path.Combine(_dir, "plain");
        foreach (var repo in new[] { trusted, untrusted }) Directory.CreateDirectory(Path.Combine(repo, ".git"));
        Directory.CreateDirectory(plain);
        File.WriteAllText(_claudeJson,
            "{\"projects\":{" +
            $"\"{trusted.Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":true}}," +
            $"\"{untrusted.Replace('\\', '/')}\":{{\"hasTrustDialogAccepted\":false}}" +
            "}}");

        var specs = new[]
        {
            TestSpecs.Planner(trusted, "t"), TestSpecs.Coder(trusted),
            TestSpecs.Planner(untrusted, "t"), TestSpecs.Coder(untrusted),
            TestSpecs.Planner(plain, "t"),
        };

        var (runnable, roots) = TrustGate.Filter(specs, _claudeJson);

        Assert.Equal(3, runnable.Count);
        Assert.DoesNotContain(runnable, s => s.WorkingDirectory.Contains("untrusted", StringComparison.OrdinalIgnoreCase));
        var root = Assert.Single(roots);
        Assert.Contains("untrusted", root, StringComparison.OrdinalIgnoreCase);
    }
}
