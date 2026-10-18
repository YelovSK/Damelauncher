namespace Damelauncher.Search;

/// <summary>Which queries reach a provider.</summary>
/// <param name="Keyword">
/// Typed before a query with a space, it asks only the providers it belongs to, with the rest of
/// the query. <see langword="null"/> for none.
/// </param>
/// <param name="InNormalSearches">Whether queries without a keyword ask the provider too.</param>
/// <param name="MaxResults">
/// How many of the provider's results are shown at most, or <see langword="null"/> to be
/// limited only by how many are shown in all.
/// </param>
internal readonly record struct ProviderRouting(bool Enabled, string? Keyword, bool InNormalSearches, int? MaxResults = null)
{
    internal static readonly ProviderRouting Default = new(Enabled: true, Keyword: null, InNormalSearches: true);
}
