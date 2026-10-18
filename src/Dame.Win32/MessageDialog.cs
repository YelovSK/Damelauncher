using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.PInvoke;

namespace Dame.Win32;

/// <summary>The system message box, for questions asked outside of any window.</summary>
internal static class MessageDialog
{
    /// <returns>Whether the user chose OK.</returns>
    internal static bool Confirm(string title, string text)
    {
        MESSAGEBOX_RESULT result = MessageBox(
            default,
            text,
            title,
            MESSAGEBOX_STYLE.MB_OKCANCEL
                | MESSAGEBOX_STYLE.MB_ICONWARNING
                | MESSAGEBOX_STYLE.MB_DEFBUTTON2
                | MESSAGEBOX_STYLE.MB_SETFOREGROUND
                | MESSAGEBOX_STYLE.MB_TOPMOST);
        return result == MESSAGEBOX_RESULT.IDOK;
    }
}
