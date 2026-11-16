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

    /// <summary>All saved settings. A missing or unreadable file gives none, and the defaults are used.</summary>
    public IReadOnlyDictionary<string, PatternSettings> Load()
    {
        if (!File.Exists(_path)) return new Dictionary<string, PatternSettings>(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, PatternSettings>>(File.ReadAllText(_path), Options)
                   ?? new Dictionary<string, PatternSettings>(StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            return new Dictionary<string, PatternSettings>(StringComparer.Ordinal);
        }
    }

    /// <summary>The saved settings for a pattern, or its defaults.</summary>
    public PatternSettings For(string patternKey) =>
        Load().TryGetValue(patternKey, out var saved) ? saved : Defaults(patternKey);

    /// <summary>Writes one pattern's settings, keeping the others. The write is atomic, like the state file.</summary>
    public void Save(PatternSettings settings)
    {
        var all = new Dictionary<string, PatternSettings>(Load(), StringComparer.Ordinal)
        {
            [settings.PatternKey] = settings,
        };

        var directory = System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(all, Options));
        File.Move(temporary, _path, overwrite: true);
    }
}
