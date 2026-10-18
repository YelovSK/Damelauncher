using Damelauncher.Plugins;

namespace Damelauncher.Search;

/// <summary>Commands for the launcher itself.</summary>
internal sealed class LauncherCommandsProvider : IResultProvider
{
    private readonly LaunchResult[] _commands;
    private readonly List<ScoredResult> _matches = [];

    internal LauncherCommandsProvider(Action openSettings, Action quit)
    {
        string? icon = Environment.ProcessPath;
        _commands =
        [
            new LaunchResult(this, "settings", "Damelauncher Settings", "Hotkey, plugins and startup", icon, openSettings),
            new LaunchResult(this, "quit", "Quit Damelauncher", "Exit the launcher", icon, quit),
        ];
    }

    public string Id => "launcher";

    public string Name => "Damelauncher";

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask<IReadOnlyList<ScoredResult>> QueryAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        _matches.Clear();
        foreach (LaunchResult command in _commands)
        {
            if (FuzzyMatcher.Score(query, command.Title) is { } score)
            {
                _matches.Add(new ScoredResult(command, score));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<ScoredResult>>(_matches);
    }
}
