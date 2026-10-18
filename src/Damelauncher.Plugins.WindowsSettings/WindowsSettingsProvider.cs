namespace Damelauncher.Plugins.WindowsSettings;

/// <summary>Finds Settings pages and power commands such as sleep or restart.</summary>
internal sealed class WindowsSettingsProvider : IResultProvider
{
    private readonly SettingsEntry[] _entries = [.. PowerCommands.Create(), .. SettingsPages.Create()];

    public string Id => "settings";

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask<IReadOnlyList<LaunchResult>> QueryAsync(string query, CancellationToken cancellationToken)
    {
        var results = new List<LaunchResult>();
        foreach (SettingsEntry entry in _entries)
        {
            if (entry.Score(query) is { } score)
            {
                results.Add(new LaunchResult(entry.Id, entry.Title, entry.Subtitle, entry.IconSource, score, entry.Execute));
            }
        }

        return ValueTask.FromResult<IReadOnlyList<LaunchResult>>(results);
    }
}
