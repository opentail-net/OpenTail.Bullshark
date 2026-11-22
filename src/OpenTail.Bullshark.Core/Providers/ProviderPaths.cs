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

    /// <summary>The variable that moves opencode's data folder (its parent; the folder is <c>opencode</c> inside it).</summary>
    public const string XdgDataHomeVariable = "XDG_DATA_HOME";

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

    /// <summary>
    /// opencode's paths (OC-D2). The data folder is <c>opencode</c> inside <c>XDG_DATA_HOME</c> when that variable is set and not blank, else
    /// <c>~\.local\share\opencode</c>. The database is read from that folder (OC1). <paramref name="env"/> and <paramref name="userProfile"/> are passed in
    /// so a test need not read the real environment. A provider with no archive has a null <see cref="ArchiveRoot"/>, so opencode has none.
    /// </summary>
    public static ProviderPaths ForOpenCode(Func<string, string?>? env = null, string? userProfile = null)
    {
        var read = env ?? Environment.GetEnvironmentVariable;
        string? set = read(XdgDataHomeVariable);
        string folder = !string.IsNullOrWhiteSpace(set)
            ? Path.Combine(set.Trim(), "opencode")
            : Path.Combine(userProfile ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "opencode");
        return new ProviderPaths(folder, folder, null);
    }
}
