using System.Drawing;
using Dame.UI.Foundation;
using Dame.Win32.Input;
using Damelauncher.Results;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Damelauncher.UI;

/// <summary>The results matching the query, one of which is selected and runs on Enter or a click.</summary>
internal sealed class ResultList : UiElement
{
    internal const float RowHeight = 48.0f;
    private const float RowPadding = 12.0f;
    private static readonly UiFont TitleFont = new(UiDesign.BodyFontSize, FontWeight.Medium, Ellipsis: true);
    private static readonly UiFont SubtitleFont = new(UiDesign.CaptionFontSize, Ellipsis: true);

    private IReadOnlyList<LaunchResult> _results = [];
    private int _selectedIndex;
    // The row at the top, which scrolls only as far as it takes to keep the selection in view.
    private int _firstVisibleIndex;

    internal event Action<LaunchResult>? ResultInvoked;

    internal override WindowCursor Cursor => WindowCursor.Pointer;

    private int VisibleRowCount => Math.Max(1, (int)(Bounds.Height / RowHeight));

    internal void SetResults(IReadOnlyList<LaunchResult> results)
    {
        _results = results;
        _selectedIndex = 0;
        _firstVisibleIndex = 0;
        InvalidateVisual();
    }

    internal void MoveSelection(int offset)
    {
        if (_results.Count == 0)
        {
            return;
        }

        _selectedIndex = ((_selectedIndex + offset) % _results.Count + _results.Count) % _results.Count;
        KeepSelectionVisible();
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

        int index = _firstVisibleIndex + (int)(input.Position.Y / RowHeight);
        if (index >= 0 && index < _results.Count)
        {
            _selectedIndex = index;
            InvalidateVisual();
            InvokeSelected();
        }

        return new UiPointerResult(Consumed: true);
    }

    protected override void ArrangeCore(SizeF finalSize) => KeepSelectionVisible();

    protected override void DrawCore(in UiDrawContext context)
    {
        context.PushClip(new RectangleF(PointF.Empty, Bounds.Size));
        try
        {
            int last = Math.Min(_results.Count, _firstVisibleIndex + VisibleRowCount + 1);
            for (int index = _firstVisibleIndex; index < last; index++)
            {
                float top = (index - _firstVisibleIndex) * RowHeight;
                DrawRow(context, _results[index], top, index == _selectedIndex);
            }
        }
        finally
        {
            context.PopClip();
        }
    }

    private void DrawRow(in UiDrawContext context, LaunchResult result, float top, bool selected)
    {
        if (selected)
        {
            context.FillRoundedRectangle(
                new RoundedRectangle(
                    new RectangleF(0.0f, top, Bounds.Width, RowHeight),
                    UiDesign.ControlCornerRadius,
                    UiDesign.ControlCornerRadius),
                context.Palette.ControlHover);
        }

        float textWidth = MathF.Max(0.0f, Bounds.Width - (2.0f * RowPadding));
        context.DrawText(
            result.Title,
            TitleFont,
            new Rect(RowPadding, top + 4.0f, textWidth, 22.0f),
            context.Palette.PrimaryText);
        context.DrawText(
            result.Subtitle,
            SubtitleFont,
            new Rect(RowPadding, top + 24.0f, textWidth, 18.0f),
            context.Palette.SecondaryText);
    }

    private void KeepSelectionVisible()
    {
        if (_selectedIndex < _firstVisibleIndex)
        {
            _firstVisibleIndex = _selectedIndex;
        }
        else if (_selectedIndex >= _firstVisibleIndex + VisibleRowCount)
        {
            _firstVisibleIndex = _selectedIndex - VisibleRowCount + 1;
        }
    }
}
