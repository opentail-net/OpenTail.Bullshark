namespace OpenTail.Bullshark.Core.Providers;

/// <summary>
/// The provider the window shows, as ui.json keeps it (stage C4, C-D6): <c>"provider": "claude"</c> or <c>"codex"</c>. A missing value, and any
/// value that is not <c>codex</c>, is Claude, so a damaged or older file starts the window as before.
/// </summary>
public static class ProviderChoice
{
    /// <summary>The ui.json key of the provider.</summary>
    public const string Key = "provider";

    /// <summary>The provider a text names. <c>codex</c>, <c>copilot</c> or <c>opencode</c> (any letter case) is that provider; anything else, including null, is Claude.</summary>
    public static ProviderId Parse(string? text)
    {
        var trimmed = text?.Trim();
        if (string.Equals(trimmed, ProviderId.Codex.Value, StringComparison.OrdinalIgnoreCase)) return ProviderId.Codex;
        if (string.Equals(trimmed, ProviderId.Copilot.Value, StringComparison.OrdinalIgnoreCase)) return ProviderId.Copilot;
        if (string.Equals(trimmed, ProviderId.OpenCode.Value, StringComparison.OrdinalIgnoreCase)) return ProviderId.OpenCode;
        return ProviderId.Claude;
    }

    /// <summary>The text ui.json keeps for a provider: <c>claude</c>, <c>codex</c>, <c>copilot</c> or <c>opencode</c>. Any other provider is kept as Claude.</summary>
    public static string TextOf(ProviderId id)
    {
        if (id == ProviderId.Codex) return ProviderId.Codex.Value;
        if (id == ProviderId.Copilot) return ProviderId.Copilot.Value;
        if (id == ProviderId.OpenCode) return ProviderId.OpenCode.Value;
        return ProviderId.Claude.Value;
    }
}
