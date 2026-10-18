using System.Drawing;
using Dame.UI.Components;
using Dame.UI.Foundation;
using Dame.UI.Layout;
using Dame.Win32.Input;
using Damelauncher.Plugins;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Damelauncher.UI;

internal sealed class LauncherUi : UiElement, IDisposable
{
    private const int MaxVisibleRows = 8;
    // The height a TextInput always measures to.
    private const float QueryHeight = 38.0f;
    internal const float WindowWidth = 640.0f;

    private readonly UiHost _host;
    private readonly ResultIcons _icons;
    private readonly TextInput _query;
    private readonly ResultList _results;
    private readonly ScrollView _resultsView;

    internal LauncherUi(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        float dpi)
    {
        _host = new UiHost(this, deviceContext, directWriteFactory, dpi, Themes.Dark);
        _query = new TextInput("Search");
        _icons = new ResultIcons(deviceContext, dpi, _host.Root.InvalidateVisual);
        _results = new ResultList(_icons);
        _resultsView = new ScrollView(_results) { IsVisible = false };

        AddChild(new StackPanel(UiOrientation.Vertical, _query, _resultsView)
        {
            Spacing = UiDesign.Spacing,
            Fill = _resultsView,
            Margin = new UiThickness(UiDesign.WindowMargin),
        });
        _host.Root.SetFocus(_query);
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

    internal event Action<string>? QueryChanged
    {
        add => _query.TextChanged += value;
        remove => _query.TextChanged -= value;
    }

    internal event Action<LaunchResult>? ResultInvoked
    {
        add => _results.ResultInvoked += value;
        remove => _results.ResultInvoked -= value;
    }

    internal UiTheme Palette => _host.Palette;

    internal static float MaxWindowHeight => GetWindowHeight(MaxVisibleRows);

    /// <summary>The height that fits the current results, up to <see cref="MaxWindowHeight"/>.</summary>
    internal float WindowHeight => GetWindowHeight(Math.Min(_results.Count, MaxVisibleRows));

    internal void SetResults(IReadOnlyList<LaunchResult> results)
    {
        _results.SetResults(results);
        _resultsView.SetScrollOffset(0.0f);
        _resultsView.IsVisible = results.Count > 0;
    }

    /// <summary>Clears the query, so the launcher opens fresh.</summary>
    internal void Reset()
    {
        _query.Clear();
        _host.Root.SetFocus(_query);
    }

    internal override bool OnKeyEvent(WindowKeyEvent input)
    {
        switch (input.Key)
        {
            case WindowKey.Up:
                _results.MoveSelection(-1);
                return true;

            case WindowKey.Down:
                _results.MoveSelection(1);
                return true;

            case WindowKey.Enter:
                _results.InvokeSelected();
                return true;

            default:
                return false;
        }
    }

    internal bool HandleKey(WindowKeyEvent input) =>
        _host.Root.HandleKey(input, this, wrapFocus: true, directionalNavigation: false);

    internal bool HandleTextInput(string text) => _host.Root.HandleTextInput(text);

    internal void SetDpi(float dpi)
    {
        _host.Root.SetDpi(dpi);
        _icons.SetDpi(dpi);
    }

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

    public void Dispose()
    {
        _icons.Dispose();
        _host.Dispose();
    }

    private static float GetWindowHeight(int rows) =>
        (2.0f * UiDesign.WindowMargin) + QueryHeight
        + (rows == 0 ? 0.0f : UiDesign.Spacing + (rows * ResultList.RowHeight));
}
