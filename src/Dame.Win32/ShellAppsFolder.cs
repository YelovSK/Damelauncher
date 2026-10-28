using System.Diagnostics;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.Shell;
using static Windows.Win32.PInvoke;

namespace Dame.Win32;

/// <param name="ParsingName">
/// The app's id within the Apps folder: an AppUserModelID for packaged and pinned apps, or a
/// path, sometimes behind a known folder GUID, for plain shortcuts.
/// </param>
/// <param name="TargetPath">The file a desktop program's shortcut opens.</param>
internal readonly record struct ShellApp(string Name, string ParsingName, string? TargetPath);

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
                string? targetPath = IsPackaged(parsingName) ? null : GetTargetPath((IShellItem2)next[0]);
                apps.Add(new ShellApp(name, parsingName, targetPath));
            }
        }
    }

    /// <summary>Reports when apps are added, removed, or renamed, on the calling thread.</summary>
    /// <remarks>
    /// The shell announces changes to shortcuts, but packaged apps only come and go through the
    /// package catalog, so both are watched.
    /// </remarks>
    internal static IDisposable WatchChanges(Action changed) =>
        new Watchers(
            new ShellChangeWatcher(
                FOLDERID_AppsFolder,
                SHCNE_ID.SHCNE_UPDATEDIR | SHCNE_ID.SHCNE_UPDATEITEM | SHCNE_ID.SHCNE_CREATE
                    | SHCNE_ID.SHCNE_DELETE | SHCNE_ID.SHCNE_RENAMEITEM,
                changed),
            new PackageCatalogWatcher(changed));

    /// <summary>The app's location in the shell, where its icon can be found too.</summary>
    internal static string GetPath(string parsingName) => FolderPath + parsingName;

    internal static void Launch(string parsingName, bool asAdministrator = false) =>
        Invoke(parsingName, asAdministrator ? "runas" : null);

    /// <summary>Uninstalls a packaged app, after asking first.</summary>
    /// <remarks>
    /// A desktop program would only open the Programs and Features window, so run its own uninstaller instead.
    /// </remarks>
    internal static void UninstallPackage(string parsingName) => Invoke(parsingName, "uninstall");

    /// <summary>Whether the app comes from a package, which can't run as administrator.</summary>
    /// <remarks>A packaged app's id is its package family name and app id, joined by "!".</remarks>
    internal static bool IsPackaged(string parsingName) => parsingName.Contains('!', StringComparison.Ordinal);

    private sealed class Watchers(ShellChangeWatcher shell, PackageCatalogWatcher packages) : IDisposable
    {
        public void Dispose()
        {
            shell.Dispose();
            packages.Dispose();
        }
    }

    private static void Invoke(string parsingName, string? verb)
    {
        using var process = Process.Start(new ProcessStartInfo(GetPath(parsingName))
        {
            UseShellExecute = true,
            Verb = verb,
        });
    }

    private static unsafe string? GetTargetPath(IShellItem2 item)
    {
        item.GetProperty(PKEY_Link_TargetParsingPath, out PROPVARIANT value);
        try
        {
            return value.vt == VARENUM.VT_LPWSTR
                ? value.Anonymous.Anonymous.Anonymous.pwszVal.ToString()
                : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
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
