using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.PInvoke;

namespace Dame.Win32;

internal delegate void CopyDataHandler(nuint dataId, ReadOnlySpan<byte> data);

/// <summary>
/// An invisible window that only receives messages, as an address for other processes to send
/// <c>WM_COPYDATA</c> replies to. Its messages arrive on the thread that created it.
/// </summary>
internal sealed unsafe class MessageWindow : IDisposable
{
    private static readonly string WindowClassName = typeof(MessageWindow).FullName!;
    // HWND_MESSAGE, the parent that makes a window message-only.
    private static readonly HWND MessageOnlyParent = new(-3);
    private static bool _windowClassRegistered;

    private readonly CopyDataHandler _copyDataReceived;
    private GCHandle _selfHandle;

    /// <param name="copyDataReceived">Gets the data only for the duration of the call.</param>
    internal MessageWindow(CopyDataHandler copyDataReceived)
    {
        _copyDataReceived = copyDataReceived;
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
                default,
                className,
                null,
                default,
                0,
                0,
                0,
                0,
                MessageOnlyParent,
                default,
                (HINSTANCE)instance,
                (void*)GCHandle.ToIntPtr(_selfHandle));
        }

        if (Handle == 0)
        {
            _selfHandle.Free();
            throw NativeMethods.CreateLastErrorException("Could not create a message window.");
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
                throw NativeMethods.CreateLastErrorException("Could not register the message window class.");
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

        if (message == WM_COPYDATA
            && handle != 0
            && GCHandle.FromIntPtr(handle).Target is MessageWindow self
            && (WindowCopyData.Data*)(nint)lParam is var data
            && data is not null)
        {
            var bytes = new ReadOnlySpan<byte>((void*)data->DataPointer, checked((int)data->ByteCount));
            self._copyDataReceived(data->DataId, bytes);
            return (LRESULT)1;
        }

        return DefWindowProc(window, message, wParam, lParam);
    }
}
