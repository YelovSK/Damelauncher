namespace Damelauncher.Plugins;

/// <param name="Id">
/// Identifies the result within its provider across queries and restarts, such as a program's
/// app id, so the launcher can remember how often it's picked.
/// </param>
/// <param name="IconSource">
/// A path or shell location, such as <c>shell:AppsFolder\…</c>, whose icon the launcher shows
/// next to the result, or <see langword="null"/> for none.
/// </param>
/// <param name="Score">How well the result matches the query, on the scale of <see cref="FuzzyMatcher"/>.</param>
/// <param name="Execute">Runs the result once the launcher has hidden itself.</param>
internal sealed record LaunchResult(
    string Id,
    string Title,
    string Subtitle,
    string? IconSource,
    int Score,
    Action Execute);
