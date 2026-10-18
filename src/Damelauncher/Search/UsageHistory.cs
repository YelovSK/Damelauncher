using System.Globalization;

namespace Damelauncher.Search;

/// <summary>
/// How often and how recently each result was launched, as one score per result that grows by
/// one with every launch and halves over every <see cref="HalfLife"/> without one.
/// </summary>
/// <remarks>
/// Saved as text, one result per line: the score, when it was last updated, and the result's id,
/// separated by tabs. The id comes last, so it can hold anything but a line break.
/// </remarks>
internal sealed class UsageHistory
{
    private static readonly TimeSpan HalfLife = TimeSpan.FromDays(14);
    // About two months unused after a single launch, past which an entry isn't worth keeping.
    private const double ForgottenScore = 0.05;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Damelauncher",
        "history.txt");

    private readonly Dictionary<string, Entry> _entries;

    private UsageHistory(Dictionary<string, Entry> entries) => _entries = entries;

    internal static UsageHistory Load()
    {
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        try
        {
            foreach (string line in File.ReadLines(FilePath))
            {
                string[] fields = line.Split('\t', 3);
                if (fields.Length == 3
                    && double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double score)
                    && DateTime.TryParse(fields[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime updated))
                {
                    entries[fields[2]] = new Entry(score, updated.ToUniversalTime());
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // No history yet, or none that can be read, only means nothing ranks higher for it.
        }

        return new UsageHistory(entries);
    }

    /// <returns>The result's score now, or 0 when it hasn't been launched.</returns>
    internal double GetScore(string id, DateTime now) =>
        _entries.TryGetValue(id, out Entry entry) ? entry.ScoreAt(now) : 0.0;

    internal void RecordLaunch(string id)
    {
        DateTime now = DateTime.UtcNow;
        _entries[id] = new Entry(GetScore(id, now) + 1.0, now);
        try
        {
            Save(now);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The launch still counts for this session.
        }
    }

    private void Save(DateTime now)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        // Written aside and swapped in, so a crash halfway through can't lose the whole history.
        string temporaryPath = FilePath + ".tmp";
        using (var writer = new StreamWriter(temporaryPath))
        {
            foreach ((string id, Entry entry) in _entries)
            {
                if (entry.ScoreAt(now) >= ForgottenScore)
                {
                    writer.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{entry.Score:0.####}\t{entry.Updated:O}\t{id}"));
                }
            }
        }

        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    private readonly record struct Entry(double Score, DateTime Updated)
    {
        internal double ScoreAt(DateTime now) =>
            Score * Math.Pow(0.5, Math.Max(0.0, (now - Updated) / HalfLife));
    }
}
