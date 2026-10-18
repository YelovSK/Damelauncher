using System.Diagnostics;
using Damelauncher.Plugins;

namespace Damelauncher.Search;

/// <summary>
/// Asks every provider about the query and merges their results as each one answers, ranking
/// results that were launched often and recently higher.
/// </summary>
/// <remarks>Used on the window thread, which the providers' awaits resume on.</remarks>
internal sealed class ResultSearch : IDisposable
{
    private const int MaxResults = 50;
    // How long a query's results wait for every provider to answer before showing what's there.
    private static readonly TimeSpan AnswerDeadline = TimeSpan.FromMilliseconds(100);
    // The boost grows with each doubling of a result's usage, so the first few launches count
    // the most, and stops at a cap, so habit can't outrank a much better match.
    private const double BoostPointsPerDoubling = 6.0;
    private const double MaxBoostPoints = 24.0;

    private readonly UsageHistory _history;
    private readonly IResultProvider[] _providers;
    private readonly Task[] _initializations;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _query;

    internal ResultSearch(UsageHistory history, params IResultProvider[] providers)
    {
        _history = history;
        _providers = providers;
        _initializations = [.. providers.Select(provider => provider.InitializeAsync(_lifetime.Token))];
    }

    /// <summary>
    /// The latest query's results, once every provider has answered or the deadline passed, and
    /// again for each provider that answers after it.
    /// </summary>
    internal event Action<IReadOnlyList<LaunchResult>>? ResultsChanged;

    /// <summary>The ids of the providers left out of searches, from the next one on.</summary>
    internal IReadOnlySet<string> DisabledProviders { get; set; } = new HashSet<string>();

    internal void Search(string query)
    {
        _query?.Cancel();
        _query?.Dispose();
        _query = null;

        query = query.Trim();
        int[] enabled = [.. Enumerable.Range(0, _providers.Length).Where(index => !DisabledProviders.Contains(_providers[index].Id))];
        if (query.Length == 0 || enabled.Length == 0)
        {
            ResultsChanged?.Invoke([]);
            return;
        }

        // The previous results stay up until the new ones are complete, so typing doesn't flicker
        // through the partial results of whichever providers answer first.
        _query = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var pending = new PendingQuery(_providers.Length, enabled.Length);
        foreach (int index in enabled)
        {
            _ = QueryProviderAsync(index, query, pending, _query.Token);
        }

        _ = AwaitDeadlineAsync(pending, _query.Token);
    }

    /// <param name="result">A result from <see cref="ResultsChanged"/>.</param>
    internal void RecordLaunch(LaunchResult result) => _history.RecordLaunch(result.Id);

    public void Dispose()
    {
        _lifetime.Cancel();
        _query?.Dispose();
        _lifetime.Dispose();
        foreach (IResultProvider provider in _providers)
        {
            (provider as IDisposable)?.Dispose();
        }
    }

    private async Task QueryProviderAsync(
        int index,
        string query,
        PendingQuery pending,
        CancellationToken cancellationToken)
    {
        IResultProvider provider = _providers[index];
        try
        {
            await _initializations[index].WaitAsync(cancellationToken);
            pending.Answers[index] = Rank(provider, await provider.QueryAsync(query, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            // One broken provider shouldn't take the others' results down with it.
            Debug.WriteLine($"{provider.GetType().Name} failed: {exception}");
            pending.Answers[index] = [];
        }

        pending.Unanswered--;
        if (!cancellationToken.IsCancellationRequested && (pending.Unanswered == 0 || pending.DeadlinePassed))
        {
            ResultsChanged?.Invoke(Merge(pending.Answers));
        }
    }

    private async Task AwaitDeadlineAsync(PendingQuery pending, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(AnswerDeadline, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        pending.DeadlinePassed = true;
        // With every answer in, the results were already shown when the last one arrived.
        if (pending.Unanswered > 0)
        {
            ResultsChanged?.Invoke(Merge(pending.Answers));
        }
    }

    // Qualifies each id with its provider's, which keeps ids from different providers apart in the history.
    private LaunchResult[] Rank(IResultProvider provider, IReadOnlyList<LaunchResult> results)
    {
        DateTime now = DateTime.UtcNow;
        return
        [
            .. results.Select(result =>
            {
                string id = $"{provider.Id}:{result.Id}";
                return result with { Id = id, Score = result.Score + GetBoost(id, now) };
            }),
        ];
    }

    private int GetBoost(string id, DateTime now)
    {
        double points = Math.Min(MaxBoostPoints, BoostPointsPerDoubling * Math.Log2(1.0 + _history.GetScore(id, now)));
        return (int)(points * FuzzyMatcher.Point);
    }

    // A stable sort, so equal scores keep the providers' order and each provider's own order.
    private static LaunchResult[] Merge(IReadOnlyList<LaunchResult>?[] answers) =>
    [
        .. answers
            .SelectMany(results => results ?? [])
            .OrderByDescending(result => result.Score)
            .Take(MaxResults),
    ];

    /// <summary>One query's answers so far, one slot per provider, which stays empty for one that's disabled.</summary>
    private sealed class PendingQuery(int providerCount, int askedCount)
    {
        internal IReadOnlyList<LaunchResult>?[] Answers { get; } = new IReadOnlyList<LaunchResult>?[providerCount];
        internal int Unanswered { get; set; } = askedCount;
        internal bool DeadlinePassed { get; set; }
    }
}
