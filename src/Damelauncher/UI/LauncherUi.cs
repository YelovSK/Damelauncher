using System.Drawing;
using Dame.UI.Components;
using Dame.UI.Foundation;
using Dame.UI.Layout;
using Dame.Win32.Input;
using Damelauncher.Results;
using Vortice.Direct2D1;
using Vortice.DirectWrite;

namespace Damelauncher.UI;

internal sealed class LauncherUi : UiElement, IDisposable
{
    private const int VisibleRows = 7;
    internal const float WindowWidth = 640.0f;
    internal const float WindowHeight = (2.0f * UiDesign.WindowMargin) + 38.0f + UiDesign.Spacing
        + (VisibleRows * ResultList.RowHeight);

    private readonly UiHost _host;
    private readonly ResultCatalog _catalog;
    private readonly TextInput _query;
    private readonly ResultList _results;

    internal LauncherUi(
        ID2D1DeviceContext deviceContext,
        IDWriteFactory directWriteFactory,
        float dpi,
        ResultCatalog catalog)
    {
        _host = new UiHost(this, deviceContext, directWriteFactory, dpi, Themes.Dark);
        _catalog = catalog;
        _query = new TextInput("Search");
        _results = new ResultList();
        _results.SetResults(catalog.Search(string.Empty));
        _query.TextChanged += query => _results.SetResults(_catalog.Search(query));

        AddChild(new StackPanel(UiOrientation.Vertical, _query, _results)
        {
            Spacing = UiDesign.Spacing,
            Fill = _results,
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

    internal event Action<LaunchResult>? ResultInvoked
    {
        add => _results.ResultInvoked += value;
        remove => _results.ResultInvoked -= value;
    }

    internal UiTheme Palette => _host.Palette;

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
}
