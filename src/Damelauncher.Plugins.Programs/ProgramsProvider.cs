using Dame.Win32;

namespace Damelauncher.Plugins.Programs;

/// <summary>Finds installed programs and apps, as the Start menu lists them.</summary>
internal sealed class ProgramsProvider : IResultProvider, IDisposable
{
    private readonly List<ScoredResult> _matches = [];
    private readonly ComWorkerQueue<object?> _worker = new("Programs index", 1, static () => null);
    private LaunchResult[] _programs = [];
    private ShellChangeWatcher? _watcher;
    private CancellationToken _lifetime;
    private bool _indexing;
    private bool _indexAgain;

    public event Action? Changed;

    public string Id => "programs";

    public string Name => "Programs";

    public string DefaultKeyword => "p";

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _lifetime = cancellationToken;
        // Watching first, so an app installed during the first index isn't missed.
        _watcher = ShellAppsFolder.WatchChanges(Reindex);
        await IndexAsync();
    }

    public ValueTask<IReadOnlyList<ScoredResult>> QueryAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        _matches.Clear();
        foreach (LaunchResult program in _programs)
        {
            if (FuzzyMatcher.Score(query, program.Title) is { } score)
            {
                _matches.Add(new ScoredResult(program, score));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<ScoredResult>>(_matches);
    }

    public IReadOnlyList<LaunchResult> GetActions(LaunchResult result) =>
        ShellAppsFolder.IsPackaged(result.Id)
            ? []
            :
            [
                new LaunchResult(
                    this,
                    "runas",
                    "Run as administrator",
                    result.Title,
                    null,
                    () => ShellAppsFolder.Launch(result.Id, asAdministrator: true)),
            ];

    public void Dispose()
    {
        _watcher?.Dispose();
        _worker.Dispose();
    }

    // An install arrives as several changes in a row, so the ones during an index add up to one more.
    private void Reindex()
    {
        if (_indexing)
        {
            _indexAgain = true;
        }
        else
        {
            _ = IndexAsync();
        }
    }

    private async Task IndexAsync()
    {
        _indexing = true;
        try
        {
            do
            {
                _indexAgain = false;
                List<ShellApp> apps = await _worker.Enqueue(
                    static (_, _) => ShellAppsFolder.Enumerate(),
                    cancellationToken: _lifetime);
                // Equal scores keep this order, so the whole list, shown for an empty query, reads alphabetically.
                _programs = [.. apps.Select(CreateResult).OrderBy(program => program.Title, StringComparer.CurrentCultureIgnoreCase)];
                Changed?.Invoke();
            }
            while (_indexAgain);
        }
        finally
        {
            _indexing = false;
        }
    }

    private LaunchResult CreateResult(ShellApp app) =>
        new(
            this,
            app.ParsingName,
            app.Name,
            "App",
            new ShellIcon(ShellAppsFolder.GetPath(app.ParsingName)),
            () => ShellAppsFolder.Launch(app.ParsingName));
}
