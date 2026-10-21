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
    // How long a query's results wait for every provider to answer before showing what's there.
    private static readonly TimeSpan AnswerDeadline = TimeSpan.FromMilliseconds(100);
    // The boost grows with each doubling of a result's usage, so the first few launches count
    // the most, and stops at a cap, so habit can't outrank a much better match.
    private const double BoostPointsPerDoubling = 6.0;
    private const double MaxBoostPoints = 24.0;

    private readonly UsageHistory _history;
    private readonly IResultProvider[] _providers;
    private readonly ProviderRouting[] _routing;
    // Which providers the current query asks, refilled for every query.
    private readonly bool[] _asked;
    private readonly Task[] _initializations;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SynchronizationContext? _context;
    private readonly Timer _deadline;
    // The best results while merging, highest score first: all of them, and one provider's.
    private ScoredResult[] _best = new ScoredResult[50];
    private ScoredResult[] _providerBest = new ScoredResult[50];
    private CancellationTokenSource? _query;
    private string _lastQuery = "";
    private PendingQuery? _pending;
    private long _deadlineArmedAt;

    internal ResultSearch(UsageHistory history, params IResultProvider[] providers)
    {
        _history = history;
        _providers = providers;
        _routing = [.. providers.Select(_ => ProviderRouting.Default)];
        _asked = new bool[providers.Length];
        _context = SynchronizationContext.Current;
        _deadline = new Timer(static state => ((ResultSearch)state!).OnDeadline(), this, Timeout.Infinite, Timeout.Infinite);
        foreach (IResultProvider provider in providers)
        {
            provider.Changed += SearchAgain;
        }

        _initializations = [.. providers.Select(provider => provider.InitializeAsync(_lifetime.Token))];
    }

    /// <summary>
    /// The latest query's results, once every provider has answered or the deadline passed, and
    /// again for each provider that answers after it.
    /// </summary>
    internal event Action<IReadOnlyList<LaunchResult>>? ResultsChanged;

    /// <summary>How many results are shown at most, from the next search on.</summary>
    internal int MaxResults
    {
        get => _best.Length;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _best = new ScoredResult[value];
            _providerBest = new ScoredResult[value];
        }
    }

    /// <summary>Changes which queries reach a provider, and how many of its results show, from the next search on.</summary>
    internal void SetRouting(string providerId, ProviderRouting routing)
    {
        int index = Array.FindIndex(_providers, provider => provider.Id == providerId);
        if (index >= 0)
        {
            _routing[index] = routing;
        }
    }

    internal void Search(string query)
    {
        _lastQuery = query;
        _query?.Cancel();
        _query?.Dispose();
        _query = null;
        _pending = null;
        _deadline.Change(Timeout.Infinite, Timeout.Infinite);

        int askedCount = ChooseProviders(query, out string providerQuery);
        if (providerQuery.Length == 0 || askedCount == 0)
        {
            ResultsChanged?.Invoke([]);
            return;
        }

        // The previous results stay up until the new ones are complete, so typing doesn't flicker
        // through the partial results of whichever providers answer first.
        _query = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var pending = new PendingQuery(_providers.Length, askedCount);
        _pending = pending;
        for (int index = 0; index < _providers.Length; index++)
        {
            if (_asked[index])
            {
                _ = QueryProviderAsync(index, providerQuery, pending, _query.Token);
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
            provider.Changed -= SearchAgain;
            (provider as IDisposable)?.Dispose();
        }
    }

    // Only a query that asked providers has results that can be out of date.
    private void SearchAgain()
    {
        if (_pending is not null)
        {
            Search(_lastQuery);
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
            pending.Answers[index] = await provider.QueryAsync(query, GetMaxResults(index), cancellationToken);
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

    /// <summary>
    /// Marks the providers a query asks: the ones its keyword belongs to, when it starts with
    /// one and a space, and otherwise the ones in normal searches.
    /// </summary>
    /// <param name="providerQuery">What they're asked, without the keyword or surrounding spaces.</param>
    /// <returns>How many are asked.</returns>
    private int ChooseProviders(string query, out string providerQuery)
    {
        string text = query.TrimStart();
        int separator = text.IndexOf(' ', StringComparison.Ordinal);
        if (separator > 0)
        {
            ReadOnlySpan<char> keyword = text.AsSpan(0, separator);
            int keywordCount = 0;
            for (int index = 0; index < _providers.Length; index++)
            {
                _asked[index] = _routing[index] is { Enabled: true, Keyword: { } providerKeyword }
                    && keyword.Equals(providerKeyword, StringComparison.OrdinalIgnoreCase);
                keywordCount += _asked[index] ? 1 : 0;
            }

            if (keywordCount > 0)
            {
                providerQuery = text[(separator + 1)..].Trim();
                return keywordCount;
            }
        }

        int normalCount = 0;
        for (int index = 0; index < _providers.Length; index++)
        {
            _asked[index] = _routing[index] is { Enabled: true, InNormalSearches: true };
            normalCount += _asked[index] ? 1 : 0;
        }

        providerQuery = text.TrimEnd();
        return normalCount;
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
    // query can match hundreds. A provider's own limit keeps its best ones first, scored with
    // their launch history, so one that's used often isn't the one cut.
    private LaunchResult[] Merge(IReadOnlyList<ScoredResult>?[] answers)
    {
        DateTime now = DateTime.UtcNow;
        int count = 0;
        for (int provider = 0; provider < answers.Length; provider++)
        {
            if (answers[provider] is not { } results)
            {
                continue;
            }

            int providerCount = 0;
            int providerLimit = GetMaxResults(provider);
            for (int index = 0; index < results.Count; index++)
            {
                LaunchResult result = results[index].Result;
                var scored = new ScoredResult(result, results[index].Score + GetBoost(result, now));
                Insert(_providerBest, ref providerCount, providerLimit, scored);
            }

            for (int index = 0; index < providerCount; index++)
            {
                Insert(_best, ref count, _best.Length, _providerBest[index]);
            }

            Array.Clear(_providerBest, 0, providerCount);
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

    // Places a result among the best so far, after those with an equal score, so ties keep the
    // order they came in. A full buffer drops its lowest.
    private static void Insert(ScoredResult[] best, ref int count, int capacity, ScoredResult candidate)
    {
        if (count == capacity && candidate.Score <= best[capacity - 1].Score)
        {
            return;
        }

        int position = count;
        while (position > 0 && best[position - 1].Score < candidate.Score)
        {
            position--;
        }

        int last = Math.Min(count, capacity - 1);
        Array.Copy(best, position, best, position + 1, last - position);
        best[position] = candidate;
        count = Math.Min(count + 1, capacity);
    }

    private int GetMaxResults(int provider) => Math.Min(_routing[provider].MaxResults ?? _best.Length, _best.Length);

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
