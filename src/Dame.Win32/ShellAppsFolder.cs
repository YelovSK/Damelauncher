using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using static Windows.Win32.PInvoke;

namespace Dame.Win32;

/// <param name="ParsingName">
/// The app's id within the Apps folder: an AppUserModelID for packaged and pinned apps, or a
/// path, sometimes behind a known folder GUID, for plain shortcuts.
/// </param>
internal readonly record struct ShellApp(string Name, string ParsingName);

/// <summary>
/// The Apps folder, which lists what the Start menu does: desktop programs and packaged apps alike.
/// </summary>
internal static class ShellAppsFolder
{
    private const string FolderPath = @"shell:AppsFolder\";

    /// <remarks>Runs on a COM-initialized thread.</remarks>
    internal static List<ShellApp> Enumerate()
    {
        SHGetKnownFolderItem(FOLDERID_AppsFolder, KNOWN_FOLDER_FLAG.KF_FLAG_DEFAULT, null, out IShellItem folder)
            .ThrowOnFailure();
        folder.BindToHandler(null, BHID_EnumItems, out IEnumShellItems items);

        var apps = new List<ShellApp>();
        // Some shell enumerators only hand out one item per call.
        var next = new IShellItem[1];
        while (true)
        {
            items.Next(next, out uint fetched);
            if (fetched == 0)
            {
                return apps;
            }

            if (GetDisplayName(next[0], SIGDN.SIGDN_NORMALDISPLAY) is { } name
                && GetDisplayName(next[0], SIGDN.SIGDN_PARENTRELATIVEPARSING) is { } parsingName)
            {
                apps.Add(new ShellApp(name, parsingName));
            }
        }
    }

    /// <summary>The app's location in the shell, where its icon can be found too.</summary>
    internal static string GetPath(string parsingName) => FolderPath + parsingName;

    internal static void Launch(string parsingName)
    {
        using var process = Process.Start(new ProcessStartInfo(GetPath(parsingName))
        {
            UseShellExecute = true,
        });
    }

    private static unsafe string? GetDisplayName(IShellItem item, SIGDN kind)
    {
        PWSTR name = default;
        try
        {
            item.GetDisplayName(kind, out name);
            return name.Value is null ? null : name.ToString();
        }
        finally
        {
            CoTaskMemFree(name.Value);
        }
    }
}
