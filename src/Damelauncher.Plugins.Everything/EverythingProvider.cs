using System.Diagnostics;

namespace Damelauncher.Plugins.Everything;

/// <summary>Finds files and folders anywhere through Everything, when it's running.</summary>
internal sealed class EverythingProvider : IResultProvider, IDisposable
{
    private const int MaxResults = 20;
    // Files rank below equally good matches from other providers, which are far fewer and
    // likelier the ones meant, but a file whose name matches well still beats a poor match.
    private const int ScoreOffset = -20 * FuzzyMatcher.Point;

    private readonly EverythingClient _client = new();

    public string Id => "everything";

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async ValueTask<IReadOnlyList<LaunchResult>> QueryAsync(string query, CancellationToken cancellationToken)
    {
        if (await _client.SearchAsync(query, MaxResults, cancellationToken) is not { } items)
        {
            return [];
        }

        // Everything matches by its own syntax, where "report pdf" finds "report_2024.pdf", so a
        // name the fuzzy matcher can't place still counts, just below one it can.
        return
        [
            .. items.Select(item => new LaunchResult(
                item.FullPath,
                item.Name,
                Path.GetDirectoryName(item.FullPath) ?? item.FullPath,
                ScoreOffset + (FuzzyMatcher.Score(query, item.Name) ?? 0),
                () => Open(item.FullPath))),
        ];
    }

    public void Dispose() => _client.Dispose();

    private static void Open(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
