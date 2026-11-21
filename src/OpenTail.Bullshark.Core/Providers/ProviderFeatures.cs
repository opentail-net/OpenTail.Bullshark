namespace OpenTail.Bullshark.Core.Providers;

/// <summary>
/// The optional features of a provider (R-8). A provider has a feature when it implements its interface; the UI asks here and hides what
/// is missing, so no provider declares a boolean list.
/// </summary>
public static class ProviderFeatures
{
    /// <summary>True when the provider implements <typeparamref name="T"/> (for example <see cref="ITerminalOpener"/>).</summary>
    public static bool Has<T>(IProvider provider) where T : class => provider is T;
}
