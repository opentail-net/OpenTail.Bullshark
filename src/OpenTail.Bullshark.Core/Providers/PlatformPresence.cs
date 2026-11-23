using OpenTail.Bullshark.Core.Health;

namespace OpenTail.Bullshark.Core.Providers;

/// <summary>
/// Which platforms the user has (the rail's dropdown offers only these). A platform is had when its program is on PATH, found the same way the Setup
/// page's rows find it (<see cref="SetupCheck.FindOnPath"/>), so the dropdown and the Setup page always agree. The check reads folder names only: it
/// starts nothing and reads no file's text. Use it wherever a platform should be offered only when it is there.
/// </summary>
public static class PlatformPresence
{
    /// <summary>The extensions a Windows shim can carry: Codex, Copilot and opencode install a program plus a .ps1 or .cmd shim.</summary>
    private static readonly string[] ShimExtensions = [".exe", ".cmd", ".ps1"];

    /// <summary>
    /// True when the platform's program is on PATH. <paramref name="pathVariable"/> is the PATH to search; null means this process's PATH.
    /// </summary>
    public static bool IsInstalled(ProviderId platform, string? pathVariable = null)
    {
        var path = pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        if (platform == ProviderId.Claude) return SetupCheck.FindOnPath("claude", path) is not null;
        if (platform == ProviderId.Codex) return SetupCheck.FindOnPath("codex", path, ShimExtensions) is not null;
        if (platform == ProviderId.Copilot) return SetupCheck.FindOnPath("copilot", path, ShimExtensions) is not null;
        if (platform == ProviderId.OpenCode) return SetupCheck.FindOnPath("opencode", path, ShimExtensions) is not null;
        return false;
    }

    /// <summary>The platforms among <paramref name="candidates"/> that the user has, in the order given.</summary>
    public static IReadOnlyList<ProviderId> Installed(IEnumerable<ProviderId> candidates, string? pathVariable = null) =>
        candidates.Where(platform => IsInstalled(platform, pathVariable)).ToList();
}
