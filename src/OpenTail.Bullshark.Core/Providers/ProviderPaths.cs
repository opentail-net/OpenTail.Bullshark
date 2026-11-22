using OpenTail.Bullshark.Core.Health;

namespace OpenTail.Bullshark.Core.Providers;

/// <summary>
/// Where a provider keeps its files. Claude's values come from <see cref="ClaudePaths"/> (so <c>CLAUDE_CONFIG_DIR</c> is honoured).
/// Codex's home is <c>CODEX_HOME</c> when that variable is set and not blank, else <c>~\.codex</c>; its rollouts are under
/// <c>sessions</c>. A provider with no archive has a null <see cref="ArchiveRoot"/>.
/// </summary>
public sealed record ProviderPaths(string HomeRoot, string HistoryRoot, string? ArchiveRoot)
{
    /// <summary>The variable that moves Codex's home.</summary>
    public const string CodexHomeVariable = "CODEX_HOME";

    /// <summary>The variable that moves Copilot's home.</summary>
    public const string CopilotHomeVariable = "COPILOT_HOME";

    /// <summary>Claude's paths. <paramref name="env"/> is passed in so a test can set or clear variables.</summary>
    public static ProviderPaths ForClaude(Func<string, string?>? env = null) =>
        new(ClaudePaths.ConfigDir(env), ClaudePaths.ProjectsRoot(env), ClaudePaths.ArchiveRoot(env));

    /// <summary>Codex's paths. <paramref name="userProfile"/> is passed in so a test need not read the real profile.</summary>
    public static ProviderPaths ForCodex(Func<string, string?>? env = null, string? userProfile = null)
    {
        var read = env ?? Environment.GetEnvironmentVariable;
        string? set = read(CodexHomeVariable);
        string home = !string.IsNullOrWhiteSpace(set)
            ? set.Trim()
            : Path.Combine(userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        return new ProviderPaths(home, Path.Combine(home, "sessions"), null);
    }

    /// <summary>
    /// Copilot's paths. The home is <c>COPILOT_HOME</c> when that variable is set and not blank, else <c>~\.copilot</c>; its sessions are under
    /// <c>session-state</c>. A provider with no archive has a null <see cref="ArchiveRoot"/>, so Copilot has none.
    /// </summary>
    public static ProviderPaths ForCopilot(Func<string, string?>? env = null, string? userProfile = null)
    {
        var read = env ?? Environment.GetEnvironmentVariable;
        string? set = read(CopilotHomeVariable);
        string home = !string.IsNullOrWhiteSpace(set)
            ? set.Trim()
            : Path.Combine(userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".copilot");
        return new ProviderPaths(home, Path.Combine(home, "session-state"), null);
    }
}
