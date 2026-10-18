using Damelauncher.Plugins;

namespace Damelauncher.Search;

/// <summary>Commands for the launcher itself.</summary>
internal sealed class LauncherCommandsProvider(Action quit) : IResultProvider
{
    private const string QuitTitle = "Quit Damelauncher";

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask<IReadOnlyList<LaunchResult>> QueryAsync(string query, CancellationToken cancellationToken)
    {
        IReadOnlyList<LaunchResult> results = FuzzyMatcher.Score(query, QuitTitle) is { } score
            ? [new LaunchResult(QuitTitle, "Exit the launcher", score, quit)]
            : [];
        return ValueTask.FromResult(results);
    }
}
