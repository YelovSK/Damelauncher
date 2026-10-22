using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Shutdown;
using static Windows.Win32.PInvoke;

namespace Dame.Win32;

/// <summary>Locks, suspends, or ends the session the way the Start menu's power options do.</summary>
internal static unsafe class SystemPower
{
    private const SHUTDOWN_REASON PlannedReason =
        SHUTDOWN_REASON.SHTDN_REASON_MAJOR_OTHER | SHUTDOWN_REASON.SHTDN_REASON_FLAG_PLANNED;

    internal static void Lock()
    {
        if (!LockWorkStation())
        {
            throw NativeMethods.CreateLastErrorException("Could not lock the computer.");
        }
    }

    // SetSuspendState returns only after the computer wakes. Called from the UI thread, the launcher's
    // windows couldn't answer the suspend broadcast, which holds up other apps preparing for sleep.
    internal static void Sleep()
    {
        EnableShutdownPrivilege();
        var thread = new Thread(() => SetSuspendState(false, false, false))
        {
            IsBackground = true,
            Name = "Sleep",
        };
        thread.Start();
    }

    internal static void SignOut() => ExitWindows(EXIT_WINDOWS_FLAGS.EWX_LOGOFF, "sign out");

    // Hybrid, so Fast Startup applies to the next boot, as it does when shutting down from Start.
    internal static void ShutDown() =>
        ExitWindows(EXIT_WINDOWS_FLAGS.EWX_SHUTDOWN | EXIT_WINDOWS_FLAGS.EWX_POWEROFF | EXIT_WINDOWS_FLAGS.EWX_HYBRID_SHUTDOWN, "shut down");

    internal static void Restart() => ExitWindows(EXIT_WINDOWS_FLAGS.EWX_REBOOT, "restart");

    private static void ExitWindows(EXIT_WINDOWS_FLAGS flags, string action)
    {
        EnableShutdownPrivilege();
        if (!ExitWindowsEx(flags, PlannedReason))
        {
            throw NativeMethods.CreateLastErrorException($"Could not {action}.");
        }
    }

    // Shutting down and suspending need the privilege enabled, which processes hold but don't enable by default.
    private static void EnableShutdownPrivilege()
    {
        HANDLE token;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES | TOKEN_ACCESS_MASK.TOKEN_QUERY, &token))
        {
            throw NativeMethods.CreateLastErrorException("Could not open the process token.");
        }

        try
        {
            if (!LookupPrivilegeValue(null, SE_SHUTDOWN_NAME, out LUID shutdown))
            {
                throw NativeMethods.CreateLastErrorException("Could not look up the shutdown privilege.");
            }

            TOKEN_PRIVILEGES privileges = new() { PrivilegeCount = 1 };
            privileges.Privileges[0] = new LUID_AND_ATTRIBUTES
            {
                Luid = shutdown,
                Attributes = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED,
            };
            if (!AdjustTokenPrivileges(token, false, &privileges, 0, null, null))
            {
                throw NativeMethods.CreateLastErrorException("Could not enable the shutdown privilege.");
            }
        }
        finally
        {
            CloseHandle(token);
        }
    }
}
