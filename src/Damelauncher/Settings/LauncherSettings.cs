namespace Damelauncher.Settings;

internal sealed record LauncherSettings
{
    public HotKey HotKey { get; init; } = HotKey.Default;

    /// <summary>Whether tapping the Windows key on its own opens the launcher too, instead of Start.</summary>
    public bool OpenWithWindowsKey { get; init; }

    /// <summary>The ids of the plugins that are turned off.</summary>
    public IReadOnlySet<string> DisabledPlugins { get; init; } = new HashSet<string>();
}
