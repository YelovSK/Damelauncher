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

    private readonly ResultIcons _icons;
    private IReadOnlyList<LaunchResult> _results = [];
    private int _selectedIndex;
    private float _textLeft = RowPadding;

    internal ResultList(ResultIcons icons) => _icons = icons;

    internal event Action<LaunchResult>? ResultInvoked;

    /// <summary>A right-click on a result, which asks for its actions.</summary>
    internal event Action<LaunchResult>? ActionsRequested;

    internal override WindowCursor Cursor => WindowCursor.Pointer;

    internal int Count => _results.Count;

    internal LaunchResult? SelectedResult => _results.Count > 0 ? _results[_selectedIndex] : null;

    internal void SetResults(IReadOnlyList<LaunchResult> results)
    {
        _results = results;
        // A column for icons only when some result has one, and then for every row, so the
        // titles line up whether or not their result has an icon.
        _textLeft = results.Any(result => result.IconSource is not null)
            ? RowPadding + ResultIcons.Size + RowPadding
            : RowPadding;
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
        if (input.Kind != WindowPointerEventKind.Pressed
            || input.Button is not (PointerButton.Primary or PointerButton.Secondary))
        {
            return default;
        }

        int index = (int)(input.Position.Y / RowHeight);
        if (index >= 0 && index < _results.Count)
        {
            _selectedIndex = index;
            InvalidateVisual();
            if (input.Button == PointerButton.Primary)
            {
                InvokeSelected();
            }
            else
            {
                ActionsRequested?.Invoke(_results[index]);
            }
        }

        return new UiPointerResult(Consumed: true);
    }

    protected override SizeF MeasureCore(SizeF availableSize) =>
        new(float.IsFinite(availableSize.Width) ? availableSize.Width : 0.0f, _results.Count * RowHeight);

    // Only the rows in sight, so icons load only for those. The scroll view moves this list up
    // by its offset and shows as much of it as fits its own height.
    protected override void DrawCore(in UiDrawContext context)
    {
        float visibleTop = -Bounds.Y;
        float visibleBottom = visibleTop + (Parent?.Bounds.Height ?? Bounds.Height);
        int first = Math.Max(0, (int)(visibleTop / RowHeight));
        int last = Math.Min(_results.Count, (int)MathF.Ceiling(visibleBottom / RowHeight));
        for (int index = first; index < last; index++)
        {
            DrawRow(context, _results[index], GetRowBounds(index), index == _selectedIndex);
        }
    }

    private RectangleF GetRowBounds(int index) => new(0.0f, index * RowHeight, Bounds.Width, RowHeight);

    private void DrawRow(in UiDrawContext context, LaunchResult result, RectangleF row, bool selected)
    {
        if (selected)
        {
            context.FillRoundedRectangle(
                new RoundedRectangle(row, UiDesign.ControlCornerRadius, UiDesign.ControlCornerRadius),
                context.Palette.ControlHover);
        }

        if (result.IconSource is { } source && _icons.Get(source) is { } icon)
        {
            float inset = (RowHeight - ResultIcons.Size) / 2.0f;
            context.DrawBitmapFitted(
                icon,
                new RectangleF(row.X + RowPadding, row.Y + inset, ResultIcons.Size, ResultIcons.Size));
        }

        float textWidth = MathF.Max(0.0f, row.Width - _textLeft - RowPadding);
        context.DrawText(
            result.Title,
            TitleFont,
            new Rect(row.X + _textLeft, row.Y + 4.0f, textWidth, 22.0f),
            context.Palette.PrimaryText);
        context.DrawText(
            result.Subtitle,
            SubtitleFont,
            new Rect(row.X + _textLeft, row.Y + 24.0f, textWidth, 18.0f),
            context.Palette.SecondaryText);
    }
}
