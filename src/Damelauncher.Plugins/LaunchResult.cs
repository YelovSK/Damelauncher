namespace Damelauncher.Plugins;

/// <summary>Something the launcher can show and run, independent of any one query.</summary>
/// <remarks>
/// A provider can build its results once and hand out the same ones for every query, scoring
/// them through <see cref="ScoredResult"/>, so a keystroke doesn't have to build them anew.
/// </remarks>
/// <param name="Provider">The provider the result belongs to, which its actions are asked of.</param>
/// <param name="Id">
/// Identifies the result within its provider across queries and restarts, such as a program's
/// app id, so the launcher can remember how often it's picked.
/// </param>
/// <param name="Icon">The icon shown next to the result, or <see langword="null"/> for none.</param>
/// <param name="Execute">Runs the result once the launcher has hidden itself.</param>
internal sealed record LaunchResult(
    IResultProvider Provider,
    string Id,
    string Title,
    string Subtitle,
    ResultIcon? Icon,
    Action Execute);
