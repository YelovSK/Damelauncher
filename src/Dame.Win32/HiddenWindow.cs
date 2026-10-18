using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.PInvoke;

namespace Dame.Win32;

/// <returns>The message's result, or <see langword="null"/> to leave it to the default handling.</returns>
/// <remarks>
/// Runs inside the window procedure, where an exception ends the process, so work that can fail
/// should be posted elsewhere.
/// </remarks>
internal delegate nint? WindowMessageHandler(uint message, nuint wParam, nint lParam);

/// <summary>
/// A window that is never shown, which exists to receive messages: replies from other
/// processes, or notifications from the shell. Its messages arrive on the thread that created it.
/// </summary>
internal sealed unsafe class HiddenWindow : IDisposable
{
    private static readonly string WindowClassName = typeof(HiddenWindow).FullName!;
    // HWND_MESSAGE, the parent that makes a window message-only.
    private static readonly HWND MessageOnlyParent = new(-3);
    private static bool _windowClassRegistered;

    private readonly WindowMessageHandler _handler;
    private GCHandle _selfHandle;

    /// <param name="messageOnly">
    /// Whether the window only receives messages sent straight to it. It then stays out of
    /// window enumeration, but misses broadcasts and can't be brought to the foreground, which a
    /// popup menu's owner has to be.
    /// </param>
    internal HiddenWindow(WindowMessageHandler handler, bool messageOnly)
    {
        _handler = handler;
        nint instance = GetModuleHandle(default(PCWSTR));
        if (!_windowClassRegistered)
        {
            RegisterWindowClass(instance);
            _windowClassRegistered = true;
        }

        _selfHandle = GCHandle.Alloc(this);
        fixed (char* className = WindowClassName)
        {
            Handle = CreateWindowEx(
                messageOnly ? default : WINDOW_EX_STYLE.WS_EX_TOOLWINDOW,
                className,
                null,
                messageOnly ? default : WINDOW_STYLE.WS_POPUP,
                0,
                0,
                0,
                0,
                messageOnly ? MessageOnlyParent : default,
                default,
                (HINSTANCE)instance,
                (void*)GCHandle.ToIntPtr(_selfHandle));
        }

        if (Handle == 0)
        {
            _selfHandle.Free();
            throw NativeMethods.CreateLastErrorException("Could not create a hidden window.");
        }
    }

    internal nint Handle { get; private set; }

    public void Dispose()
    {
        if (Handle != 0)
        {
            DestroyWindow((HWND)Handle);
            Handle = 0;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private static void RegisterWindowClass(nint instance)
    {
        fixed (char* className = WindowClassName)
        {
            WNDCLASSEXW windowClass = new()
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = &WindowProcedure,
                hInstance = (HINSTANCE)instance,
                lpszClassName = className,
            };

            if (RegisterClassEx(in windowClass) == 0)
            {
                throw NativeMethods.CreateLastErrorException("Could not register the hidden window class.");
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        nint handle;
        if (message == WM_NCCREATE)
        {
            handle = (nint)((CREATESTRUCTW*)(nint)lParam)->lpCreateParams;
            SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA, handle);
        }
        else
        {
            handle = GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA);
        }

        if (handle != 0
            && GCHandle.FromIntPtr(handle).Target is HiddenWindow self
            && self._handler(message, wParam, lParam) is { } result)
        {
            return (LRESULT)result;
        }

        return DefWindowProc(window, message, wParam, lParam);
    }
}
