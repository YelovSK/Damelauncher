using System.Diagnostics;
using Microsoft.Win32;

namespace Damelauncher.Plugins.Programs;

/// <summary>
/// The installed programs Windows lists for uninstalling, matched to the apps the Start menu shows.
/// </summary>
/// <remarks>
/// Nothing links a shortcut to the program that installed it, so a match is a guess from names and
/// folders. Where the guess is ambiguous there is no match, since a wrong one would uninstall something else.
/// </remarks>
internal sealed class Uninstallers
{
    private const string UninstallPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";

    // Folders that hold many programs, which an installer may still report as its own location.
    private static readonly HashSet<string> SharedFolders = new(
        [
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly List<Entry> _entries = [];

    private Uninstallers()
    {
    }

    internal static Uninstallers Load()
    {
        var uninstallers = new Uninstallers();
        uninstallers.Add(RegistryHive.LocalMachine, RegistryView.Registry64);
        uninstallers.Add(RegistryHive.LocalMachine, RegistryView.Registry32);
        uninstallers.Add(RegistryHive.CurrentUser, RegistryView.Default);
        uninstallers.ForgetVendorFolders();
        return uninstallers;
    }

    /// <summary>
    /// Finds the uninstaller of the app named <paramref name="name"/> that opens <paramref name="targetPath"/>:
    /// the program of the same name, else the one whose icon is that file, else the one installed in the
    /// closest folder around it.
    /// </summary>
    internal Command? Find(string name, string targetPath)
    {
        Command? byIcon = null;
        bool iconAmbiguous = false;
        Command? closest = null;
        int closestLength = 0;
        bool closestAmbiguous = false;
        foreach (Entry entry in _entries)
        {
            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)
                && (entry.Folders.Count == 0 || entry.Folders.Any(folder => IsInFolder(targetPath, folder))))
            {
                return entry.Uninstall;
            }

            if (string.Equals(entry.IconPath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                iconAmbiguous |= byIcon is not null && byIcon != entry.Uninstall;
                byIcon = entry.Uninstall;
            }

            foreach (string folder in entry.OwnFolders)
            {
                if (!IsInFolder(targetPath, folder) || folder.Length < closestLength)
                {
                    continue;
                }

                closestAmbiguous = folder.Length == closestLength && (closestAmbiguous || closest != entry.Uninstall);
                closest = entry.Uninstall;
                closestLength = folder.Length;
            }
        }

        return byIcon is not null
            ? iconAmbiguous ? null : byIcon
            : closestAmbiguous ? null : closest;
    }

    private static bool IsInFolder(string path, string folder) =>
        path.Length > folder.Length
        && path[folder.Length] == '\\'
        && path.StartsWith(folder, StringComparison.OrdinalIgnoreCase);

    private static string? GetFullPath(string? path)
    {
        path = path?.Trim().Trim('"');
        return string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path)
            ? null
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static string? GetIconPath(RegistryKey key)
    {
        if (key.GetValue("DisplayIcon") is not string icon)
        {
            return null;
        }

        // An icon may name its index within the file after a comma.
        int comma = icon.LastIndexOf(',');
        return GetFullPath(comma > icon.LastIndexOfAny(['\\', '/']) ? icon[..comma] : icon);
    }

    private static void AddFolder(List<string> folders, string? path)
    {
        if (path is not null
            && Path.GetPathRoot(path) != path
            && !SharedFolders.Contains(path)
            && !folders.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            folders.Add(path);
        }
    }

    private void Add(RegistryHive hive, RegistryView view)
    {
        using var root = RegistryKey.OpenBaseKey(hive, view);
        using RegistryKey? uninstall = root.OpenSubKey(UninstallPath);
        if (uninstall is null)
        {
            return;
        }

        foreach (string keyName in uninstall.GetSubKeyNames())
        {
            using RegistryKey? key = uninstall.OpenSubKey(keyName);
            // Updates name the program they belong to, which has its own entry.
            if (key is null
                || key.GetValue("SystemComponent") is 1
                || key.GetValue("ParentKeyName") is not null
                || key.GetValue("DisplayName") is not string displayName)
            {
                continue;
            }

            // An installer package's uninstall string often opens its repair options instead.
            Command? command = key.GetValue("WindowsInstaller") is 1
                ? new Command("msiexec.exe", $"/x {keyName}")
                : key.GetValue("UninstallString") is string line ? Command.Parse(line) : null;
            if (command is null)
            {
                continue;
            }

            string? iconPath = GetIconPath(key);
            string? installFolder = GetFullPath(key.GetValue("InstallLocation") as string);
            var folders = new List<string>();
            AddFolder(folders, installFolder);
            AddFolder(folders, Path.GetDirectoryName(GetFullPath(command.Path)));
            AddFolder(folders, Path.GetDirectoryName(iconPath));
            _entries.Add(new Entry(displayName.Trim(), command, iconPath, installFolder, folders, [.. folders]));
        }
    }

    // A folder holding another program's install folder belongs to a vendor, not to one of its programs.
    private void ForgetVendorFolders()
    {
        List<string> installFolders = [.. _entries.Select(entry => entry.InstallFolder).OfType<string>()];
        foreach (Entry entry in _entries)
        {
            entry.OwnFolders.RemoveAll(folder => installFolders.Any(installFolder => IsInFolder(installFolder, folder)));
        }
    }

    internal sealed record Command(string Path, string Arguments)
    {
        // An uninstall string is a command line whose program path is often left unquoted despite spaces.
        internal static Command? Parse(string line)
        {
            line = line.Trim();
            if (line.Length == 0)
            {
                return null;
            }

            if (line[0] == '"')
            {
                int end = line.IndexOf('"', 1);
                return end < 0
                    ? new Command(line[1..], "")
                    : new Command(line[1..end], line[(end + 1)..].Trim());
            }

            for (int space = line.IndexOf(' '); space >= 0; space = line.IndexOf(' ', space + 1))
            {
                string candidate = line[..space];
                if (File.Exists(candidate))
                {
                    return new Command(candidate, line[(space + 1)..].Trim());
                }
            }

            if (File.Exists(line))
            {
                return new Command(line, "");
            }

            int first = line.IndexOf(' ');
            return first < 0 ? new Command(line, "") : new Command(line[..first], line[(first + 1)..].Trim());
        }

        internal void Run()
        {
            using var process = Process.Start(new ProcessStartInfo(Path, Arguments) { UseShellExecute = true });
        }
    }

    /// <param name="Folders">Every folder the program names as its own, where its app is surely found.</param>
    /// <param name="OwnFolders">
    /// Those of <paramref name="Folders"/> without vendor folders, where any app found is surely the program's.
    /// </param>
    private sealed record Entry(
        string Name, Command Uninstall, string? IconPath, string? InstallFolder, List<string> Folders, List<string> OwnFolders);
}
