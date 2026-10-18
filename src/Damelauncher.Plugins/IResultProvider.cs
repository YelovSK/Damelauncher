namespace Damelauncher.Plugins;

/// <summary>
/// A source of results, such as installed programs. The launcher asks every provider about each
/// query and merges what they find by score.
/// </summary>
/// <remarks>
/// Both methods are called on the window thread, where awaits also resume. A provider that
/// needs more than a moment, to index or to ask another process, does that work elsewhere.
/// The launcher disposes a provider that is <see cref="IDisposable"/> when it exits.
/// </remarks>
internal interface IResultProvider
{
    /// <summary>A short name, unique among providers, that saved usage history refers to.</summary>
    public string Id { get; }

    /// <summary>What the provider is called in the settings.</summary>
    public string Name { get; }

    /// <summary>
    /// The keyword that, typed before a query with a space, asks only this provider, until the
    /// user picks another. <see langword="null"/> for none.
    /// </summary>
    public string? DefaultKeyword => null;

    /// <summary>Prepares the provider, for example by indexing. Runs once, before its first query.</summary>
    public Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// How many of the provider's results are shown at most, until the user picks another
    /// number. <see langword="null"/> to be limited only by how many the launcher shows in all.
    /// </summary>
    public int? DefaultMaxResults => null;

    /// <summary>The results for a trimmed, non-empty query. A newer query cancels this one.</summary>
    /// <param name="maxResults">
    /// How many of them can be shown. The launcher keeps the best that many itself, so a provider
    /// only needs this when finding fewer saves it work, such as asking another process for less.
    /// </param>
    /// <remarks>
    /// The launcher is done with the list before it asks again, so a provider can hand out the
    /// same list every time, refilled, and keep a keystroke from allocating.
    /// </remarks>
    public ValueTask<IReadOnlyList<ScoredResult>> QueryAsync(string query, int maxResults, CancellationToken cancellationToken);

    /// <summary>
    /// What else can be done with one of this provider's results, such as running it as
    /// administrator, which the launcher offers in the result's actions menu.
    /// </summary>
    /// <param name="result">One of this provider's results.</param>
    public IReadOnlyList<LaunchResult> GetActions(LaunchResult result) => [];
}
