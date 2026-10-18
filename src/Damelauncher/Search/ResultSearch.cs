using System.Diagnostics;
using Damelauncher.Plugins;

namespace Damelauncher.Search;

/// <summary>
/// Asks every provider about the query and merges their results as each one answers, ranking
/// results that were launched often and recently higher.
/// </summary>
/// <remarks>
/// Used on the window thread, which the providers' awaits resume on. Searching runs on every
/// keystroke, so it reuses what it can: a search whose providers all answer at once allocates
/// little more than the list of results it shows.
/// </remarks>
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
    private readonly SynchronizationContext? _context;
    private readonly Timer _deadline;
    // The best results while merging, highest score first.
    private readonly ScoredResult[] _best = new ScoredResult[MaxResults];
    private CancellationTokenSource? _query;
    private PendingQuery? _pending;
    private long _deadlineArmedAt;

    internal ResultSearch(UsageHistory history, params IResultProvider[] providers)
    {
        _history = history;
        _providers = providers;
        _context = SynchronizationContext.Current;
        _deadline = new Timer(static state => ((ResultSearch)state!).OnDeadline(), this, Timeout.Infinite, Timeout.Infinite);
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
        _pending = null;
        _deadline.Change(Timeout.Infinite, Timeout.Infinite);

        query = query.Trim();
        int enabledCount = 0;
        foreach (IResultProvider provider in _providers)
        {
            if (!DisabledProviders.Contains(provider.Id))
            {
                enabledCount++;
            }
        }

        if (query.Length == 0 || enabledCount == 0)
        {
            ResultsChanged?.Invoke([]);
            return;
        }

        // The previous results stay up until the new ones are complete, so typing doesn't flicker
        // through the partial results of whichever providers answer first.
        _query = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var pending = new PendingQuery(_providers.Length, enabledCount);
        _pending = pending;
        for (int index = 0; index < _providers.Length; index++)
        {
            if (!DisabledProviders.Contains(_providers[index].Id))
            {
                _ = QueryProviderAsync(index, query, pending, _query.Token);
            }
        }

        // Providers that answered at once are already shown, so only a slower one needs a deadline.
        if (pending.Unanswered > 0)
        {
            _deadlineArmedAt = Stopwatch.GetTimestamp();
            _deadline.Change(AnswerDeadline, Timeout.InfiniteTimeSpan);
        }
    }

    /// <param name="result">A result from <see cref="ResultsChanged"/>.</param>
    internal void RecordLaunch(LaunchResult result) => _history.RecordLaunch(result);

    /// <summary>
    /// The result's actions menu: what its provider offers for it, and forgetting its launches
    /// when it has any.
    /// </summary>
    /// <param name="result">A result from <see cref="ResultsChanged"/>.</param>
    internal IReadOnlyList<LaunchResult> GetActions(LaunchResult result)
    {
        var actions = new List<LaunchResult>(result.Provider.GetActions(result));
        if (_history.GetScore(result, DateTime.UtcNow) > 0.0)
        {
            actions.Add(new LaunchResult(
                result.Provider,
                "forget",
                "Forget launches",
                result.Title,
                null,
                () => _history.Forget(result)));
        }

        return actions;
    }

    public void Dispose()
    {
        _deadline.Dispose();
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
            pending.Answers[index] = await provider.QueryAsync(query, cancellationToken);
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

    // On the timer's thread, which only hands the deadline to the window thread. Without a
    // context to hand it to, as in benchmarks, it's handled right here.
    private void OnDeadline()
    {
        if (_context is null)
        {
            ShowPartialResults();
        }
        else
        {
            _context.Post(static state => ((ResultSearch)state!).ShowPartialResults(), this);
        }
    }

    private void ShowPartialResults()
    {
        // A deadline that passed just as a newer search started belongs to the older one, which
        // the newer search's own deadline, still ahead, tells apart.
        if (_pending is not { Unanswered: > 0 } pending
            || Stopwatch.GetElapsedTime(_deadlineArmedAt) < AnswerDeadline)
        {
            return;
        }

        pending.DeadlinePassed = true;
        ResultsChanged?.Invoke(Merge(pending.Answers));
    }

    // Keeps the best results in order as they come instead of sorting them all, since a short
    // query can match hundreds. Each goes after those with an equal score, so ties keep the
    // providers' order and each provider's own.
    private LaunchResult[] Merge(IReadOnlyList<ScoredResult>?[] answers)
    {
        DateTime now = DateTime.UtcNow;
        int count = 0;
        foreach (IReadOnlyList<ScoredResult>? results in answers)
        {
            if (results is null)
            {
                continue;
            }

            for (int index = 0; index < results.Count; index++)
            {
                LaunchResult result = results[index].Result;
                int score = results[index].Score + GetBoost(result, now);
                if (count == MaxResults && score <= _best[MaxResults - 1].Score)
                {
                    continue;
                }

                int position = count;
                while (position > 0 && _best[position - 1].Score < score)
                {
                    position--;
                }

                int last = Math.Min(count, MaxResults - 1);
                Array.Copy(_best, position, _best, position + 1, last - position);
                _best[position] = new ScoredResult(result, score);
                count = Math.Min(count + 1, MaxResults);
            }
        }

        if (count == 0)
        {
            return [];
        }

        var merged = new LaunchResult[count];
        for (int index = 0; index < count; index++)
        {
            merged[index] = _best[index].Result;
        }

        Array.Clear(_best, 0, count);
        return merged;
    }

    private int GetBoost(LaunchResult result, DateTime now)
    {
        double usage = _history.GetScore(result, now);
        if (usage == 0.0)
        {
            return 0;
        }

        double points = Math.Min(MaxBoostPoints, BoostPointsPerDoubling * Math.Log2(1.0 + usage));
        return (int)(points * FuzzyMatcher.Point);
    }

    /// <summary>One query's answers so far, one slot per provider, which stays empty for one that's disabled.</summary>
    private sealed class PendingQuery(int providerCount, int askedCount)
    {
        internal IReadOnlyList<ScoredResult>?[] Answers { get; } = new IReadOnlyList<ScoredResult>?[providerCount];
        internal int Unanswered { get; set; } = askedCount;
        internal bool DeadlinePassed { get; set; }
    }
}
