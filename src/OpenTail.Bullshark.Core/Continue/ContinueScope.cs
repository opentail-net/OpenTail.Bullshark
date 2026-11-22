namespace OpenTail.Bullshark.Core.Continue;

/// <summary>
/// Which sessions Continue looks at (CN-D1): the active provider only (the default), or the sessions of every provider. The scope never
/// mixes providers in one list; <see cref="All"/> is decided by the caller, which passes no provider to <see cref="ContinueChooser"/>.
/// </summary>
public enum ContinueScope
{
    Provider,
    All,
}

/// <summary>The setting texts of <see cref="ContinueScope"/>: <c>provider</c> and <c>all</c>.</summary>
public static class ContinueScopeText
{
    /// <summary>The scope of a setting text. The match is exact and case-sensitive. Any other text, or null, is null.</summary>
    public static ContinueScope? Parse(string? text) => text switch
    {
        "provider" => ContinueScope.Provider,
        "all" => ContinueScope.All,
        _ => null,
    };

    /// <summary>The setting text of a scope, as the settings file holds it.</summary>
    public static string ToText(ContinueScope scope) => scope switch
    {
        ContinueScope.Provider => "provider",
        ContinueScope.All => "all",
        _ => throw new ArgumentOutOfRangeException(nameof(scope)),
    };
}
