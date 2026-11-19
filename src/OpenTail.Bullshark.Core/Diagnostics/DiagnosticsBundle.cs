using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using OpenTail.Bullshark.Core.Health;
using OpenTail.Bullshark.Core.Supervisor;

namespace OpenTail.Bullshark.Core.Diagnostics;

/// <summary>One file of the diagnostics zip: its entry name and its text.</summary>
public sealed record BundleEntry(string Name, string Text);

/// <summary>
/// The texts the diagnostics zip is built from. The App fills <see cref="VersionsText"/>, <see cref="SetupText"/> and <see cref="PreflightText"/>;
/// <see cref="DiagnosticsSources"/> reads the three others from the state folder. Every text is redacted when the zip is built.
/// </summary>
public sealed record DiagnosticsInput(
    string VersionsText,
    string SetupText,
    string PreflightText,
    IReadOnlyList<string> AppLogLines,
    IReadOnlyList<string> EventLines,
    string SettingsText);

/// <summary>
/// The diagnostics zip for a bug report (firstrun batch B4): <c>diagnostics\diagnostics-yyyyMMdd-HHmmss.zip</c> beside the state file. Its entries are
/// versions.txt, setup-check.txt, preflight.txt, app.log (the last 500 lines), events.tail.txt (the last 200 events) and settings.txt (ui.json's
/// top-level keys with booleans, numbers and choice names, never the text of a value). Nothing else is read or written: never cache\, transcripts,
/// library.json or runs.json. Every line goes through <see cref="Redactor"/>. The newest 5 zips are kept. Nothing is sent anywhere.
/// </summary>
public static class DiagnosticsBundle
{
    /// <summary>The folder beside the state file that holds the zips.</summary>
    public const string FolderName = "diagnostics";

    /// <summary>The zips kept: the newest.</summary>
    public const int KeepNewest = 5;

    /// <summary>The file name pattern of a zip, for <see cref="KeptFiles"/>.</summary>
    public const string Pattern = "diagnostics-*.zip";

    /// <summary>The most app.log lines in the zip.</summary>
    public const int AppLogLinesKept = 500;

    /// <summary>The most events in the zip.</summary>
    public const int EventsKept = 200;

    /// <summary>The entries of the zip, in their order. The line lists are cut to their limits, then every line is redacted.</summary>
    public static IReadOnlyList<BundleEntry> Entries(DiagnosticsInput input, string userProfile)
    {
        ArgumentNullException.ThrowIfNull(input);
        var appLog = input.AppLogLines.Count > AppLogLinesKept ? input.AppLogLines.Skip(input.AppLogLines.Count - AppLogLinesKept) : input.AppLogLines;
        var events = input.EventLines.Count > EventsKept ? input.EventLines.Skip(input.EventLines.Count - EventsKept) : input.EventLines;
        return
        [
            new BundleEntry("versions.txt", Redactor.Text(input.VersionsText, userProfile)),
            new BundleEntry("setup-check.txt", Redactor.Text(input.SetupText, userProfile)),
            new BundleEntry("preflight.txt", Redactor.Text(input.PreflightText, userProfile)),
            new BundleEntry("app.log", Join(Redactor.Lines(appLog, userProfile))),
            new BundleEntry("events.tail.txt", Join(Redactor.Lines(events, userProfile))),
            new BundleEntry("settings.txt", Redactor.Text(input.SettingsText, userProfile)),
        ];
    }

    /// <summary>
    /// Writes the zip into <c>diagnostics</c> under <paramref name="stateFolder"/>, keeps the newest <see cref="KeepNewest"/>, and returns the zip's
    /// full path. The write throws the file errors; the caller says them on the status line.
    /// </summary>
    public static string Save(string stateFolder, DateTimeOffset now, DiagnosticsInput input, string userProfile, Action<string>? log = null)
    {
        var folder = Path.Combine(stateFolder, FolderName);
        Directory.CreateDirectory(folder);
        var name = KeptFiles.UniqueName(folder, $"diagnostics-{now.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.zip");
        var path = Path.Combine(folder, name);
        WriteZip(path, Entries(input, userProfile));
        KeptFiles.KeepNewest(folder, Pattern, KeepNewest, log);
        return path;
    }

    /// <summary>Writes the entries as a zip file, UTF-8 text, that must not exist yet.</summary>
    public static void WriteZip(string path, IReadOnlyList<BundleEntry> entries)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var entry in entries)
        {
            var archived = zip.CreateEntry(entry.Name, CompressionLevel.Optimal);
            using var writer = new StreamWriter(archived.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(entry.Text);
        }
    }

    /// <summary>The setup check's rows as text: one line each, area, name, state and detail.</summary>
    public static string SetupText(IReadOnlyList<SetupItem> items) =>
        Join(items.Select(item => $"{item.Area} | {item.Name} | {item.State} | {item.Detail}"));

    /// <summary>The preflight's verdict, then one line per row: name, status and text.</summary>
    public static string PreflightText(PreflightReport report) =>
        Join([report.Verdict, .. report.Rows.Select(row => $"{row.Name}: {row.Status} - {row.Text}")]);

    private static string Join(IEnumerable<string> lines) => string.Join(Environment.NewLine, lines);
}

/// <summary>
/// Reads the state folder for the diagnostics zip (firstrun batch B4). Read only, and only these files: app.log (its tail), events.jsonl (the last
/// events, the rotated file first) and ui.json (its keys). A file that cannot be read gives one line that says so, and nothing else is read.
/// </summary>
public static class DiagnosticsSources
{
    /// <summary>The choice names a ui.json string may hold as its value: the rail theme. Any other string is described by its length.</summary>
    private static readonly HashSet<string> ChoiceNames = new(["Dark", "Light"], StringComparer.Ordinal);

    /// <summary>A quiet-hours window such as 22:00-07:00: a time of day, not text.</summary>
    private static readonly Regex TimeWindow = new(@"^\d{1,2}:\d{2}-\d{1,2}:\d{2}$", RegexOptions.Compiled);

    /// <summary>The last <paramref name="count"/> lines of a text file. A missing file gives none; a file that cannot be read gives one line.</summary>
    public static IReadOnlyList<string> LastLines(string path, int count)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            var last = new Queue<string>(count);
            while (reader.ReadLine() is { } line)
            {
                if (last.Count == count) last.Dequeue();
                last.Enqueue(line);
            }
            return last.ToList();
        }
        catch (FileNotFoundException)
        {
            return [];
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [$"(could not be read: {ex.GetType().Name})"];
        }
    }

    /// <summary>The last <paramref name="count"/> events, one line each: time, event, session and detail.</summary>
    public static IReadOnlyList<string> EventLines(string eventsFile, int count)
    {
        try
        {
            var events = new EventLog(eventsFile).Read();
            return events
                .Skip(Math.Max(0, events.Count - count))
                .Select(evt => $"{evt.At.ToLocalTime():yyyy-MM-dd HH:mm:ss} {evt.Event} {evt.Session}"
                    + (string.IsNullOrEmpty(evt.Detail) ? string.Empty : $": {evt.Detail}"))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return [$"(could not be read: {ex.GetType().Name})"];
        }
    }

    /// <summary>
    /// ui.json as its top-level keys, one line each. A boolean, a number, null, a choice name (Dark, Light) and a time window show their value. Any
    /// other string shows its length only; an object or an array shows its count. So no note, path or label in ui.json reaches the zip.
    /// </summary>
    public static string SettingsText(string uiFile)
    {
        JsonNode? root;
        try
        {
            if (!File.Exists(uiFile)) return "(no ui.json)";
            using var stream = new FileStream(uiFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            root = JsonNode.Parse(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return $"(ui.json could not be read: {ex.GetType().Name})";
        }
        if (root is not JsonObject settings) return "(ui.json is not an object)";
        return string.Join(Environment.NewLine, settings.Select(pair => $"{pair.Key}: {Describe(pair.Value)}"));
    }

    private static string Describe(JsonNode? value)
    {
        switch (value)
        {
            case null:
                return "null";
            case JsonObject obj:
                return $"(object, {obj.Count} entries)";
            case JsonArray array:
                return $"(array, {array.Count} items)";
        }

        var json = value.ToJsonString();
        if (json is "true" or "false") return json;
        if (!json.StartsWith('"')) return json; // a number
        var text = value.GetValue<string>();
        return ChoiceNames.Contains(text) || TimeWindow.IsMatch(text) ? text : $"(text, {text.Length} characters)";
    }
}

/// <summary>The version line the owner reads in Settings (firstrun batch B5), and the split of the assembly's informational version.</summary>
public static class AppVersion
{
    /// <summary>
    /// "0.1.0+abc1234" gives the version "0.1.0" and the hash "abc1234". A text without a "+" has no hash. A missing text gives "unknown".
    /// </summary>
    public static (string Version, string? Hash) Split(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational)) return ("unknown", null);
        var text = informational.Trim();
        var plus = text.IndexOf('+');
        if (plus < 0) return (text, null);
        var hash = text[(plus + 1)..];
        return (text[..plus], hash.Length == 0 ? null : hash);
    }

    /// <summary>
    /// "Version 0.1.0 (abc1234) · .NET 10 · Claude Code 2.1.326". The hash is left out when there is none, and the Claude Code part when its version
    /// is not known (it is not started for this line).
    /// </summary>
    public static string Line(string version, string? hash, int dotnetMajor, string? claudeVersion)
    {
        var line = $"Version {version}{(hash is null ? string.Empty : $" ({hash})")} · .NET {dotnetMajor}";
        return claudeVersion is null ? line : $"{line} · Claude Code {claudeVersion}";
    }
}
