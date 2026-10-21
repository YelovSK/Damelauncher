using System.Diagnostics;
using Dame.Win32;

namespace Damelauncher.Plugins.Processes;

/// <summary>Ends running programs, each program's processes together.</summary>
internal sealed class ProcessesProvider : IResultProvider
{
    // Long enough that a query typed after the keyword reuses the list taken for the keyword,
    // short enough that a program started since shows up.
    private static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(5);

    private readonly List<ScoredResult> _matches = [];
    private Task<ProgramProcesses[]>? _programs;
    private long _listedAt;

    public string Id => "processes";

    public string Name => "Processes";

    public string DefaultKeyword => "kill";

    // Searching for a program to open shouldn't offer to end it.
    public bool DefaultInNormalSearches => false;

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async ValueTask<IReadOnlyList<ScoredResult>> QueryAsync(string query, int maxResults, CancellationToken cancellationToken)
    {
        // The keyword typed alone always looks again.
        if (query.Length == 0 || _programs is null || Stopwatch.GetElapsedTime(_listedAt) > MaxAge)
        {
            _listedAt = Stopwatch.GetTimestamp();
            _programs = Task.Run(List, CancellationToken.None);
        }

        ProgramProcesses[] programs = await _programs.WaitAsync(cancellationToken);
        _matches.Clear();
        foreach (ProgramProcesses program in programs)
        {
            if (FuzzyMatcher.Score(query, program.Result.Title) is { } score)
            {
                _matches.Add(new ScoredResult(program.Result, score));
            }
        }

        return _matches;
    }

    public IReadOnlyList<LaunchResult> GetActions(LaunchResult result)
    {
        if (_programs is not { IsCompletedSuccessfully: true }
            || Array.Find(_programs.Result, program => program.Result.Id == result.Id) is not { } match)
        {
            return [];
        }

        var actions = new List<LaunchResult>();
        if (match.Path is { } path)
        {
            actions.Add(new(this, "folder", "Open file location", path, null, () => ShellIntegration.ShowInFolder(path)));
        }

        if (match.Processes.Length > 1)
        {
            foreach (RunningProcess process in match.Processes)
            {
                actions.Add(new(
                    this,
                    $"end:{process.Id}",
                    $"End process {process.Id}",
                    process.Name,
                    null,
                    () => RunningProcesses.TryTerminate(process)));
            }
        }

        return actions;
    }

    private ProgramProcesses[] List() =>
        [
            .. RunningProcesses.ListTerminable()
                .GroupBy(process => process.Path ?? process.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => CreateProgram(group.Key, [.. group]))
                // Equal scores keep this order, so the whole list, shown for an empty query, reads alphabetically.
                .OrderBy(program => program.Result.Title, StringComparer.CurrentCultureIgnoreCase),
        ];

    private ProgramProcesses CreateProgram(string key, RunningProcess[] processes)
    {
        RunningProcess first = processes[0];
        string location = first.Path ?? first.Name;
        var result = new LaunchResult(
            this,
            key.ToLowerInvariant(),
            Path.GetFileNameWithoutExtension(first.Name),
            processes.Length == 1 ? location : $"{processes.Length} processes · {location}",
            first.Path is { } path ? new ShellIcon(path) : null,
            () =>
            {
                foreach (RunningProcess process in processes)
                {
                    RunningProcesses.TryTerminate(process);
                }
            });
        return new ProgramProcesses(result, first.Path, processes);
    }

    private sealed record ProgramProcesses(LaunchResult Result, string? Path, RunningProcess[] Processes);
}
