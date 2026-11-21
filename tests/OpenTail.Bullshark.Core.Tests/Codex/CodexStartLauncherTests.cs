using System.Globalization;
using System.Text;
using OpenTail.Bullshark.Core.Codex;
using Xunit;

namespace OpenTail.Bullshark.Core.Tests.Codex;

/// <summary>
/// Step R3: the start file, the launch line, the PowerShell 7 lookup and the cleanup of old start files. The launch line is decoded here, not run (the
/// run is in <see cref="CodexStartLaunchTests"/>). Every folder is synthetic or a temp folder that the test deletes.
/// </summary>
public sealed class CodexStartLauncherTests
{
    private const string Prefix = "pwsh.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand ";

    private static readonly DateTimeOffset Now = new(2026, 11, 21, 9, 30, 15, TimeSpan.FromHours(2));

    /// <summary>
    /// The script the launcher is expected to build, with the paths already quoted for PowerShell. Line 0 is the error preference, lines 1 and 2 the
    /// shim check, lines 3 to 5 the prompt and the folder, and line 6 the call.
    /// </summary>
    private static string ExpectedScript(string folderQuoted, string promptQuoted) => string.Join(
        "\n",
        "$ErrorActionPreference = 'Stop'",
        "$shim = Get-Command codex -CommandType ExternalScript -ErrorAction SilentlyContinue | Select-Object -First 1",
        "if ($null -eq $shim) { Write-Output 'Codex PowerShell shim (codex.ps1) not found. The start text is kept in: " + promptQuoted + "'; exit 1 }",
        "$prompt = [System.IO.File]::ReadAllText('" + promptQuoted + "', [System.Text.Encoding]::UTF8)",
        "Remove-Item -LiteralPath '" + promptQuoted + "' -Force",
        "Set-Location -LiteralPath '" + folderQuoted + "'",
        "& $shim.Source $prompt");

    /// <summary>The script inside a command line: the base64 after the fixed prefix, decoded as UTF-16LE.</summary>
    private static string DecodeScript(string commandLine)
    {
        Assert.StartsWith(Prefix, commandLine);
        return Encoding.Unicode.GetString(Convert.FromBase64String(commandLine[Prefix.Length..]));
    }

    [Fact]
    public void TheEncodedScriptDecodesToTheExpectedScriptWithTheGivenPaths()
    {
        var line = CodexStartLauncher.CommandLine("C:\\Work\\Synthetic Folder", "C:\\Work\\Starts\\prompt.txt");

        Assert.Equal(
            ExpectedScript("C:\\Work\\Synthetic Folder", "C:\\Work\\Starts\\prompt.txt"),
            DecodeScript(line));
    }

    [Fact]
    public void TheScriptChecksForTheShimBeforeItReadsAndDeletesThePromptFile()
    {
        var script = DecodeScript(CodexStartLauncher.CommandLine("C:\\Work", "C:\\Work\\Starts\\prompt.txt")).Split('\n');

        Assert.Equal("$shim = Get-Command codex -CommandType ExternalScript -ErrorAction SilentlyContinue | Select-Object -First 1", script[1]);
        Assert.StartsWith("if ($null -eq $shim) { Write-Output 'Codex PowerShell shim (codex.ps1) not found. The start text is kept in: C:\\Work\\Starts\\prompt.txt'; exit 1 }", script[2]);
        Assert.StartsWith("$prompt = ", script[3]);
        Assert.Equal("& $shim.Source $prompt", script[6]);
    }

    [Fact]
    public void TheLineHasTheFixedPrefixAndItsBase64HasNoSpaceAndNoCmdSpecialCharacter()
    {
        var line = CodexStartLauncher.CommandLine("C:\\Work\\Synthetic Folder", "C:\\Work\\Starts\\prompt.txt");

        Assert.StartsWith(Prefix, line);
        var base64 = line[Prefix.Length..];
        Assert.Matches("^[A-Za-z0-9+/=]+$", base64);
        Assert.DoesNotContain(line, c => "\"&|<>^%!".Contains(c));
    }

    [Theory]
    [InlineData("C:\\Work\\With Spaces", "C:\\Work\\With Spaces")]
    [InlineData("C:\\Work\\O'Brien", "C:\\Work\\O''Brien")]
    [InlineData("C:\\Work\\100%", "C:\\Work\\100%")]
    [InlineData("C:\\Work\\R&D", "C:\\Work\\R&D")]
    [InlineData("C:\\Work\\$HOME", "C:\\Work\\$HOME")]
    [InlineData("C:\\Work\\Hi!", "C:\\Work\\Hi!")]
    [InlineData("C:\\Work\\`tick", "C:\\Work\\`tick")]
    [InlineData("C:\\Work\\''", "C:\\Work\\''''")]
    public void APathWithSpecialCharactersIsCarriedAndItsSingleQuotesAreDoubled(string folder, string folderQuoted)
    {
        var script = DecodeScript(CodexStartLauncher.CommandLine(folder, "C:\\Work\\Starts\\prompt.txt")).Split('\n');

        Assert.Equal("Set-Location -LiteralPath '" + folderQuoted + "'", script[5]);
    }

    [Fact]
    public void TheSingleQuotesOfThePromptPathAreDoubledOnEveryLineThatHoldsIt()
    {
        var script = DecodeScript(CodexStartLauncher.CommandLine("C:\\Work", "C:\\Work\\O'Brien\\prompt.txt")).Split('\n');

        Assert.Equal("$prompt = [System.IO.File]::ReadAllText('C:\\Work\\O''Brien\\prompt.txt', [System.Text.Encoding]::UTF8)", script[3]);
        Assert.Equal("Remove-Item -LiteralPath 'C:\\Work\\O''Brien\\prompt.txt' -Force", script[4]);
        Assert.Contains("kept in: C:\\Work\\O''Brien\\prompt.txt'; exit 1 }", script[2]);
    }

    [Fact]
    public void TheTypographicSingleQuotesAreDoubledToo()
    {
        // PowerShell reads these four as single quotes: a lone one ends the string, so each is doubled, as the plain apostrophe is.
        foreach (var quote in new[] { 0x2018, 0x2019, 0x201A, 0x201B })
        {
            var q = (char)quote;
            var folder = "C:\\Work\\O" + q + "Brien";
            var script = DecodeScript(CodexStartLauncher.CommandLine(folder, "C:\\Work\\Starts\\prompt.txt")).Split('\n');

            Assert.Equal("Set-Location -LiteralPath 'C:\\Work\\O" + q + q + "Brien'", script[5]);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\Work\\a\nb")]
    [InlineData("C:\\Work\\a\rb")]
    [InlineData("C:\\Work\\a\tb")]
    [InlineData("C:\\Work\\a\0b")]
    public void AFolderThatIsBlankOrHasAControlCharacterIsRefused(string folder)
    {
        Assert.Throws<ArgumentException>(() => CodexStartLauncher.CommandLine(folder, "C:\\Work\\Starts\\prompt.txt"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("C:\\Work\\prompt\n.txt")]
    [InlineData("C:\\Work\\prompt\u0007.txt")]
    public void APromptPathThatIsBlankOrHasAControlCharacterIsRefused(string promptPath)
    {
        Assert.Throws<ArgumentException>(() => CodexStartLauncher.CommandLine("C:\\Work", promptPath));
    }

    [Fact]
    public void ALineSeparatorInsideAPathIsRefused()
    {
        var folder = "C:\\Work\\a" + (char)0x2028 + "b";

        Assert.Throws<ArgumentException>(() => CodexStartLauncher.CommandLine(folder, "C:\\Work\\Starts\\prompt.txt"));
    }

    [Fact]
    public void FindPwshReturnsTheFullPathOfPwshExeInTheFirstFolderThatHasIt()
    {
        using var temp = new TempFolder();
        var first = Directory.CreateDirectory(Path.Combine(temp.Folder, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(temp.Folder, "second")).FullName;
        File.WriteAllText(Path.Combine(second, "pwsh.exe"), string.Empty);
        var searchPath = first + Path.PathSeparator + "C:\\no such folder" + Path.PathSeparator + second;

        Assert.Equal(Path.Combine(second, "pwsh.exe"), CodexStartLauncher.FindPwsh(searchPath));
    }

    [Fact]
    public void FindPwshIsNullWhenPwshIsNotInTheFoldersOrThePathIsEmpty()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Folder);

        Assert.Null(CodexStartLauncher.FindPwsh(temp.Folder));
        Assert.Null(CodexStartLauncher.FindPwsh(string.Empty));
        Assert.Null(CodexStartLauncher.FindPwsh(null));
    }

    [Fact]
    public void FindPwshFindsPowerShell7OnThisMachinesPath()
    {
        // The launch test needs PowerShell 7 on the PATH of this process, so a missing pwsh.exe is a failure of the environment, not a pass.
        Assert.NotNull(CodexStartLauncher.FindPwsh());
    }

    [Fact]
    public void WriteStartCreatesTheFolderAndWritesTheTextAsUtf8WithoutAByteOrderMark()
    {
        using var temp = new TempFolder();
        var folder = Path.Combine(temp.Folder, "Starts");
        const string text = "Plain text, a letter é, Greek Ωμέγα, a new\nline, a \"double\" quote, and 100% sure.";

        var path = CodexStartLauncher.WriteStart(folder, text, Now);

        Assert.Equal(Path.GetFullPath(path), path);
        Assert.Equal(Path.GetFullPath(folder), Path.GetDirectoryName(path));
        var name = Path.GetFileName(path);
        Assert.Matches(@"^[0-9]{8}-[0-9]{6}-[0-9a-f]{8}\.txt$", name);
        Assert.StartsWith(Now.LocalDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture), name);
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "a byte-order mark was written");
        Assert.Equal(text, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void TwoStartsMadeInTheSameInstantAreTwoFilesAndNeitherIsOverwritten()
    {
        using var temp = new TempFolder();

        var first = CodexStartLauncher.WriteStart(temp.Folder, "first text", Now);
        var second = CodexStartLauncher.WriteStart(temp.Folder, "second text", Now);

        Assert.NotEqual(first, second);
        Assert.Equal("first text", File.ReadAllText(first, Encoding.UTF8));
        Assert.Equal("second text", File.ReadAllText(second, Encoding.UTF8));
    }

    [Fact]
    public void RemoveOldStartsRemovesOnlyOldStartFilesAndReturnsTheCount()
    {
        using var temp = new TempFolder();
        var oldStart = WriteFile(temp.Folder, "20261118-100000-0a1b2c3d.txt", Now.AddDays(-3));
        var newStart = WriteFile(temp.Folder, "20261121-110000-1f2e3d4c.txt", Now.AddHours(-1));
        var oldJson = WriteFile(temp.Folder, "20261118-100000-0a1b2c3d.json", Now.AddDays(-3));
        var oldNotes = WriteFile(temp.Folder, "notes.txt", Now.AddDays(-3));
        var oldUpperHex = WriteFile(temp.Folder, "20261118-100000-9F8E7D6C.txt", Now.AddDays(-3));

        var removed = CodexStartLauncher.RemoveOldStarts(temp.Folder, TimeSpan.FromDays(1), Now);

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

        Assert.Equal(0, CodexStartLauncher.RemoveOldStarts(Path.Combine(temp.Folder, "missing"), TimeSpan.Zero, Now));
    }

    private static string WriteFile(string folder, string name, DateTimeOffset written)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllText(path, "synthetic");
        File.SetLastWriteTimeUtc(path, written.UtcDateTime);
        return path;
    }

    /// <summary>A temp folder for one test. It is deleted when the test ends.</summary>
    private sealed class TempFolder : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "bullshark-starts-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (!Directory.Exists(Folder)) return;
            try { Directory.Delete(Folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
