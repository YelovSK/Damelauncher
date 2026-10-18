using System.ComponentModel;
using System.Diagnostics;
using Dame.Win32;

namespace Damelauncher.Installation;

/// <summary>
/// Installing for the current user: the executable copied to its programs folder, a Start menu
/// shortcut, an entry in installed apps that uninstalls it, and starting with Windows.
/// </summary>
internal static class AppInstallation
{
    private const string UninstallArgument = "--uninstall";
    private const string SilentArgument = "--silent";

    private static readonly InstalledProgramRegistration InstalledProgram = new(LauncherApp.AppName);

    private static string InstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Programs",
        LauncherApp.AppName);

    private static string InstalledExecutablePath => Path.Combine(InstallDirectory, "Damelauncher.exe");

    private static string CurrentExecutablePath => Environment.ProcessPath
        ?? throw new InvalidOperationException("Could not determine the executable path.");

    private static string StartMenuShortcutPath => Path.Combine(NativeMethods.GetProgramsPath(), "Damelauncher.lnk");

    internal static bool IsInstalled => File.Exists(InstalledExecutablePath);

    internal static bool IsRunningInstalled => string.Equals(
        Path.GetFullPath(CurrentExecutablePath),
        Path.GetFullPath(InstalledExecutablePath),
        StringComparison.OrdinalIgnoreCase);

    internal static string InstalledVersion => GetDisplayVersion(InstalledExecutablePath);

    internal static string CurrentVersion => GetDisplayVersion(CurrentExecutablePath);

    /// <returns>Whether the arguments asked to uninstall, which <paramref name="silent"/> does without asking first.</returns>
    internal static bool IsUninstallRequest(string[] args, out bool silent)
    {
        silent = args.Contains(SilentArgument, StringComparer.OrdinalIgnoreCase);
        return args.Contains(UninstallArgument, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Installs this executable, replacing an installed one.</summary>
    /// <remarks>Other running copies are closed first, since they would hold the hotkey.</remarks>
    /// <returns>Whether it installed. When it couldn't, it said why.</returns>
    internal static bool TryInstall()
    {
        try
        {
            CloseOtherInstances();
            Directory.CreateDirectory(InstallDirectory);
            // Copied aside and moved in, so a failed copy can't leave a broken executable behind.
            string stagedPath = Path.Combine(InstallDirectory, "Damelauncher.new.exe");
            File.Copy(CurrentExecutablePath, stagedPath, overwrite: true);
            File.Move(stagedPath, InstalledExecutablePath, overwrite: true);
            ShellIntegration.CreateShortcut(
                StartMenuShortcutPath, InstalledExecutablePath, InstallDirectory, "Keyboard launcher", InstalledExecutablePath);
            InstalledProgram.Register(
                LauncherApp.AppName, "YelovSK", InstalledVersion, InstallDirectory,
                InstalledExecutablePath, UninstallArgument, $"{UninstallArgument} {SilentArgument}");
            StartupRegistration.Register(LauncherApp.AppName, InstalledExecutablePath);
            return true;
        }
        catch (Exception exception) when (IsInstallationError(exception))
        {
            MessageDialog.ShowError("Install Damelauncher", $"Damelauncher couldn't be installed. {exception.Message}");
            return false;
        }
    }

    internal static void LaunchInstalled()
    {
        Process.Start(new ProcessStartInfo(InstalledExecutablePath)
        {
            UseShellExecute = true,
            WorkingDirectory = InstallDirectory,
        });
    }

    /// <summary>
    /// Removes the installation once the user confirms, unless <paramref name="silent"/>, and
    /// deletes the installed executable once this process exits. Settings and launch history stay.
    /// </summary>
    internal static void Uninstall(bool silent)
    {
        if (!silent && !MessageDialog.Confirm(
            "Uninstall Damelauncher",
            "Uninstall Damelauncher? Your settings and launch history stay, in case you install it again."))
        {
            return;
        }

        try
        {
            CloseOtherInstances();
            File.Delete(StartMenuShortcutPath);
            StartupRegistration.Unregister(LauncherApp.AppName);
            InstalledProgram.Unregister();
            DeleteAfterExit();
        }
        catch (Exception exception) when (IsInstallationError(exception) && !silent)
        {
            MessageDialog.ShowError("Uninstall Damelauncher", $"Damelauncher couldn't be uninstalled. {exception.Message}");
        }
    }

    private static bool IsInstallationError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or Win32Exception;

    private static void CloseOtherInstances()
    {
        if (!AppWindow.CloseInOtherProcesses(LauncherApp.AppName, TimeSpan.FromSeconds(5)))
        {
            throw new IOException("Damelauncher is still running. Quit it from its tray icon and try again.");
        }
    }

    // The running executable can't delete itself, so a command does it after a moment.
    private static void DeleteAfterExit()
    {
        string command = $"choice /C Y /N /D Y /T 1 > nul & del /F /Q \"{InstalledExecutablePath}\" & rmdir \"{InstallDirectory}\"";
        Process.Start(new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            // cmd strips only the outer quotes, so the inner path quotes stay as they are.
            Arguments = $"/D /C \"{command}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
        });
    }

    private static string GetDisplayVersion(string path)
    {
        if (!File.Exists(path))
        {
            return "unknown";
        }

        var version = FileVersionInfo.GetVersionInfo(path);
        return version.ProductVersion?.Split('+')[0] ?? version.FileVersion ?? "unknown";
    }
}
