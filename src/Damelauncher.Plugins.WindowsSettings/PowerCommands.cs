using Dame.Win32;

namespace Damelauncher.Plugins.WindowsSettings;

/// <summary>The Start menu's power options, plus locking.</summary>
internal static class PowerCommands
{
    private const string Subtitle = "System";

    internal static SettingsEntry[] Create(IResultProvider provider)
    {
        return
        [
            Command("power:lock", "Lock", ["lock screen", "lock computer"], SystemPower.Lock),
            Command("power:sleep", "Sleep", ["suspend", "standby"], SystemPower.Sleep),
            Confirmed("power:signout", "Sign out", "Sign out of Windows?", ["log off", "log out"], SystemPower.SignOut),
            Confirmed("power:shutdown", "Shut down", "Shut down the computer?", ["power off", "turn off", "shutdown"], SystemPower.ShutDown),
            Confirmed("power:restart", "Restart", "Restart the computer?", ["reboot"], SystemPower.Restart),
        ];

        SettingsEntry Command(string id, string title, string[] keywords, Action execute) =>
            new(new LaunchResult(provider, id, title, Subtitle, null, execute), keywords);

        // Picking one of these by accident would close every open program, so they ask first.
        SettingsEntry Confirmed(string id, string title, string question, string[] keywords, Action execute) =>
            Command(id, title, keywords, () =>
            {
                if (MessageDialog.Confirm(title, question))
                {
                    execute();
                }
            });
    }
}
