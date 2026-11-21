namespace OpenTail.Bullshark.Core.Providers;

/// <summary>
/// The fields every provider can give for a session (R-2). A null field means unknown, never zero. Provider-specific data (Claude's
/// limit stops, recaps, continuations) stays on the provider's own digest.
/// </summary>
public sealed record SessionSummary(
    SessionKey Key,
    string? Title,
    string? Folder,
    DateTimeOffset? FirstActivity,
    DateTimeOffset? LastActivity,
    bool? IsSubagent,
    int? UserMessages,
    int? AssistantMessages,
    int? Compactions,
    string? Model);
