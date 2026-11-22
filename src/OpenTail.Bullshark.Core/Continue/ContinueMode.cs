namespace OpenTail.Bullshark.Core.Continue;

/// <summary>
/// The three ways Continue starts a session (CN-D2): the full transcript, the last turns copied word for word, or a summary. This is not
/// any provider's screen enum: the App maps it to each resume screen's own mode.
/// </summary>
public enum ContinueMode
{
    Full,
    LastTurns,
    Summary,
}

/// <summary>The setting and library texts of <see cref="ContinueMode"/>: <c>full</c>, <c>lastTurns</c> and <c>summary</c>.</summary>
public static class ContinueModeText
{
    /// <summary>
    /// The mode of a setting text. The match is exact and case-sensitive. Any other text, or null, is null, so the caller uses its default.
    /// </summary>
    public static ContinueMode? Parse(string? text) => text switch
    {
        "full" => ContinueMode.Full,
        "lastTurns" => ContinueMode.LastTurns,
        "summary" => ContinueMode.Summary,
        _ => null,
    };

    /// <summary>The setting text of a mode, as the settings file and the library hold it.</summary>
    public static string ToText(ContinueMode mode) => mode switch
    {
        ContinueMode.Full => "full",
        ContinueMode.LastTurns => "lastTurns",
        ContinueMode.Summary => "summary",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}
