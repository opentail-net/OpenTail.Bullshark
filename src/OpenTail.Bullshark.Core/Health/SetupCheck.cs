using System.Text;
using System.Text.Json;

namespace OpenTail.Bullshark.Core.Health;

/// <summary>How one check came out. A warning is a setup that works but is missing something; a problem is one that will break a session.</summary>
public enum SetupState
{
    Ok,
    Warning,
    Problem,
}

/// <summary>One line of the setup check: the area it belongs to, what was checked, the result, and a short detail.</summary>
public sealed record SetupItem(string Area, string Name, SetupState State, string Detail);

/// <summary>
/// A read-only check of the Claude Code setup on this computer (docs/plans/setup-check.md). It reads settings.json, ~/.claude.json and
/// CLAUDE.md. For each command that a hook or a local MCP server starts with, it checks that the program exists, and that any script
/// named in its arguments exists. It never runs a hook or a server, never contacts a remote server, and writes nothing.
/// </summary>
public static class SetupCheck
{
    private const int MaxDetail = 160;

    public static string DefaultClaudeDir => OpenTail.Bullshark.Core.Instructions.ClaudeConfig.Dir;

    public static string DefaultClaudeJsonPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");

    /// <summary>
    /// The checks, in the order they are made. <paramref name="pathVariable"/> is the PATH that program names are searched on; the
    /// process's own PATH is used when it is null.
    /// </summary>
    public static IReadOnlyList<SetupItem> Run(string claudeDir, string claudeJsonPath, string? pathVariable = null)
    {
        var path = pathVariable ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var items = new List<SetupItem>();
        CheckSettings(Path.Combine(claudeDir, "settings.json"), path, items);
        CheckClaudeJson(claudeJsonPath, path, items);
        CheckInstructions(Path.Combine(claudeDir, "CLAUDE.md"), items);
        return items;
    }

    private static void CheckSettings(string file, string path, List<SetupItem> items)
    {
        const string area = "Settings";
        if (!File.Exists(file))
        {
            items.Add(new SetupItem(area, "settings.json", SetupState.Warning, "not found: Claude Code runs on its defaults"));
            return;
        }

        using var document = ReadJson(file, area, "settings.json", items);
        if (document is null) return;
        if (!IsObject(document.RootElement))
        {
            items.Add(new SetupItem(area, "settings.json", SetupState.Problem, "it is not a JSON object, so Claude Code cannot read its settings"));
            return;
        }
        items.Add(new SetupItem(area, "settings.json", SetupState.Ok, "reads as valid JSON"));

        var hooks = 0;
        if (document.RootElement.TryGetProperty("hooks", out var events) && events.ValueKind == JsonValueKind.Object)
        {
            foreach (var eventGroup in events.EnumerateObject())
            {
                if (eventGroup.Value.ValueKind != JsonValueKind.Array) continue;
                foreach (var group in eventGroup.Value.EnumerateArray())
                {
                    if (!IsObject(group))
                    {
                        items.Add(new SetupItem($"Hook {eventGroup.Name}", "entry", SetupState.Problem, "the entry is not an object"));
                        continue;
                    }
                    if (!group.TryGetProperty("hooks", out var commands) || commands.ValueKind != JsonValueKind.Array) continue;
                    foreach (var hook in commands.EnumerateArray())
                    {
                        if (!IsObject(hook))
                        {
                            items.Add(new SetupItem($"Hook {eventGroup.Name}", "entry", SetupState.Problem, "the hook is not an object"));
                            continue;
                        }
                        if (!hook.TryGetProperty("command", out var command) || command.ValueKind != JsonValueKind.String) continue;
                        CheckHook(eventGroup.Name, command.GetString() ?? string.Empty, path, items);
                        hooks++;
                    }
                }
            }
        }
        if (hooks == 0) items.Add(new SetupItem("Hooks", "no hooks", SetupState.Ok, "none configured in settings.json"));
    }

    private static void CheckClaudeJson(string file, string path, List<SetupItem> items)
    {
        const string area = "State";
        if (!File.Exists(file))
        {
            items.Add(new SetupItem(area, ".claude.json", SetupState.Warning, "not found: Claude Code has not run for this user"));
            return;
        }

        using var document = ReadJson(file, area, ".claude.json", items);
        if (document is null) return;
        if (!IsObject(document.RootElement))
        {
            items.Add(new SetupItem(area, ".claude.json", SetupState.Problem, "it is not a JSON object, so Claude Code cannot read its state"));
            return;
        }
        items.Add(new SetupItem(area, ".claude.json", SetupState.Ok, "reads as valid JSON"));

        var servers = 0;
        if (document.RootElement.TryGetProperty("mcpServers", out var userServers))
            servers += CheckServers("user", userServers, path, items);
        if (document.RootElement.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Object)
        {
            foreach (var project in projects.EnumerateObject())
            {
                if (IsObject(project.Value) && project.Value.TryGetProperty("mcpServers", out var projectServers))
                    servers += CheckServers(FolderName(project.Name), projectServers, path, items);
            }
        }
        if (servers == 0) items.Add(new SetupItem("MCP servers", "none", SetupState.Ok, "none configured"));
    }

    /// <summary>Checks each MCP server in one scope. A server with a URL is listed as remote and not contacted.</summary>
    private static int CheckServers(string scope, JsonElement servers, string path, List<SetupItem> items)
    {
        if (servers.ValueKind != JsonValueKind.Object) return 0;
        var count = 0;
        foreach (var server in servers.EnumerateObject())
        {
            count++;
            var area = $"MCP server ({scope})";
            var definition = server.Value;
            if (definition.ValueKind != JsonValueKind.Object)
            {
                items.Add(new SetupItem(area, server.Name, SetupState.Problem, "the entry is not an object"));
                continue;
            }
            if (definition.TryGetProperty("command", out var command) && command.ValueKind == JsonValueKind.String)
            {
                var arguments = new List<string>();
                if (definition.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Array)
                {
                    foreach (var argument in args.EnumerateArray())
                    {
                        if (argument.ValueKind == JsonValueKind.String) arguments.Add(argument.GetString() ?? string.Empty);
                    }
                }
                CheckProgram(area, server.Name, command.GetString() ?? string.Empty, arguments, path, items);
            }
            else if (definition.TryGetProperty("url", out var url) && url.ValueKind == JsonValueKind.String)
            {
                items.Add(new SetupItem(area, server.Name, SetupState.Ok, Clip($"remote, not contacted: {url.GetString()}")));
            }
            else
            {
                items.Add(new SetupItem(area, server.Name, SetupState.Problem, "no command and no url"));
            }
        }
        return count;
    }

    private static void CheckInstructions(string file, List<SetupItem> items)
    {
        items.Add(File.Exists(file)
            ? new SetupItem("Instructions", "CLAUDE.md", SetupState.Ok, "present")
            : new SetupItem("Instructions", "CLAUDE.md", SetupState.Warning, "not found: Claude has no standing instructions from you"));
    }

    private static void CheckHook(string eventName, string command, string path, List<SetupItem> items)
    {
        var area = $"Hook {eventName}";
        var parts = SplitCommand(command);
        if (parts.Count == 0)
        {
            items.Add(new SetupItem(area, "empty command", SetupState.Problem, "the hook names no program"));
            return;
        }
        CheckProgram(area, Clip(command, 80), parts[0], parts.Skip(1), path, items);
    }

    /// <summary>Checks that the program exists (a path, or a name on PATH), and that each script among the arguments exists.</summary>
    private static void CheckProgram(string area, string name, string program, IEnumerable<string> arguments, string path, List<SetupItem> items)
    {
        var expanded = Expand(program);
        if (expanded.Length == 0)
        {
            items.Add(new SetupItem(area, name, SetupState.Problem, "no program given"));
            return;
        }

        var problems = new List<string>();
        var notChecked = UsesVariable(expanded);
        if (!notChecked)
        {
            if (LooksLikePath(expanded))
            {
                if (!File.Exists(expanded)) problems.Add($"program not found: {expanded}");
            }
            else if (!OnPath(expanded, path))
            {
                problems.Add($"program not found on PATH: {expanded}");
            }
        }

        foreach (var argument in arguments)
        {
            var value = Expand(argument);
            if (IsAbsoluteScript(value) && !File.Exists(value)) problems.Add($"script not found: {value}");
        }

        items.Add(problems.Count == 0
            ? new SetupItem(area, name, SetupState.Ok, Clip(notChecked ? $"uses a variable; not checked: {expanded}" : $"found: {expanded}"))
            : new SetupItem(area, name, SetupState.Problem, Clip(string.Join("; ", problems))));
    }

    /// <summary>
    /// True when a program is named by a variable that is not set here: <c>$VAR</c>, <c>${VAR}</c>, or a <c>%VAR%</c> left as it is by
    /// <see cref="Expand"/>. Where it lives is not known, so it is not checked, and it is not reported as missing (a hook that works).
    /// </summary>
    private static bool UsesVariable(string value) =>
        value.StartsWith('$') || value.StartsWith('%') || value.Contains("${", StringComparison.Ordinal);

    /// <summary>
    /// True for a JSON object. TryGetProperty throws on any other kind, and the files are hand-edited: an array, a string or null where an
    /// object should be is reported as a problem, not allowed to stop the check.
    /// </summary>
    private static bool IsObject(JsonElement element) => element.ValueKind == JsonValueKind.Object;

    private static JsonDocument? ReadJson(string file, string area, string name, List<SetupItem> items)
    {
        try
        {
            return JsonDocument.Parse(File.ReadAllText(file));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            items.Add(new SetupItem(area, name, SetupState.Problem, Clip($"cannot be read: {ex.Message}")));
            return null;
        }
    }

    /// <summary>
    /// Splits a command line into its words. A word in double quotes keeps its spaces, and the quotes are not part of it. This is the
    /// simple rule a shell uses for a program path with spaces, not a full shell parser.
    /// </summary>
    private static List<string> SplitCommand(string command)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        var quoted = false;
        var started = false;
        foreach (var c in command)
        {
            if (c == '"')
            {
                quoted = !quoted;
                started = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (started) words.Add(word.ToString());
                word.Clear();
                started = false;
            }
            else
            {
                word.Append(c);
                started = true;
            }
        }
        if (started) words.Add(word.ToString());
        return words;
    }

    /// <summary>True when a name is found as a file in one of the PATH folders, with a Windows program extension when it has none.</summary>
    private static bool OnPath(string program, string pathVariable)
    {
        var names = new List<string>();
        if (Path.HasExtension(program))
        {
            names.Add(program);
        }
        else
        {
            var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var extension in extensions) names.Add(program + extension);
        }

        foreach (var folder in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = Expand(folder.Trim('"'));
            if (directory.Length == 0) continue;
            foreach (var name in names)
            {
                try
                {
                    if (File.Exists(Path.Combine(directory, name))) return true;
                }
                catch (ArgumentException)
                {
                    // A PATH entry with characters a path cannot hold is skipped.
                }
            }
        }
        return false;
    }

    private static bool LooksLikePath(string value) =>
        value.Contains('\\') || value.Contains('/') || value.Contains(':');

    /// <summary>An absolute path with a file extension and a folder part: the kind of argument that names a script.</summary>
    private static bool IsAbsoluteScript(string value) =>
        (value.Contains('\\') || value.Contains('/')) && Path.IsPathRooted(value) && Path.HasExtension(value);

    /// <summary>Expands %VAR% and a leading ~ to the user's folder.</summary>
    private static string Expand(string value)
    {
        var expanded = Environment.ExpandEnvironmentVariables(value.Trim());
        if (expanded.StartsWith("~\\", StringComparison.Ordinal) || expanded.StartsWith("~/", StringComparison.Ordinal))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), expanded[2..]);
        return expanded;
    }

    private static string FolderName(string projectPath)
    {
        var trimmed = projectPath.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        return name.Length == 0 ? projectPath : name;
    }

    private static string Clip(string text, int max = MaxDetail) =>
        text.Length <= max ? text : text[..max] + "…";
}
