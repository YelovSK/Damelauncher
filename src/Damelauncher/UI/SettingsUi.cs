using System.Drawing;
using Dame.UI.Components;
using Dame.UI.Foundation;
using Dame.UI.Layout;
using Dame.Win32.Input;
using Damelauncher.Plugins;
using Damelauncher.Settings;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Damelauncher.UI;

internal sealed class SettingsUi : UiElement, IDisposable
{
    internal const float WindowWidth = 480.0f;
    internal const float WindowHeight = 640.0f;

    private readonly UiHost _host;
    private readonly ISettingsActions _actions;
    private readonly HotKeyChip _hotKeyChip;
    private readonly TextBlock _hotKeyError;
    private readonly PopupHost _popupHost = new();

    internal SettingsUi(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        float dpi,
        ISettingsActions actions)
    {
        _host = new UiHost(this, deviceContext, directWriteFactory, dpi, Themes.Get(actions.Settings.Theme).Palette);
        _actions = actions;
        _hotKeyChip = new HotKeyChip(actions.Settings.HotKey, RecordHotKey, actions.SuspendHotKey)
        {
            HorizontalAlignment = UiAlignment.Start,
        };
        _hotKeyError = new TextBlock(string.Empty, UiTextStyle.Body, UiTextTone.Error, UiTextWrapping.Wrap)
        {
            IsVisible = false,
        };
        if (!actions.IsHotKeyRegistered)
        {
            ShowHotKeyError(actions.Settings.HotKey);
        }

        var theme = new SettingsRow(
            "Theme",
            new Dropdown<ThemeId>(
                _popupHost,
                [.. Themes.All.Select(option => new Choice<ThemeId>(option.DisplayName, option.Id))],
                actions.Settings.Theme,
                value => actions.Theme = value));
        var general = new SettingsGroup(
            "General",
            theme,
            new SettingsRow(
                "Hotkey",
                new StackPanel(UiOrientation.Vertical, _hotKeyChip, _hotKeyError) { Spacing = UiDesign.SmallSpacing }),
            new StackPanel(
                UiOrientation.Vertical,
                new Toggle("Open with the Windows key", actions.OpensWithWindowsKey, value => actions.OpensWithWindowsKey = value),
                new TextBlock(
                    "Tapping it on its own opens the launcher instead of Start. Shortcuts like Win+E still work.",
                    UiTextStyle.Body,
                    UiTextTone.Secondary,
                    UiTextWrapping.Wrap))
            {
                Spacing = UiDesign.SmallSpacing,
            },
            new Toggle("Start with Windows", actions.StartsWithWindows, value => actions.StartsWithWindows = value));
        var plugins = new SettingsGroup(
            "Plugins",
            [.. actions.Plugins.Select(CreatePluginSettings)]);
        var title = new TextBlock("Settings", UiTextStyle.Heading, UiTextTone.Primary, UiTextWrapping.NoWrap);

        AddChild(new ScrollView(new StackPanel(UiOrientation.Vertical, title, general, plugins)
        {
            Spacing = UiDesign.LargeSpacing * 1.5f,
            Margin = new UiThickness(24.0f),
        }));
        // Above the settings, where the theme list opens.
        AddChild(_popupHost);
        _host.Root.SetFocus(_hotKeyChip);
    }

    internal event Action? Invalidated
    {
        add => _host.Root.Invalidated += value;
        remove => _host.Root.Invalidated -= value;
    }

    internal event Action<WindowCursor>? CursorChanged
    {
        add => _host.Root.CursorChanged += value;
        remove => _host.Root.CursorChanged -= value;
    }

    internal UiTheme Palette
    {
        get => _host.Palette;
        set
        {
            _host.Palette = value;
            _host.Root.InvalidateVisual();
        }
    }

    /// <summary>Sends a key to whatever is recording it first, then to the focused setting.</summary>
    internal bool HandleKey(WindowKeyEvent input) =>
        _host.Root.HandleCapturedKey(input)
        || _host.Root.HandleKey(input, this, wrapFocus: true, directionalNavigation: false);

    internal bool HandleTextInput(string text) => _host.Root.HandleTextInput(text);

    internal void SetDpi(float dpi) => _host.Root.SetDpi(dpi);

    internal bool Update()
    {
        bool continues = _host.Update();
        if (!continues)
        {
            _host.ResetClock();
        }

        return continues;
    }

    internal void DrawFrame(SizeF pixelSize) => _host.Draw(pixelSize);

    internal bool HandlePointer(in WindowPointerEvent input) => _host.Root.HandlePointer(input);

    protected override bool HitTestCore(PointF position) => false;

    public void Dispose() => _host.Dispose();

    private SettingsRow CreatePluginSettings(IResultProvider plugin)
    {
        PluginSettings settings = _actions.Settings.GetPlugin(plugin.Id);
        var keyword = new TextInput("No keyword")
        {
            Text = settings.Keyword ?? plugin.DefaultKeyword ?? string.Empty,
            MaxWidth = 160.0f,
            HorizontalAlignment = UiAlignment.Start,
        };
        keyword.TextChanged += text => UpdatePlugin(plugin, current => current with { Keyword = text.Trim() });

        return new SettingsRow(
            plugin.Name,
            new StackPanel(
                UiOrientation.Vertical,
                new Toggle("Enabled", settings.Enabled, enabled => UpdatePlugin(plugin, current => current with { Enabled = enabled })),
                new Toggle(
                    "Include in normal searches",
                    settings.InNormalSearches,
                    included => UpdatePlugin(plugin, current => current with { InNormalSearches = included })),
                new SettingsRow("Keyword, typed with a space before the query to search only this", keyword))
            {
                Spacing = UiDesign.Spacing,
            });
    }

    private void UpdatePlugin(IResultProvider plugin, Func<PluginSettings, PluginSettings> change) =>
        _actions.SetPlugin(plugin, change(_actions.Settings.GetPlugin(plugin.Id)));

    private void RecordHotKey(HotKey hotKey)
    {
        if (!_actions.TrySetHotKey(hotKey))
        {
            ShowHotKeyError(hotKey);
            return;
        }

        _hotKeyChip.HotKey = hotKey;
        _hotKeyError.IsVisible = false;
    }

    private void ShowHotKeyError(HotKey hotKey)
    {
        _hotKeyError.Text = $"{hotKey.Text} is taken by another app. Close it, or pick another combination.";
        _hotKeyError.IsVisible = true;
    }
}
