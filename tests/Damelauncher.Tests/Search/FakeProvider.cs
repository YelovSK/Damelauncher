using Damelauncher.Plugins;

namespace Damelauncher.Tests.Search;

/// <summary>A provider whose answers each test decides: right away, or later, when the test completes them.</summary>
internal sealed class FakeProvider(string id) : IResultProvider
{
    // Refilled for every query, the way the real providers keep a keystroke from allocating.
    private readonly List<ScoredResult> _answer = [];

    public string Id => id;

    public string Name => id;

    /// <summary>What a query finds, or <see langword="null"/> to answer only once the test completes it.</summary>
    internal Func<string, IEnumerable<ScoredResult>>? Answer { get; set; } = _ => [];

    internal Exception? Failure { get; set; }

    internal List<LaunchResult> Actions { get; } = [];

    internal List<string> Queries { get; } = [];

    /// <summary>The queries answered later, in the order they were asked.</summary>
    internal List<TaskCompletionSource<IReadOnlyList<ScoredResult>>> Pending { get; } = [];

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ValueTask<IReadOnlyList<ScoredResult>> QueryAsync(string query, CancellationToken cancellationToken)
    {
        Queries.Add(query);
        if (Failure is not null)
        {
            throw Failure;
        }

        if (Answer is null)
        {
            var later = new TaskCompletionSource<IReadOnlyList<ScoredResult>>();
            Pending.Add(later);
            return new ValueTask<IReadOnlyList<ScoredResult>>(later.Task);
        }

        _answer.Clear();
        _answer.AddRange(Answer(query));
        return ValueTask.FromResult<IReadOnlyList<ScoredResult>>(_answer);
    }

    public IReadOnlyList<LaunchResult> GetActions(LaunchResult result) => Actions;

    internal LaunchResult Result(string resultId) => new(this, resultId, resultId, string.Empty, null, static () => { });
}
