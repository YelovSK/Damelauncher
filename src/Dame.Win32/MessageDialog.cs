using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.PInvoke;

namespace Dame.Win32;

/// <summary>The system message box, for questions asked outside of any window.</summary>
internal static class MessageDialog
{
    private const MESSAGEBOX_STYLE OnTop = MESSAGEBOX_STYLE.MB_SETFOREGROUND | MESSAGEBOX_STYLE.MB_TOPMOST;

    /// <returns>Whether the user chose OK.</returns>
    internal static bool Confirm(string title, string text)
    {
        MESSAGEBOX_RESULT result = MessageBox(
            default,
            text,
            title,
            MESSAGEBOX_STYLE.MB_OKCANCEL | MESSAGEBOX_STYLE.MB_ICONWARNING | MESSAGEBOX_STYLE.MB_DEFBUTTON2 | OnTop);
        return result == MESSAGEBOX_RESULT.IDOK;
    }

    /// <returns>Whether the user chose Yes.</returns>
    internal static bool Ask(string title, string text)
    {
        MESSAGEBOX_RESULT result = MessageBox(
            default,
            text,
            title,
            MESSAGEBOX_STYLE.MB_YESNO | MESSAGEBOX_STYLE.MB_ICONQUESTION | OnTop);
        return result == MESSAGEBOX_RESULT.IDYES;
    }

    internal static void ShowError(string title, string text)
    {
        _ = MessageBox(default, text, title, MESSAGEBOX_STYLE.MB_OK | MESSAGEBOX_STYLE.MB_ICONERROR | OnTop);
    }
}
