using Dame.Win32.Input;

namespace Damelauncher.Settings;

/// <summary>A system-wide key combination, written the way it reads, such as <c>Alt+Shift+Space</c>.</summary>
internal readonly record struct HotKey(HotKeyModifiers Modifiers, WindowKey Key)
{
    internal static readonly HotKey Default = new(HotKeyModifiers.Alt | HotKeyModifiers.Shift, WindowKey.Space);

    private static readonly (HotKeyModifiers Modifier, string Label)[] ModifierLabels =
    [
        (HotKeyModifiers.Control, "Ctrl"),
        (HotKeyModifiers.Alt, "Alt"),
        (HotKeyModifiers.Shift, "Shift"),
        (HotKeyModifiers.Windows, "Win"),
    ];

    // Keys whose enum name would read badly. Anything absent uses the enum name.
    private static readonly (WindowKey Key, string Label)[] KeyLabels =
    [
        (WindowKey.Number0, "0"),
        (WindowKey.Number1, "1"),
        (WindowKey.Number2, "2"),
        (WindowKey.Number3, "3"),
        (WindowKey.Number4, "4"),
        (WindowKey.Number5, "5"),
        (WindowKey.Number6, "6"),
        (WindowKey.Number7, "7"),
        (WindowKey.Number8, "8"),
        (WindowKey.Number9, "9"),
        (WindowKey.Semicolon, ";"),
        (WindowKey.Equals, "="),
        (WindowKey.Comma, ","),
        (WindowKey.Minus, "-"),
        (WindowKey.Period, "."),
        (WindowKey.Slash, "/"),
        (WindowKey.Grave, "`"),
        (WindowKey.BracketLeft, "["),
        (WindowKey.Backslash, "\\"),
        (WindowKey.BracketRight, "]"),
        (WindowKey.Quote, "'"),
    ];

    internal string Text
    {
        get
        {
            HotKeyModifiers held = Modifiers;
            string modifiers = string.Concat(ModifierLabels
                .Where(entry => held.HasFlag(entry.Modifier))
                .Select(entry => entry.Label + "+"));
            return modifiers + GetKeyLabel(Key);
        }
    }

    internal static HotKey? Parse(string text)
    {
        string[] parts = text.Split('+', StringSplitOptions.TrimEntries);
        HotKeyModifiers modifiers = HotKeyModifiers.None;
        foreach (string part in parts[..^1])
        {
            int match = Array.FindIndex(
                ModifierLabels,
                entry => string.Equals(entry.Label, part, StringComparison.OrdinalIgnoreCase));
            if (match < 0)
            {
                return null;
            }

            modifiers |= ModifierLabels[match].Modifier;
        }

        return TryParseKey(parts[^1], out WindowKey key) ? new HotKey(modifiers, key) : null;
    }

    private static string GetKeyLabel(WindowKey key)
    {
        foreach ((WindowKey candidate, string label) in KeyLabels)
        {
            if (candidate == key)
            {
                return label;
            }
        }

        return key.ToString();
    }

    private static bool TryParseKey(string text, out WindowKey key)
    {
        foreach ((WindowKey candidate, string label) in KeyLabels)
        {
            if (string.Equals(label, text, StringComparison.OrdinalIgnoreCase))
            {
                key = candidate;
                return true;
            }
        }

        return Enum.TryParse(text, ignoreCase: true, out key) && Enum.IsDefined(key);
    }
}
