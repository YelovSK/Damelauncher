using Dame.Win32;

namespace Damelauncher.Plugins.Programs;

/// <summary>Finds installed programs and apps, as the Start menu lists them.</summary>
internal sealed class ProgramsProvider : IResultProvider, IDisposable
{
    private readonly List<ScoredResult> _matches = [];
    private readonly ComWorkerQueue<object?> _worker = new("Programs index", 1, static () => null);
    private LaunchResult[] _programs = [];
    private Dictionary<string, Uninstallers.Command> _uninstallers = [];
    private IDisposable? _watcher;
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

    public IReadOnlyList<LaunchResult> GetActions(LaunchResult result)
    {
        if (ShellAppsFolder.IsPackaged(result.Id))
        {
            return [CreateUninstallAction(result, () => ShellAppsFolder.UninstallPackage(result.Id))];
        }

        var runAsAdministrator = new LaunchResult(
            this,
            "runas",
            "Run as administrator",
            result.Title,
            null,
            () => ShellAppsFolder.Launch(result.Id, asAdministrator: true));
        return _uninstallers.TryGetValue(result.Id, out Uninstallers.Command? uninstaller)
            ? [runAsAdministrator, CreateUninstallAction(result, () => ConfirmUninstall(result.Title, uninstaller))]
            : [runAsAdministrator];
    }

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
                (List<ShellApp> apps, Dictionary<string, Uninstallers.Command> uninstallers) = await _worker.Enqueue(
                    static (_, _) => FindApps(),
                    cancellationToken: _lifetime);
                // Equal scores keep this order, so the whole list, shown for an empty query, reads alphabetically.
                _programs = [.. apps.Select(CreateResult).OrderBy(program => program.Title, StringComparer.CurrentCultureIgnoreCase)];
                _uninstallers = uninstallers;
                Changed?.Invoke();
            }
            while (_indexAgain);
        }
        finally
        {
            _indexing = false;
        }
    }

    private static (List<ShellApp> Apps, Dictionary<string, Uninstallers.Command> Uninstallers) FindApps()
    {
        List<ShellApp> apps = ShellAppsFolder.Enumerate();
        var uninstallers = Uninstallers.Load();
        var found = new Dictionary<string, Uninstallers.Command>();
        foreach (ShellApp app in apps)
        {
            if (app.TargetPath is { } targetPath && uninstallers.Find(app.Name, targetPath) is { } uninstaller)
            {
                found[app.ParsingName] = uninstaller;
            }
        }

        return (apps, found);
    }

    // Some uninstallers remove the program without asking, and the match to the app is only a guess,
    // so the command is shown for a wrong one to stand out.
    private static void ConfirmUninstall(string title, Uninstallers.Command uninstaller)
    {
        if (MessageDialog.Confirm($"Uninstall {title}", $"Uninstall {title}? This runs:\n\n\"{uninstaller.Path}\" {uninstaller.Arguments}"))
        {
            uninstaller.Run();
        }
    }

    private LaunchResult CreateUninstallAction(LaunchResult result, Action uninstall) =>
        new(this, "uninstall", "Uninstall", result.Title, null, uninstall);

    private LaunchResult CreateResult(ShellApp app) =>
        new(
            this,
            app.ParsingName,
            app.Name,
            "App",
            new ShellIcon(ShellAppsFolder.GetPath(app.ParsingName)),
            () => ShellAppsFolder.Launch(app.ParsingName));
}
