using System.Globalization;
using System.Text.Json.Nodes;

namespace OpenTail.Bullshark.Core.Cache;

/// <summary>One choice of the context window on the Settings page: its tokens, its label, and whether it is not one of the presets.</summary>
public sealed record ContextWindowChoice(long Tokens, string Label, bool IsCustom);

/// <summary>
/// The context meter on a rail row ("ctx 142k"): the tokens the session's last request carried, and whether they are close to the window.
/// The window is the owner's setting (<c>contextWindow</c> in ui.json). No window is read from Claude, so 200,000 is an assumed default, which
/// the Settings page labels as assumed. Pure: no file is read here.
/// </summary>
public static class ContextMeter
{
    /// <summary>The window used when the file holds nothing usable. It is an assumption (the desktop research could not confirm a size).</summary>
    public const long DefaultWindow = 200_000;

    /// <summary>The smallest window a setting can hold; a smaller number is raised to this.</summary>
    public const long MinWindow = 50_000;

    /// <summary>The largest window a setting can hold; a larger number is lowered to this.</summary>
    public const long MaxWindow = 5_000_000;

    /// <summary>The choices on the Settings page: the assumed 200,000 and 1,000,000 (a 1M-token model).</summary>
    public static readonly IReadOnlyList<long> Presets = [DefaultWindow, 1_000_000];

    /// <summary>The share of the window from which the warning mark shows: 4 in 5, which is 80 %.</summary>
    private const long WarnNumerator = 4;
    private const long WarnDenominator = 5;

    /// <summary>
    /// The figure on a row: "ctx 999" below a thousand, "ctx 142k" in thousands (rounded down), "ctx 1.2M" from a million (rounded down to a tenth).
    /// Empty when there are no tokens.
    /// </summary>
    public static string Short(long tokens)
    {
        if (tokens <= 0) return string.Empty;
        if (tokens < 1_000) return $"ctx {tokens}";
        if (tokens < 1_000_000) return $"ctx {tokens / 1_000}k";
        var tenths = tokens / 100_000;
        return $"ctx {tenths / 10}.{tenths % 10}M";
    }

    /// <summary>
    /// The window to judge a session against. A session that already holds more tokens than the chosen window cannot be in that window, so it is
    /// judged against the larger standard one (1,000,000): the owner's 200k choice must not put a warning on every long session of a 1M model
    /// (found on the owner's real data, 2026-11-19: contexts of 300k to 900k all showed the mark).
    /// </summary>
    public static long EffectiveWindow(long tokens, long window) => tokens > window ? Math.Max(window, 1_000_000) : window;

    /// <summary>True when the tokens are at least 80 % of the window. Exact: no rounding of the window.</summary>
    public static bool IsHigh(long tokens, long window) =>
        window > 0 && tokens * WarnDenominator >= window * WarnNumerator;

    /// <summary>
    /// The window a stored value gives. A number is clamped to 50,000 to 5,000,000 tokens. A missing value, or one that is not a positive
    /// number (text, a bool, zero, a negative), gives <see cref="DefaultWindow"/>. The value is only read, never changed.
    /// </summary>
    public static long WindowFrom(JsonNode? stored)
    {
        // The node's JSON text, as AttentionEscalation.IntervalOf reads it: a number reads alike whether it was made in code or read from the file,
        // and text or a bool does not parse as a number.
        if (stored is not JsonValue json
            || !double.TryParse(json.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var tokens)
            || !double.IsFinite(tokens)
            || tokens <= 0)
            return DefaultWindow;
        return (long)Math.Clamp(Math.Round(tokens), MinWindow, MaxWindow);
    }

    /// <summary>The label of a preset on the Settings page: "200k (assumed)" for the default, "1M" for one million.</summary>
    public static string PresetLabel(long tokens) => tokens switch
    {
        DefaultWindow => "200k (assumed)",
        1_000_000 => "1M",
        _ => $"{tokens / 1_000}k",
    };

    /// <summary>The choice a stored value shows on the Settings page: a preset, or "Custom (N)" for any other window the engine uses.</summary>
    public static ContextWindowChoice ChoiceOf(JsonNode? stored)
    {
        var window = WindowFrom(stored);
        if (Presets.Contains(window)) return new ContextWindowChoice(window, PresetLabel(window), IsCustom: false);
        return new ContextWindowChoice(window, $"Custom ({window.ToString("N0", CultureInfo.InvariantCulture)})", IsCustom: true);
    }
}
