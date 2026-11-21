using OpenTail.Bullshark.Core.Codex;
using OpenTail.Bullshark.Core.Health;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>
/// Stage C5 and C6 (h): the Codex row of the setup check. Only a PATH given by the test is searched, so the owner's PATH and the codex
/// program on it are never looked at, and no program is run.
/// </summary>
public sealed class CodexSetupCheckTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-codex-setup-" + Guid.NewGuid().ToString("N"));

    public CodexSetupCheckTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void WithNoCodexOnThePathTheRowIsAProblem()
    {
        // The folder on this PATH holds no codex program, so the one row says how to fix it.
        var empty = Path.Combine(_dir, "empty");
        Directory.CreateDirectory(empty);

        var items = CodexSetupCheck.Run(empty);

        var item = Assert.Single(items);
        Assert.Equal("Codex CLI", item.Area);
        Assert.Equal("codex", item.Name);
        Assert.Equal(SetupState.Problem, item.State);
        Assert.StartsWith("not found on PATH: install Codex CLI", item.Detail);
    }

    [Theory]
    [InlineData("codex.cmd")]
    [InlineData("codex.exe")]
    [InlineData("codex.ps1")]
    public void AProgramOnThePathGivesAnOkRowWithItsPath(string fileName)
    {
        // The file is only looked for, never run: an empty file with the name is enough.
        var folder = Path.Combine(_dir, "bin");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, fileName);
        File.WriteAllText(file, string.Empty);

        var item = Assert.Single(CodexSetupCheck.Run(folder));

        Assert.Equal(SetupState.Ok, item.State);
        Assert.Equal("found: " + file, item.Detail);
    }
}
