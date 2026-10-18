using System.Diagnostics;
using Damelauncher.Plugins;
using Damelauncher.Plugins.Programs;
using Damelauncher.Plugins.WindowsSettings;
using Damelauncher.Search;

namespace Damelauncher.Benchmarks;

/// <summary>
/// Times what one keystroke costs in the search path: each provider on its own, then the whole
/// search with ranking and merging. Everything is left out, since it waits on another process.
/// </summary>
/// <remarks>Run in Release: <c>dotnet run -c Release --project tests/Damelauncher.Benchmarks</c>.</remarks>
internal static class Program
{
    private const int WarmupIterations = 1_000;
    private const int Iterations = 5_000;

    // Each as typed one key at a time, since that's how they arrive.
    private static readonly string[] Words = ["visual", "notepad", "settings", "xyzq"];

    private static async Task Main()
    {
        var programs = new ProgramsProvider();
        var settings = new WindowsSettingsProvider();
        await programs.InitializeAsync(CancellationToken.None);

        Measure("Programs", query => Query(programs, query));
        Measure("Windows settings", query => Query(settings, query));

        // Results come out synchronously, since none of these providers waits on anything.
        IReadOnlyList<LaunchResult> latest = [];
        using var search = new ResultSearch(
            UsageHistory.Load(UsageHistory.DefaultPath),
            new ProgramsProvider(),
            new WindowsSettingsProvider(),
            new LauncherCommandsProvider(static () => { }, static () => { }));
        search.ResultsChanged += results => latest = results;
        await WaitUntilIndexedAsync(search, () => latest);

        Measure("Search (ranked and merged)", query =>
        {
            search.Search(query);
            return latest.Count;
        });
    }

    private static int Query(IResultProvider provider, string query)
    {
        ValueTask<IReadOnlyList<ScoredResult>> results = provider.QueryAsync(query, 50, CancellationToken.None);
        if (!results.IsCompleted)
        {
            throw new InvalidOperationException($"{provider.Name} didn't answer synchronously.");
        }

        return results.Result.Count;
    }

    // Searches wait for the programs to be indexed, and the other providers' answers can show up
    // first, after the search's deadline, so only program results say the index is ready.
    private static async Task WaitUntilIndexedAsync(ResultSearch search, Func<IReadOnlyList<LaunchResult>> latest)
    {
        while (!latest().Any(result => result.Provider is ProgramsProvider))
        {
            search.Search("a");
            await Task.Delay(50);
        }
    }

    private static void Measure(string name, Func<string, int> run)
    {
        Console.WriteLine();
        Console.WriteLine(name);
        Console.WriteLine($"  {"query",-10} {"results",8} {"µs",10} {"bytes",10}");
        foreach (string word in Words)
        {
            for (int length = 1; length <= word.Length; length++)
            {
                string query = word[..length];
                for (int iteration = 0; iteration < WarmupIterations; iteration++)
                {
                    run(query);
                }

                int results = 0;
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                for (int iteration = 0; iteration < Iterations; iteration++)
                {
                    results = run(query);
                }

                double microseconds = Stopwatch.GetElapsedTime(started).TotalMicroseconds / Iterations;
                long bytes = (GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / Iterations;
                Console.WriteLine($"  {query,-10} {results,8} {microseconds,10:F1} {bytes,10:N0}");
            }
        }
    }
}
