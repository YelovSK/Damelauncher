namespace Damelauncher.Plugins;

/// <param name="Id">
/// Identifies the result within its provider across queries and restarts, such as a program's
/// app id, so the launcher can remember how often it's picked.
/// </param>
/// <param name="Score">How well the result matches the query, on the scale of <see cref="FuzzyMatcher"/>.</param>
/// <param name="Execute">Runs the result once the launcher has hidden itself.</param>
internal sealed record LaunchResult(string Id, string Title, string Subtitle, int Score, Action Execute);
