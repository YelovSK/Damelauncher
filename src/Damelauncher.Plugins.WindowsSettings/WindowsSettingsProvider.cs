namespace Damelauncher.Plugins.WindowsSettings;

/// <summary>Finds Settings pages and power commands such as sleep or restart.</summary>
internal sealed class WindowsSettingsProvider : IResultProvider
{
    private readonly SettingsEntry[] _entries;
    private readonly List<ScoredResult> _matches = [];

    internal WindowsSettingsProvider() => _entries = [.. PowerCommands.Create(this), .. SettingsPages.Create(this)];

    public string Id => "settings";

    public string Name => "Windows settings";

    public string DefaultKeyword => "s";

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask<IReadOnlyList<ScoredResult>> QueryAsync(string query, CancellationToken cancellationToken)
    {
        _matches.Clear();
        foreach (SettingsEntry entry in _entries)
        {
            if (entry.Score(query) is { } score)
            {
                _matches.Add(new ScoredResult(entry.Result, score));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<ScoredResult>>(_matches);
    }
}
