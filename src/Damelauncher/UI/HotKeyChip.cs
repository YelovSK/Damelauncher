using System.Drawing;
using Dame.UI.Components;
using Dame.UI.Foundation;
using Dame.Win32.Input;
using Damelauncher.Settings;
using Vortice.Direct2D1;
using Vortice.DirectWrite;
using Vortice.Mathematics;

namespace Damelauncher.UI;

/// <summary>Shows a hotkey, and records a new one from the next key combination after a click.</summary>
internal sealed class HotKeyChip : InteractiveControl
{
    private const float Height = 32.0f;
    private const float HorizontalPadding = 12.0f;
    private const float MinimumWidth = 120.0f;
    private const string RecordingLabel = "Press a key combination";
    private static readonly UiFont LabelFont = new(UiDesign.BodyFontSize, FontWeight.SemiBold, TextAlignment.Center);

    private readonly Action<HotKey> _recorded;
    private readonly Action<bool> _recordingChanged;
    private bool _recording;

    internal HotKeyChip(HotKey hotKey, Action<HotKey> recorded, Action<bool> recordingChanged)
    {
        HotKey = hotKey;
        _recorded = recorded;
        _recordingChanged = recordingChanged;
    }

    internal HotKey HotKey
    {
        get;
        set
        {
            field = value;
            InvalidateLayout();
        }
    }

    private string Label => _recording ? RecordingLabel : HotKey.Text;

    internal override bool OnKeyEvent(WindowKeyEvent input)
    {
        if (!_recording)
        {
            return base.OnKeyEvent(input);
        }

        HotKeyModifiers modifiers =
            (input.Control ? HotKeyModifiers.Control : HotKeyModifiers.None)
            | (input.Alt ? HotKeyModifiers.Alt : HotKeyModifiers.None)
            | (input.Shift ? HotKeyModifiers.Shift : HotKeyModifiers.None);
        switch (input.Key)
        {
            case WindowKey.Escape:
                Root?.ReleaseKeyboard(this);
                break;

            // A modifier on its own is still on its way to a combination, and a key without one
            // would be taken from typing everywhere.
            case var key when !Enum.IsDefined(key) || modifiers == HotKeyModifiers.None:
                break;

            default:
                _recorded(new HotKey(modifiers, input.Key));
                Root?.ReleaseKeyboard(this);
                break;
        }

        return true;
    }

    internal override void OnKeyboardCaptureLost()
    {
        _recording = false;
        _recordingChanged(false);
        InvalidateLayout();
    }

    protected override void Activate()
    {
        if (_recording)
        {
            return;
        }

        // Holding the keyboard also keeps the keys from reaching anything else while recording.
        Root?.CaptureKeyboard(this);
        _recording = true;
        _recordingChanged(true);
        InvalidateLayout();
    }

    protected override SizeF MeasureCore(SizeF availableSize)
    {
        float textWidth = MathF.Ceiling(TextLayouts
            .Get(Label, LabelFont, new SizeF(10_000.0f, Height))
            .Metrics.WidthIncludingTrailingWhitespace);
        return new SizeF(MathF.Max(MinimumWidth, textWidth + (2.0f * HorizontalPadding)), Height);
    }

    protected override void DrawCore(in UiDrawContext context)
    {
        var background = new RoundedRectangle(
            new RectangleF(0.0f, 0.0f, Bounds.Width, Bounds.Height),
            UiDesign.ControlCornerRadius,
            UiDesign.ControlCornerRadius);
        context.FillRoundedRectangle(background, context.Palette.ControlSurface);
        if (HoverAmount > 0.0f)
        {
            context.FillRoundedRectangle(background, context.Palette.ControlHover, HoverAmount);
        }

        if (PressedAmount > 0.0f)
        {
            context.FillRoundedRectangle(background, context.Palette.ControlPressed, PressedAmount);
        }

        context.DrawRoundedRectangle(
            background,
            _recording || HasVisualState(UiVisualState.Focused) ? context.Palette.Accent : context.Palette.SurfaceBorder);
        context.DrawText(
            Label,
            LabelFont,
            new Rect(0.0f, 0.0f, Bounds.Width, Bounds.Height),
            _recording ? context.Palette.SecondaryText : context.Palette.PrimaryText,
            DrawTextOptions.Clip);
    }
}
