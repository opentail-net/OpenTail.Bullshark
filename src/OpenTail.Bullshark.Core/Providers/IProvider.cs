namespace OpenTail.Bullshark.Core.Providers;

/// <summary>
/// One tool the window can show. Its optional capabilities are the other interfaces in this folder that it implements.
/// </summary>
public interface IProvider
{
    ProviderId Id { get; }

    /// <summary>The name the dropdown shows.</summary>
    string DisplayName { get; }

    ProviderPaths Paths { get; }
}
