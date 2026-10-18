namespace Damelauncher.Plugins;

/// <summary>
/// A source of results, such as installed programs. The launcher asks every provider about each
/// query and merges what they find by score.
/// </summary>
/// <remarks>
/// Both methods are called on the window thread, where awaits also resume. A provider that
/// needs more than a moment, to index or to ask another process, does that work elsewhere.
/// </remarks>
internal interface IResultProvider
{
    /// <summary>Prepares the provider, for example by indexing. Runs once, before its first query.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>The results for a trimmed, non-empty query. A newer query cancels this one.</summary>
    public ValueTask<IReadOnlyList<LaunchResult>> QueryAsync(string query, CancellationToken cancellationToken);
}
