using System.Text;
using OpenTail.Bullshark.Core.Providers;
using Xunit;
using OpenTail.Bullshark.Core.Tests.Support;

namespace OpenTail.Bullshark.Core.Tests.Providers;

/// <summary>
/// The shared start launcher (provider kit R1): the order and text of the script, the quoting, the path checks, the shim and PowerShell lookups, the
/// start file and the cleanup of old start files. A provider's own tests cover its shim name and its call. Every path is synthetic, or a temp folder
/// that the test deletes.
/// </summary>
public sealed class CliStartLauncherTests
{
    private const string DisplayName = "Demo";

    private const string ShimName = "demo.ps1";

    private const string FinalCall = "& $shim.Source $prompt";

    private const string Folder = "C:\\Fixture\\Synthetic Folder";

    private const string PromptPath = "C:\\Fixture\\Starts\\prompt.txt";

    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 30, 15, TimeSpan.FromHours(2));

    private static string Script(string folder = Folder, string promptPath = PromptPath) =>
        CliStartLauncher.BuildScript(DisplayName, ShimName, folder, promptPath, FinalCall);

    private static string[] Lines(string folder = Folder, string promptPath = PromptPath) => Script(folder, promptPath).Split('\n');

    /// <summary>The script inside a command line: the base64 after the fixed prefix, decoded as UTF-16LE.</summary>
    private static string Decode(string commandLine)
    {
        Assert.StartsWith(CliStartLauncher.Prefix, commandLine);
        return Encoding.Unicode.GetString(Convert.FromBase64String(commandLine[CliStartLauncher.Prefix.Length..]));
    }

    [Fact]
    public void ThePrefixIsPowerShell7WithEncodedCommand()
    {
        Assert.Equal("pwsh.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand ", CliStartLauncher.Prefix);
    }

    [Fact]
    public void TheScriptIsTheSevenLinesWithTheExpectedTextInOrder()
    {
        var expected = string.Join(
            "\n",
            "$ErrorActionPreference = 'Stop'",
            "$shim = Get-Command demo -CommandType ExternalScript -ErrorAction SilentlyContinue | Select-Object -First 1",
            "if ($null -eq $shim) { Write-Output 'Demo PowerShell shim (demo.ps1) not found. The start text is kept in: C:\\Fixture\\Starts\\prompt.txt'; exit 1 }",
            "$prompt = [System.IO.File]::ReadAllText('C:\\Fixture\\Starts\\prompt.txt', [System.Text.Encoding]::UTF8)",
            "Set-Location -LiteralPath 'C:\\Fixture\\Synthetic Folder'",
            "Remove-Item -LiteralPath 'C:\\Fixture\\Starts\\prompt.txt' -Force",
            "& $shim.Source $prompt");

        Assert.Equal(expected, Script());
    }

    [Fact]
    public void TheFolderIsSetBeforeThePromptFileIsDeletedAndTheCallRunsLast()
    {
        var lines = Lines();

        var read = Array.FindIndex(lines, line => line.StartsWith("$prompt = ", StringComparison.Ordinal));
        var setLocation = Array.FindIndex(lines, line => line.StartsWith("Set-Location -LiteralPath ", StringComparison.Ordinal));
        var remove = Array.FindIndex(lines, line => line.StartsWith("Remove-Item -LiteralPath ", StringComparison.Ordinal));
        Assert.True(read >= 0 && setLocation >= 0 && remove >= 0, "the script lacks one of its steps");
        Assert.True(read < setLocation && setLocation < remove, "the steps are not in the order read, set the folder, delete");
        Assert.Equal(FinalCall, lines[^1]);
    }

    [Fact]
    public void TheShimIsLookedUpByItsNameWithoutTheExtension()
    {
        Assert.StartsWith("$shim = Get-Command demo -CommandType ExternalScript ", Lines()[1], StringComparison.Ordinal);
    }

    [Fact]
    public void TheMissingShimLineNamesTheDisplayNameTheShimAndWhereTheTextIsKept()
    {
        var line = Lines()[2];

        Assert.StartsWith(
            "if ($null -eq $shim) { Write-Output 'Demo PowerShell shim (demo.ps1) not found. The start text is kept in: C:\\Fixture\\Starts\\prompt.txt'",
            line,
            StringComparison.Ordinal);
        Assert.EndsWith("'; exit 1 }", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("C:\\Fixture\\With Spaces", "C:\\Fixture\\With Spaces")]
    [InlineData("C:\\Fixture\\O'Brien", "C:\\Fixture\\O''Brien")]
    [InlineData("C:\\Fixture\\100%", "C:\\Fixture\\100%")]
    [InlineData("C:\\Fixture\\R&D", "C:\\Fixture\\R&D")]
    [InlineData("C:\\Fixture\\$HOME", "C:\\Fixture\\$HOME")]
    [InlineData("C:\\Fixture\\Hi!", "C:\\Fixture\\Hi!")]
    [InlineData("C:\\Fixture\\`tick", "C:\\Fixture\\`tick")]
    [InlineData("C:\\Fixture\\\"quoted\"", "C:\\Fixture\\\"quoted\"")]
    [InlineData("C:\\Fixture\\''", "C:\\Fixture\\''''")]
    [InlineData("C:\\Fixture\\O\u2018Brien", "C:\\Fixture\\O\u2018\u2018Brien")]
    [InlineData("C:\\Fixture\\O\u2019Brien", "C:\\Fixture\\O\u2019\u2019Brien")]
    [InlineData("C:\\Fixture\\O\u201ABrien", "C:\\Fixture\\O\u201A\u201ABrien")]
    [InlineData("C:\\Fixture\\O\u201BBrien", "C:\\Fixture\\O\u201B\u201BBrien")]
    public void ASpecialFolderIsCarriedAndItsSingleQuotesAreDoubled(string folder, string folderQuoted)
    {
        var lines = Lines(folder: folder);

        Assert.Equal("Set-Location -LiteralPath '" + folderQuoted + "'", lines[4]);
    }

    [Theory]
    [InlineData("C:\\Fixture\\O'Brien\\prompt.txt", "C:\\Fixture\\O''Brien\\prompt.txt")]
    [InlineData("C:\\Fixture\\O\u2018Brien\\p\u2019.txt", "C:\\Fixture\\O\u2018\u2018Brien\\p\u2019\u2019.txt")]
    [InlineData("C:\\Fixture\\100% & $x; Hi!\\prompt.txt", "C:\\Fixture\\100% & $x; Hi!\\prompt.txt")]
    public void ThePromptPathIsQuotedOnEveryLineThatHoldsIt(string promptPath, string promptQuoted)
    {
        var lines = Lines(promptPath: promptPath);

        Assert.Contains("kept in: " + promptQuoted + "'; exit 1 }", lines[2], StringComparison.Ordinal);
        Assert.Equal("$prompt = [System.IO.File]::ReadAllText('" + promptQuoted + "', [System.Text.Encoding]::UTF8)", lines[3]);
        Assert.Equal("Remove-Item -LiteralPath '" + promptQuoted + "' -Force", lines[5]);
    }

    [Theory]
    [InlineData("'", "''")]
    [InlineData("\u2018", "\u2018\u2018")]
    [InlineData("\u2019", "\u2019\u2019")]
    [InlineData("\u201A", "\u201A\u201A")]
    [InlineData("\u201B", "\u201B\u201B")]
    [InlineData("a\"b%c&d$e!f", "a\"b%c&d$e!f")]
    public void QuotedDoublesEachSingleQuoteCharacterAndNothingElse(string path, string expected)
    {
        Assert.Equal(expected, CliStartLauncher.Quoted(path));
    }

    [Fact]
    public void TheSingleQuoteCharactersAreTheApostropheAndTheFourTypographicOnes()
    {
        Assert.Equal("'\u2018\u2019\u201A\u201B", CliStartLauncher.SingleQuoteChars);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\Fixture\\a\nb")]
    [InlineData("C:\\Fixture\\a\rb")]
    [InlineData("C:\\Fixture\\a\tb")]
    [InlineData("C:\\Fixture\\a\0b")]
    [InlineData("C:\\Fixture\\a\u2028b")]
    [InlineData("C:\\Fixture\\a\u2029b")]
    public void RequirePlainPathRefusesABlankPathOrAControlCharacterOrLineBreak(string path)
    {
        Assert.Throws<ArgumentException>(() => CliStartLauncher.RequirePlainPath(path, "folder"));
    }

    [Fact]
    public void RequirePlainPathAcceptsSpacesAndTypographicQuotes()
    {
        var exception = Record.Exception(() => CliStartLauncher.RequirePlainPath("C:\\Fixture\\O\u2019Brien And \u201CMore\u201D", "folder"));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData("", "C:\\Fixture\\Starts\\prompt.txt")]
    [InlineData("C:\\Fixture\\a\u2028b", "C:\\Fixture\\Starts\\prompt.txt")]
    [InlineData("C:\\Fixture", "C:\\Fixture\\prompt\n.txt")]
    [InlineData("C:\\Fixture", "")]
    public void CommandLineRefusesAFolderOrPromptPathThatIsNotPlain(string folder, string promptPath)
    {
        Assert.Throws<ArgumentException>(() => CliStartLauncher.CommandLine(DisplayName, ShimName, folder, promptPath, FinalCall));
    }

    [Fact]
    public void TheCommandLineDecodesToTheScript()
    {
        var line = CliStartLauncher.CommandLine(DisplayName, ShimName, Folder, PromptPath, FinalCall);

        Assert.Equal(Script(), Decode(line));
    }

    [Fact]
    public void TheBase64HasNoSpaceAndNoCmdSpecialCharacter()
    {
        var line = CliStartLauncher.CommandLine(DisplayName, ShimName, "C:\\Fixture\\Synthetic Folder", PromptPath, FinalCall);

        var base64 = line[CliStartLauncher.Prefix.Length..];
        Assert.Matches("^[A-Za-z0-9+/=]+$", base64);
        Assert.DoesNotContain(line, c => "\"&|<>^%!".Contains(c));
    }

    [Fact]
    public void FindShimReturnsTheFullPathInTheFirstFolderThatHasIt()
    {
        using var temp = new TempFolder();
        var first = Directory.CreateDirectory(Path.Combine(temp.Folder, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(temp.Folder, "second")).FullName;
        File.WriteAllText(Path.Combine(first, "demo.exe"), string.Empty);
        File.WriteAllText(Path.Combine(second, "demo.ps1"), string.Empty);
        var searchPath = first + Path.PathSeparator + "C:\\no such folder" + Path.PathSeparator + second;

        // demo.exe in the first folder does not count: the shim is the .ps1 file.
        Assert.Equal(Path.Combine(second, "demo.ps1"), CliStartLauncher.FindShim(ShimName, searchPath));
    }

    [Fact]
    public void FindShimIsNullWhenTheShimIsNotInAnyFolder()
    {
        using var temp = new TempFolder();
        Directory.CreateDirectory(temp.Folder);
        File.WriteAllText(Path.Combine(temp.Folder, "demo.exe"), string.Empty);

        Assert.Null(CliStartLauncher.FindShim(ShimName, temp.Folder));
    }

    [Fact]
    public void FindShimIsNullForABlankPath()
    {
        Assert.Null(CliStartLauncher.FindShim(ShimName, string.Empty));
        Assert.Null(CliStartLauncher.FindShim(ShimName, "   "));
    }

    [Fact]
    public void FindShimAcceptsAQuotedFolderEntry()
    {
        using var temp = new TempFolder();
        var folder = Directory.CreateDirectory(Path.Combine(temp.Folder, "quoted folder")).FullName;
        File.WriteAllText(Path.Combine(folder, "demo.ps1"), string.Empty);

        Assert.Equal(Path.Combine(folder, "demo.ps1"), CliStartLauncher.FindShim(ShimName, "\"" + folder + "\""));
    }

    [Fact]
    public void FindPwshIsNullForANullOrBlankPathAndDoesNotReadTheProcessPath()
    {
        Assert.Null(CliStartLauncher.FindPwsh(null));
        Assert.Null(CliStartLauncher.FindPwsh(string.Empty));
    }

    [Fact]
    public void FindPwshReturnsTheFullPathInTheFirstFolderThatHasIt()
    {
        using var temp = new TempFolder();
        var first = Directory.CreateDirectory(Path.Combine(temp.Folder, "first")).FullName;
        var second = Directory.CreateDirectory(Path.Combine(temp.Folder, "second")).FullName;
        File.WriteAllText(Path.Combine(second, "pwsh.exe"), string.Empty);
        var searchPath = first + Path.PathSeparator + second;

        Assert.Equal(Path.Combine(second, "pwsh.exe"), CliStartLauncher.FindPwsh(searchPath));
    }

    [Fact]
    public void WriteStartReadsBackExactlyWithAUnicodeLetterAndNewLines()
    {
        using var temp = new TempFolder();
        var folder = Path.Combine(temp.Folder, "Starts");
        const string text = "First line, a letter \u00E9 and Greek \u03A9\u03BC\u03AD\u03B3\u03B1, an emoji \uD83D\uDE00.\nSecond line with \"quotes\" and 'apostrophes'.\r\nThird line: 100% & $HOME ! ^";

        var path = CliStartLauncher.WriteStart(folder, text, Now);

        Assert.Equal(Path.GetFullPath(folder), Path.GetDirectoryName(path));
        Assert.Matches(@"^[0-9]{8}-[0-9]{6}-[0-9a-f]{8}\.txt$", Path.GetFileName(path));
        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "a byte-order mark was written");
        Assert.Equal(text, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void RemoveOldStartsRemovesOnlyOldFilesWithTheStartFileShape()
    {
        using var temp = new TempFolder();
        var oldStart = StampedFiles.Write(temp.Folder, "20261119-100000-0a1b2c3d.txt", Now.AddDays(-3));
        var newStart = StampedFiles.Write(temp.Folder, "20261122-110000-1f2e3d4c.txt", Now.AddHours(-1));
        var oldJson = StampedFiles.Write(temp.Folder, "20261119-100000-0a1b2c3d.json", Now.AddDays(-3));
        var oldNotes = StampedFiles.Write(temp.Folder, "notes.txt", Now.AddDays(-3));
        var oldUpperHex = StampedFiles.Write(temp.Folder, "20261119-100000-9F8E7D6C.txt", Now.AddDays(-3));
        var oldPrefixed = StampedFiles.Write(temp.Folder, "x20261119-100000-0a1b2c3d.txt", Now.AddDays(-3));
        var oldShortHex = StampedFiles.Write(temp.Folder, "20261119-100000-0a1b2c3.txt", Now.AddDays(-3));
        var oldFolder = Path.Combine(temp.Folder, "20261119-100000-5e6f7a8b.txt");
        Directory.CreateDirectory(oldFolder);

        var removed = CliStartLauncher.RemoveOldStarts(temp.Folder, TimeSpan.FromDays(1), Now);

        Assert.Equal(1, removed);
        Assert.False(File.Exists(oldStart));
        Assert.True(File.Exists(newStart));
        Assert.True(File.Exists(oldJson));
        Assert.True(File.Exists(oldNotes));
        Assert.True(File.Exists(oldUpperHex));
        Assert.True(File.Exists(oldPrefixed));
        Assert.True(File.Exists(oldShortHex));
        Assert.True(Directory.Exists(oldFolder));
    }

    [Fact]
    public void RemoveOldStartsKeepsAFileWrittenExactlyAtTheCutoffAndRemovesOneJustBeforeIt()
    {
        using var temp = new TempFolder();
        var atCutoff = StampedFiles.Write(temp.Folder, "20261121-093015-0a1b2c3d.txt", Now.AddDays(-1));
        var justBefore = StampedFiles.Write(temp.Folder, "20261121-093015-1f2e3d4c.txt", Now.AddDays(-1).AddSeconds(-1));

        var removed = CliStartLauncher.RemoveOldStarts(temp.Folder, TimeSpan.FromDays(1), Now);

        Assert.Equal(1, removed);
        Assert.True(File.Exists(atCutoff));
        Assert.False(File.Exists(justBefore));
    }

    [Fact]
    public void RemoveOldStartsOfAMissingFolderIsZero()
    {
        using var temp = new TempFolder();

        Assert.Equal(0, CliStartLauncher.RemoveOldStarts(Path.Combine(temp.Folder, "missing"), TimeSpan.Zero, Now));
    }

    [Fact]
    public void TheStartsFolderIsNamedStartsAndSitsBesideTheStateFile()
    {
        var state = Path.Combine(Path.GetTempPath(), "bullshark-state", "state.json");

        var folder = CliStartLauncher.StartsFolderBeside(state);

        Assert.Equal("Starts", Path.GetFileName(folder));
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(state)), Path.GetDirectoryName(folder));
    }

    /// <summary>A temp folder for one test. It is deleted when the test ends.</summary>
    private sealed class TempFolder : IDisposable
    {
        public string Folder { get; } = Path.Combine(Path.GetTempPath(), "bullshark-cli-starts-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (!Directory.Exists(Folder)) return;
            try { Directory.Delete(Folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
