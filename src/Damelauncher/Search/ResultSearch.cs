using System.Diagnostics;
using Damelauncher.Plugins;

namespace Damelauncher.Search;

/// <summary>Asks every provider about the query and merges their results as each one answers.</summary>
/// <remarks>Used on the window thread, which the providers' awaits resume on.</remarks>
internal sealed class ResultSearch : IDisposable
{
    private const int MaxResults = 50;

    private readonly IResultProvider[] _providers;
    private readonly Task[] _initializations;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _query;

    internal ResultSearch(params IResultProvider[] providers)
    {
        _providers = providers;
        _initializations = [.. providers.Select(provider => provider.InitializeAsync(_lifetime.Token))];
    }

    /// <summary>The latest query's results, raised again each time another provider answers.</summary>
    internal event Action<IReadOnlyList<LaunchResult>>? ResultsChanged;

    internal void Search(string query)
    {
        _query?.Cancel();
        _query?.Dispose();
        _query = null;

        query = query.Trim();
        if (query.Length == 0)
        {
            ResultsChanged?.Invoke([]);
            return;
        }

        // The previous results stay up until the first provider answers, so typing doesn't flicker.
        _query = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var answers = new IReadOnlyList<LaunchResult>?[_providers.Length];
        for (int index = 0; index < _providers.Length; index++)
        {
            _ = QueryProviderAsync(index, query, answers, _query.Token);
        }
    }

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
        IReadOnlyList<LaunchResult>?[] answers,
        CancellationToken cancellationToken)
    {
        IResultProvider provider = _providers[index];
        try
        {
            await _initializations[index].WaitAsync(cancellationToken);
            answers[index] = await provider.QueryAsync(query, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            // One broken provider shouldn't take the others' results down with it.
            Debug.WriteLine($"{provider.GetType().Name} failed: {exception}");
            answers[index] = [];
        }

        if (!cancellationToken.IsCancellationRequested)
        {
            ResultsChanged?.Invoke(Merge(answers));
        }
    }

    // A stable sort, so equal scores keep the providers' order and each provider's own order.
    private static LaunchResult[] Merge(IReadOnlyList<LaunchResult>?[] answers) =>
    [
        .. answers
            .SelectMany(results => results ?? [])
            .OrderByDescending(result => result.Score)
            .Take(MaxResults),
    ];
}
