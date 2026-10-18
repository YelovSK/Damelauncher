using System.Diagnostics;
using Dame.Win32;

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

    public string Name => "Everything";

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // Files are new on every query, unlike the other providers' results, since they come from
    // another process. Reusing their list wouldn't save much next to the trip there.
    public async ValueTask<IReadOnlyList<ScoredResult>> QueryAsync(string query, CancellationToken cancellationToken)
    {
        if (await _client.SearchAsync(query, MaxResults, cancellationToken) is not { } items)
        {
            return [];
        }

        // Everything matches by its own syntax, where "report pdf" finds "report_2024.pdf", so a
        // name the fuzzy matcher can't place still counts, just below one it can.
        var results = new ScoredResult[items.Length];
        for (int index = 0; index < items.Length; index++)
        {
            EverythingItem item = items[index];
            var result = new LaunchResult(
                this,
                item.FullPath,
                item.Name,
                Path.GetDirectoryName(item.FullPath) ?? item.FullPath,
                item.FullPath,
                () => Open(item.FullPath));
            results[index] = new ScoredResult(result, ScoreOffset + (FuzzyMatcher.Score(query, item.Name) ?? 0));
        }

        return results;
    }

    public IReadOnlyList<LaunchResult> GetActions(LaunchResult result)
    {
        string path = result.Id;
        var actions = new List<LaunchResult>
        {
            new(this, "folder", "Open containing folder", result.Title, null, () => ShellIntegration.ShowInFolder(path)),
            new(this, "copy", "Copy path", path, null, () => Win32Clipboard.TrySetText(path)),
        };
        if (File.Exists(path))
        {
            actions.Add(new(this, "openwith", "Open with…", result.Title, null, () => ShellIntegration.TryShowOpenWith(0, path)));
        }

        return actions;
    }

    public void Dispose() => _client.Dispose();

    private static void Open(string path)
    {
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }
}
