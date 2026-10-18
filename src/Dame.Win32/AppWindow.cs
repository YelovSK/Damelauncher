using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Dame.Win32.Input;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;
using Windows.Win32.UI.Input.KeyboardAndMouse;
using Windows.Win32.UI.WindowsAndMessaging;
using static Windows.Win32.PInvoke;

namespace Dame.Win32;

internal sealed unsafe class AppWindow : IDisposable
{
    internal static readonly string WindowClassName = typeof(AppWindow).FullName!;
    // Every window on the thread, which the one running the loop renders along with itself.
    private static readonly List<AppWindow> OpenWindows = [];
    private static bool _windowClassRegistered;

    private readonly ConcurrentQueue<Action> _postedActions = new();
    private readonly Utf16TextInputDecoder _textInputDecoder = new();
    private readonly AppWindowFrame _frame;
    private readonly List<int> _hotKeyIds = [];
    private Timer? _repaintTimer;
    private GCHandle _selfHandle;
    // Shared by every window on the thread, since one loop runs them all and reports the failure.
    private static Exception? _unhandledException;
    private bool _frameRequested;
    private bool _runsLoop;
    private bool _trackingMouseLeave;
    private bool _maximized;
    private WINDOWPLACEMENT? _windowedPlacement;
    private WINDOW_STYLE _windowedStyle;
    private SHOW_WINDOW_CMD _initialShowCommand = SHOW_WINDOW_CMD.SW_SHOWNORMAL;
    private WindowPlacementState? _lastPlacement;
    private WindowCursor _cursor = WindowCursor.Default;
    private string? _title;
    private FileDropTarget? _dropTarget;
    private bool _oleInitialized;
    // Set while the app places the window itself, so a DPI change on the way doesn't resize it.
    private bool _placing;

    internal AppWindow(
        string title,
        int width,
        int height,
        WindowPlacementState? placement = null,
        AppWindowFrame frame = AppWindowFrame.Standard)
    {
        _frame = frame;
        nint instance = GetModuleHandle(default(PCWSTR));
        if (instance == 0)
        {
            throw NativeMethods.CreateLastErrorException("Could not get the application module handle.");
        }

        if (!_windowClassRegistered)
        {
            RegisterWindowClass(instance);
            _windowClassRegistered = true;
        }

        _selfHandle = GCHandle.Alloc(this);
        Handle = CreateWindow(instance, title, width, height, placement);

        if (Handle == 0)
        {
            _selfHandle.Free();
            throw NativeMethods.CreateLastErrorException("Could not create the main window.");
        }

        OpenWindows.Add(this);

        UpdateClientSize();
        Dpi = GetDpiForWindow((HWND)Handle);
        if (frame == AppWindowFrame.Popup)
        {
            NativeMethods.SetDwmWindowAttribute(
                (HWND)Handle,
                DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE,
                (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND);
        }
    }

    internal event Action? RenderFrame;
    internal event Action? Shown;
    internal event Action<int, int>? Resized;
    internal event Action? SizeMoveStarted;
    internal event Action? SizeMoveEnded;
    internal event Action<float>? DpiChanged;
    internal event Action<WindowFileDragEvent>? FileDragInput;
    internal event Action<WindowKeyEvent>? KeyPressed;
    internal event Action<string>? TextInput;
    internal event Action<WindowPointerEvent>? PointerInput;
    internal event Action<nuint, string>? CopyDataReceived;
    internal event Action<bool>? MaximizedChanged;
    internal event Action? Deactivated;
    /// <summary>The window was destroyed, by the user closing it or by <see cref="Close"/>.</summary>
    internal event Action? Closed;
    /// <summary>A hotkey registered with <see cref="RegisterHotKey"/> was pressed, anywhere in the system.</summary>
    internal event Action<int>? HotKeyPressed;

    internal nint Handle { get; private set; }

    /// <summary>
    /// The swap chain's signal that it's ready for another frame, which the loop waits for
    /// before rendering a requested one. Asked for on every wait, since it changes with the swap
    /// chain. Without one, a requested frame renders as soon as the loop gets to it.
    /// </summary>
    internal Func<nint>? FrameLatencyWaitHandle { get; set; }
    internal bool IsVisible => Handle != 0 && IsWindowVisible((HWND)Handle);
    internal int ClientWidth { get; private set; }
    internal int ClientHeight { get; private set; }
    internal float Dpi { get; private set; }
    internal bool IsFullscreen => _windowedPlacement.HasValue;
    internal bool IsMaximized => Handle != 0 && IsZoomed((HWND)Handle);

    /// <summary>
    /// How many client pixels at the top are off the screen. A maximized window hangs its frame
    /// past the screen edges, and with a custom title bar that part of the top is client area.
    /// </summary>
    internal int HiddenTopPixels =>
        _frame == AppWindowFrame.CustomTitleBar && !IsFullscreen && IsMaximized
            ? GetResizeBorderThickness((HWND)Handle)
            : 0;
    /// <summary>With a custom title bar, whether a client point in pixels drags the window.</summary>
    internal Func<PointF, bool>? IsDragArea { get; set; }

    // These are posted because the system ignores them while the window holds the mouse, as it
    // does through the click that asks for one.
    internal void Minimize() => Post(() => SendSystemCommand(SC_MINIMIZE));

    internal void ToggleMaximized() => Post(() => SendSystemCommand(IsMaximized ? SC_RESTORE : SC_MAXIMIZE));

    /// <summary>Closes the window the way its system close button would.</summary>
    internal void RequestClose() => Post(() => SendSystemCommand(SC_CLOSE));

    private void SendSystemCommand(uint command)
    {
        if (Handle != 0)
        {
            _ = SendMessage((HWND)Handle, WM_SYSCOMMAND, command, 0);
        }
    }

    internal void ApplyCursor(WindowCursor cursor)
    {
        _cursor = cursor;
        PCWSTR cursorName = cursor switch
        {
            WindowCursor.Pointer => IDC_HAND,
            WindowCursor.Text => IDC_IBEAM,
            WindowCursor.ResizeHorizontal => IDC_SIZEWE,
            WindowCursor.ResizeVertical => IDC_SIZENS,
            _ => IDC_ARROW,
        };
        HCURSOR cursorHandle = LoadCursor(default, cursorName);
        _ = SetCursor(cursorHandle);
    }

    internal void SetTitleBarTheme(bool dark, Color captionColor, Color textColor)
    {
        NativeMethods.SetDwmWindowAttribute(
            (HWND)Handle,
            DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE,
            dark ? 1 : 0);
        NativeMethods.SetDwmWindowAttribute(
            (HWND)Handle,
            DWMWINDOWATTRIBUTE.DWMWA_CAPTION_COLOR,
            ToColorRef(captionColor));
        NativeMethods.SetDwmWindowAttribute(
            (HWND)Handle,
            DWMWINDOWATTRIBUTE.DWMWA_TEXT_COLOR,
            ToColorRef(textColor));
    }

    internal void SetTitle(string title)
    {
        if (Handle == 0 || title == _title)
        {
            return;
        }

        _title = title;
        _ = SetWindowText((HWND)Handle, title);
    }

    internal void CenterOnPrimaryMonitor()
    {
        HMONITOR monitor = MonitorFromPoint(default, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
        MONITORINFO monitorInfo = new() { cbSize = (uint)sizeof(MONITORINFO) };
        if (monitor.IsNull
            || !GetMonitorInfo(monitor, ref monitorInfo)
            || !GetWindowRect((HWND)Handle, out RECT windowBounds))
        {
            return;
        }

        RECT workArea = monitorInfo.rcWork;
        int x = workArea.left + (workArea.Width - windowBounds.Width) / 2;
        int y = workArea.top + (workArea.Height - windowBounds.Height) / 2;
        SetWindowPos(
            (HWND)Handle,
            default,
            x,
            y,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
                | SET_WINDOW_POS_FLAGS.SWP_NOZORDER
                | SET_WINDOW_POS_FLAGS.SWP_NOSIZE);
    }

    /// <summary>Sizes the window in DIPs for the monitor under the cursor and centers it there.</summary>
    internal void CenterOnCursorMonitor(float width, float height)
    {
        if (!GetCursorPos(out Point cursor))
        {
            return;
        }

        HMONITOR monitor = MonitorFromPoint(cursor, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        MONITORINFO monitorInfo = new() { cbSize = (uint)sizeof(MONITORINFO) };
        if (monitor.IsNull
            || !GetMonitorInfo(monitor, ref monitorInfo)
            || GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out uint monitorDpi, out _).Failed)
        {
            return;
        }

        int pixelWidth = (int)MathF.Round(width * monitorDpi / 96.0f);
        int pixelHeight = (int)MathF.Round(height * monitorDpi / 96.0f);
        RECT workArea = monitorInfo.rcWork;
        _placing = true;
        try
        {
            SetWindowPos(
                (HWND)Handle,
                default,
                workArea.left + (workArea.Width - pixelWidth) / 2,
                workArea.top + (workArea.Height - pixelHeight) / 2,
                pixelWidth,
                pixelHeight,
                SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
        }
        finally
        {
            _placing = false;
        }

        // A hidden window isn't necessarily told that it moved to a monitor with another DPI.
        uint windowDpi = GetDpiForWindow((HWND)Handle);
        if (windowDpi != Dpi)
        {
            Dpi = windowDpi;
            DpiChanged?.Invoke(Dpi);
        }
    }

    /// <summary>
    /// Resizes a window without a frame, whose client area is the whole window, in DIPs, keeping
    /// its top-left corner in place.
    /// </summary>
    /// <remarks>
    /// Growing, the window first raises <see cref="Resized"/> and draws a frame at the new size,
    /// and only then grows. A swap chain anchored at the top-left shows that frame cropped to the
    /// old size, which looks the same, so the window never shows its new area empty or the old
    /// frame stretched over it. Shrinking just crops the frame already there until the next one.
    /// </remarks>
    internal void SetSize(float width, float height)
    {
        if (Handle == 0)
        {
            return;
        }

        int pixelWidth = (int)MathF.Round(width * Dpi / 96.0f);
        int pixelHeight = (int)MathF.Round(height * Dpi / 96.0f);
        if (pixelWidth > ClientWidth || pixelHeight > ClientHeight)
        {
            Resized?.Invoke(pixelWidth, pixelHeight);
            _frameRequested = true;
            RenderRequestedFrame();
        }

        SetWindowPos(
            (HWND)Handle,
            default,
            0,
            0,
            pixelWidth,
            pixelHeight,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOMOVE);
    }

    /// <summary>Shows the window with a fresh frame and brings it to the foreground.</summary>
    internal void Show()
    {
        if (Handle == 0)
        {
            return;
        }

        // Drawn before showing, so the window doesn't open on whatever it showed when it was hidden.
        _frameRequested = true;
        RenderRequestedFrame();
        ShowWindow((HWND)Handle, SHOW_WINDOW_CMD.SW_SHOW);
        _ = SetForegroundWindow((HWND)Handle);
    }

    internal void Hide()
    {
        if (IsVisible)
        {
            ShowWindow((HWND)Handle, SHOW_WINDOW_CMD.SW_HIDE);
        }
    }

    /// <summary>Registers a system-wide hotkey, which raises <see cref="HotKeyPressed"/> with its id.</summary>
    /// <returns>Whether it was registered, which it isn't when another app already has it.</returns>
    internal bool TryRegisterHotKey(int id, HotKeyModifiers modifiers, WindowKey key)
    {
        // Without NOREPEAT, holding the keys down would keep firing the hotkey.
        if (!RegisterHotKey(
            (HWND)Handle,
            id,
            (HOT_KEY_MODIFIERS)modifiers | HOT_KEY_MODIFIERS.MOD_NOREPEAT,
            (uint)key))
        {
            return false;
        }

        _hotKeyIds.Add(id);
        return true;
    }

    internal void UnregisterHotKey(int id)
    {
        if (_hotKeyIds.Remove(id))
        {
            _ = Windows.Win32.PInvoke.UnregisterHotKey((HWND)Handle, id);
        }
    }

    internal void Close()
    {
        if (Handle != 0)
        {
            DestroyWindow((HWND)Handle);
        }
    }

    internal bool Activate()
    {
        if (Handle != 0)
        {
            if (IsIconic((HWND)Handle))
            {
                ShowWindow((HWND)Handle, SHOW_WINDOW_CMD.SW_RESTORE);
            }

            return SetForegroundWindow((HWND)Handle);
        }

        return false;
    }

    internal void ToggleFullscreen()
    {
        if (_windowedPlacement is null)
        {
            EnterFullscreen();
        }
        else
        {
            ExitFullscreen();
        }
    }

    private void EnterFullscreen()
    {
        WINDOWPLACEMENT placement = new() { length = (uint)sizeof(WINDOWPLACEMENT) };
        HMONITOR monitor = MonitorFromWindow(
            (HWND)Handle,
            MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        MONITORINFO monitorInfo = new() { cbSize = (uint)sizeof(MONITORINFO) };
        if (!GetWindowPlacement((HWND)Handle, ref placement)
            || monitor.IsNull
            || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        _windowedPlacement = placement;
        _windowedStyle = (WINDOW_STYLE)(nuint)GetWindowLongPtr(
            (HWND)Handle,
            WINDOW_LONG_PTR_INDEX.GWL_STYLE);
        _ = SetWindowLongPtr(
            (HWND)Handle,
            WINDOW_LONG_PTR_INDEX.GWL_STYLE,
            (nint)(nuint)(_windowedStyle & ~WINDOW_STYLE.WS_OVERLAPPEDWINDOW));

        RECT bounds = monitorInfo.rcMonitor;
        SetWindowPos(
            (HWND)Handle,
            default,
            bounds.left,
            bounds.top,
            bounds.Width,
            bounds.Height,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
                | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER
                | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
    }

    private void ExitFullscreen()
    {
        if (_windowedPlacement is not { } placement)
        {
            return;
        }

        // Cleared first, so the frame changes below get the windowed frame.
        _windowedPlacement = null;
        _ = SetWindowLongPtr(
            (HWND)Handle,
            WINDOW_LONG_PTR_INDEX.GWL_STYLE,
            (nint)(nuint)_windowedStyle);
        _ = SetWindowPlacement((HWND)Handle, in placement);
        SetWindowPos(
            (HWND)Handle,
            default,
            0,
            0,
            0,
            0,
            SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE
                | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER
                | SET_WINDOW_POS_FLAGS.SWP_NOMOVE
                | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
                | SET_WINDOW_POS_FLAGS.SWP_NOZORDER
                | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
    }

    internal void RestorePlacement(WindowPlacementState placement)
    {
        if (!placement.IsUsable)
        {
            return;
        }

        (int originX, int originY) = GetWorkAreaOrigin(Handle);
        int left = placement.X - originX;
        int top = placement.Y - originY;
        WINDOWPLACEMENT native = new()
        {
            length = (uint)sizeof(WINDOWPLACEMENT),
            showCmd = placement.Maximized ? SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED : SHOW_WINDOW_CMD.SW_SHOWNORMAL,
            rcNormalPosition = new RECT(
                left,
                top,
                left + placement.Width,
                top + placement.Height),
        };

        if (SetWindowPlacement((HWND)Handle, in native))
        {
            _initialShowCommand = native.showCmd;
        }
    }

    internal WindowPlacementState CapturePlacement()
    {
        if (_windowedPlacement is { } windowedPlacement)
        {
            return ToPlacementState(windowedPlacement, Handle);
        }

        if (Handle == 0)
        {
            return _lastPlacement ?? new WindowPlacementState
            {
                Width = ClientWidth,
                Height = ClientHeight,
            };
        }

        return CapturePlacement(Handle);
    }

    private static (int X, int Y) GetWorkAreaOrigin(nint window)
    {
        HMONITOR monitor = MonitorFromWindow((HWND)window, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        MONITORINFO info = new() { cbSize = (uint)sizeof(MONITORINFO) };
        return monitor.IsNull || !GetMonitorInfo(monitor, ref info)
            ? (0, 0)
            : (info.rcWork.left, info.rcWork.top);
    }

    private static WindowPlacementState CapturePlacement(nint window)
    {
        WINDOWPLACEMENT native = new() { length = (uint)sizeof(WINDOWPLACEMENT) };
        if (!GetWindowPlacement((HWND)window, ref native))
        {
            return new WindowPlacementState
            {
                Width = 0,
                Height = 0,
            };
        }

        return ToPlacementState(native, window);
    }

    // Windows reports the restored rectangle in work-area coordinates but places new windows in
    // screen coordinates, so storing it as-is would shift the window by the taskbar every run.
    private static WindowPlacementState ToPlacementState(WINDOWPLACEMENT native, nint window)
    {
        RECT normal = native.rcNormalPosition;
        (int originX, int originY) = GetWorkAreaOrigin(window);
        return new WindowPlacementState
        {
            X = normal.left + originX,
            Y = normal.top + originY,
            Width = normal.Width,
            Height = normal.Height,
            Maximized = native.showCmd == SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED,
        };
    }

    internal void RequestRepaint()
    {
        if (_frameRequested)
        {
            return;
        }

        _frameRequested = true;
        if (Handle != 0)
        {
            _ = PostMessage((HWND)Handle, NativeMethods.MessageRenderFrame, 0, 0);
        }
    }

    internal void RequestRepaintAfter(TimeSpan delay)
    {
        if (Handle == 0)
        {
            return;
        }

        _repaintTimer ??= new Timer(
            static state =>
            {
                var window = (AppWindow)state!;
                window.Post(window.RequestRepaint);
            },
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _repaintTimer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    internal void Post(Action action)
    {
        nint window = Handle;
        if (window == 0)
        {
            return;
        }

        _postedActions.Enqueue(action);
        _ = PostMessage((HWND)window, NativeMethods.MessageDispatch, 0, 0);
    }

    /// <summary>
    /// Runs the thread's message loop until this window is destroyed. Other windows on the
    /// thread need no loop of their own and can come and go meanwhile; the loop renders them too.
    /// </summary>
    /// <remarks>
    /// Frames render between messages, never inside one, so a running animation can't keep input
    /// waiting: posted messages, like a request for the next frame, come before input otherwise.
    /// </remarks>
    internal void Run()
    {
        _runsLoop = true;
        RequestRepaint();
        RenderRequestedFrame();
        ShowWindow((HWND)Handle, _initialShowCommand);
        Shown?.Invoke();

        // Registering initializes OLE, which the first frame shouldn't wait for.
        RegisterFileDropTarget();

        var waiting = new List<AppWindow>();
        nint[] handles = [];
        bool quit = false;
        while (!quit)
        {
            while (PeekMessage(out MSG message, default, 0, 0, PEEK_MESSAGE_REMOVE_TYPE.PM_REMOVE))
            {
                if (message.message == WM_QUIT)
                {
                    quit = true;
                    break;
                }

                TranslateMessage(in message);
                DispatchMessage(in message);
            }

            if (quit)
            {
                break;
            }

            waiting.Clear();
            // By index, since rendering can fail and destroy a window, which leaves the list.
            for (int index = 0; index < OpenWindows.Count; index++)
            {
                AppWindow window = OpenWindows[index];
                if (!window._frameRequested)
                {
                    continue;
                }

                nint handle = window.FrameLatencyWaitHandle?.Invoke() ?? 0;
                if (handle == 0)
                {
                    window.RenderRequestedFrame();
                    continue;
                }

                if (handles.Length == waiting.Count)
                {
                    Array.Resize(ref handles, waiting.Count + 1);
                }

                handles[waiting.Count] = handle;
                waiting.Add(window);
            }

            WAIT_EVENT waitResult = NativeMethods.WaitForMessageOrHandles(handles.AsSpan(0, waiting.Count));
            if (waitResult == WAIT_EVENT.WAIT_FAILED)
            {
                throw NativeMethods.CreateLastErrorException("Could not wait for a window message or frame.");
            }

            int ready = (int)(waitResult - WAIT_EVENT.WAIT_OBJECT_0);
            if (ready >= 0 && ready < waiting.Count)
            {
                waiting[ready].RenderRequestedFrame();
            }
        }

        if (_unhandledException is not null)
        {
            ExceptionDispatchInfo.Capture(_unhandledException).Throw();
        }
    }

    public void Dispose()
    {
        _repaintTimer?.Dispose();
        if (Handle != 0)
        {
            // Sends WM_DESTROY synchronously, which is where the drop target is revoked.
            DestroyWindow((HWND)Handle);
            Handle = 0;
        }

        if (_oleInitialized)
        {
            OleUninitialize();
            _oleInitialized = false;
        }

        if (_selfHandle.IsAllocated)
        {
            _selfHandle.Free();
        }
    }

    private void RegisterFileDropTarget()
    {
        // RegisterDragDrop requires OleInitialize. It returns S_FALSE here because COM is
        // already initialized, which still needs a matching OleUninitialize.
        _oleInitialized = OleInitialize().Succeeded;
        var target = new FileDropTarget(Handle, input => FileDragInput?.Invoke(input));

        // Failing here only costs drag-and-drop, so the window still opens.
        if (RegisterDragDrop((HWND)Handle, target).Succeeded)
        {
            _dropTarget = target;
        }
    }

    // Created at its final size and state, so nothing built from the client size is redone.
    private nint CreateWindow(
        nint instance,
        string title,
        int width,
        int height,
        WindowPlacementState? placement)
    {
        WINDOW_STYLE style = WINDOW_STYLE.WS_OVERLAPPEDWINDOW;
        WINDOW_EX_STYLE extendedStyle = default;
        if (_frame == AppWindowFrame.Popup)
        {
            style = WINDOW_STYLE.WS_POPUP;
            extendedStyle = WINDOW_EX_STYLE.WS_EX_TOOLWINDOW | WINDOW_EX_STYLE.WS_EX_TOPMOST;
            _initialShowCommand = SHOW_WINDOW_CMD.SW_HIDE;
        }

        int x = CW_USEDEFAULT;
        int y = CW_USEDEFAULT;
        if (placement is { IsUsable: true } saved && IsOnAnyMonitor(saved))
        {
            x = saved.X;
            y = saved.Y;
            width = saved.Width;
            height = saved.Height;
            if (saved.Maximized)
            {
                style |= WINDOW_STYLE.WS_MAXIMIZE;
                _initialShowCommand = SHOW_WINDOW_CMD.SW_SHOWMAXIMIZED;
            }
        }

        fixed (char* className = WindowClassName)
        fixed (char* windowTitle = title)
        {
            HWND window = CreateWindowEx(
                extendedStyle,
                className,
                windowTitle,
                style,
                x,
                y,
                width,
                height,
                default,
                default,
                (HINSTANCE)instance,
                (void*)GCHandle.ToIntPtr(_selfHandle));

            return window;
        }
    }

    // CreateWindowEx doesn't pull a window back on screen the way SetWindowPlacement does,
    // so a placement saved on a monitor that's gone would open off-screen.
    private static bool IsOnAnyMonitor(WindowPlacementState placement)
    {
        RECT bounds = new()
        {
            left = placement.X,
            top = placement.Y,
            right = placement.X + placement.Width,
            bottom = placement.Y + placement.Height,
        };
        return !MonitorFromRect(bounds, MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONULL).IsNull;
    }

    private static void RegisterWindowClass(nint instance)
    {
        fixed (char* className = WindowClassName)
        {
            WNDCLASSEXW windowClass = new()
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                style = WNDCLASS_STYLES.CS_HREDRAW
                    | WNDCLASS_STYLES.CS_VREDRAW
                    | WNDCLASS_STYLES.CS_DBLCLKS,
                lpfnWndProc = &WindowProcedure,
                hInstance = (HINSTANCE)instance,
                hIcon = LoadIcon((HINSTANCE)instance, IDI_APPLICATION),
                hCursor = LoadCursor(default, IDC_ARROW),
                lpszClassName = className,
            };

            windowClass.hIconSm = windowClass.hIcon;

            if (RegisterClassEx(in windowClass) == 0)
            {
                throw NativeMethods.CreateLastErrorException("Could not register the window class.");
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static LRESULT WindowProcedure(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        AppWindow? appWindow = GetWindow(window, message, lParam);
        if (appWindow is null)
        {
            return DefWindowProc(window, message, wParam, lParam);
        }

        try
        {
            return appWindow.ProcessMessage(window, message, wParam, lParam);
        }
        catch (Exception exception)
        {
            _unhandledException ??= exception;
            DestroyWindow(window);
            // Destroying the window normally ends the loop, but not when the failure
            // happened while the window was being destroyed.
            NativeMethods.RequestMessageLoopExit();
            return default;
        }
    }

    private static AppWindow? GetWindow(HWND window, uint message, LPARAM lParam)
    {
        nint handle;

        if (message == WM_NCCREATE)
        {
            var create = (CREATESTRUCTW*)(nint)lParam;
            handle = (nint)create->lpCreateParams;
            SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA, handle);
        }
        else
        {
            handle = GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWLP_USERDATA);
        }

        return handle == 0 ? null : GCHandle.FromIntPtr(handle).Target as AppWindow;
    }

    private LRESULT ProcessMessage(HWND window, uint message, WPARAM wParam, LPARAM lParam)
    {
        switch (message)
        {
            case WM_PAINT:
                BeginPaint(window, out PAINTSTRUCT paint);
                EndPaint(window, in paint);
                _frameRequested = true;
                return default;

            // Only wakes the loop, which renders the frame.
            case NativeMethods.MessageRenderFrame:
                return default;

            case NativeMethods.MessageDispatch:
                while (_postedActions.TryDequeue(out Action? action))
                {
                    action();
                }

                return default;

            case WM_ERASEBKGND:
                return (LRESULT)1;

            // Creating the window sends this without the extra rectangles (wParam is 0), but the
            // window rectangle comes first either way. Skipping that call would leave the system
            // caption in place until something else recalculates the frame.
            case WM_NCCALCSIZE when _frame == AppWindowFrame.CustomTitleBar && !IsFullscreen:
                var frame = (RECT*)(nint)lParam;
                int top = frame->top;
                LRESULT calculated = DefWindowProc(window, message, wParam, lParam);
                // The sides and bottom keep their frame to resize by, while the client area takes
                // over the caption. Even maximized, the top stays client area where it hangs off
                // the screen, since an empty frame there shows while the window animates open.
                frame->top = top;
                return calculated;

            case WM_NCHITTEST when _frame == AppWindowFrame.CustomTitleBar && !IsFullscreen:
                return HitTestCustomTitleBar(window, wParam, lParam);

            case WM_SETCURSOR when GetLowWord(lParam) == HTCLIENT:
                ApplyCursor(_cursor);
                return (LRESULT)1;

            // Keys pressed with Alt arrive as system keys, which are the app's too, except for
            // Alt+F4 still closing the window.
            case WM_KEYDOWN:
            case WM_SYSKEYDOWN:
                KeyPressed?.Invoke(new WindowKeyEvent(
                    (WindowKey)(nuint)wParam,
                    GetKeyState((int)VIRTUAL_KEY.VK_SHIFT) < 0,
                    GetKeyState((int)VIRTUAL_KEY.VK_CONTROL) < 0,
                    GetKeyState((int)VIRTUAL_KEY.VK_MENU) < 0,
                    IsRepeat: (((long)lParam >> 16) & KF_REPEAT) != 0));
                return message == WM_SYSKEYDOWN && (WindowKey)(nuint)wParam == WindowKey.F4
                    ? DefWindowProc(window, message, wParam, lParam)
                    : default;

            case WM_CHAR:
                if (_textInputDecoder.Push((char)(nuint)wParam) is { } text)
                {
                    TextInput?.Invoke(text);
                }

                return default;

            case WM_LBUTTONDOWN:
                _ = SetCapture(window);
                PointerInput?.Invoke(new WindowPointerEvent(
                    WindowPointerEventKind.Pressed,
                    GetPoint(lParam),
                    PointerButton.Primary));
                return default;

            case WM_MOUSEMOVE:
                if (!_trackingMouseLeave)
                {
                    TRACKMOUSEEVENT tracking = new()
                    {
                        cbSize = (uint)sizeof(TRACKMOUSEEVENT),
                        dwFlags = TRACKMOUSEEVENT_FLAGS.TME_LEAVE,
                        hwndTrack = window,
                    };
                    _trackingMouseLeave = TrackMouseEvent(ref tracking);
                }

                PointerInput?.Invoke(new WindowPointerEvent(
                    WindowPointerEventKind.Moved,
                    GetPoint(lParam)));
                return default;

            case WM_MOUSELEAVE:
                _trackingMouseLeave = false;
                PointerInput?.Invoke(new WindowPointerEvent(WindowPointerEventKind.Left, PointF.Empty));
                return default;

            case WM_LBUTTONUP:
                PointerInput?.Invoke(new WindowPointerEvent(
                    WindowPointerEventKind.Released,
                    GetPoint(lParam),
                    PointerButton.Primary));
                _ = ReleaseCapture();
                return default;

            case WM_CAPTURECHANGED:
                PointerInput?.Invoke(new WindowPointerEvent(
                    WindowPointerEventKind.Cancelled,
                    PointF.Empty));
                return default;

            case WM_LBUTTONDBLCLK:
                PointerInput?.Invoke(new WindowPointerEvent(
                    WindowPointerEventKind.DoubleClicked,
                    GetPoint(lParam),
                    PointerButton.Primary));
                return default;

            case WM_MBUTTONDOWN:
            case WM_MBUTTONDBLCLK:
                PointerInput?.Invoke(new WindowPointerEvent(
                    WindowPointerEventKind.Pressed,
                    GetPoint(lParam),
                    PointerButton.Middle));
                return default;

            // Nothing double-clicks this button, so the second click is just another press.
            case WM_RBUTTONDOWN:
            case WM_RBUTTONDBLCLK:
                PointerInput?.Invoke(new WindowPointerEvent(
                    WindowPointerEventKind.Pressed,
                    GetPoint(lParam),
                    PointerButton.Secondary));
                return default;

            case WM_RBUTTONUP:
                PointerInput?.Invoke(new WindowPointerEvent(
                    WindowPointerEventKind.Released,
                    GetPoint(lParam),
                    PointerButton.Secondary));
                return default;

            case WM_MOUSEWHEEL:
                var wheelPoint = new Point(GetLowWord(lParam), GetHighWord(lParam));
                _ = ScreenToClient(window, ref wheelPoint);
                PointerInput?.Invoke(new WindowPointerEvent(
                    WindowPointerEventKind.Wheel,
                    new PointF(wheelPoint.X, wheelPoint.Y),
                    WheelDelta: GetHighWord((nint)(nuint)wParam)));
                return default;

            case WM_SIZE:
                ClientWidth = unchecked((ushort)(long)lParam);
                ClientHeight = unchecked((ushort)((long)lParam >> 16));
                // A minimized window remembers whether it comes back maximized.
                bool maximized = (nuint)wParam == SIZE_MAXIMIZED;
                if ((nuint)wParam != SIZE_MINIMIZED && maximized != _maximized)
                {
                    _maximized = maximized;
                    MaximizedChanged?.Invoke(maximized);
                }

                Resized?.Invoke(ClientWidth, ClientHeight);
                RenderRequestedFrame();
                return default;

            case WM_ENTERSIZEMOVE:
                SizeMoveStarted?.Invoke();
                return default;

            case WM_EXITSIZEMOVE:
                SizeMoveEnded?.Invoke();
                return default;

            case WM_DPICHANGED:
                Dpi = unchecked((ushort)(ulong)wParam);
                if (!_placing)
                {
                    var suggested = (RECT*)(nint)lParam;
                    SetWindowPos(
                        window,
                        default,
                        suggested->left,
                        suggested->top,
                        suggested->Width,
                        suggested->Height,
                        SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER);
                }

                DpiChanged?.Invoke(Dpi);
                return default;

            case WM_ACTIVATE:
                if (((nuint)wParam & 0xFFFF) == WA_INACTIVE)
                {
                    Deactivated?.Invoke();
                }

                return DefWindowProc(window, message, wParam, lParam);

            case WM_HOTKEY:
                HotKeyPressed?.Invoke((int)(nuint)wParam);
                return default;

            case WM_COPYDATA:
                var data = (WindowCopyData.Data*)(nint)lParam;
                if (data is null
                    || data->ByteCount % sizeof(char) != 0
                    || data->ByteCount > (uint)int.MaxValue * sizeof(char)
                    || (data->ByteCount > 0 && data->DataPointer == 0))
                {
                    return default;
                }

                int characterCount = checked((int)(data->ByteCount / sizeof(char)));
                string copyDataText = characterCount == 0
                    ? string.Empty
                    : new string((char*)data->DataPointer, 0, characterCount);
                CopyDataReceived?.Invoke(data->DataId, copyDataText);
                return (LRESULT)1;

            case WM_DESTROY:
                WindowPlacementState placement = CapturePlacement();
                if (placement.IsUsable)
                {
                    _lastPlacement = placement;
                }

                // OLE holds a reference to the drop target until the window revokes it,
                // and this is the last point where the handle is still valid.
                if (_dropTarget is not null)
                {
                    RevokeDragDrop(window);
                    _dropTarget = null;
                }

                foreach (int id in _hotKeyIds)
                {
                    Windows.Win32.PInvoke.UnregisterHotKey(window, id);
                }

                _hotKeyIds.Clear();

                Handle = 0;
                _frameRequested = false;
                _postedActions.Clear();
                OpenWindows.Remove(this);
                if (_runsLoop)
                {
                    NativeMethods.RequestMessageLoopExit();
                }

                Closed?.Invoke();
                return default;

            default:
                return DefWindowProc(window, message, wParam, lParam);
        }
    }

    private LRESULT HitTestCustomTitleBar(HWND window, WPARAM wParam, LPARAM lParam)
    {
        var point = new Point(GetLowWord(lParam), GetHighWord(lParam));
        _ = ScreenToClient(window, ref point);

        // The system still finds its own caption buttons where its title bar used to be, even
        // inside the client area, so it only gets to answer for the frame kept around the sides.
        if (point.X < 0 || point.Y < 0 || point.X >= ClientWidth || point.Y >= ClientHeight)
        {
            return DefWindowProc(window, WM_NCHITTEST, wParam, lParam);
        }

        // Without a frame above the client area, its top edge stands in for one to resize by.
        if (!IsZoomed(window) && GetResizeBorderThickness(window) is var border && point.Y < border)
        {
            uint edge = point.X < border ? HTTOPLEFT
                : point.X >= ClientWidth - border ? HTTOPRIGHT
                : HTTOP;
            return (LRESULT)(nint)edge;
        }

        bool dragArea = IsDragArea?.Invoke(new PointF(point.X, point.Y)) == true;
        return (LRESULT)(nint)(dragArea ? HTCAPTION : HTCLIENT);
    }

    private static int GetResizeBorderThickness(HWND window)
    {
        uint dpi = GetDpiForWindow(window);
        return GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CYSIZEFRAME, dpi)
            + GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXPADDEDBORDER, dpi);
    }

    private void UpdateClientSize()
    {
        if (!GetClientRect((HWND)Handle, out RECT clientRect))
        {
            throw NativeMethods.CreateLastErrorException("Could not get the client size.");
        }

        ClientWidth = clientRect.Width;
        ClientHeight = clientRect.Height;
    }

    private void RenderRequestedFrame()
    {
        if (!_frameRequested)
        {
            return;
        }

        _frameRequested = false;
        RenderFrame?.Invoke();
    }

    private static PointF GetPoint(nint value) => new(GetLowWord(value), GetHighWord(value));

    private static int GetLowWord(nint value) => unchecked((short)(long)value);

    private static int GetHighWord(nint value) => unchecked((short)((long)value >> 16));

    private static int ToColorRef(Color color)
    {
        return color.R | (color.G << 8) | (color.B << 16);
    }
}
