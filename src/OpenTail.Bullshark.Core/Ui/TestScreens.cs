namespace OpenTail.Bullshark.Core.Ui;

/// <summary>A screen the window can open at start, with no click (round 4, item 5): for the screenshots.</summary>
public enum TestScreen
{
    Storage,
    Timeline,
    Changes,
}

/// <summary>
/// What <c>--open-tab</c> asks for: <c>storage</c>, <c>timeline</c>, or <c>changes:&lt;session name&gt;</c>. <see cref="SessionName"/> is set
/// for the changes screen only.
/// </summary>
public sealed record TestScreenRequest(TestScreen Screen, string? SessionName)
{
    /// <summary>
    /// Reads the value of <c>--open-tab</c>. Null when the value is missing, or is not one of the three screens (a typo opens nothing,
    /// and the window opens as it does without the option).
    /// </summary>
    public static TestScreenRequest? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Trim().Split(':', 2);
        var kind = parts[0].ToLowerInvariant();
        var name = parts.Length == 2 ? parts[1].Trim() : null;
        return kind switch
        {
            "storage" when name is null => new TestScreenRequest(TestScreen.Storage, null),
            "timeline" when name is null => new TestScreenRequest(TestScreen.Timeline, null),
            "changes" when !string.IsNullOrEmpty(name) => new TestScreenRequest(TestScreen.Changes, name),
            _ => null,
        };
    }
}

/// <summary>
/// Where the transcripts and the archive are read when a test home is given (<c>--claude-home</c>): the same folders the owner's
/// <c>.claude</c> has, under the test folder. Without the option the window reads the owner's folders, as before.
/// </summary>
public static class TestHome
{
    public static string ProjectsRootUnder(string claudeHome) => Path.Combine(claudeHome, "projects");

    public static string ArchiveRootUnder(string claudeHome) => Path.Combine(claudeHome, "bullshark-archive");
}
