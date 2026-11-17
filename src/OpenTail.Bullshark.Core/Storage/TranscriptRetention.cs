using System.Text.Json;

namespace OpenTail.Bullshark.Core.Storage;

/// <summary>Whether a transcript is near its deletion or past it (round 7 item 5). <see cref="None"/> is the normal case.</summary>
public enum RetentionMark
{
    None,

    /// <summary>Within the last three days before the deletion age.</summary>
    Near,

    /// <summary>At or past the deletion age: the next clean-up deletes it.</summary>
    Due,
}

/// <summary>
/// The transcript clean-up Claude runs (round 7 item 5): transcripts older than `cleanupPeriodDays` are deleted. The docs give the default
/// as 30 days (code.claude.com/docs/en/data-usage, "Data retention": transcripts are kept "for 30 days by default ... Adjust the period with
/// `cleanupPeriodDays`"). The docs do not say whether the age is counted from creation or from the last write. This uses the file's last
/// write time, which is an assumption, recorded in the round 7 plan.
/// </summary>
public static class TranscriptRetention
{
    /// <summary>The default number of days, from the data-usage docs (see the class summary).</summary>
    public const int DefaultDays = 30;

    /// <summary>How many days before the deletion age a transcript is marked <see cref="RetentionMark.Near"/>.</summary>
    public const int NearDays = 3;

    /// <summary>
    /// What a settings file says about the setting: the days when the key is a positive whole number, null when the key is absent, and a
    /// problem when the text is not JSON or the key is not such a number. A problem is shown, and the default is used.
    /// </summary>
    public static (int? Days, string? Problem) Parse(string settingsJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(settingsJson);
        }
        catch (JsonException)
        {
            return (null, "settings.json is not valid JSON");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object) return (null, null);
            if (!document.RootElement.TryGetProperty("cleanupPeriodDays", out var value)) return (null, null);
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var days) && days > 0) return (days, null);
            return (null, "cleanupPeriodDays is not a whole number of days");
        }
    }

    /// <summary>
    /// The settings file beside a projects folder: `settings.json` in the folder that holds the projects folder (`~/.claude`, round 8 item 11). A
    /// trailing separator on the projects folder is trimmed first, since `GetDirectoryName("…\projects\")` is the projects folder itself. Null when
    /// the folder has no parent (a drive root).
    /// </summary>
    public static string? SettingsBeside(string projectsRoot)
    {
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(projectsRoot));
        return parent is null ? null : Path.Combine(parent, "settings.json");
    }

    /// <summary>
    /// Reads the setting from a settings file. A missing file, or no path at all, is no setting, and a file that cannot be read gives the reason.
    /// </summary>
    public static (int? Days, string? Problem) ReadDays(string? settingsPath)
    {
        if (settingsPath is null || !File.Exists(settingsPath)) return (null, null);
        try
        {
            return Parse(File.ReadAllText(settingsPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>
    /// The mark of a transcript of this age, for a retention of <paramref name="days"/>: <see cref="RetentionMark.Due"/> at or past the
    /// deletion age, <see cref="RetentionMark.Near"/> within the three days before it, and <see cref="RetentionMark.None"/> otherwise.
    /// </summary>
    public static RetentionMark MarkOf(TimeSpan age, int days)
    {
        var limit = TimeSpan.FromDays(days);
        if (age >= limit) return RetentionMark.Due;
        if (age >= limit - TimeSpan.FromDays(NearDays)) return RetentionMark.Near;
        return RetentionMark.None;
    }

    /// <summary>The whole days left before the deletion, at least one, for a transcript marked Near or Due.</summary>
    public static int DaysLeft(TimeSpan age, int days) =>
        Math.Max(1, (int)Math.Ceiling((TimeSpan.FromDays(days) - age).TotalDays));

    /// <summary>
    /// The sentence the Storage page shows: the deletion age, and whether it is the default or the owner's setting. A setting is the one in
    /// `~/.claude/settings.json` (round 8 item 11): other settings files (the project's, the local one, the managed one) can also set it, and
    /// this does not read them, so the sentence says so.
    /// </summary>
    public static string Sentence(int days, bool fromSettings) =>
        $"Claude deletes transcripts older than {days} days{(fromSettings ? " (from ~/.claude/settings.json; other settings files may override it)" : " (Claude's default)")}; archive to keep them.";
}
