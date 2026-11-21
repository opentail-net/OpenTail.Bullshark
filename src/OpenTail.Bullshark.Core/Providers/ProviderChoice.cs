namespace OpenTail.Bullshark.Core.Providers;

/// <summary>
/// The provider the window shows, as ui.json keeps it (stage C4, C-D6): <c>"provider": "claude"</c> or <c>"codex"</c>. A missing value, and any
/// value that is not <c>codex</c>, is Claude, so a damaged or older file starts the window as before.
/// </summary>
public static class ProviderChoice
{
    /// <summary>The ui.json key of the provider.</summary>
    public const string Key = "provider";

    /// <summary>The provider a text names. <c>codex</c> (any letter case) is Codex; anything else, including null, is Claude.</summary>
    public static ProviderId Parse(string? text) =>
        string.Equals(text?.Trim(), ProviderId.Codex.Value, StringComparison.OrdinalIgnoreCase) ? ProviderId.Codex : ProviderId.Claude;

    /// <summary>The text ui.json keeps for a provider: <c>claude</c> or <c>codex</c>. Any other provider is kept as Claude.</summary>
    public static string TextOf(ProviderId id) => id == ProviderId.Codex ? ProviderId.Codex.Value : ProviderId.Claude.Value;
}
