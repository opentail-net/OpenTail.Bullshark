using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.Copilot;

/// <summary>
/// Starts a new Copilot session with a start text (G1.7, G-D14). The text is written to a new file in the Starts folder. A terminal tab runs the line
/// from <see cref="CommandLine"/>: a PowerShell 7 script that checks for the Copilot PowerShell shim (<c>copilot.ps1</c>), reads the file, deletes it,
/// sets the session's folder and runs <c>copilot.ps1 -i &lt;text&gt; --sync-session=off</c>. The text never goes on a <c>cmd.exe</c> command line.
/// The line holds only the script in base64. Windows PowerShell 5.1 is not used: it drops the double quotes of an argument passed to a script.
/// This copies the Codex launcher's encoding and rules (Codex R3); only the shim name, the call and the wording differ.
/// </summary>
public static class CopilotStartLauncher
{
    /// <summary>The fixed start of the command line, PowerShell 7 by its plain name. The script follows it, in base64.</summary>
    private const string Prefix = "pwsh.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand ";

    /// <summary>
    /// The characters PowerShell reads as a single quote: the plain apostrophe and four typographic ones (U+2018, U+2019, U+201A, U+201B). Inside a
    /// single-quoted string each one must be doubled, or it ends the string early.
    /// </summary>
    private static readonly char[] SingleQuoteChars = ['\'', (char)0x2018, (char)0x2019, (char)0x201A, (char)0x201B];

    /// <summary>The name of a start file: the local time, a dash, eight hex digits of a GUID, and <c>.txt</c>.</summary>
    private static readonly Regex StartFileName = new(@"\A[0-9]{8}-[0-9]{6}-[0-9a-f]{8}\.txt\z", RegexOptions.CultureInvariant);

    /// <summary>
    /// The Starts folder that goes with a state file: a folder named <c>Starts</c> beside it, as the Codex launcher and the summary files do.
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
    /// The command line of a terminal tab that starts Copilot with the text in <paramref name="promptPath"/>:
    /// <c>pwsh.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand &lt;base64&gt;</c>. The base64 is the UTF-16LE script. When the shim is missing the script
    /// writes a line that says where the text is kept and exits with 1, leaving the prompt file in place. Otherwise it reads the prompt file as UTF-8,
    /// deletes it, sets the location to <paramref name="folder"/> and runs <c>copilot.ps1 -i &lt;text&gt; --sync-session=off</c>. Single quotes in every path
    /// are doubled. A path that is blank, or has a control character or a line break, throws <see cref="ArgumentException"/>.
    /// </summary>
    public static string CommandLine(string folder, string promptPath)
    {
        RequirePlainPath(folder, nameof(folder));
        RequirePlainPath(promptPath, nameof(promptPath));

        var script = string.Join(
            "\n",
            "$ErrorActionPreference = 'Stop'",
            "$shim = Get-Command copilot -CommandType ExternalScript -ErrorAction SilentlyContinue | Select-Object -First 1",
            "if ($null -eq $shim) { Write-Output 'Copilot PowerShell shim (copilot.ps1) not found. The start text is kept in: " + Quoted(promptPath) + "'; exit 1 }",
            $"$prompt = [System.IO.File]::ReadAllText('{Quoted(promptPath)}', [System.Text.Encoding]::UTF8)",
            $"Remove-Item -LiteralPath '{Quoted(promptPath)}' -Force",
            $"Set-Location -LiteralPath '{Quoted(folder)}'",
            $"& $shim.Source -i $prompt {CopilotTerminalOpener.SyncOff}");
        return Prefix + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    }

    /// <summary>The full path of <c>pwsh.exe</c> on the PATH of this process, or null when PowerShell 7 is not on it.</summary>
    public static string? FindPwsh() => FindPwsh(Environment.GetEnvironmentVariable("PATH"));

    /// <summary>
    /// The full path of <c>pwsh.exe</c> in the folders of <paramref name="searchPath"/> (a PATH value, searched in order), or null when it is not in
    /// any of them or the value is blank.
    /// </summary>
    public static string? FindPwsh(string? searchPath)
    {
        if (string.IsNullOrWhiteSpace(searchPath)) return null;
        foreach (var folder in searchPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(folder.Trim('"'), "pwsh.exe");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }

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
    private static void RequirePlainPath(string path, string name)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("The path is empty.", name);
        if (path.Any(c => char.IsControl(c) || c is (char)0x2028 or (char)0x2029))
            throw new ArgumentException("The path has a control character or a line break, so it cannot be put in the script.", name);
    }

    /// <summary>The path with each single-quote character doubled, for use inside a PowerShell single-quoted string.</summary>
    private static string Quoted(string path)
    {
        var quoted = new StringBuilder(path.Length + 8);
        foreach (var c in path)
        {
            if (Array.IndexOf(SingleQuoteChars, c) >= 0) quoted.Append(c);
            quoted.Append(c);
        }
        return quoted.ToString();
    }
}
