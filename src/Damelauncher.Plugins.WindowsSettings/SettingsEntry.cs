namespace Damelauncher.Plugins.WindowsSettings;

/// <param name="Keywords">Other words people search for it by, such as "wallpaper" for Background.</param>
internal sealed record SettingsEntry(LaunchResult Result, string[] Keywords)
{
    // A keyword match ranks just below an equally good match on the title.
    private const int KeywordPenalty = FuzzyMatcher.Point;

    internal int? Score(string query)
    {
        int? best = FuzzyMatcher.Score(query, Result.Title);
        foreach (string keyword in Keywords)
        {
            if (FuzzyMatcher.Score(query, keyword) - KeywordPenalty is { } score && score > best.GetValueOrDefault(int.MinValue))
            {
                best = score;
            }
        }

        return best;
    }
}
