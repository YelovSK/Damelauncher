using Dame.Win32;

namespace Damelauncher.Plugins.Programs;

/// <summary>Finds installed programs and apps, as the Start menu lists them.</summary>
internal sealed class ProgramsProvider : IResultProvider
{
    private ShellApp[] _apps = [];

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        using var worker = new ComWorkerQueue<object?>("Programs index", 1, static () => null);
        List<ShellApp> apps = await worker.Enqueue(
            static (_, _) => ShellAppsFolder.Enumerate(),
            cancellationToken: cancellationToken);
        _apps = [.. apps];
    }

    public ValueTask<IReadOnlyList<LaunchResult>> QueryAsync(string query, CancellationToken cancellationToken)
    {
        var results = new List<LaunchResult>();
        foreach (ShellApp app in _apps)
        {
            if (FuzzyMatcher.Score(query, app.Name) is { } score)
            {
                results.Add(new LaunchResult(app.Name, "App", score, () => ShellAppsFolder.Launch(app.ParsingName)));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<LaunchResult>>(results);
    }
}
