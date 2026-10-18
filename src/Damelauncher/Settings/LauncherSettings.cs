namespace Damelauncher.Settings;

internal sealed record LauncherSettings
{
    public HotKey HotKey { get; init; } = HotKey.Default;

    /// <summary>The ids of the plugins that are turned off.</summary>
    public IReadOnlySet<string> DisabledPlugins { get; init; } = new HashSet<string>();
}
