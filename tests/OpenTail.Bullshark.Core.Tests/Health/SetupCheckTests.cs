using OpenTail.Bullshark.Core.Health;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Health;

/// <summary>The read-only setup check (docs/plans/setup-check.md). Files are written in temp folders; no hook or server is run.</summary>
public sealed class SetupCheckTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bullshark-setup-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _claude;
    private readonly string _json;

    public SetupCheckTests()
    {
        _claude = Path.Combine(_dir, ".claude");
        Directory.CreateDirectory(_claude);
        _json = Path.Combine(_dir, ".claude.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private void WriteSettings(string json) => File.WriteAllText(Path.Combine(_claude, "settings.json"), json);

    private void WriteClaudeJson(string json) => File.WriteAllText(_json, json);

    private void WriteInstructions() => File.WriteAllText(Path.Combine(_claude, "CLAUDE.md"), "rules");

    /// <summary>A file that exists, in the temp folder or in a named folder under it.</summary>
    private string Tool(string name, string? folder = null)
    {
        var dir = folder is null ? _dir : Path.Combine(_dir, folder);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, name);
        File.WriteAllText(file, string.Empty);
        return file;
    }

    /// <summary>A path as it goes into JSON: backslashes doubled.</summary>
    private static string Json(string path) => path.Replace("\\", "\\\\");

    private IReadOnlyList<SetupItem> Run(string path = "") => SetupCheck.Run(_claude, _json, path);

    [Fact]
    public void MissingFilesAreWarningsNotProblems()
    {
        var items = Run();

        Assert.Contains(items, i => i.Name == "settings.json" && i.State == SetupState.Warning);
        Assert.Contains(items, i => i.Name == ".claude.json" && i.State == SetupState.Warning);
        Assert.Contains(items, i => i.Name == "CLAUDE.md" && i.State == SetupState.Warning);
        Assert.DoesNotContain(items, i => i.State == SetupState.Problem);
    }

    [Fact]
    public void BrokenSettingsIsAProblem()
    {
        WriteSettings("{ not json");

        Assert.Contains(Run(), i => i.Name == "settings.json" && i.State == SetupState.Problem);
    }

    [Fact]
    public void ValidSettingsWithNoHooksIsOk()
    {
        WriteSettings("{\"model\":\"opus\"}");

        var items = Run();

        Assert.Contains(items, i => i.Name == "settings.json" && i.State == SetupState.Ok);
        Assert.Contains(items, i => i.Area == "Hooks" && i.Name == "no hooks" && i.State == SetupState.Ok);
    }

    [Fact]
    public void AHookWhoseProgramExistsIsOk()
    {
        var program = Tool("hook.cmd");
        WriteSettings($"{{\"hooks\":{{\"PreToolUse\":[{{\"matcher\":\"Bash\",\"hooks\":[{{\"type\":\"command\",\"command\":\"\\\"{Json(program)}\\\" --check\"}}]}}]}}}}");

        Assert.Contains(Run(), i => i.Area == "Hook PreToolUse" && i.State == SetupState.Ok);
    }

    [Fact]
    public void AHookWhoseScriptIsMissingIsAProblem()
    {
        var program = Tool("runner.exe");
        var script = Path.Combine(_dir, "missing.js");
        WriteSettings($"{{\"hooks\":{{\"PostToolUse\":[{{\"hooks\":[{{\"type\":\"command\",\"command\":\"\\\"{Json(program)}\\\" \\\"{Json(script)}\\\"\"}}]}}]}}}}");

        var hook = Assert.Single(Run(), i => i.Area == "Hook PostToolUse");
        Assert.Equal(SetupState.Problem, hook.State);
        Assert.Contains("script not found", hook.Detail);
    }

    [Fact]
    public void AProgramNamedOnThePathIsFoundThere()
    {
        Tool("mytool.exe", "bin");
        WriteSettings("{\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"mytool\"}]}]}}");

        Assert.Contains(Run(Path.Combine(_dir, "bin")), i => i.Area == "Hook Stop" && i.State == SetupState.Ok);
    }

    [Fact]
    public void AProgramNotOnThePathIsAProblem()
    {
        Tool("mytool.exe", "bin");
        WriteSettings("{\"hooks\":{\"Stop\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"no-such-tool\"}]}]}}");

        var hook = Assert.Single(Run(Path.Combine(_dir, "bin")), i => i.Area == "Hook Stop");
        Assert.Equal(SetupState.Problem, hook.State);
        Assert.Contains("not found on PATH", hook.Detail);
    }

    [Fact]
    public void AQuotedPathWithSpacesIsOneProgram()
    {
        var program = Tool("hook tool.cmd", "with space");
        WriteSettings($"{{\"hooks\":{{\"Stop\":[{{\"hooks\":[{{\"type\":\"command\",\"command\":\"\\\"{Json(program)}\\\"\"}}]}}]}}}}");

        Assert.Contains(Run(), i => i.Area == "Hook Stop" && i.State == SetupState.Ok);
    }

    [Fact]
    public void AnMcpServerWhoseProgramIsMissingIsAProblem()
    {
        WriteClaudeJson("{\"mcpServers\":{\"notion\":{\"command\":\"C:\\\\nope\\\\server.exe\"}}}");

        var server = Assert.Single(Run(), i => i.Name == "notion");
        Assert.Equal("MCP server (user)", server.Area);
        Assert.Equal(SetupState.Problem, server.State);
    }

    [Fact]
    public void ARemoteMcpServerIsListedAndNotContacted()
    {
        WriteClaudeJson("{\"mcpServers\":{\"remote\":{\"url\":\"https://example.invalid/mcp\"}}}");

        var server = Assert.Single(Run(), i => i.Name == "remote");
        Assert.Equal(SetupState.Ok, server.State);
        Assert.Contains("not contacted", server.Detail);
    }

    [Fact]
    public void AProjectsServersAreCheckedToo()
    {
        WriteClaudeJson("{\"projects\":{\"C:/git/app\":{\"mcpServers\":{\"x\":{\"command\":\"missing-x\"}}}}}");

        var server = Assert.Single(Run(), i => i.Name == "x");
        Assert.Equal("MCP server (app)", server.Area);
        Assert.Equal(SetupState.Problem, server.State);
    }

    [Fact]
    public void ABrokenClaudeJsonIsAProblem()
    {
        WriteClaudeJson("{");

        Assert.Contains(Run(), i => i.Name == ".claude.json" && i.State == SetupState.Problem);
    }

    [Fact]
    public void NoServersIsOk()
    {
        WriteClaudeJson("{}");

        Assert.Contains(Run(), i => i.Area == "MCP servers" && i.State == SetupState.Ok);
    }

    [Fact]
    public void AClaudeMdThatExistsIsOk()
    {
        WriteInstructions();

        Assert.Contains(Run(), i => i.Name == "CLAUDE.md" && i.State == SetupState.Ok);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    public void ASettingsFileThatIsNotAnObjectIsAProblemAndTheCheckGoesOn(string json)
    {
        WriteSettings(json);
        WriteClaudeJson("{}");
        WriteInstructions();

        var items = Run();

        Assert.Contains(items, i => i.Area == "Settings" && i.State == SetupState.Problem);
        Assert.Contains(items, i => i.Name == ".claude.json" && i.State == SetupState.Ok);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    public void AClaudeJsonThatIsNotAnObjectIsAProblem(string json)
    {
        WriteSettings("{}");
        WriteClaudeJson(json);
        WriteInstructions();

        var items = Run();

        Assert.Contains(items, i => i.Area == "State" && i.State == SetupState.Problem);
    }

    [Theory]
    [InlineData("{\"hooks\":\"text\"}")]
    [InlineData("{\"hooks\":[]}")]
    [InlineData("{\"hooks\":{\"PreToolUse\":\"text\"}}")]
    [InlineData("{\"hooks\":{\"PreToolUse\":[{\"hooks\":\"text\"}]}}")]
    public void HooksOfAnOddShapeDoNotStopTheCheck(string json)
    {
        WriteSettings(json);
        WriteClaudeJson("{\"projects\":[]}");
        WriteInstructions();

        var items = Run();

        Assert.Contains(items, i => i.Area == "Settings" && i.State == SetupState.Ok);
    }

    [Fact]
    public void AHookThatIsNotAnObjectIsAProblemAndTheOthersAreStillChecked()
    {
        var tool = Tool("hook.exe");
        WriteSettings($"{{\"hooks\":{{\"PreToolUse\":[\"text\",null,{{\"hooks\":[\"text\",{{\"command\":\"{Json(tool)}\"}}]}}]}}}}");
        WriteClaudeJson("{}");
        WriteInstructions();

        var items = Run();

        Assert.Equal(3, items.Count(i => i.Area == "Hook PreToolUse" && i.State == SetupState.Problem));
        Assert.Contains(items, i => i.Area == "Hook PreToolUse" && i.State == SetupState.Ok);
    }

    [Theory]
    [InlineData("$CLAUDE_PROJECT_DIR/hooks/x.sh")]
    [InlineData("${CLAUDE_PLUGIN_ROOT}/hooks/y.sh")]
    [InlineData("%CLAUDE_TEST_UNSET_VARIABLE%\\hooks\\z.exe")]
    public void AHookThatNamesAVariableIsNotCheckedAndNotReportedMissing(string command)
    {
        WriteSettings($"{{\"hooks\":{{\"PreToolUse\":[{{\"hooks\":[{{\"command\":\"{Json(command)}\"}}]}}]}}}}");
        WriteClaudeJson("{}");
        WriteInstructions();

        var hook = Assert.Single(Run(), i => i.Area == "Hook PreToolUse");

        Assert.Equal(SetupState.Ok, hook.State);
        Assert.Contains("uses a variable; not checked", hook.Detail);
    }

    [Fact]
    public void AHookThatNamesASetVariableIsExpandedAndChecked()
    {
        // %TEMP% is set, so the path is known and is checked: the program is not there, and that is a problem.
        WriteSettings($"{{\"hooks\":{{\"PreToolUse\":[{{\"hooks\":[{{\"command\":\"{Json("%TEMP%\\bullshark-missing-tool.exe")}\"}}]}}]}}}}");
        WriteClaudeJson("{}");
        WriteInstructions();

        var hook = Assert.Single(Run(), i => i.Area == "Hook PreToolUse");

        Assert.Equal(SetupState.Problem, hook.State);
        Assert.Contains("program not found", hook.Detail);
    }
}
