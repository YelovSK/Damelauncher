using System.Globalization;
using Damelauncher.Plugins;

namespace Damelauncher.Search;

/// <summary>
/// How often and how recently each result was launched, as one score per result that grows by
/// one with every launch and halves over every <see cref="HalfLife"/> without one.
/// </summary>
/// <remarks>
/// Saved as text, one result per line: the score, when it was last updated, and the result's
/// provider and id joined by a colon, separated by tabs. The id comes last, so it can hold
/// anything but a line break.
/// </remarks>
internal sealed class UsageHistory
{
    private static readonly TimeSpan HalfLife = TimeSpan.FromDays(14);
    // About two months unused after a single launch, past which an entry isn't worth keeping.
    private const double ForgottenScore = 0.05;

    internal static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Damelauncher",
        "history.txt");

    private readonly string _path;
    private readonly Dictionary<Key, Entry> _entries;

    private UsageHistory(string path, Dictionary<Key, Entry> entries)
    {
        _path = path;
        _entries = entries;
    }

    /// <param name="path">Where the history is read from, and saved to on every change.</param>
    internal static UsageHistory Load(string path)
    {
        var entries = new Dictionary<Key, Entry>();
        try
        {
            foreach (string line in File.ReadLines(path))
            {
                string[] fields = line.Split('\t', 3);
                if (fields.Length == 3
                    && double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double score)
                    && DateTime.TryParse(fields[1], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime updated)
                    && fields[2].Split(':', 2) is [string provider, string id])
                {
                    entries[new Key(provider, id)] = new Entry(score, updated.ToUniversalTime());
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // No history yet, or none that can be read, only means nothing ranks higher for it.
        }

        return new UsageHistory(path, entries);
    }

    /// <returns>The result's score now, or 0 when it hasn't been launched.</returns>
    internal double GetScore(LaunchResult result, DateTime now) =>
        _entries.TryGetValue(GetKey(result), out Entry entry) ? entry.ScoreAt(now) : 0.0;

    internal void RecordLaunch(LaunchResult result)
    {
        DateTime now = DateTime.UtcNow;
        _entries[GetKey(result)] = new Entry(GetScore(result, now) + 1.0, now);
        TrySave(now);
    }

    internal void Forget(LaunchResult result)
    {
        if (_entries.Remove(GetKey(result)))
        {
            TrySave(DateTime.UtcNow);
        }
    }

    // Made of strings the result already holds, so looking one up allocates nothing.
    private static Key GetKey(LaunchResult result) => new(result.Provider.Id, result.Id);

    private void TrySave(DateTime now)
    {
        try
        {
            Save(now);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The change still counts for this session.
        }
    }

    private void Save(DateTime now)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        // Written aside and swapped in, so a crash halfway through can't lose the whole history.
        string temporaryPath = _path + ".tmp";
        using (var writer = new StreamWriter(temporaryPath))
        {
            foreach ((Key key, Entry entry) in _entries)
            {
                if (entry.ScoreAt(now) >= ForgottenScore)
                {
                    writer.WriteLine(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{entry.Score:0.####}\t{entry.Updated:O}\t{key.Provider}:{key.Id}"));
                }
            }
        }

        File.Move(temporaryPath, _path, overwrite: true);
    }

    private readonly record struct Key(string Provider, string Id);

    private readonly record struct Entry(double Score, DateTime Updated)
    {
        internal double ScoreAt(DateTime now) =>
            Score * Math.Pow(0.5, Math.Max(0.0, (now - Updated) / HalfLife));
    }
}
