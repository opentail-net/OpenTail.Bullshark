using System.Text.Json;
using System.Text.Json.Nodes;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Behaviour;

/// <summary>
/// What loading behaviour.json gave. <see cref="Settings"/> holds the values in force: the file's value for each key it has and can use, and the default
/// for the rest. <see cref="Problem"/> is the exception type name when the file could not be read or parsed ("NotAnObject" when its root is not an
/// object), and null otherwise. <see cref="Ignored"/> counts the values that were present but not usable. Only these counts and the type name are logged;
/// the content never is.
/// </summary>
public sealed record BehaviourLoad(BehaviourSettings Settings, string? Problem, int Ignored);

/// <summary>
/// Reads <c>behaviour.json</c>, the one file of the behaviour settings (decisions of 2026-11-22). It sits in the folder of the state file. A missing file
/// gives the defaults. A value that is absent, of the wrong type, or outside its range gives the default for that key, and the others are kept. An
/// unreadable or malformed file gives the defaults for everything. The store never writes the file and never throws.
/// </summary>
public static class BehaviourStore
{
    /// <summary>The file name, beside the state file.</summary>
    public const string FileName = "behaviour.json";

    /// <summary>The longest re-alert interval in minutes. A larger value is not usable, so its default applies (the ui.json escalation clamps at the same bound).</summary>
    private const int MaxRepeatMinutes = 1440;

    private static readonly IReadOnlyDictionary<string, FocusGate> FocusGates = new Dictionary<string, FocusGate>(StringComparer.Ordinal)
    {
        ["unfocused"] = FocusGate.Unfocused,
        ["unfocusedOrUnselected"] = FocusGate.UnfocusedOrUnselected,
    };

    private static readonly IReadOnlyDictionary<string, AlertGroup> KindNames = new Dictionary<string, AlertGroup>(StringComparer.Ordinal)
    {
        ["needsInput"] = AlertGroup.NeedsInput,
        ["errored"] = AlertGroup.Errored,
        ["finished"] = AlertGroup.Finished,
    };

    private static readonly IReadOnlyDictionary<string, UnreadableVersion> UnreadableNames = new Dictionary<string, UnreadableVersion>(StringComparer.Ordinal)
    {
        ["warn"] = UnreadableVersion.Warn,
        ["refuse"] = UnreadableVersion.Refuse,
    };

    private static readonly IReadOnlyDictionary<string, AmbiguousResume> AmbiguousNames = new Dictionary<string, AmbiguousResume>(StringComparer.Ordinal)
    {
        ["refuse"] = AmbiguousResume.Refuse,
        ["earliest"] = AmbiguousResume.Earliest,
    };

    private static readonly IReadOnlyDictionary<string, IndexStamp> StampNames = new Dictionary<string, IndexStamp>(StringComparer.Ordinal)
    {
        ["countAndNewest"] = IndexStamp.CountAndNewest,
    };

    private static readonly IReadOnlyDictionary<string, UsageAttribution> AttributionNames = new Dictionary<string, UsageAttribution>(StringComparer.Ordinal)
    {
        ["lastUpdate"] = UsageAttribution.LastUpdate,
        ["start"] = UsageAttribution.Start,
        ["event"] = UsageAttribution.Event,
    };

    /// <summary>The path of behaviour.json: the folder of <paramref name="stateFile"/>, with the file name.</summary>
    public static string PathBeside(string stateFile) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(stateFile)) ?? ".", FileName);

    /// <summary>
    /// Loads behaviour.json beside <paramref name="stateFile"/>. <paramref name="legacyRepeat"/> is the ui.json <c>escalateMinutes</c> value, or null when
    /// the key is absent: the Settings page writes that key, so it still sets the re-alert interval until behaviour.json has <c>alerts.repeatMinutes</c>.
    /// With neither, the interval is zero (never re-alert).
    /// </summary>
    public static BehaviourLoad LoadBeside(string stateFile, JsonNode? legacyRepeat = null)
    {
        // A missing file is the normal case (every pass asks), so it is checked first, and no exception is thrown for it.
        string text;
        try
        {
            var path = PathBeside(stateFile);
            if (!File.Exists(path)) return Parse(null, legacyRepeat);
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            return new BehaviourLoad(Parse(null, legacyRepeat).Settings, ex.GetType().Name, 0);
        }
        return Parse(text, legacyRepeat);
    }

    /// <summary>
    /// The settings in a behaviour.json text (<paramref name="text"/>; null or blank gives the defaults). Never throws. Comments and trailing commas are
    /// accepted. <paramref name="legacyRepeat"/> is as for <see cref="LoadBeside"/>.
    /// </summary>
    public static BehaviourLoad Parse(string? text, JsonNode? legacyRepeat = null)
    {
        var repeatDefault = legacyRepeat is null ? BehaviourSettings.Defaults.Alerts.RepeatMinutes : EscalationChoices.MinutesOf(legacyRepeat);
        JsonObject? root = null;
        string? problem = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                var node = JsonNode.Parse(text, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (node is JsonObject obj) root = obj;
                else problem = "NotAnObject";
            }
            catch (Exception ex)
            {
                problem = ex.GetType().Name;
            }
        }

        var reader = new Reader();
        var settings = Read(root, reader, repeatDefault);
        return new BehaviourLoad(settings, problem, reader.Ignored);
    }

    /// <summary>Reads every section. Each key takes its default from <see cref="BehaviourSettings.Defaults"/>, so a default is written once.</summary>
    private static BehaviourSettings Read(JsonObject? root, Reader r, int repeatDefault)
    {
        var d = BehaviourSettings.Defaults;
        var alerts = r.Section(root, "alerts");
        var lookups = r.Section(root, "lookups");
        var versions = r.Section(root, "versions");
        var minimum = r.Section(versions, "minimum");
        var exit = r.Section(root, "exit");
        var resume = r.Section(root, "resume");
        var state = r.Section(root, "state");
        var index = r.Section(root, "index");
        var usage = r.Section(root, "usage");

        return new BehaviourSettings
        {
            Alerts = new AlertsSettings
            {
                RepeatMinutes = r.Int(alerts, "repeatMinutes", repeatDefault, 0, MaxRepeatMinutes),
                FocusGate = r.Choice(alerts, "focusGate", d.Alerts.FocusGate, FocusGates),
                Kinds = r.Kinds(alerts, "kinds", d.Alerts.Kinds),
            },
            Lookups = new LookupsSettings
            {
                MissMemorySeconds = r.Int(lookups, "missMemorySeconds", d.Lookups.MissMemorySeconds, 0, int.MaxValue),
            },
            Versions = new VersionsSettings
            {
                Minimum = new MinimumVersions
                {
                    Claude = r.Version(minimum, "claude", d.Versions.Minimum.Claude),
                    Codex = r.Version(minimum, "codex", d.Versions.Minimum.Codex),
                },
                Unreadable = r.Choice(versions, "unreadable", d.Versions.Unreadable, UnreadableNames),
            },
            Exit = new ExitSettings
            {
                EndOnProcessExit = r.Bool(exit, "endOnProcessExit", d.Exit.EndOnProcessExit),
            },
            Resume = new ResumeSettings
            {
                AllowNewestInFolder = r.Bool(resume, "allowNewestInFolder", d.Resume.AllowNewestInFolder),
                Ambiguous = r.Choice(resume, "ambiguous", d.Resume.Ambiguous, AmbiguousNames),
            },
            State = new StateSettings
            {
                WorkingDecayMinutes = r.Int(state, "workingDecayMinutes", d.State.WorkingDecayMinutes, 0, int.MaxValue),
                ClampClockBackwards = r.Bool(state, "clampClockBackwards", d.State.ClampClockBackwards),
            },
            Index = new IndexSettings
            {
                RefreshSeconds = r.Int(index, "refreshSeconds", d.Index.RefreshSeconds, 0, int.MaxValue),
                Stamp = r.Choice(index, "stamp", d.Index.Stamp, StampNames),
            },
            Usage = new UsageSettings
            {
                Attribution = r.Choice(usage, "attribution", d.Usage.Attribution, AttributionNames),
                ReasoningAsOutput = r.Bool(usage, "reasoningAsOutput", d.Usage.ReasoningAsOutput),
            },
        };
    }

    /// <summary>Reads one kind of value from a section, and counts a value that is present but not usable.</summary>
    private sealed class Reader
    {
        /// <summary>The values that were present but not usable, so their defaults apply.</summary>
        public int Ignored { get; private set; }

        /// <summary>The object under <paramref name="name"/>. Null when it is absent, or when it is present but not an object (counted).</summary>
        public JsonObject? Section(JsonObject? parent, string name)
        {
            var node = Node(parent, name);
            if (node is null) return null;
            if (node is JsonObject obj) return obj;
            Ignored++;
            return null;
        }

        public int Int(JsonObject? section, string key, int fallback, int min, int max)
        {
            var node = Node(section, key);
            if (node is null) return fallback;
            if (node is JsonValue value && value.TryGetValue<int>(out var number) && number >= min && number <= max) return number;
            Ignored++;
            return fallback;
        }

        public bool Bool(JsonObject? section, string key, bool fallback)
        {
            var node = Node(section, key);
            if (node is null) return fallback;
            if (node is JsonValue value && value.TryGetValue<bool>(out var flag)) return flag;
            Ignored++;
            return fallback;
        }

        public T Choice<T>(JsonObject? section, string key, T fallback, IReadOnlyDictionary<string, T> names) where T : struct
        {
            var node = Node(section, key);
            if (node is null) return fallback;
            if (node is JsonValue value && value.TryGetValue<string>(out var text) && names.TryGetValue(text, out var chosen)) return chosen;
            Ignored++;
            return fallback;
        }

        /// <summary>A list of kinds. An empty list is valid (no kind alerts). An unknown name makes the whole list unusable, so the default applies.</summary>
        public IReadOnlySet<AlertGroup> Kinds(JsonObject? section, string key, IReadOnlySet<AlertGroup> fallback)
        {
            var node = Node(section, key);
            if (node is null) return fallback;
            if (node is JsonArray items)
            {
                var kinds = new HashSet<AlertGroup>();
                var usable = true;
                foreach (var item in items)
                {
                    if (item is JsonValue value && value.TryGetValue<string>(out var text) && KindNames.TryGetValue(text, out var kind)) kinds.Add(kind);
                    else
                    {
                        usable = false;
                        break;
                    }
                }
                if (usable) return kinds;
            }
            Ignored++;
            return fallback;
        }

        /// <summary>A dotted version such as "2.1.330": digits in dot-separated parts. Anything else is not usable.</summary>
        public string Version(JsonObject? section, string key, string fallback)
        {
            var node = Node(section, key);
            if (node is null) return fallback;
            if (node is JsonValue value && value.TryGetValue<string>(out var text) && IsDottedNumber(text)) return text;
            Ignored++;
            return fallback;
        }

        private static JsonNode? Node(JsonObject? section, string key) =>
            section is not null && section.TryGetPropertyValue(key, out var node) ? node : null;

        private static bool IsDottedNumber(string text) =>
            text.Length is > 0 and <= 32 && text.Split('.').All(part => part.Length > 0 && part.All(char.IsAsciiDigit));
    }
}
