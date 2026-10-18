using System.Drawing;
using Dame.UI.Foundation;
using Dame.Win32.Input;
using Damelauncher.Plugins;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Damelauncher.UI;

/// <summary>The results matching the query, one of which is selected and runs on Enter or a click.</summary>
/// <remarks>As tall as all its rows, for a scroll view to show part of.</remarks>
internal sealed class ResultList : UiElement
{
    internal const float RowHeight = 48.0f;
    private const float RowPadding = 12.0f;
    private static readonly UiFont TitleFont = new(UiDesign.BodyFontSize, FontWeight.Medium, Ellipsis: true);
    private static readonly UiFont SubtitleFont = new(UiDesign.CaptionFontSize, Ellipsis: true);

    private IReadOnlyList<LaunchResult> _results = [];
    private int _selectedIndex;

    internal event Action<LaunchResult>? ResultInvoked;

    internal override WindowCursor Cursor => WindowCursor.Pointer;

    internal int Count => _results.Count;

    internal void SetResults(IReadOnlyList<LaunchResult> results)
    {
        _results = results;
        _selectedIndex = 0;
        InvalidateLayout();
    }

    internal void MoveSelection(int offset)
    {
        if (_results.Count == 0)
        {
            return;
        }

        _selectedIndex = ((_selectedIndex + offset) % _results.Count + _results.Count) % _results.Count;
        ScrollIntoView(GetRowBounds(_selectedIndex));
        InvalidateVisual();
    }

    internal void InvokeSelected()
    {
        if (_results.Count > 0)
        {
            ResultInvoked?.Invoke(_results[_selectedIndex]);
        }
    }

    internal override UiPointerResult OnPointerEvent(in WindowPointerEvent input)
    {
        if (input.Kind != WindowPointerEventKind.Pressed || input.Button != PointerButton.Primary)
        {
            return default;
        }

        int index = (int)(input.Position.Y / RowHeight);
        if (index >= 0 && index < _results.Count)
        {
            _selectedIndex = index;
            InvalidateVisual();
            InvokeSelected();
        }

        return new UiPointerResult(Consumed: true);
    }

    protected override SizeF MeasureCore(SizeF availableSize) =>
        new(float.IsFinite(availableSize.Width) ? availableSize.Width : 0.0f, _results.Count * RowHeight);

    // Every row is drawn: there are few enough, and the scroll view clips the ones out of sight.
    protected override void DrawCore(in UiDrawContext context)
    {
        for (int index = 0; index < _results.Count; index++)
        {
            DrawRow(context, _results[index], GetRowBounds(index), index == _selectedIndex);
        }
    }

    private RectangleF GetRowBounds(int index) => new(0.0f, index * RowHeight, Bounds.Width, RowHeight);

    private static void DrawRow(in UiDrawContext context, LaunchResult result, RectangleF row, bool selected)
    {
        if (selected)
        {
            context.FillRoundedRectangle(
                new RoundedRectangle(row, UiDesign.ControlCornerRadius, UiDesign.ControlCornerRadius),
                context.Palette.ControlHover);
        }

        float textWidth = MathF.Max(0.0f, row.Width - (2.0f * RowPadding));
        context.DrawText(
            result.Title,
            TitleFont,
            new Rect(row.X + RowPadding, row.Y + 4.0f, textWidth, 22.0f),
            context.Palette.PrimaryText);
        context.DrawText(
            result.Subtitle,
            SubtitleFont,
            new Rect(row.X + RowPadding, row.Y + 24.0f, textWidth, 18.0f),
            context.Palette.SecondaryText);
    }
}
