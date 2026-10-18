using Damelauncher.Plugins;

namespace Damelauncher.Search;

/// <summary>Commands for the launcher itself.</summary>
internal sealed class LauncherCommandsProvider : IResultProvider
{
    private readonly LaunchResult[] _commands;

    internal LauncherCommandsProvider(Action openSettings, Action quit)
    {
        string? icon = Environment.ProcessPath;
        _commands =
        [
            new LaunchResult("settings", "Damelauncher Settings", "Hotkey, plugins and startup", icon, 0, openSettings),
            new LaunchResult("quit", "Quit Damelauncher", "Exit the launcher", icon, 0, quit),
        ];
    }

    public string Id => "launcher";

    public string Name => "Damelauncher";

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask<IReadOnlyList<LaunchResult>> QueryAsync(string query, CancellationToken cancellationToken)
    {
        var results = new List<LaunchResult>();
        foreach (LaunchResult command in _commands)
        {
            if (FuzzyMatcher.Score(query, command.Title) is { } score)
            {
                results.Add(command with { Score = score });
            }
        }

        return ValueTask.FromResult<IReadOnlyList<LaunchResult>>(results);
    }
}
