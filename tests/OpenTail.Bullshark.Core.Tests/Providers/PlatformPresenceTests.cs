using OpenTail.Bullshark.Core.Providers;

namespace OpenTail.Bullshark.Core.Tests.Providers;

/// <summary>
/// Which platforms the user has (the rail's dropdown, 2026-11-23). Each test searches a temp folder as the PATH, so the result does not depend on what
/// is installed on the machine that runs the tests.
/// </summary>
public sealed class PlatformPresenceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "opentail-platforms-" + Guid.NewGuid().ToString("N"));

    public PlatformPresenceTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private void Program(string name) => File.WriteAllText(Path.Combine(_folder, name), string.Empty);

    [Fact]
    public void AnEmptyPathHasNoPlatform()
    {
        Assert.Empty(PlatformPresence.Installed([ProviderId.Claude, ProviderId.Codex, ProviderId.Copilot, ProviderId.OpenCode], _folder));
    }

    [Fact]
    public void ClaudeIsFoundByItsExecutable()
    {
        Program("claude.exe");

        Assert.True(PlatformPresence.IsInstalled(ProviderId.Claude, _folder));
        Assert.False(PlatformPresence.IsInstalled(ProviderId.Codex, _folder));
    }

    [Fact]
    public void CopilotIsFoundByItsCommandShim()
    {
        Program("copilot.cmd");

        Assert.True(PlatformPresence.IsInstalled(ProviderId.Copilot, _folder));
    }

    [Fact]
    public void CodexIsFoundByItsPowerShellShim()
    {
        Program("codex.ps1");

        Assert.True(PlatformPresence.IsInstalled(ProviderId.Codex, _folder));
    }

    [Fact]
    public void OpenCodeIsFoundByItsExecutable()
    {
        Program("opencode.exe");

        Assert.True(PlatformPresence.IsInstalled(ProviderId.OpenCode, _folder));
    }

    [Fact]
    public void InstalledKeepsOnlyThePlatformsTheUserHasInTheOrderGiven()
    {
        Program("opencode.exe");
        Program("claude.exe");

        var installed = PlatformPresence.Installed([ProviderId.OpenCode, ProviderId.Codex, ProviderId.Claude], _folder);

        Assert.Equal([ProviderId.OpenCode, ProviderId.Claude], installed);
    }

    [Fact]
    public void AFolderNamedLikeAProgramIsNotTheProgram()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "claude.exe"));

        Assert.False(PlatformPresence.IsInstalled(ProviderId.Claude, _folder));
    }
}
