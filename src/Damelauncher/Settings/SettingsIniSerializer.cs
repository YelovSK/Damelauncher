using Damelauncher.Serialization;
using Binding = Damelauncher.Serialization.IniBinding<Damelauncher.Settings.LauncherSettings>;

namespace Damelauncher.Settings;

internal static class SettingsIniSerializer
{
    private const string Root = "";

    private static readonly Binding[] Bindings =
    [
        new IniKeyBinding<LauncherSettings, HotKey>(
            Root,
            "hotkey",
            HotKey.Parse,
            hotKey => hotKey.Text,
            s => s.HotKey,
            (s, value) => s with { HotKey = value }),
        Binding.Bool(Root, "windowsKey", s => s.OpenWithWindowsKey, (s, value) => s with { OpenWithWindowsKey = value }),
        Binding.Enum(Root, "theme", s => s.Theme, (s, value) => s with { Theme = value }),
        Binding.Custom(ReadPlugins, WritePlugins),
    ];

    // Each plugin has a section of its own, named after its id, such as [plugins.everything].
    private const string PluginSectionPrefix = "plugins.";


    /// <returns>The settings, and the values that couldn't be read and were left at their defaults.</returns>
    internal static (LauncherSettings Settings, IReadOnlyList<string> Ignored) Read(string text)
    {
        var document = IniDocument.Parse(text);
        var ignored = new List<string>();
        var settings = new LauncherSettings();
        foreach (Binding binding in Bindings)
        {
            settings = binding.Read(document, settings, ignored);
        }

        return (settings, ignored);
    }

    private static LauncherSettings ReadPlugins(IniDocument document, LauncherSettings settings, List<string> ignored)
    {
        var plugins = new Dictionary<string, PluginSettings>();
        foreach (string section in document.SectionNames)
        {
            if (!section.StartsWith(PluginSectionPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var plugin = new PluginSettings
            {
                Enabled = ReadBool(document, section, "enabled", ignored) ?? true,
                Keyword = document.Get(section, "keyword"),
                InNormalSearches = ReadBool(document, section, "normalSearch", ignored) ?? true,
            };
            plugins[section[PluginSectionPrefix.Length..]] = plugin;
        }

        return settings with { Plugins = plugins };
    }

    private static void WritePlugins(IniDocument document, LauncherSettings settings)
    {
        foreach ((string id, PluginSettings plugin) in settings.Plugins.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            string section = PluginSectionPrefix + id;
            document.Set(section, "enabled", IniValue.FormatBool(plugin.Enabled));
            document.Set(section, "normalSearch", IniValue.FormatBool(plugin.InNormalSearches));
            if (plugin.Keyword is { } keyword)
            {
                document.Set(section, "keyword", keyword);
            }
        }
    }

    private static bool? ReadBool(IniDocument document, string section, string key, List<string> ignored)
    {
        if (document.Get(section, key) is not { } text)
        {
            return null;
        }

        bool? value = IniValue.ParseBool(text);
        if (value is null)
        {
            ignored.Add($"{section}.{key} '{text}'");
        }

        return value;
    }

    /// <param name="existing">The file as it is now, whose comments and unknown keys are kept.</param>
    internal static string Write(LauncherSettings settings, string existing)
    {
        IniDocument document;
        try
        {
            document = IniDocument.Parse(existing);
        }
        catch (IniFormatException)
        {
            document = IniDocument.Parse(string.Empty);
        }

        foreach (Binding binding in Bindings)
        {
            binding.Write(document, settings);
        }

        return document.Write();
    }
}
