namespace Damelauncher.Settings;

internal sealed record PluginSettings
{
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// The keyword that asks only this plugin: <see langword="null"/> for the plugin's own
    /// default, and empty for none.
    /// </summary>
    public string? Keyword { get; init; }

    /// <summary>Whether queries without a keyword ask the plugin too.</summary>
    public bool InNormalSearches { get; init; } = true;

    /// <summary>
    /// How many of the plugin's results are shown at most: <see langword="null"/> for the
    /// plugin's own default.
    /// </summary>
    public int? MaxResults { get; init; }
}
