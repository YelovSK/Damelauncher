using Dame.Win32;

namespace Damelauncher.Plugins.WindowsSettings;

/// <summary>The Start menu's power options, plus locking.</summary>
internal static class PowerCommands
{
    private const string Subtitle = "System";

    internal static SettingsEntry[] Create() =>
    [
        new("power:lock", "Lock", Subtitle, null, ["lock screen", "lock computer"], SystemPower.Lock),
        new("power:sleep", "Sleep", Subtitle, null, ["suspend", "standby"], SystemPower.Sleep),
        Confirmed("power:signout", "Sign out", "Sign out of Windows?", ["log off", "log out"], SystemPower.SignOut),
        Confirmed("power:shutdown", "Shut down", "Shut down the computer?", ["power off", "turn off", "shutdown"], SystemPower.ShutDown),
        Confirmed("power:restart", "Restart", "Restart the computer?", ["reboot"], SystemPower.Restart),
    ];

    // Picking one of these by accident would close every open program, so they ask first.
    private static SettingsEntry Confirmed(string id, string title, string question, string[] keywords, Action execute) =>
        new(id, title, Subtitle, null, keywords, () =>
        {
            if (MessageDialog.Confirm(title, question))
            {
                execute();
            }
        });
}
