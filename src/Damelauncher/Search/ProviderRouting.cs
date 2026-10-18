namespace Damelauncher.Search;

/// <summary>Which queries reach a provider.</summary>
/// <param name="Keyword">
/// Typed before a query with a space, it asks only the providers it belongs to, with the rest of
/// the query. <see langword="null"/> for none.
/// </param>
/// <param name="InNormalSearches">Whether queries without a keyword ask the provider too.</param>
internal readonly record struct ProviderRouting(bool Enabled, string? Keyword, bool InNormalSearches)
{
    internal static readonly ProviderRouting Default = new(Enabled: true, Keyword: null, InNormalSearches: true);
}
