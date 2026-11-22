using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OpenTail.Bullshark.Core.Codex;

/// <summary>
/// Starts a new Codex session with a start text (step R3, decisions R-D6 and R-D8). The text is written to a new file in the Starts folder. A terminal
/// tab runs the line from <see cref="CommandLine"/>: a PowerShell 7 script that checks for the Codex PowerShell shim (<c>codex.ps1</c>), reads the
/// file, sets the session's folder, deletes it and runs the shim with the text as its last argument (after <c>-m</c> and the model, when a model is set). The text never goes on a <c>cmd.exe</c> command
/// line, which breaks on quotes, percent signs and new lines. The line itself holds only the script in base64, so nothing in it is special to
/// <c>cmd.exe</c>. Windows PowerShell 5.1 is not used: it drops the double quotes of an argument passed to a script.
/// </summary>
public static class CodexStartLauncher
{
    /// <summary>The fixed start of the command line, PowerShell 7 by its plain name. The script follows it, in base64.</summary>
    private const string Prefix = "pwsh.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand ";

    /// <summary>
    /// The characters PowerShell reads as a single quote: the plain apostrophe and four typographic ones (U+2018, U+2019, U+201A, U+201B). Inside a
    /// single-quoted string each one must be doubled, or it ends the string early. Checked with Windows PowerShell 5.1 and PowerShell 7: a doubled
    /// one reads back as the same character, and a lone typographic one breaks the script.
    /// </summary>
    private static readonly char[] SingleQuoteChars = ['\'', (char)0x2018, (char)0x2019, (char)0x201A, (char)0x201B];

    /// <summary>The name of a start file: the local time, a dash, eight hex digits of a GUID, and <c>.txt</c>.</summary>
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
    /// The command line of a terminal tab that starts Codex with the text in <paramref name="promptPath"/>:
    /// <c>pwsh.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand &lt;base64&gt;</c>. The base64 is the UTF-16LE script (what
    /// <c>-EncodedCommand</c> needs). The script first looks for the Codex PowerShell shim (<c>codex.ps1</c>) and, when it is missing, writes a line
    /// that says where the text is kept and exits with 1, leaving the prompt file in place. Otherwise it reads the prompt file as UTF-8, sets the location
    /// to <paramref name="folder"/>, deletes the prompt file and runs the shim with the text as its last argument. The location is set before the file is
    /// deleted, so a folder that is missing stops the script with the text still kept. Single quotes in every path are doubled (see
    /// <see cref="SingleQuoteChars"/>). A path that is blank, or has a control character or a line break, throws <see cref="ArgumentException"/>.
    /// With <paramref name="model"/> (P9, decision M-D5), the shim runs as <c>-m '&lt;model&gt;' $prompt</c>; the model must pass
    /// <see cref="CodexModelList.IsValidModelId"/>, or <see cref="ArgumentException"/> is thrown before any script is built. Null keeps Codex's own default.
    /// </summary>
    public static string CommandLine(string folder, string promptPath, string? model = null)
    {
        RequirePlainPath(folder, nameof(folder));
        RequirePlainPath(promptPath, nameof(promptPath));
        if (model is not null && !CodexModelList.IsValidModelId(model))
            throw new ArgumentException("The model id has a character outside lower-case letters, digits, dots and hyphens.", nameof(model));

        var run = model is null ? "& $shim.Source $prompt" : $"& $shim.Source -m '{model}' $prompt";
        var script = string.Join(
            "\n",
            "$ErrorActionPreference = 'Stop'",
            "$shim = Get-Command codex -CommandType ExternalScript -ErrorAction SilentlyContinue | Select-Object -First 1",
            "if ($null -eq $shim) { Write-Output 'Codex PowerShell shim (codex.ps1) not found. The start text is kept in: " + Quoted(promptPath) + "'; exit 1 }",
            $"$prompt = [System.IO.File]::ReadAllText('{Quoted(promptPath)}', [System.Text.Encoding]::UTF8)",
            $"Set-Location -LiteralPath '{Quoted(folder)}'",
            $"Remove-Item -LiteralPath '{Quoted(promptPath)}' -Force",
            run);
        return Prefix + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    }

    /// <summary>The full path of <c>pwsh.exe</c> on the PATH of this process, or null when PowerShell 7 is not on it.</summary>
    public static string? FindPwsh() => FindPwsh(Environment.GetEnvironmentVariable("PATH"));

    /// <summary>
    /// The full path of <c>pwsh.exe</c> in the folders of <paramref name="searchPath"/> (a PATH value: folders separated by semicolons, searched in
    /// order), or null when it is not in any of them or the value is blank. The App uses it to refuse a text launch with a clear message when
    /// PowerShell 7 is missing.
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
    /// The full path of <c>codex.ps1</c> in the folders of <paramref name="searchPath"/> (a PATH value, searched in order, as <see cref="FindPwsh(string?)"/>
    /// searches), or null when it is in none of them or the value is blank. Null means the PATH of this process. A start from text needs the shim: the
    /// launcher's script finds it with Get-Command, so a <c>codex.exe</c> alone does not count.
    /// </summary>
    public static string? FindShim(string? searchPath = null)
    {
        var pathValue = searchPath ?? Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue)) return null;
        foreach (var folder in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(folder.Trim('"'), "codex.ps1");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        return null;
    }

    /// <summary>
    /// Deletes the start files in <paramref name="startsFolder"/> whose last write is older than <paramref name="olderThan"/> before
    /// <paramref name="now"/>. Only files whose name has the start-file shape (see <see cref="StartFileName"/>) are deleted; no other file is touched.
    /// A file that is in use or already gone is left for the next run. Returns the number of files removed; a missing folder gives zero.
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
