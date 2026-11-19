namespace OpenTail.Bullshark.Core.Health;

/// <summary>
/// Where Claude Code keeps its files for this user (reliability batch R2). Every path to a Claude file goes through here, so a change of
/// <c>CLAUDE_CONFIG_DIR</c> moves them all together. With the variable set, Claude keeps its folder there, and it keeps <c>.claude.json</c>
/// inside that folder too (tested with claude 2.1.328 on 2026-11-19: <c>claude mcp list</c> wrote it there). Without the variable, the
/// folder is <c>~\.claude</c> and the file is <c>~\.claude.json</c>, beside it.
/// </summary>
/// <remarks>
/// The environment is passed in, so a test can set or clear the variable. A value that is empty or only spaces is not set. A set value is
/// trimmed; a relative value is left relative, as Claude itself would read it.
/// </remarks>
public static class ClaudePaths
{
    /// <summary>The variable Claude Code reads for its folder.</summary>
    public const string ConfigDirVariable = "CLAUDE_CONFIG_DIR";

    /// <summary>The variable's value when it is set, trimmed; null when it is not set or is only spaces.</summary>
    private static string? SetValue(Func<string, string?> env)
    {
        var value = env(ConfigDirVariable);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Claude's folder: <c>CLAUDE_CONFIG_DIR</c> when it is set, else <c>~\.claude</c>.</summary>
    public static string ConfigDir(Func<string, string?>? env = null) =>
        SetValue(env ?? Environment.GetEnvironmentVariable) ?? Path.Combine(UserProfile, ".claude");

    /// <summary>The settings and state file: <c>&lt;ConfigDir&gt;\.claude.json</c> when the variable is set, else <c>~\.claude.json</c>.</summary>
    public static string ClaudeJson(Func<string, string?>? env = null)
    {
        var set = SetValue(env ?? Environment.GetEnvironmentVariable);
        return set is null
            ? Path.Combine(UserProfile, ".claude.json")
            : Path.Combine(set, ".claude.json");
    }

    /// <summary>The transcripts: <c>&lt;ConfigDir&gt;\projects</c>.</summary>
    public static string ProjectsRoot(Func<string, string?>? env = null) => Path.Combine(ConfigDir(env), "projects");

    /// <summary>
    /// The archive of old transcripts (gems item 6): <c>&lt;ConfigDir&gt;\bullshark-archive</c>. It is not under the projects folder, so Claude
    /// does not read it. With the variable set, it moves with the folder; an archive made before that stays where it was, and is not listed.
    /// </summary>
    public static string ArchiveRoot(Func<string, string?>? env = null) => Path.Combine(ConfigDir(env), "bullshark-archive");
}
