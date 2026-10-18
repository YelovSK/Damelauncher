using Damelauncher.Plugins;
using Damelauncher.Settings;

namespace Damelauncher.UI;

/// <summary>What the settings window shows and changes, which the app applies and saves.</summary>
internal interface ISettingsActions
{
    public LauncherSettings Settings { get; }

    public ThemeId Theme { set; }

    public int MaxResults { set; }

    /// <summary>The plugins whose settings can be changed.</summary>
    public IReadOnlyList<IResultProvider> Plugins { get; }

    /// <summary>Whether the hotkey in <see cref="Settings"/> works, which it doesn't while another app has it.</summary>
    public bool IsHotKeyRegistered { get; }

    public bool StartsWithWindows { get; set; }

    public bool OpensWithWindowsKey { get; set; }

    /// <returns>Whether the hotkey could be registered. When it couldn't, the previous one stays.</returns>
    public bool TrySetHotKey(HotKey hotKey);

    /// <summary>
    /// Releases the hotkey while a new one is being recorded, so pressing the current one reaches
    /// the settings window instead of toggling the launcher.
    /// </summary>
    public void SuspendHotKey(bool suspended);

    public void SetPlugin(IResultProvider plugin, PluginSettings settings);
}
