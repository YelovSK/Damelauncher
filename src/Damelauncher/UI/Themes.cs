using Dame.UI.Foundation;
using Vortice.Mathematics;

namespace Damelauncher.UI;

internal static class Themes
{
    internal static UiTheme Dark { get; } = new(
        Background: FromHex("#121418"),
        Surface: FromHex("#121418"),
        OverlaySurface: FromHex("#0E1013", alpha: 0.94f),
        SurfaceBorder: FromHex("#292E38"),
        ControlSurface: FromHex("#1C2027"),
        Accent: FromHex("#599EFF"),
        ControlHover: FromHex("#292E38"),
        ControlPressed: FromHex("#3D4759"),
        PrimaryText: FromHex("#F0F2F7"),
        SecondaryText: FromHex("#919CAD"),
        ErrorText: FromHex("#FF6E73"),
        WarningText: FromHex("#E3B341"),
        SuccessText: FromHex("#56D364"));

    private static Color4 FromHex(string hex, float alpha = 1.0f)
    {
        ReadOnlySpan<char> digits = hex.AsSpan(hex[0] == '#' ? 1 : 0);
        byte r = (byte)Convert.ToInt32(digits[..2].ToString(), 16);
        byte g = (byte)Convert.ToInt32(digits[2..4].ToString(), 16);
        byte b = (byte)Convert.ToInt32(digits[4..6].ToString(), 16);
        return new Color4(r / 255f, g / 255f, b / 255f, alpha);
    }
}
