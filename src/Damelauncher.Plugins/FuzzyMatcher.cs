namespace Damelauncher.Plugins;

/// <summary>
/// Scores how well a query matches a name, so results from different providers rank on one scale.
/// </summary>
/// <remarks>
/// The query's characters have to appear in the name in order, but not next to each other, so
/// "vsc" finds "Visual Studio Code". Matches at word starts and runs of adjacent characters score
/// higher, as does a name that starts with the whole query.
/// </remarks>
internal static class FuzzyMatcher
{
    private const int MatchScore = 1;
    private const int WordStartBonus = 8;
    private const int AdjacentBonus = 4;
    private const int PrefixBonus = 16;
    // Keeps the length tiebreak below one point of match score.
    private const int LengthScale = 100;

    /// <returns>Higher is better, or <see langword="null"/> when the name doesn't match.</returns>
    internal static int? Score(string query, string name)
    {
        if (query.Length == 0 || query.Length > name.Length)
        {
            return null;
        }

        // Matching greedily from the first possible start can settle for a poor match, like the
        // "c" of "Command Code" for "code", so every start gets a try.
        int? best = null;
        for (int start = 0; start <= name.Length - query.Length; start++)
        {
            if (AreEqual(name[start], query[0]) && ScoreFrom(query, name, start) is { } score && score > best.GetValueOrDefault(int.MinValue))
            {
                best = score;
            }
        }

        if (best is not { } total)
        {
            return null;
        }

        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            total += PrefixBonus;
        }

        // Of two names that match equally well, the shorter is likelier the one meant.
        return (total * LengthScale) - Math.Min(name.Length, LengthScale - 1);
    }

    private static int? ScoreFrom(string query, string name, int start)
    {
        int score = 0;
        int queryIndex = 0;
        int previousMatch = -2;
        for (int nameIndex = start; nameIndex < name.Length && queryIndex < query.Length; nameIndex++)
        {
            if (!AreEqual(name[nameIndex], query[queryIndex]))
            {
                continue;
            }

            score += MatchScore;
            if (IsWordStart(name, nameIndex))
            {
                score += WordStartBonus;
            }

            if (previousMatch == nameIndex - 1)
            {
                score += AdjacentBonus;
            }

            previousMatch = nameIndex;
            queryIndex++;
        }

        return queryIndex == query.Length ? score : null;
    }

    private static bool AreEqual(char a, char b) => char.ToLowerInvariant(a) == char.ToLowerInvariant(b);

    private static bool IsWordStart(string name, int index) =>
        index == 0
        || !char.IsLetterOrDigit(name[index - 1])
        || (char.IsUpper(name[index]) && char.IsLower(name[index - 1]));
}
