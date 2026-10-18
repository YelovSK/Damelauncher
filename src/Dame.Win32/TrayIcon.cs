using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.PInvoke;

namespace Dame.Win32;

internal sealed record TrayMenuItem(string Label, Action Invoke);

/// <summary>
/// An icon in the notification area, which raises an action when clicked and shows a menu when
/// right-clicked. It uses the executable's icon, or the generic one when it has none.
/// </summary>
/// <remarks>
/// The actions run inside a window procedure, where an exception ends the process, so ones
/// that can fail should be posted elsewhere.
/// </remarks>
internal sealed unsafe class TrayIcon : IDisposable
{
    // The icon has a window of its own, so any application message will do.
    private const uint CallbackMessage = WM_APP;
    // NIN_KEYSELECT: Enter or Space on the icon, which version 4 reports apart from a click.
    private const uint KeySelect = NIN_SELECT | 0x1;
    private const uint IconId = 1;

    // Explorer drops every icon when it restarts, and announces its new taskbar with this.
    private static readonly uint TaskbarCreated = RegisterWindowMessage("TaskbarCreated");

    private readonly string _tooltip;
    private readonly Action _activated;
    private readonly IReadOnlyList<TrayMenuItem> _menu;
    private readonly HiddenWindow _window;

    internal TrayIcon(string tooltip, Action activated, IReadOnlyList<TrayMenuItem> menu)
    {
        _tooltip = tooltip;
        _activated = activated;
        _menu = menu;
        // Not message-only: that would miss the taskbar's broadcast, and can't own the menu.
        _window = new HiddenWindow(HandleMessage, messageOnly: false);
        Add();
    }

    public void Dispose()
    {
        NOTIFYICONDATAW data = CreateData();
        _ = Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_DELETE, in data);
        _window.Dispose();
    }

    private void Add()
    {
        NOTIFYICONDATAW data = CreateData();
        data.uFlags = NOTIFY_ICON_DATA_FLAGS.NIF_MESSAGE
            | NOTIFY_ICON_DATA_FLAGS.NIF_ICON
            | NOTIFY_ICON_DATA_FLAGS.NIF_TIP
            | NOTIFY_ICON_DATA_FLAGS.NIF_SHOWTIP;
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = LoadAppIcon();
        Span<char> tip = data.szTip.AsSpan();
        _tooltip.AsSpan(0, Math.Min(_tooltip.Length, tip.Length - 1)).CopyTo(tip);

        // Fails while there's no taskbar yet, such as when starting with Windows, and the icon
        // is then added once the taskbar announces itself.
        if (Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_ADD, in data))
        {
            data.Anonymous.uVersion = NOTIFYICON_VERSION_4;
            _ = Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_SETVERSION, in data);
        }
    }

    private NOTIFYICONDATAW CreateData() => new()
    {
        cbSize = (uint)sizeof(NOTIFYICONDATAW),
        hWnd = (HWND)_window.Handle,
        uID = IconId,
    };

    private nint? HandleMessage(uint message, nuint wParam, nint lParam)
    {
        if (message == TaskbarCreated)
        {
            Add();
            return 0;
        }

        if (message != CallbackMessage)
        {
            return null;
        }

        // With version 4, the low word of lParam is the event, and wParam is where it happened.
        switch ((uint)(lParam & 0xFFFF))
        {
            case NIN_SELECT or KeySelect:
                _activated();
                break;

            case WM_CONTEXTMENU:
                ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                break;
        }

        return 0;
    }

    private void ShowMenu(int x, int y)
    {
        HMENU menu = CreatePopupMenu();
        try
        {
            for (int index = 0; index < _menu.Count; index++)
            {
                fixed (char* label = _menu[index].Label)
                {
                    // Ids start at 1, since choosing nothing returns 0.
                    _ = AppendMenu(menu, MENU_ITEM_FLAGS.MF_STRING, (nuint)(index + 1), label);
                }
            }

            var owner = (HWND)_window.Handle;
            // The menu only closes on a click elsewhere while its owner is in the foreground, and
            // only closes cleanly when the owner gets a message after it.
            _ = SetForegroundWindow(owner);
            int chosen = TrackPopupMenuEx(
                menu,
                (uint)(TRACK_POPUP_MENU_FLAGS.TPM_RETURNCMD
                    | TRACK_POPUP_MENU_FLAGS.TPM_RIGHTBUTTON
                    | TRACK_POPUP_MENU_FLAGS.TPM_NONOTIFY),
                x,
                y,
                owner,
                null);
            _ = PostMessage(owner, WM_NULL, 0, 0);

            if (chosen > 0)
            {
                _menu[chosen - 1].Invoke();
            }
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    // An executable built with an application icon carries it under the IDI_APPLICATION id.
    private static HICON LoadAppIcon()
    {
        HICON icon = LoadIcon((HINSTANCE)GetModuleHandle(default(PCWSTR)), IDI_APPLICATION);
        return icon.IsNull ? LoadIcon(default, IDI_APPLICATION) : icon;
    }
}
