using System.Drawing;
using Dame.UI.Animation;
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
    internal const float WindowWidth = 600.0f;
    internal const float WindowHeight = 520.0f;

    // Pages sit side by side like one long strip, this far apart, and the pages area cuts them off.
    private const float PageGapDips = 24.0f;
    private static readonly UiTransition PageTransition = new(Response: 22.0);

    private readonly UiHost _host;
    private readonly ISettingsActions _actions;
    private readonly HotKeyChip _hotKeyChip;
    private readonly TextBlock _hotKeyError;
    private readonly PopupHost _popupHost = new();
    // The general settings, then one page per plugin, in the order of their tabs.
    private readonly ScrollView[] _pages;
    private readonly Overlay _pagesArea;
    private int _shownPage;

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

        StackPanel install = Described(
            new Button("Install", actions.Install) { HorizontalAlignment = UiAlignment.Start },
            "This copy isn't installed. Installing gives it a place of its own, in Start and installed apps, and starts it with Windows.");
        install.IsVisible = !actions.IsInstalled;
        ScrollView general = Page(
            install,
            new SettingsRow(
                "Theme",
                new Dropdown<ThemeId>(
                    _popupHost,
                    [.. Themes.All.Select(option => new Choice<ThemeId>(option.DisplayName, option.Id))],
                    actions.Settings.Theme,
                    value => actions.Theme = value)),
            new SettingsRow(
                "Hotkey",
                new StackPanel(UiOrientation.Vertical, _hotKeyChip, _hotKeyError) { Spacing = UiDesign.SmallSpacing }),
            Described(
                new Toggle("Open with the Windows key", actions.OpensWithWindowsKey, value => actions.OpensWithWindowsKey = value),
                "Tapping it on its own opens the launcher instead of Start. Shortcuts like Win+E still work."),
            new Toggle("Start with Windows", actions.StartsWithWindows, value => actions.StartsWithWindows = value),
            new SettingsRow(
                "Maximum results",
                MaxResultsSlider(actions.Settings.MaxResults, value => actions.MaxResults = value)));
        _pages = [general, .. actions.Plugins.Select(CreatePluginPage)];
        _pagesArea = new Overlay(_pages)
        {
            Margin = new UiThickness(0.0f, UiDesign.SmallSpacing, 0.0f, 0.0f),
        };
        var tabs = new TabStrip<int>(
            [new("General", 0), .. actions.Plugins.Select((plugin, index) => new Choice<int>(plugin.Name, index + 1))],
            0,
            SelectPage);

        AddChild(new StackPanel(
            UiOrientation.Vertical,
            new TextBlock("Settings", UiTextStyle.Heading, UiTextTone.Primary, UiTextWrapping.NoWrap),
            tabs,
            _pagesArea,
            new TextBlock("Changes are saved automatically.", UiTextStyle.Body, UiTextTone.Secondary, UiTextWrapping.Wrap))
        {
            Spacing = 12.0f,
            Fill = _pagesArea,
            Margin = new UiThickness(24.0f, 20.0f, 24.0f, 12.0f),
        });
        // Above the settings, where the theme list opens.
        AddChild(_popupHost);
        for (int index = 1; index < _pages.Length; index++)
        {
            _pages[index].IsPresent = false;
        }

        _host.Root.SetFocus(tabs.SelectedSegment);
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

    private static ScrollView Page(params UiElement[] settings) =>
        new(new StackPanel(UiOrientation.Vertical, settings) { Spacing = UiDesign.LargeSpacing })
        {
            Transition = PageTransition,
        };

    private static Slider MaxResultsSlider(int value, Action<int> changed) =>
        new(
            LauncherSettings.SmallestMaxResults,
            LauncherSettings.LargestMaxResults,
            step: 1.0,
            value,
            count => changed((int)count),
            count => $"{count:0}");

    private static StackPanel Described(UiElement setting, string description) =>
        new(
            UiOrientation.Vertical,
            setting,
            new TextBlock(description, UiTextStyle.Body, UiTextTone.Secondary, UiTextWrapping.Wrap))
        {
            Spacing = UiDesign.SmallSpacing,
        };

    private ScrollView CreatePluginPage(IResultProvider plugin)
    {
        PluginSettings settings = _actions.Settings.GetPlugin(plugin.Id);
        var keyword = new TextInput("No keyword")
        {
            Text = settings.Keyword ?? plugin.DefaultKeyword ?? string.Empty,
            MaxWidth = 160.0f,
            HorizontalAlignment = UiAlignment.Start,
        };
        keyword.TextChanged += text => UpdatePlugin(plugin, current => current with { Keyword = text.Trim() });

        return Page(
            new Toggle("Enabled", settings.Enabled, enabled => UpdatePlugin(plugin, current => current with { Enabled = enabled })),
            Described(
                new Toggle(
                    "Include in normal searches",
                    settings.InNormalSearches ?? plugin.DefaultInNormalSearches,
                    included => UpdatePlugin(plugin, current => current with { InNormalSearches = included })),
                "When off, it only answers queries that start with its keyword."),
            new SettingsRow(
                "Keyword",
                Described(keyword, $"Typed with a space before a query, it searches only {plugin.Name}.")),
            new SettingsRow(
                "Maximum results",
                Described(
                    MaxResultsSlider(
                        settings.MaxResults ?? plugin.DefaultMaxResults ?? _actions.Settings.MaxResults,
                        value => UpdatePlugin(plugin, current => current with { MaxResults = value })),
                    "Its best ones are kept, counting how often they were launched. Never more than the maximum in General.")));
    }

    private void SelectPage(int page)
    {
        _popupHost.Close();
        // The new page slides in from the side its tab is on, and the old one leaves the other way.
        float slide = Math.Sign(page - _shownPage) * (_pagesArea.Bounds.Width + PageGapDips);
        for (int index = 0; index < _pages.Length; index++)
        {
            ScrollView candidate = _pages[index];
            bool present = index == page;
            if (candidate.IsPresent == present)
            {
                continue;
            }

            candidate.Transition = PageTransition with
            {
                HiddenOffset = new PointF((present ? 1.0f : -1.0f) * slide, 0.0f),
            };
            candidate.IsPresent = present;
        }

        _shownPage = page;
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
