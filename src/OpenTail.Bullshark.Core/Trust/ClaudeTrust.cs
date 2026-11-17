using System.Text.Json;

namespace OpenTail.Bullshark.Core.Trust;

/// <summary>
/// Claude's trust record, read once. A pass asks about many folders, and reading <c>~/.claude.json</c> for each one is slow.
/// It is also wrong: Claude may write the file between two reads, so one pass would see one answer for one folder and
/// another answer for the next. This holds one read's answers, so every folder in a pass is judged on the same file.
/// </summary>
public sealed class ClaudeTrust
{
    /// <summary>Repository roots, normalised, mapped to whether trust is accepted. Filled with TryAdd: the first entry wins.</summary>
    private readonly Dictionary<string, bool> _trusted;

    private ClaudeTrust(string? problem, Dictionary<string, bool> trusted)
    {
        Problem = problem;
        _trusted = trusted;
    }

    /// <summary>
    /// Why the file could not be read, or null when it was read or does not exist. A file that cannot be parsed (Claude may be
    /// writing it) gives no trust, and it is reported so that the owner is not told to accept trust that may already be recorded.
    /// While this is set, nothing is trusted.
    /// </summary>
    public string? Problem { get; }

    /// <summary>
    /// Reads the file once. A missing file means Claude has never run here: nothing is trusted and there is no problem. A file that is there
    /// but cannot be read is a problem. The file is opened rather than checked with File.Exists first, because that check is false for a file
    /// the user cannot reach, and such a file would then read as missing (round 5 item 17).
    /// </summary>
    public static ClaudeTrust Read(string claudeJsonPath)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(claudeJsonPath));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new ClaudeTrust(null, new Dictionary<string, bool>(StringComparer.Ordinal));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new ClaudeTrust(
                $"{claudeJsonPath} cannot be read ({ex.GetType().Name}): no session starts or restarts until it can.",
                new Dictionary<string, bool>(StringComparer.Ordinal));
        }

        using (document)
        {
            var trusted = new Dictionary<string, bool>(StringComparer.Ordinal);
            var root = document.RootElement;
            // A file that is valid JSON but not an object has no projects, so nothing is trusted. It is not a problem with the file.
            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("projects", out var projects)
                && projects.ValueKind == JsonValueKind.Object)
            {
                foreach (var project in projects.EnumerateObject())
                    trusted.TryAdd(TrustCheck.Normalise(project.Name), IsAccepted(project.Value));
            }
            return new ClaudeTrust(null, trusted);
        }
    }

    /// <summary>
    /// True when the first project entry for this repository root has <c>hasTrustDialogAccepted</c> set to true. Nothing is trusted
    /// while <see cref="Problem"/> is set, and a root with no entry is not trusted.
    /// </summary>
    public bool IsTrusted(string root) =>
        _trusted.TryGetValue(TrustCheck.Normalise(root), out var trusted) && trusted;

    /// <summary>An entry that is not an object (Claude never writes one) is not trusted, rather than an exception.</summary>
    private static bool IsAccepted(JsonElement project) =>
        project.ValueKind == JsonValueKind.Object
        && project.TryGetProperty("hasTrustDialogAccepted", out var trusted)
        && trusted.ValueKind == JsonValueKind.True;
}
