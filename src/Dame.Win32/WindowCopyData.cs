using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using static Windows.Win32.PInvoke;

namespace Dame.Win32;

internal static unsafe class WindowCopyData
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Data
    {
        internal nuint DataId;
        internal uint ByteCount;
        internal nint DataPointer;
    }

    /// <summary>Sends text to a window of this app, which may then take the foreground.</summary>
    internal static bool TrySend(string windowClassName, nuint dataId, string payload)
    {
        HWND window = Find(windowClassName);
        if (window.IsNull)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(window, out uint processId) != 0
            && AllowSetForegroundWindow(processId);
        return Send(window, dataId, MemoryMarshal.AsBytes(payload.AsSpan()));
    }

    /// <returns>Whether a window of the class was found and accepted the data.</returns>
    internal static bool TrySend(string windowClassName, nuint dataId, ReadOnlySpan<byte> payload)
    {
        HWND window = Find(windowClassName);
        return !window.IsNull && Send(window, dataId, payload);
    }

    /// <summary>Reads a received <c>WM_COPYDATA</c>, whose data is only valid while it's being handled.</summary>
    internal static bool TryRead(uint message, nint lParam, out nuint dataId, out ReadOnlySpan<byte> payload)
    {
        var data = (Data*)lParam;
        if (message != WM_COPYDATA || data is null)
        {
            dataId = 0;
            payload = default;
            return false;
        }

        dataId = data->DataId;
        payload = new ReadOnlySpan<byte>((void*)data->DataPointer, checked((int)data->ByteCount));
        return true;
    }

    private static HWND Find(string windowClassName)
    {
        fixed (char* className = windowClassName)
        {
            return FindWindow(className, default);
        }
    }

    private static bool Send(HWND window, nuint dataId, ReadOnlySpan<byte> payload)
    {
        fixed (byte* bytes = payload)
        {
            var data = new Data
            {
                DataId = dataId,
                ByteCount = checked((uint)payload.Length),
                DataPointer = payload.IsEmpty ? 0 : (nint)bytes,
            };
            return SendMessage(
                window,
                WM_COPYDATA,
                default,
                (LPARAM)(nint)(&data)) != 0;
        }
    }
}
