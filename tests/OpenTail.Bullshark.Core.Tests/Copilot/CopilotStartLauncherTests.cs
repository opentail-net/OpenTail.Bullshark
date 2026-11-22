using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OpenTail.Bullshark.Core.Copilot;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Copilot;

/// <summary>
/// Stage G1.7: the start file, the launch line, the PowerShell 7 lookup, the cleanup of old start files, and the launch run for real with a fake
/// <c>copilot.ps1</c> that records its arguments (the deferred check P-C8). No copilot program runs and no model is called. The test text is
/// synthetic, and nothing the test records is printed.
/// </summary>
public sealed class CopilotStartLauncherTests
{
    private const string Prefix = "pwsh.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand ";

    private const string LineStart = "pwsh.exe ";

    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 30, 15, TimeSpan.FromHours(2));

    /// <summary>
    /// The synthetic prompt: double and single quotes, a percent sign, cmd's special characters, a semicolon and Unicode letters. It does not start
    /// with a dash, which copilot would read as an option.
    /// </summary>
    private const string Prompt = "First line with \"double\" and 'single' quotes\nSecond line: %PATH% & ^ ! ; and é Ωμέγα\nThird line";

    private const string NoShimMessage = "Copilot PowerShell shim (copilot.ps1) not found";

    private const string NoPwshMessage =
        "pwsh.exe (PowerShell 7) is not on PATH, so the launch cannot be checked. Install PowerShell 7 or add it to PATH, then run this test again.";

    /// <summary>
    /// The script the launcher is expected to build, with the paths already quoted for PowerShell. Line 0 is the error preference, lines 1 and 2 the
    /// shim check, lines 3 to 5 the prompt and the folder, and line 6 the call.
    /// </summary>
    private static string ExpectedScript(string folderQuoted, string promptQuoted) => string.Join(
        "\n",
        "$ErrorActionPreference = 'Stop'",
        "$shim = Get-Command copilot -CommandType ExternalScript -ErrorAction SilentlyContinue | Select-Object -First 1",
        "if ($null -eq $shim) { Write-Output 'Copilot PowerShell shim (copilot.ps1) not found. The start text is kept in: " + promptQuoted + "'; exit 1 }",
        "$prompt = [System.IO.File]::ReadAllText('" + promptQuoted + "', [System.Text.Encoding]::UTF8)",
        "Remove-Item -LiteralPath '" + promptQuoted + "' -Force",
        "Set-Location -LiteralPath '" + folderQuoted + "'",
        "& $shim.Source -i $prompt --sync-session=off");

    /// <summary>The script inside a command line: the base64 after the fixed prefix, decoded as UTF-16LE.</summary>
    private static string DecodeScript(string commandLine)
    {
        Assert.StartsWith(Prefix, commandLine);
        return Encoding.Unicode.GetString(Convert.FromBase64String(commandLine[Prefix.Length..]));
    }

    [Fact]
    public void TheEncodedScriptDecodesToTheExpectedScriptWithTheGivenPaths()
    {
        var line = CopilotStartLauncher.CommandLine("C:\\Fixture\\Synthetic Folder", "C:\\Fixture\\Starts\\prompt.txt");

        Assert.Equal(
            ExpectedScript("C:\\Fixture\\Synthetic Folder", "C:\\Fixture\\Starts\\prompt.txt"),
            DecodeScript(line));
    }

    [Fact]
    public void TheScriptChecksForTheShimThenReadsDeletesAndRunsWithTextAndSyncOff()
    {
        var script = DecodeScript(CopilotStartLauncher.CommandLine("C:\\Fixture", "C:\\Fixture\\Starts\\prompt.txt")).Split('\n');

        Assert.Equal(7, script.Length);
        Assert.Equal("$shim = Get-Command copilot -CommandType ExternalScript -ErrorAction SilentlyContinue | Select-Object -First 1", script[1]);
        Assert.StartsWith("if ($null -eq $shim) { Write-Output 'Copilot PowerShell shim (copilot.ps1) not found. The start text is kept in: C:\\Fixture\\Starts\\prompt.txt'; exit 1 }", script[2]);
        Assert.StartsWith("$prompt = ", script[3]);
        Assert.Equal("& $shim.Source -i $prompt --sync-session=off", script[6]);
    }

    [Fact]
    public void EveryCopilotLaunchScriptCarriesSyncOff()
    {
        var script = DecodeScript(CopilotStartLauncher.CommandLine("C:\\Fixture", "C:\\Fixture\\Starts\\prompt.txt"));

        Assert.Contains("--sync-session=off", script);
    }

    [Fact]
    public void TheLineHasTheFixedPrefixAndItsBase64HasNoSpaceAndNoCmdSpecialCharacter()
    {
        var line = CopilotStartLauncher.CommandLine("C:\\Fixture\\Synthetic Folder", "C:\\Fixture\\Starts\\prompt.txt");

        Assert.StartsWith(Prefix, line);
        Assert.Matches("^[A-Za-z0-9+/=]+$", line[Prefix.Length..]);
        Assert.DoesNotContain(line, c => "\"&|<>^%!".Contains(c));
    }

    [Theory]
    [InlineData("C:\\Fixture\\With Spaces", "C:\\Fixture\\With Spaces")]
    [InlineData("C:\\Fixture\\O'Brien", "C:\\Fixture\\O''Brien")]
    [InlineData("C:\\Fixture\\100%", "C:\\Fixture\\100%")]
    [InlineData("C:\\Fixture\\R&D", "C:\\Fixture\\R&D")]
    [InlineData("C:\\Fixture\\$HOME", "C:\\Fixture\\$HOME")]
    [InlineData("C:\\Fixture\\Hi!", "C:\\Fixture\\Hi!")]
    [InlineData("C:\\Fixture\\\"quoted\"", "C:\\Fixture\\\"quoted\"")]
    [InlineData("C:\\Fixture\\''", "C:\\Fixture\\''''")]
    public void APathWithSpecialCharactersIsCarriedAndItsSingleQuotesAreDoubled(string folder, string folderQuoted)
    {
        var script = DecodeScript(CopilotStartLauncher.CommandLine(folder, "C:\\Fixture\\Starts\\prompt.txt")).Split('\n');

        Assert.Equal("Set-Location -LiteralPath '" + folderQuoted + "'", script[5]);
    }

    [Fact]
    public void TheSingleQuotesOfThePromptPathAreDoubledOnEveryLineThatHoldsIt()
    {
        var script = DecodeScript(CopilotStartLauncher.CommandLine("C:\\Fixture", "C:\\Fixture\\O'Brien\\prompt.txt")).Split('\n');

        Assert.Equal("$prompt = [System.IO.File]::ReadAllText('C:\\Fixture\\O''Brien\\prompt.txt', [System.Text.Encoding]::UTF8)", script[3]);
        Assert.Equal("Remove-Item -LiteralPath 'C:\\Fixture\\O''Brien\\prompt.txt' -Force", script[4]);
        Assert.Contains("kept in: C:\\Fixture\\O''Brien\\prompt.txt'; exit 1 }", script[2]);
    }

    [Fact]
    public void TheTypographicSingleQuotesAreDoubledToo()
    {
        foreach (var quote in new[] { 0x2018, 0x2019, 0x201A, 0x201B })
        {
            var q = (char)quote;
            var script = DecodeScript(CopilotStartLauncher.CommandLine("C:\\Fixture\\O" + q + "Brien", "C:\\Fixture\\Starts\\prompt.txt")).Split('\n');

            Assert.Equal("Set-Location -LiteralPath 'C:\\Fixture\\O" + q + q + "Brien'", script[5]);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\Fixture\\a\nb")]
    [InlineData("C:\\Fixture\\a\rb")]
    [InlineData("C:\\Fixture\\a\tb")]
    [InlineData("C:\\Fixture\\a\0b")]
    [InlineData("C:\\Fixture\\a\u2028b")]
    public void AFolderThatIsBlankOrHasAControlOrLineBreakIsRefused(string folder)
    {
        Assert.Throws<ArgumentException>(() => CopilotStartLauncher.CommandLine(folder, "C:\\Fixture\\Starts\\prompt.txt"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("C:\\Fixture\\prompt\n.txt")]
    [InlineData("C:\\Fixture\\prompt\u0007.txt")]
    public void APromptPathThatIsBlankOrHasAControlCharacterIsRefused(string promptPath)
    {
        Assert.Throws<ArgumentException>(() => CopilotStartLauncher.CommandLine("C:\\Fixture", promptPath));
    }

    [Fact]
    public void WriteStartCreatesTheFolderAndWritesTheTextAsUtf8WithoutAByteOrderMark()
    {
        using var temp = new TempFolder();
        var folder = Path.Combine(temp.Folder, "Starts");
        const string text = "Plain text, a letter é, Greek Ωμέγα, a new\nline, a \"double\" quote, and 100% sure.";

        var path = CopilotStartLauncher.WriteStart(folder, text, Now);

        Assert.Equal(Path.GetFullPath(folder), Path.GetDirectoryName(path));
        Assert.Matches(@"^[0-9]{8}-[0-9]{6}-[0-9a-f]{8}\.txt$", Path.GetFileName(path));
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "a byte-order mark was written");
        Assert.Equal(text, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void TwoStartsMadeInTheSameInstantAreTwoFilesAndNeitherIsOverwritten()
    {
        using var temp = new TempFolder();

        var first = CopilotStartLauncher.WriteStart(temp.Folder, "first text", Now);
        var second = CopilotStartLauncher.WriteStart(temp.Folder, "second text", Now);

        Assert.NotEqual(first, second);
        Assert.Equal("first text", File.ReadAllText(first, Encoding.UTF8));
        Assert.Equal("second text", File.ReadAllText(second, Encoding.UTF8));
    }

    [Fact]
    public void RemoveOldStartsRemovesOnlyOldStartFilesAndReturnsTheCount()
    {
        using var temp = new TempFolder();
        var oldStart = WriteFile(temp.Folder, "20261119-100000-0a1b2c3d.txt", Now.AddDays(-3));
        var newStart = WriteFile(temp.Folder, "20261122-110000-1f2e3d4c.txt", Now.AddHours(-1));
        var oldJson = WriteFile(temp.Folder, "20261119-100000-0a1b2c3d.json", Now.AddDays(-3));
        var oldNotes = WriteFile(temp.Folder, "notes.txt", Now.AddDays(-3));
        var oldUpperHex = WriteFile(temp.Folder, "20261119-100000-9F8E7D6C.txt", Now.AddDays(-3));

        var removed = CopilotStartLauncher.RemoveOldStarts(temp.Folder, TimeSpan.FromDays(1), Now);

        Assert.Equal(1, removed);
        Assert.False(File.Exists(oldStart));
        Assert.True(File.Exists(newStart));
        Assert.True(File.Exists(oldJson));
        Assert.True(File.Exists(oldNotes));
        Assert.True(File.Exists(oldUpperHex));
    }

    [Fact]
    public void RemoveOldStartsOfAMissingFolderIsZero()
    {
        using var temp = new TempFolder();

        Assert.Equal(0, CopilotStartLauncher.RemoveOldStarts(Path.Combine(temp.Folder, "missing"), TimeSpan.Zero, Now));
    }

    [Fact]
    public void TheStartsFolderIsNamedStartsAndSitsBesideTheStateFile()
    {
        var state = Path.Combine(Path.GetTempPath(), "bullshark-state", "state.json");

        var folder = CopilotStartLauncher.StartsFolderBeside(state);

        Assert.Equal("Starts", Path.GetFileName(folder));
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(state)), Path.GetDirectoryName(folder));
    }

    [Fact]
    public void FindPwshReturnsTheFullPathOfPwshExeInTheFirstFolderThatHasIt()
    {
        using var temp = new TempFolder();
        var first = Directory.CreateDirectory(Path.Combine(temp.Folder, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(temp.Folder, "second")).FullName;
        File.WriteAllText(Path.Combine(second, "pwsh.exe"), string.Empty);
        var searchPath = first + Path.PathSeparator + "C:\\no such folder" + Path.PathSeparator + second;

        Assert.Equal(Path.Combine(second, "pwsh.exe"), CopilotStartLauncher.FindPwsh(searchPath));
    }

    [Fact]
    public void FindPwshIsNullWhenPwshIsNotInTheFoldersOrThePathIsEmpty()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Folder);

        Assert.Null(CopilotStartLauncher.FindPwsh(temp.Folder));
        Assert.Null(CopilotStartLauncher.FindPwsh(string.Empty));
        Assert.Null(CopilotStartLauncher.FindPwsh(null));
    }

    [Fact]
    public void FindPwshFindsPowerShell7OnThisMachinesPath()
    {
        // The launch test needs PowerShell 7 on the PATH of this process, so a missing pwsh.exe is a failure of the environment, not a pass.
        Assert.NotNull(CopilotStartLauncher.FindPwsh());
    }

    [Fact]
    public void TheLaunchPassesTheTextUnchangedToCopilotWithSyncOffAndRemovesTheStartFile()
    {
        var pwsh = CopilotStartLauncher.FindPwsh();
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
            File.WriteAllText(Path.Combine(shimFolder, "copilot.ps1"), recorder + "\r\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var promptPath = CopilotStartLauncher.WriteStart(Path.Combine(root, "starts"), Prompt, DateTimeOffset.Now);
            var argsOut = Path.Combine(root, "args.json");
            var run = Launch(pwsh, CopilotStartLauncher.CommandLine(root, promptPath), shimFolder + ";" + OriginalPath(), argsOut, promptPath);

            var problems = new List<string>();
            if (run.TimedOut) problems.Add("the launch did not end within 60 seconds");
            else
            {
                if (run.ExitCode != 0) problems.Add($"the launch exited with code {run.ExitCode}");
                if (!File.Exists(argsOut)) problems.Add("the shim recorded nothing");
                else
                {
                    var args = JsonSerializer.Deserialize<string[]>(File.ReadAllText(argsOut, Encoding.UTF8)) ?? Array.Empty<string>();
                    if (args.Length != 3 || args[0] != "-i" || args[1] != Prompt || args[2] != "--sync-session=off")
                        problems.Add($"expected -i, the text and --sync-session=off; {args.Length} arguments arrived");
                }
                if (File.Exists(promptPath)) problems.Add("the start file was not removed");
            }
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
        var pwsh = CopilotStartLauncher.FindPwsh();
        if (pwsh is null)
        {
            Assert.Fail(NoPwshMessage);
            return;
        }

        var root = NewRoot();
        try
        {
            // A copilot.cmd is present but is not a PowerShell shim, and the PATH holds no folder with a copilot.ps1, so none can be found.
            var cmdFolder = Directory.CreateDirectory(Path.Combine(root, "cmd")).FullName;
            File.WriteAllText(Path.Combine(cmdFolder, "copilot.cmd"), "@echo off\r\nrem not run by the launch\r\n", new UTF8Encoding(false));
            var systemFolder = Environment.GetFolderPath(Environment.SpecialFolder.System);

            var promptPath = CopilotStartLauncher.WriteStart(Path.Combine(root, "starts"), Prompt, DateTimeOffset.Now);
            var run = Launch(pwsh, CopilotStartLauncher.CommandLine(root, promptPath), cmdFolder + ";" + systemFolder, Path.Combine(root, "args.json"), promptPath);

            Assert.False(run.TimedOut, "the launch did not end within 60 seconds");
            Assert.Equal(1, run.ExitCode);
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
        var root = Path.Combine(Path.GetTempPath(), "bullshark-copilot-launch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string OriginalPath() => Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

    /// <summary>
    /// Writes the recorder and returns the statement the shim runs: node when node is on PATH, otherwise a PowerShell script run by
    /// <paramref name="pwsh"/>. Both record the arguments as a JSON array in the file named by <c>BULLSHARK_ARGS_OUT</c>.
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

        var psScript = Path.Combine(root, "record.ps1");
        File.WriteAllText(
            psScript,
            "[System.IO.File]::WriteAllText($env:BULLSHARK_ARGS_OUT, (ConvertTo-Json -InputObject @($args) -Compress), (New-Object System.Text.UTF8Encoding $false))",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return $"& \"{pwsh}\" -NoProfile -ExecutionPolicy Bypass -File \"{psScript}\" @args";
    }

    /// <summary>
    /// Runs the launch line with <paramref name="childPath"/> as the child's PATH and <c>BULLSHARK_ARGS_OUT</c> set to <paramref name="argsOut"/>. The
    /// output is kept for the checks that compare it; it is never printed.
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
            return new LaunchRun(ExitCode: -1, TimedOut: true, Output: string.Empty);
        }
        var output = stdout.Wait(TimeSpan.FromSeconds(10)) ? stdout.Result : string.Empty;
        return new LaunchRun(process.ExitCode, TimedOut: false, Output: output);
    }

    /// <summary>The first file of that name on PATH, or null.</summary>
    private static string? FindOnPath(string fileName) =>
        OriginalPath()
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(dir => Path.Combine(dir, fileName))
            .FirstOrDefault(File.Exists);

    private static string WriteFile(string folder, string name, DateTimeOffset written)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, "synthetic");
        File.SetLastWriteTimeUtc(path, written.UtcDateTime);
        return path;
    }

    private static void DeleteFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    /// <summary>A temp folder for one test. It is deleted when the test ends.</summary>
    private sealed class TempFolder : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "bullshark-copilot-starts-" + Guid.NewGuid().ToString("N"));

        public void Dispose() => DeleteFolder(Folder);
    }

    /// <summary>One launch: its exit code (-1 when it timed out) and its standard output (compared, never printed).</summary>
    private sealed record LaunchRun(int ExitCode, bool TimedOut, string Output);
}
