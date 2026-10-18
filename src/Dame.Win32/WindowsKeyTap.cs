using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.PInvoke;

namespace Dame.Win32;

/// <summary>
/// Notices the Windows key being pressed and released on its own, and keeps the Start menu from
/// opening for that tap. Combinations such as Win+E work as before.
/// </summary>
/// <remarks>
/// <para>
/// It watches every key press in the system through a low-level keyboard hook. Windows calls the
/// hook on the thread that installed it, through that thread's message loop, and removes a hook
/// that keeps the keyboard waiting, so it only keeps track of the key and hands the tap on.
/// </para>
/// <para>
/// While an elevated window has focus, the hook isn't told about keys, and the tap opens Start.
/// Only one can watch at a time.
/// </para>
/// </remarks>
internal sealed unsafe class WindowsKeyTap : IDisposable
{
    // An unassigned key, pressed just before the Windows key is let go, so the shell takes the
    // tap for part of a combination and leaves Start closed. AutoHotkey masks Start the same way.
    private const VIRTUAL_KEY MaskKey = (VIRTUAL_KEY)0xE8;

    private static WindowsKeyTap? _current;

    private readonly Action _tapped;
    private readonly HHOOK _hook;
    private bool _windowsKeyDown;
    private bool _combined;

    /// <param name="tapped">Runs inside the hook, where it should only post the work elsewhere.</param>
    internal WindowsKeyTap(Action tapped)
    {
        if (_current is not null)
        {
            throw new InvalidOperationException("The Windows key is already being watched.");
        }

        _tapped = tapped;
        _hook = SetWindowsHookEx(
            WINDOWS_HOOK_ID.WH_KEYBOARD_LL,
            &HookProcedure,
            (HINSTANCE)GetModuleHandle(default(PCWSTR)),
            0);
        if (_hook.IsNull)
        {
            throw NativeMethods.CreateLastErrorException("Could not watch the keyboard.");
        }

        _current = this;
    }

    public void Dispose()
    {
        if (_current != this)
        {
            return;
        }

        _ = UnhookWindowsHookEx(_hook);
        _current = null;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT HookProcedure(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code == HC_ACTION
            && _current is { } watcher
            && watcher.HoldsBack((uint)wParam.Value, (KBDLLHOOKSTRUCT*)lParam.Value))
        {
            return (LRESULT)1;
        }

        return CallNextHookEx(default, code, wParam, lParam);
    }

    /// <returns>Whether to keep the key from the rest of the system.</returns>
    private bool HoldsBack(uint message, KBDLLHOOKSTRUCT* key)
    {
        // Keys sent from here come back through the hook, and other programs' are no tap either.
        if ((key->flags & KBDLLHOOKSTRUCT_FLAGS.LLKHF_INJECTED) != 0)
        {
            return false;
        }

        bool pressed = message is WM_KEYDOWN or WM_SYSKEYDOWN;
        if (key->vkCode is not ((uint)VIRTUAL_KEY.VK_LWIN or (uint)VIRTUAL_KEY.VK_RWIN))
        {
            if (pressed && _windowsKeyDown)
            {
                _combined = true;
            }

            return false;
        }

        if (pressed)
        {
            // Holding the key repeats the press, which mustn't forget a combination under way.
            if (!_windowsKeyDown)
            {
                _windowsKeyDown = true;
                _combined = false;
            }

            return false;
        }

        _windowsKeyDown = false;
        if (_combined)
        {
            return false;
        }

        // The release is held back and sent again after the mask key, so the shell sees them in
        // that order.
        SendMaskedRelease((VIRTUAL_KEY)key->vkCode);
        _tapped();
        return true;
    }

    private static void SendMaskedRelease(VIRTUAL_KEY windowsKey)
    {
        Span<INPUT> inputs =
        [
            CreateKeyInput(MaskKey, default),
            CreateKeyInput(MaskKey, KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP),
            CreateKeyInput(windowsKey, KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP),
        ];
        _ = SendInput(inputs, sizeof(INPUT));
    }

    private static INPUT CreateKeyInput(VIRTUAL_KEY key, KEYBD_EVENT_FLAGS flags)
    {
        var input = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
        input.ki = new KEYBDINPUT { wVk = key, dwFlags = flags };
        return input;
    }
}
