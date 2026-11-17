using System.Text.Json;

namespace OpenTail.Bullshark.Core.Trust;

public enum TrustState
{
    /// <summary>The folder is in a git repository and Claude has recorded that the repository root is trusted.</summary>
    Trusted,

    /// <summary>The folder is in a git repository whose root is not recorded as trusted. Background sessions refuse to start.</summary>
    NotTrusted,

    /// <summary>The folder is not in a git repository. Trust is not required to start a background session (F28).</summary>
    NotARepository,
}

public sealed record TrustResult(TrustState State, string? RepositoryRoot)
{
    public string Explanation => State switch
    {
        TrustState.Trusted => $"Trusted: {RepositoryRoot} has been accepted.",
        TrustState.NotTrusted =>
            $"Not trusted: {RepositoryRoot} is a git repository that Claude has not been told to trust. " +
            $"Run `claude` in {RepositoryRoot} once, accept the trust prompt, then start OpenTail.Bullshark again.",
        _ => "Not a git repository: no trust prompt is expected.",
    };
}

/// <summary>
/// Read-only check of workspace trust. Claude Code records trust per git repository root in
/// <c>~/.claude.json</c>, under <c>projects</c>, as <c>hasTrustDialogAccepted</c> (findings F24, F28).
/// There is no CLI command for this in the build checked on 2026-11-14. OpenTail.Bullshark never writes this file:
/// accepting trust is the owner's decision, made in Claude itself.
/// </summary>
public static class TrustCheck
{
    public static string DefaultClaudeJsonPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");

    /// <summary>Walks up from the folder to the nearest directory that holds a <c>.git</c> entry.</summary>
    public static string? FindRepositoryRoot(string folder)
    {
        var current = new DirectoryInfo(Path.GetFullPath(folder));
        while (current is not null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, ".git")))
                return current.FullName;
            current = current.Parent;
        }
        return null;
    }

    /// <summary>Reads Claude's file once for this one folder. A pass that checks many folders uses the <see cref="ClaudeTrust"/> overload.</summary>
    public static TrustResult Check(string folder, string claudeJsonPath) => Check(folder, ClaudeTrust.Read(claudeJsonPath));

    /// <summary>The trust answer for a folder from a file that has already been read, so every folder of a pass sees the same record.</summary>
    public static TrustResult Check(string folder, ClaudeTrust trust)
    {
        var root = FindRepositoryRoot(folder);
        if (root is null) return new TrustResult(TrustState.NotARepository, null);

        return new TrustResult(trust.IsTrusted(root) ? TrustState.Trusted : TrustState.NotTrusted, root);
    }

    /// <summary>
    /// The file Claude keeps its settings in. Claude may be writing it, so a read that fails or is half written gives an empty
    /// document: nothing is trusted, and no session starts because of it.
    /// </summary>
    private static JsonDocument ReadClaudeJson(string path)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return JsonDocument.Parse("{}");
        }
    }

    /// <summary>True when the project entry for this path has <c>hasTrustDialogAccepted</c> set to true.</summary>
    public static bool IsRecordedTrusted(string path, string claudeJsonPath) => ClaudeTrust.Read(claudeJsonPath).IsTrusted(path);

    /// <summary>
    /// The repositories Claude has recorded as trusted, spelled as the file spells them (for example <c>C:/Git/x</c>).
    /// Read only: OpenTail.Bullshark never writes the trust record.
    /// </summary>
    public static IReadOnlyList<string> TrustedRepositories(string claudeJsonPath)
    {
        if (!File.Exists(claudeJsonPath)) return [];

        using var document = ReadClaudeJson(claudeJsonPath);
        // A root or entry that is not an object reads as nothing trusted, as in ClaudeTrust: TryGetProperty would throw on it.
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Object)
            return [];

        return projects.EnumerateObject()
            .Where(p => p.Value.ValueKind == JsonValueKind.Object
                        && p.Value.TryGetProperty("hasTrustDialogAccepted", out var trusted) && trusted.ValueKind == JsonValueKind.True)
            .Select(p => p.Name)
            .ToList();
    }

    /// <summary>
    /// The problem with reading Claude's settings file, or null when it can be read or does not exist. A file that cannot be
    /// parsed (Claude may be writing it) gives no trust, and it is reported so that the owner is not told to accept trust that
    /// may already be recorded. Phase 1 of the review, bad JSON in ~/.claude.json.
    /// </summary>
    public static string? UnreadableProblem(string claudeJsonPath) => ClaudeTrust.Read(claudeJsonPath).Problem;

    /// <summary>Claude writes both forward-slash and back-slash spellings of the same path. Compare them as one.</summary>
    public static string Normalise(string path) =>
        path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
}
