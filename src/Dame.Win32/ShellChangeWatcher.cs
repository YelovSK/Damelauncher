using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.Shell.Common;
using static Windows.Win32.PInvoke;

namespace Dame.Win32;

/// <summary>
/// Reports the changes the shell announces in a folder, which covers virtual folders like the
/// Apps folder too, not only folders on disk. Its reports arrive on the thread that created it.
/// </summary>
internal sealed unsafe class ShellChangeWatcher : IDisposable
{
    private const uint ChangeMessage = WM_APP;

    private readonly SHCNE_ID _events;
    private readonly Action _changed;
    private readonly HiddenWindow _window;
    private readonly uint _registration;

    /// <param name="changed">Runs inside a window procedure, so work that can fail should go elsewhere.</param>
    internal ShellChangeWatcher(Guid knownFolder, SHCNE_ID events, Action changed)
    {
        _events = events;
        _changed = changed;
        _window = new HiddenWindow(HandleMessage, messageOnly: true);

        SHGetKnownFolderIDList(knownFolder, 0, null, out ITEMIDLIST* folder).ThrowOnFailure();
        try
        {
            var entry = new SHChangeNotifyEntry { pidl = folder, fRecursive = true };
            _registration = SHChangeNotifyRegister(
                (HWND)_window.Handle,
                SHCNRF_SOURCE.SHCNRF_InterruptLevel | SHCNRF_SOURCE.SHCNRF_ShellLevel | SHCNRF_SOURCE.SHCNRF_NewDelivery,
                (int)events,
                ChangeMessage,
                1,
                in entry);
        }
        finally
        {
            ILFree(folder);
        }

        if (_registration == 0)
        {
            _window.Dispose();
            throw new InvalidOperationException("Could not watch the folder for changes.");
        }
    }

    public void Dispose()
    {
        SHChangeNotifyDeregister(_registration);
        _window.Dispose();
    }

    private nint? HandleMessage(uint message, nuint wParam, nint lParam)
    {
        if (message != ChangeMessage)
        {
            return null;
        }

        int changeEvent;
        HANDLE notification = SHChangeNotification_Lock((HANDLE)(nint)wParam, (uint)lParam, null, &changeEvent);
        if (notification == default)
        {
            return 0;
        }

        SHChangeNotification_Unlock(notification);
        if (((SHCNE_ID)changeEvent & _events) != 0)
        {
            _changed();
        }

        return 0;
    }
}
