using Dame.Win32;
using Damelauncher.Installation;
using Damelauncher.Settings;

namespace Damelauncher;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        NativeMethods.EnablePerMonitorDpiAwareness();
        NativeMethods.InitializeComApartment(ComApartment.ApartmentThreaded);

        try
        {
            if (AppInstallation.IsUninstallRequest(args, out bool silent))
            {
                AppInstallation.Uninstall(silent);
                return;
            }

            var settings = SettingsService.Load();
#if !DEBUG
            // Builds being worked on run from their output folder, where asking every time would only get in the way.
            if (OfferInstall(settings))
            {
                return;
            }
#endif

            using var app = new LauncherApp(settings);
            app.Run();
        }
        finally
        {
            NativeMethods.UninitializeComApartment();
        }
    }

#if !DEBUG
    /// <returns>Whether it installed and started the installed copy, so this one has nothing left to do.</returns>
    private static bool OfferInstall(SettingsService settings)
    {
        if (AppInstallation.IsRunningInstalled || !(AppInstallation.IsInstalled || settings.Current.OfferInstall))
        {
            return false;
        }

        bool accepted = AppInstallation.IsInstalled
            ? MessageDialog.Ask(
                "Update Damelauncher",
                $"Replace the installed Damelauncher {AppInstallation.InstalledVersion} with this one, {AppInstallation.CurrentVersion}?\n\n"
                + "Choose No to run this copy without installing it.")
            : MessageDialog.Ask(
                "Install Damelauncher",
                "Install Damelauncher, so it has a place of its own and starts with Windows?\n\n"
                + "Choose No to run it from here. You can install it later from the settings.");
        if (!accepted)
        {
            settings.Update(settings.Current with { OfferInstall = false });
            return false;
        }

        if (!AppInstallation.TryInstall())
        {
            return false;
        }

        AppInstallation.LaunchInstalled();
        return true;
    }
#endif
}
