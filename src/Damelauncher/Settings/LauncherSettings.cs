namespace Damelauncher.Settings;

internal sealed record LauncherSettings
{
    public ThemeId Theme { get; init; } = ThemeId.Dark;

    public HotKey HotKey { get; init; } = HotKey.Default;

    /// <summary>Whether tapping the Windows key on its own opens the launcher too, instead of Start.</summary>
    public bool OpenWithWindowsKey { get; init; }

    /// <summary>Each plugin's settings by its id, holding only the plugins whose settings were changed.</summary>
    public IReadOnlyDictionary<string, PluginSettings> Plugins { get; init; } = new Dictionary<string, PluginSettings>();

    internal PluginSettings GetPlugin(string id) => Plugins.TryGetValue(id, out PluginSettings? plugin) ? plugin : new PluginSettings();
}
