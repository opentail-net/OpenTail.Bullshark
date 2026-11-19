using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OpenTail.Bullshark.Core.Reports;
using OpenTail.Bullshark.Core.Setup;

namespace OpenTail.Bullshark.Core.Ui;

/// <summary>
/// The night report file (night batch N5): the "While you were away" digest as Markdown, saved to <c>reports\</c> beside the state file when
/// the owner returns to news. It is a convenience copy: the Away page and its "Copy as Markdown" are unchanged. A report is never overwritten
/// (a second one in the same minute gets a <c>-2</c> name), and the folder keeps only the newest <see cref="KeepCount"/> reports. The
/// cleanup deletes only files whose names this feature makes (<see cref="IsReportName"/>), so a file the owner put there is never touched.
/// </summary>
public static class NightReport
{
    /// <summary>How many reports the folder keeps. Older ones are deleted when a new one is saved.</summary>
    public const int KeepCount = 60;

    /// <summary>The folder name under the state folder.</summary>
    public const string FolderName = "reports";

    private static readonly Regex ReportName = new(@"^away-(\d{4}-\d{2}-\d{2}-\d{4})(?:-(\d+))?\.md$", RegexOptions.CultureInvariant);

    /// <summary>
    /// The digest as Markdown, with the page's sections (<see cref="AwayExport.Markdown"/>), then a Plans section and a line of the tokens
    /// of the period. <paramref name="plans"/> is each session's plan: boxes done of all, and the next open item (null when none). A plan with
    /// no boxes is left out. <paramref name="usage"/> is null when the usage report could not be read; its days that fall in the period are
    /// added up, so a day that only partly falls in the period is counted whole.
    /// </summary>
    public static string Markdown(
        AwayDigest digest,
        DateTimeOffset since,
        DateTimeOffset now,
        IReadOnlyList<(string Session, int Done, int Total, string? Next)> plans,
        UsageReport? usage)
    {
        ArgumentNullException.ThrowIfNull(digest);
        ArgumentNullException.ThrowIfNull(plans);
        var text = new StringBuilder(AwayExport.Markdown(digest, since, now).TrimEnd()).Append('\n');

        var withBoxes = plans.Where(plan => plan.Total > 0).ToList();
        if (withBoxes.Count > 0)
        {
            text.Append("\n## Plans\n\n");
            foreach (var plan in withBoxes)
            {
                var next = plan.Next is { Length: > 0 } item ? $"; next: {AwayExport.Clean(item)}" : "; no open item";
                text.Append("- **").Append(AwayExport.Clean(plan.Session)).Append("**: ")
                    .Append(plan.Done).Append(" of ").Append(plan.Total).Append(" boxes done").Append(next).Append('\n');
            }
        }

        text.Append("\n**Tokens in this period:** ").Append(TokensLine(usage, since, now)).Append('\n');
        return text.ToString();
    }

    /// <summary>The file name for a report saved at <paramref name="now"/>, in local time: <c>away-2026-11-19-0905.md</c>. It sorts by time.</summary>
    public static string FileName(DateTimeOffset now) =>
        $"away-{now.ToLocalTime().ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture)}.md";

    /// <summary>
    /// The first path in <paramref name="folder"/> for <paramref name="name"/> that is free: the name itself, then <c>-2</c>, <c>-3</c> before the
    /// extension. It does not create anything, and the check is not a lock: <see cref="Save"/> uses the exclusive create that makes it safe.
    /// </summary>
    public static string UniquePath(string folder, string name)
    {
        for (var n = 1; ; n++)
        {
            var path = Candidate(folder, name, n);
            if (!File.Exists(path)) return path;
        }
    }

    /// <summary>
    /// Saves a report as a new file: the first free name for <see cref="FileName"/>, created exclusively (so nothing is overwritten, even if
    /// another write takes the name meanwhile). Returns the full path. The file errors are thrown; the caller says so on the page.
    /// </summary>
    public static string Save(string folder, DateTimeOffset now, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Directory.CreateDirectory(folder);
        var name = FileName(now);
        for (var n = 1; ; n++)
        {
            var path = Candidate(folder, name, n);
            try
            {
                using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(text);
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
                // Taken since the check: the next number is tried.
            }
        }
    }

    /// <summary>True for a name this feature makes: <c>away-yyyy-MM-dd-HHmm.md</c>, with an optional <c>-n</c> before the extension.</summary>
    public static bool IsReportName(string name) => ReportName.IsMatch(name);

    /// <summary>
    /// The names to delete so that only the newest <paramref name="keep"/> report names remain. Only names <see cref="IsReportName"/> accepts are
    /// considered. Newest is by the time in the name, then by the <c>-n</c> number, so <c>0905-2</c> is newer than <c>0905</c>.
    /// </summary>
    public static IReadOnlyList<string> NamesToDelete(IEnumerable<string> names, int keep = KeepCount)
    {
        ArgumentNullException.ThrowIfNull(names);
        var reports = names
            .Select(name => (Name: name, Match: ReportName.Match(name)))
            .Where(item => item.Match.Success)
            .OrderBy(item => item.Match.Groups[1].Value, StringComparer.Ordinal)
            .ThenBy(item => item.Match.Groups[2].Success ? int.Parse(item.Match.Groups[2].Value, CultureInfo.InvariantCulture) : 1)
            .Select(item => item.Name)
            .ToList();
        var extra = reports.Count - Math.Max(0, keep);
        return extra <= 0 ? [] : reports.Take(extra).ToList();
    }

    /// <summary>
    /// Deletes the report files in <paramref name="folder"/> beyond the newest <paramref name="keep"/>, and returns their names. Only files whose
    /// names <see cref="IsReportName"/> accepts are looked at. A file that cannot be deleted is left and skipped. Never throws.
    /// </summary>
    public static IReadOnlyList<string> Prune(string folder, int keep = KeepCount)
    {
        var deleted = new List<string>();
        try
        {
            if (!Directory.Exists(folder)) return deleted;
            var names = Directory.EnumerateFiles(folder, "*.md").Select(Path.GetFileName).Where(name => name is not null).Select(name => name!);
            foreach (var name in NamesToDelete(names.ToList(), keep))
            {
                try
                {
                    File.Delete(Path.Combine(folder, name));
                    deleted.Add(name);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // A file that cannot be deleted stays; the next save tries again.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The folder cannot be listed: nothing is deleted.
        }
        return deleted;
    }

    private static string Candidate(string folder, string name, int n)
    {
        if (n == 1) return Path.Combine(folder, name);
        var stem = Path.GetFileNameWithoutExtension(name);
        return Path.Combine(folder, $"{stem}-{n.ToString(CultureInfo.InvariantCulture)}{Path.GetExtension(name)}");
    }

    /// <summary>"New 1,234 and cache read 5,678, over the usage days of the period", or that it was not read.</summary>
    private static string TokensLine(UsageReport? usage, DateTimeOffset since, DateTimeOffset now)
    {
        if (usage is null) return "not read (the usage report could not be read).";
        var first = DateOnly.FromDateTime(since.ToLocalTime().DateTime);
        var last = DateOnly.FromDateTime(now.ToLocalTime().DateTime);
        var days = usage.Days.Where(day => day.Day >= first && day.Day <= last).ToList();
        var newTokens = days.Sum(day => day.Total.NewTokens());
        var cacheRead = days.Sum(day => day.Total.CacheRead);
        return $"New {newTokens.ToString("N0", CultureInfo.InvariantCulture)} and cache read {cacheRead.ToString("N0", CultureInfo.InvariantCulture)}, "
            + $"over the {days.Count} usage {(days.Count == 1 ? "day" : "days")} in the period.";
    }
}
