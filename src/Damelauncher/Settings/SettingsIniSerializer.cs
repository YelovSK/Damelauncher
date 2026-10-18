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
        Binding.Custom(
            (document, s, _) => document.Get(Root, "disabledPlugins") is { } text
                ? s with { DisabledPlugins = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToHashSet() }
                : s,
            (document, s) => document.Set(Root, "disabledPlugins", string.Join(',', s.DisabledPlugins.Order(StringComparer.Ordinal)))),
    ];

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
