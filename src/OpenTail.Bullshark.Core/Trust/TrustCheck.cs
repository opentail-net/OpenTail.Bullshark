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

    public static TrustResult Check(string folder, string claudeJsonPath)
    {
        var root = FindRepositoryRoot(folder);
        if (root is null) return new TrustResult(TrustState.NotARepository, null);

        return new TrustResult(IsRecordedTrusted(root, claudeJsonPath) ? TrustState.Trusted : TrustState.NotTrusted, root);
    }

    /// <summary>True when the project entry for this path has <c>hasTrustDialogAccepted</c> set to true.</summary>
    public static bool IsRecordedTrusted(string path, string claudeJsonPath)
    {
        if (!File.Exists(claudeJsonPath)) return false;

        using var document = JsonDocument.Parse(File.ReadAllText(claudeJsonPath));
        if (!document.RootElement.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Object)
            return false;

        var wanted = Normalise(path);
        foreach (var project in projects.EnumerateObject())
        {
            if (Normalise(project.Name) != wanted) continue;
            return project.Value.TryGetProperty("hasTrustDialogAccepted", out var trusted)
                   && trusted.ValueKind == JsonValueKind.True;
        }
        return false;
    }

    /// <summary>
    /// The repositories Claude has recorded as trusted, spelled as the file spells them (for example <c>C:/Git/x</c>).
    /// Read only: OpenTail.Bullshark never writes the trust record.
    /// </summary>
    public static IReadOnlyList<string> TrustedRepositories(string claudeJsonPath)
    {
        if (!File.Exists(claudeJsonPath)) return [];

        using var document = JsonDocument.Parse(File.ReadAllText(claudeJsonPath));
        if (!document.RootElement.TryGetProperty("projects", out var projects) || projects.ValueKind != JsonValueKind.Object)
            return [];

        return projects.EnumerateObject()
            .Where(p => p.Value.TryGetProperty("hasTrustDialogAccepted", out var trusted) && trusted.ValueKind == JsonValueKind.True)
            .Select(p => p.Name)
            .ToList();
    }

    /// <summary>
    /// Opens an interactive Claude in the repository root, in a new console window, so the owner can accept the
    /// trust prompt. OpenTail.Bullshark does not answer the prompt: the owner does.
    /// </summary>
    public static void OpenClaudeForTrust(string repositoryRoot)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/k claude",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = true,
        });
    }

    /// <summary>Claude writes both forward-slash and back-slash spellings of the same path. Compare them as one.</summary>
    public static string Normalise(string path) =>
        path.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();
}
