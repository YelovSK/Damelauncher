using Dame.Win32;

namespace Damelauncher.Plugins.Programs;

/// <summary>Finds installed programs and apps, as the Start menu lists them.</summary>
internal sealed class ProgramsProvider : IResultProvider
{
    private readonly List<ScoredResult> _matches = [];
    private LaunchResult[] _programs = [];

    public string Id => "programs";

    public string Name => "Programs";

    public string DefaultKeyword => "p";

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var worker = new ComWorkerQueue<object?>("Programs index", 1, static () => null);
        List<ShellApp> apps = await worker.Enqueue(
            static (_, _) => ShellAppsFolder.Enumerate(),
            cancellationToken: cancellationToken);
        _programs = [.. apps.Select(CreateResult)];
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

    private LaunchResult CreateResult(ShellApp app) =>
        new(
            this,
            app.ParsingName,
            app.Name,
            "App",
            new ShellIcon(ShellAppsFolder.GetPath(app.ParsingName)),
            () => ShellAppsFolder.Launch(app.ParsingName));
}
