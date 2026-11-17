using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Setup;

/// <summary>
/// What the setup page remembers for one pattern (D-S6): the models and efforts, coder count, and the saved briefs.
/// A saved brief is the owner's text, with its tags, and it is expanded at each launch. Null means no brief is saved, so the
/// built-in template is used. A running session keeps the brief it started with.
/// PlannerExtra and CoderExtra are read from files written before the briefs, and they are not used: no launch adds them.
/// </summary>
public sealed record PatternSettings(
    string PatternKey,
    string PlannerModel,
    string PlannerEffort,
    string CoderModel,
    string CoderEffort,
    int Coders,
    string PlannerExtra,
    string CoderExtra,
    string Reviewer = "sonnet",
    string Assistant = "none",
    string AssistantEffort = "medium",
    string? PlannerBrief = null,
    string? CoderBrief = null);

/// <summary>Reads and writes patterns.json. The file sits beside state.json, so a scratch state file isolates it too.</summary>
public sealed class PatternStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public PatternStore(string path)
    {
        _path = path;
    }

    /// <summary>The store that goes with a state file: patterns.json in the same folder.</summary>
    public static PatternStore Beside(string stateFile) =>
        new(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(stateFile))!, "patterns.json"));

    public string Path => _path;

    /// <summary>The settings a pattern starts with: the CLI defaults from D-S5, and one coder, or two for many coders.</summary>
    public static PatternSettings Defaults(string patternKey)
    {
        var kind = PatternSpec.Find(patternKey)?.Kind;
        return new PatternSettings(
            patternKey,
            RunPlan.PlannerModel,
            RunPlan.PlannerEffort,
            RunPlan.CoderModel,
            RunPlan.CoderEffort,
            kind == PatternKind.PlanAndManyCoders ? Choices.MinManyCoders : 1,
            PlannerExtra: string.Empty,
            CoderExtra: string.Empty,
            Reviewer: "sonnet",
            Assistant: "none",
            AssistantEffort: "medium");
    }

    /// <summary>
    /// All saved settings, read under the file lock. A missing file gives none. A file that cannot be read or parsed throws
    /// <see cref="StoreUnreadableException"/>; it is never read as empty, because the next save would overwrite it.
    /// </summary>
    public IReadOnlyDictionary<string, PatternSettings> Load()
    {
        using var _ = FileLock.Hold(_path);
        return Read();
    }

    /// <summary>The saved settings for a pattern, or its defaults.</summary>
    public PatternSettings For(string patternKey) =>
        Load().TryGetValue(patternKey, out var saved) ? saved : Defaults(patternKey);

    /// <summary>
    /// Writes one pattern's settings, keeping the others. The file is read again under the lock first, so a save by another
    /// window since this one was read is kept. The write is atomic (<see cref="FileLock.WriteAtomic"/>). An unreadable file throws
    /// <see cref="StoreUnreadableException"/> and is not changed.
    /// </summary>
    public void Save(PatternSettings settings)
    {
        using var _ = FileLock.Hold(_path);
        var all = new Dictionary<string, PatternSettings>(Read(), StringComparer.Ordinal)
        {
            [settings.PatternKey] = settings,
        };
        FileLock.WriteAtomic(_path, JsonSerializer.Serialize(all, Options));
    }

    /// <summary>
    /// The read itself, without the lock. A missing file gives none. Any other failure to read or parse throws
    /// <see cref="StoreUnreadableException"/>, so a file that is locked or damaged is reported, never replaced.
    /// </summary>
    private Dictionary<string, PatternSettings> Read()
    {
        string text;
        try
        {
            text = File.ReadAllText(_path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new Dictionary<string, PatternSettings>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new StoreUnreadableException(_path, ex.Message);
        }

        Dictionary<string, PatternSettings>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, PatternSettings>>(text, Options);
        }
        catch (JsonException ex)
        {
            throw new StoreUnreadableException(_path, ex.Message);
        }

        // A null entry is no settings for its pattern, so it is dropped, as an absent one is.
        return (parsed ?? new Dictionary<string, PatternSettings>(StringComparer.Ordinal))
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }
}
