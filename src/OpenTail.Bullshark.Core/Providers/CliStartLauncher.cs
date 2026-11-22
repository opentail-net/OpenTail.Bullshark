using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.Providers;

/// <summary>
/// The start launcher that the CLI providers share (provider kit R1). A start text is written to a new file in the Starts folder. A terminal tab runs
/// the line from <see cref="CommandLine"/>: a PowerShell 7 script that checks for the CLI's PowerShell shim, reads the file, sets the session's folder,
/// deletes the file and then runs the provider's final call. The text never goes on a <c>cmd.exe</c> command line, and the line holds only the script in
/// base64. Windows PowerShell 5.1 is not used: it drops the double quotes of an argument passed to a script. The provider names its shim and passes
/// its final call; this class does not know which CLI it starts.
/// </summary>
public static class CliStartLauncher
{
    /// <summary>The fixed start of the command line, PowerShell 7 by its plain name. The script follows it, in base64.</summary>
    public const string Prefix = "pwsh.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand ";

    /// <summary>
    /// The characters PowerShell reads as a single quote: the plain apostrophe and four typographic ones (U+2018, U+2019, U+201A, U+201B). Inside a
    /// single-quoted string each one must be doubled, or it ends the string early. Checked with Windows PowerShell 5.1 and PowerShell 7: a doubled
    /// one reads back as the same character, and a lone typographic one breaks the script.
    /// </summary>
    public const string SingleQuoteChars = "'‘’‚‛";

    /// <summary>The name of a start file: the local time, a dash, eight lower-case hex digits of a GUID, and <c>.txt</c>.</summary>
    private static readonly Regex StartFileName = new(@"\A[0-9]{8}-[0-9]{6}-[0-9a-f]{8}\.txt\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// The Starts folder that goes with a state file: a folder named <c>Starts</c> beside it, as <c>SummaryFiles.FolderBeside</c> does for the
    /// Summaries folder. A scratch run with its own state file keeps its own Starts folder.
    /// </summary>
    public static string StartsFolderBeside(string statePath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(statePath))!, "Starts");

    /// <summary>
    /// Writes the start text to a new file in <paramref name="startsFolder"/> (created when missing), as UTF-8 without a byte-order mark, and returns
    /// the file's full path. The name is the local time of <paramref name="now"/> with eight hex digits of a new GUID, so a file is never overwritten.
    /// An IO failure is thrown to the caller.
    /// </summary>
    public static string WriteStart(string startsFolder, string text, DateTimeOffset now)
    {
        Directory.CreateDirectory(startsFolder);
        var name = now.LocalDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..8] + ".txt";
        var path = Path.GetFullPath(Path.Combine(startsFolder, name));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(text);
        return path;
    }

    /// <summary>
    /// The PowerShell script of a start: its lines joined with a line feed, in this fixed order. The error preference. The lookup of
    /// <paramref name="shimName"/> with Get-Command, by its name without the extension. When the shim is missing, a line that names the
    /// <paramref name="displayName"/> and the shim, says where the text is kept, and exits with 1 (the prompt file stays). Then the prompt file is
    /// read as UTF-8, the location is set to <paramref name="folder"/>, the prompt file is deleted, and last <paramref name="finalCall"/> runs as given
    /// (a PowerShell line that uses <c>$shim.Source</c> and <c>$prompt</c>). The location is set before the file is deleted, so a missing folder
    /// stops the script with the text kept. Single quotes in both paths are doubled (see <see cref="SingleQuoteChars"/>). A path that is blank, or
    /// has a control character or a line break, throws <see cref="ArgumentException"/>.
    /// </summary>
    public static string BuildScript(string displayName, string shimName, string folder, string promptPath, string finalCall)
    {
        RequirePlainPath(folder, nameof(folder));
        RequirePlainPath(promptPath, nameof(promptPath));

        var command = Path.GetFileNameWithoutExtension(shimName);
        var quotedFolder = Quoted(folder);
        var quotedPrompt = Quoted(promptPath);
        return string.Join(
            "\n",
            "$ErrorActionPreference = 'Stop'",
            $"$shim = Get-Command {command} -CommandType ExternalScript -ErrorAction SilentlyContinue | Select-Object -First 1",
            $"if ($null -eq $shim) {{ Write-Output '{displayName} PowerShell shim ({shimName}) not found. The start text is kept in: {quotedPrompt}'; exit 1 }}",
            $"$prompt = [System.IO.File]::ReadAllText('{quotedPrompt}', [System.Text.Encoding]::UTF8)",
            $"Set-Location -LiteralPath '{quotedFolder}'",
            $"Remove-Item -LiteralPath '{quotedPrompt}' -Force",
            finalCall);
    }

    /// <summary>
    /// The command line of a terminal tab that runs the start script (see <see cref="BuildScript"/>): <see cref="Prefix"/> followed by the base64 of the
    /// script as UTF-16LE, which is what <c>-EncodedCommand</c> needs.
    /// </summary>
    public static string CommandLine(string displayName, string shimName, string folder, string promptPath, string finalCall) =>
        Prefix + Convert.ToBase64String(Encoding.Unicode.GetBytes(BuildScript(displayName, shimName, folder, promptPath, finalCall)));

    /// <summary>The full path of <c>pwsh.exe</c> on the PATH of this process, or null when PowerShell 7 is not on it.</summary>
    public static string? FindPwsh() => FindOnPath("pwsh.exe", Environment.GetEnvironmentVariable("PATH"));

    /// <summary>
    /// The full path of <c>pwsh.exe</c> in the folders of <paramref name="searchPath"/> (a PATH value: folders separated by semicolons, searched in
    /// order), or null when it is not in any of them or the value is blank. A null value is blank: the process PATH is not read.
    /// </summary>
    public static string? FindPwsh(string? searchPath) => FindOnPath("pwsh.exe", searchPath);

    /// <summary>The full path of <paramref name="shimName"/> on the PATH of this process, or null (see <see cref="FindShim(string, string?)"/>).</summary>
    public static string? FindShim(string shimName) => FindShim(shimName, null);

    /// <summary>
    /// The full path of <paramref name="shimName"/> (for example <c>codex.ps1</c>) in the folders of <paramref name="searchPath"/> (a PATH value,
    /// searched in order, with each quoted entry trimmed of its quotes), or null when it is in none of them or the value is blank. Null means the PATH
    /// of this process. A start from text needs the shim, because the script finds it with Get-Command: an executable of the same name alone does not count.
    /// </summary>
    public static string? FindShim(string shimName, string? searchPath) =>
        FindOnPath(shimName, searchPath ?? Environment.GetEnvironmentVariable("PATH"));

    /// <summary>
    /// Deletes the start files in <paramref name="startsFolder"/> whose last write is older than <paramref name="olderThan"/> before
    /// <paramref name="now"/>. Only files whose name has the start-file shape are deleted; no other file is touched. A file that is in use or already
    /// gone is left for the next run. Returns the number of files removed; a missing folder gives zero.
    /// </summary>
    public static int RemoveOldStarts(string startsFolder, TimeSpan olderThan, DateTimeOffset now)
    {
        if (!Directory.Exists(startsFolder)) return 0;

        var cutoff = now - olderThan;
        var removed = 0;
        foreach (var file in new DirectoryInfo(startsFolder).EnumerateFiles())
        {
            if (!StartFileName.IsMatch(file.Name)) continue;
            if (new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero) >= cutoff) continue;
            try
            {
                file.Delete();
                removed++;
            }
            catch (IOException)
            {
                // In use, or removed by the launcher a moment ago: the next run tries again.
            }
            catch (UnauthorizedAccessException)
            {
                // Read-only or locked for another reason: left in place.
            }
        }
        return removed;
    }

    /// <summary>A path that is non-blank and has no control character or line break (U+2028 and U+2029 count as line breaks).</summary>
    public static void RequirePlainPath(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("The path is empty.", name);
        if (path.Any(c => char.IsControl(c) || c is (char)0x2028 or (char)0x2029))
            throw new ArgumentException("The path has a control character or a line break, so it cannot be put in the script.", name);
    }

    /// <summary>The path with each character of <see cref="SingleQuoteChars"/> doubled, for use inside a PowerShell single-quoted string.</summary>
    public static string Quoted(string path)
    {
        var quoted = new StringBuilder(path.Length + 8);
        foreach (var c in path)
        {
            if (SingleQuoteChars.IndexOf(c) >= 0) quoted.Append(c);
            quoted.Append(c);
        }
        return quoted.ToString();
    }

    /// <summary>The first folder of <paramref name="pathValue"/> that has <paramref name="fileName"/>, as a full path; null when the value is blank or no folder has it.</summary>
    private static string? FindOnPath(string fileName, string? pathValue)
    {
        if (string.IsNullOrWhiteSpace(pathValue)) return null;
        foreach (var folder in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(folder.Trim('"'), fileName);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }
}
