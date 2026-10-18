using System.Diagnostics;

namespace Damelauncher.Results;

/// <summary>A fixed set of results to search until real sources exist.</summary>
internal sealed class ResultCatalog(Action quit)
{
    private readonly LaunchResult[] _results =
    [
        Shell("Notepad", "notepad.exe"),
        Shell("Calculator", "calc.exe"),
        Shell("Paint", "mspaint.exe"),
        Shell("Command Prompt", "cmd.exe"),
        Shell("Windows Terminal", "wt.exe"),
        Shell("File Explorer", "explorer.exe"),
        Shell("Task Manager", "taskmgr.exe"),
        Shell("Settings", "ms-settings:"),
        Shell("Control Panel", "control.exe"),
        Shell("Snipping Tool", "ms-screenclip:"),
        new("Quit Damelauncher", "Exit the launcher", quit),
    ];

    /// <summary>Results whose title contains the query, those starting with it first.</summary>
    internal IReadOnlyList<LaunchResult> Search(string query)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return _results;
        }

        return
        [
            .. _results
                .Where(result => result.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(result => !result.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase)),
        ];
    }

    private static LaunchResult Shell(string title, string target) =>
        new(title, target, () => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose());
}
