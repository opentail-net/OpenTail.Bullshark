using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenTail.Bullshark.Core.Codex;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>
/// Step R4: the launch line run for real, with a fake Codex PowerShell shim (<c>codex.ps1</c>) that records the arguments it receives. The launch runs
/// under PowerShell 7 (<c>pwsh.exe</c>), which is found on PATH and started by its full path. No codex program runs and no model is called. The recorder
/// is node when node is on PATH, and a PowerShell script otherwise. The test text is synthetic, and nothing the test records is printed.
/// A missing <c>pwsh.exe</c> fails the test: xUnit 2.x has no dynamic skip, and a pass would hide that the launch was not checked.
/// </summary>
public sealed class CodexStartLaunchTests
{
    /// <summary>The launch line starts with this word; the rest of it is the arguments, which are the same for the launch line's shell.</summary>
    private const string LineStart = "pwsh.exe ";

    /// <summary>
    /// The synthetic prompt: double and single quotes, a percent sign, cmd's special characters, a semicolon and a Unicode letter. It does not start
    /// with a dash, which codex would read as an option.
    /// </summary>
    private const string Prompt = "First line with \"double\" and 'single' quotes\nSecond line: %PATH% & ^ ! ; and é Ωμέγα\nThird line";

    /// <summary>The message the launcher writes when the shim is missing. It is compared, not printed.</summary>
    private const string NoShimMessage = "Codex PowerShell shim (codex.ps1) not found";

    private const string NoPwshMessage =
        "pwsh.exe (PowerShell 7) is not on PATH, so the launch cannot be checked. Install PowerShell 7 or add it to PATH, then run this test again.";

    [Fact]
    public void TheLaunchPassesTheTextUnchangedToCodex()
    {
        var pwsh = CodexStartLauncher.FindPwsh();
        if (pwsh is null)
        {
            Assert.Fail(NoPwshMessage);
            return;
        }

        var root = NewRoot();
        try
        {
            var recorder = WriteRecorder(root, pwsh);
            var shimFolder = Directory.CreateDirectory(Path.Combine(root, "ps")).FullName;
            File.WriteAllText(Path.Combine(shimFolder, "codex.ps1"), recorder + "\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var promptPath = CodexStartLauncher.WriteStart(Path.Combine(root, "starts"), Prompt, DateTimeOffset.Now);
            var argsOut = Path.Combine(root, "args.json");
            var run = Launch(pwsh, CodexStartLauncher.CommandLine(root, promptPath), shimFolder + ";" + OriginalPath(), argsOut, promptPath);

            var problems = new List<string>();
            AddProblems(problems, run);
            Assert.True(problems.Count == 0, string.Join("; ", problems));
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    [Fact]
    public void TheLaunchStopsWhenThereIsNoShimAndKeepsTheText()
    {
        var pwsh = CodexStartLauncher.FindPwsh();
        if (pwsh is null)
        {
            Assert.Fail(NoPwshMessage);
            return;
        }

        var root = NewRoot();
        try
        {
            // A codex.cmd is present but is not a PowerShell shim, and the PATH holds no npm folder, so no codex.ps1 can be found.
            var cmdFolder = Directory.CreateDirectory(Path.Combine(root, "cmd")).FullName;
            File.WriteAllText(Path.Combine(cmdFolder, "codex.cmd"), "@echo off\r\nrem not run by the launch\r\n", new UTF8Encoding(false));
            var systemFolder = Environment.GetFolderPath(Environment.SpecialFolder.System);

            var promptPath = CodexStartLauncher.WriteStart(Path.Combine(root, "starts"), Prompt, DateTimeOffset.Now);
            var run = Launch(
                pwsh,
                CodexStartLauncher.CommandLine(root, promptPath),
                cmdFolder + ";" + systemFolder,
                Path.Combine(root, "args.json"),
                promptPath);

            Assert.True(!run.TimedOut, "the launch did not end within 60 seconds");
            Assert.True(run.ExitCode == 1, $"the launch exited with code {run.ExitCode}, not 1");
            Assert.Contains(NoShimMessage, run.Output);
            Assert.True(File.Exists(promptPath), "the start text was removed although there was no shim to run it");
            File.Delete(promptPath);
        }
        finally
        {
            DeleteFolder(root);
        }
    }

    private static string NewRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "bullshark-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string OriginalPath() => Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

    /// <summary>
    /// Writes the recorder script in <paramref name="root"/> and returns the statement the shim runs: node when node is on PATH, otherwise a PowerShell
    /// script run by <paramref name="pwsh"/>. Both record the arguments as a JSON array in the file named by <c>BULLSHARK_ARGS_OUT</c>.
    /// </summary>
    private static string WriteRecorder(string root, string pwsh)
    {
        if (FindOnPath("node.exe") is not null)
        {
            var script = Path.Combine(root, "record.js");
            File.WriteAllText(
                script,
                "require('fs').writeFileSync(process.env.BULLSHARK_ARGS_OUT, JSON.stringify(process.argv.slice(2)), 'utf8');",
                new UTF8Encoding(false));
            return $"& node \"{script}\" @args";
        }

        // node is not on PATH: the recorder is written in PowerShell instead (the fallback the plan allows). It has not been run here, because node is on PATH.
        var psScript = Path.Combine(root, "record.ps1");
        File.WriteAllText(
            psScript,
            "[System.IO.File]::WriteAllText($env:BULLSHARK_ARGS_OUT, (ConvertTo-Json -InputObject @($args) -Compress), (New-Object System.Text.UTF8Encoding $false))",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return $"& \"{pwsh}\" -NoProfile -ExecutionPolicy Bypass -File \"{psScript}\" @args";
    }

    /// <summary>
    /// Runs the launch line with <paramref name="childPath"/> as the child's PATH and <c>BULLSHARK_ARGS_OUT</c> set to <paramref name="argsOut"/>. The
    /// line's shell is started by its full path, <paramref name="pwsh"/>. The output is kept for the checks that compare it; it is never printed.
    /// </summary>
    private static LaunchRun Launch(string pwsh, string commandLine, string childPath, string argsOut, string promptPath)
    {
        Assert.StartsWith(LineStart, commandLine);
        var psi = new ProcessStartInfo(pwsh, commandLine[LineStart.Length..])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.Environment["PATH"] = childPath;
        psi.Environment["BULLSHARK_ARGS_OUT"] = argsOut;

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("The launch did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60_000))
        {
            process.Kill(entireProcessTree: true);
            return new LaunchRun(ExitCode: -1, TimedOut: true, PromptPath: promptPath, ArgsOut: argsOut, Output: string.Empty);
        }
        var output = stdout.Wait(TimeSpan.FromSeconds(10)) ? stdout.Result : string.Empty;
        return new LaunchRun(process.ExitCode, TimedOut: false, PromptPath: promptPath, ArgsOut: argsOut, Output: output);
    }

    /// <summary>
    /// Adds a problem for the launch that did not pass: the process did not exit with 0, the shim did not receive exactly one argument equal to the
    /// text, or the start file is still there. Messages carry counts, lengths and positions only, never the text.
    /// </summary>
    private static void AddProblems(List<string> problems, LaunchRun run)
    {
        if (run.TimedOut)
        {
            problems.Add("the launch did not end within 60 seconds");
            return;
        }
        if (run.ExitCode != 0) problems.Add($"the launch exited with code {run.ExitCode}");
        if (!File.Exists(run.ArgsOut))
        {
            problems.Add("the shim recorded nothing");
            return;
        }

        var args = JsonSerializer.Deserialize<string[]>(File.ReadAllText(run.ArgsOut, Encoding.UTF8)) ?? Array.Empty<string>();
        if (args.Length != 1 || args[0] != Prompt)
        {
            var detail = args.Length == 1
                ? $"the argument has {args[0].Length} characters against {Prompt.Length}, and the first difference is at character {FirstDifference(args[0], Prompt)}"
                : $"{args.Length} arguments arrived";
            problems.Add($"one argument equal to the text was expected; {detail}");
        }

        if (File.Exists(run.PromptPath)) problems.Add("the start file was not removed");
    }

    private static int FirstDifference(string actual, string expected)
    {
        var shorter = Math.Min(actual.Length, expected.Length);
        for (var i = 0; i < shorter; i++)
        {
            if (actual[i] != expected[i]) return i;
        }
        return shorter;
    }

    /// <summary>The first file of that name on PATH, or null.</summary>
    private static string? FindOnPath(string fileName) =>
        OriginalPath()
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(dir => Path.Combine(dir, fileName))
            .FirstOrDefault(File.Exists);

    private static void DeleteFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>One launch: its exit code (-1 when it timed out), the files it used, and its standard output (compared, never printed).</summary>
    private sealed record LaunchRun(int ExitCode, bool TimedOut, string PromptPath, string ArgsOut, string Output);
}
