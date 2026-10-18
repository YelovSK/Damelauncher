namespace Damelauncher.Plugins;

/// <summary>The icon shown next to a result.</summary>
internal abstract record ResultIcon
{
    private protected ResultIcon()
    {
    }
}

/// <summary>The icon the shell shows for a path or shell location, such as <c>shell:AppsFolder\…</c>.</summary>
internal sealed record ShellIcon(string Location) : ResultIcon;

/// <summary>
/// A symbol from the system's icon font, drawn in the text's color, for results that aren't a
/// file or an app and so have no icon of their own.
/// </summary>
internal sealed record GlyphIcon(string Glyph) : ResultIcon;
