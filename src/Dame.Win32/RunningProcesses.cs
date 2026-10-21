using Windows.Win32.Foundation;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.System.Threading;
using static Windows.Win32.PInvoke;

namespace Dame.Win32;

/// <param name="StartTime">
/// When the process started, which tells it apart from a later one given the same id.
/// </param>
internal readonly record struct RunningProcess(uint Id, string Name, string? Path, long StartTime);

/// <summary>Lists the processes this one may end, and ends them.</summary>
internal static unsafe class RunningProcesses
{
    private const PROCESS_ACCESS_RIGHTS Access =
        PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_ACCESS_RIGHTS.PROCESS_TERMINATE;

    // Processes of other users, protected ones, and elevated ones when this isn't, refuse to be
    // opened for ending, so asking for that access is what leaves them out.
    internal static List<RunningProcess> ListTerminable()
    {
        var processes = new List<RunningProcess>();
        HANDLE snapshot = CreateToolhelp32Snapshot(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
        if ((nint)snapshot.Value == -1)
        {
            throw NativeMethods.CreateLastErrorException("Could not list the running processes.");
        }

        try
        {
            var entry = new PROCESSENTRY32W { dwSize = (uint)sizeof(PROCESSENTRY32W) };
            uint self = GetCurrentProcessId();
            for (bool more = Process32FirstW(snapshot, &entry); more; more = Process32NextW(snapshot, &entry))
            {
                if (entry.th32ProcessID == self)
                {
                    continue;
                }

                HANDLE process = OpenProcess(Access, false, entry.th32ProcessID);
                if (process.IsNull)
                {
                    continue;
                }

                try
                {
                    processes.Add(new RunningProcess(
                        entry.th32ProcessID,
                        entry.szExeFile.ToString(),
                        GetImagePath(process),
                        GetStartTime(process)));
                }
                finally
                {
                    CloseHandle(process);
                }
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return processes;
    }

    /// <summary>Ends the process, unless it has exited and its id belongs to another one since.</summary>
    /// <returns>Whether this ended it.</returns>
    internal static bool TryTerminate(RunningProcess target)
    {
        HANDLE process = OpenProcess(Access, false, target.Id);
        if (process.IsNull)
        {
            return false;
        }

        try
        {
            return GetStartTime(process) == target.StartTime && TerminateProcess(process, 1);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static string? GetImagePath(HANDLE process)
    {
        char* buffer = stackalloc char[1024];
        uint length = 1024;
        return QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, &length)
            ? new string(buffer, 0, (int)length)
            : null;
    }

    private static long GetStartTime(HANDLE process)
    {
        System.Runtime.InteropServices.ComTypes.FILETIME creation, exit, kernel, user;
        return GetProcessTimes(process, &creation, &exit, &kernel, &user)
            ? ((long)creation.dwHighDateTime << 32) | (uint)creation.dwLowDateTime
            : 0;
    }
}
