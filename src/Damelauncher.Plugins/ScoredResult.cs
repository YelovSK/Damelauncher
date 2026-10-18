namespace Damelauncher.Plugins;

/// <param name="Score">How well the result matches the query, on the scale of <see cref="FuzzyMatcher"/>.</param>
internal readonly record struct ScoredResult(LaunchResult Result, int Score);
