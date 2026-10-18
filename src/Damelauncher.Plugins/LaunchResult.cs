namespace Damelauncher.Plugins;

/// <param name="Score">How well the result matches the query, on the scale of <see cref="FuzzyMatcher"/>.</param>
/// <param name="Execute">Runs the result once the launcher has hidden itself.</param>
internal sealed record LaunchResult(string Title, string Subtitle, int Score, Action Execute);
