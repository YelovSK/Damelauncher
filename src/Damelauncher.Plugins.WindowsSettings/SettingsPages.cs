using System.Diagnostics;

namespace Damelauncher.Plugins.WindowsSettings;

/// <summary>Pages of the Settings app, opened through their <c>ms-settings:</c> links.</summary>
internal static class SettingsPages
{
    internal static SettingsEntry[] Create() =>
    [
        Page("Settings", "Windows", "ms-settings:", "preferences", "options", "control panel"),

        Page("Display", "System", "ms-settings:display", "screen", "monitor", "resolution", "scale", "brightness", "hdr"),
        Page("Night light", "System", "ms-settings:nightlight", "blue light", "warm"),
        Page("Sound", "System", "ms-settings:sound", "audio", "volume", "speakers", "headphones", "microphone"),
        Page("Notifications", "System", "ms-settings:notifications", "do not disturb", "alerts"),
        Page("Power & battery", "System", "ms-settings:powersleep", "battery", "sleep timeout", "screen timeout", "energy"),
        Page("Storage", "System", "ms-settings:storagesense", "disk", "space", "cleanup", "drive"),
        Page("Multitasking", "System", "ms-settings:multitasking", "snap", "alt tab", "virtual desktops"),
        Page("Clipboard", "System", "ms-settings:clipboard", "clipboard history", "copy paste"),
        Page("Remote Desktop", "System", "ms-settings:remotedesktop", "rdp"),
        Page("Recovery", "System", "ms-settings:recovery", "reset this pc", "advanced startup"),
        Page("Activation", "System", "ms-settings:activation", "product key", "license"),
        Page("Troubleshoot", "System", "ms-settings:troubleshoot", "fix problems"),
        Page("About", "System", "ms-settings:about", "pc name", "rename pc", "specs", "system info"),

        Page("Bluetooth & devices", "Bluetooth & devices", "ms-settings:bluetooth", "pair", "headset"),
        Page("Printers & scanners", "Bluetooth & devices", "ms-settings:printers", "print"),
        Page("Mouse", "Bluetooth & devices", "ms-settings:mousetouchpad", "pointer speed", "scroll"),
        Page("Touchpad", "Bluetooth & devices", "ms-settings:devices-touchpad", "trackpad", "gestures"),

        Page("Wi-Fi", "Network & internet", "ms-settings:network-wifi", "wifi", "wireless", "wlan"),
        Page("Network status", "Network & internet", "ms-settings:network-status", "internet", "connection"),
        Page("Ethernet", "Network & internet", "ms-settings:network-ethernet", "lan", "cable"),
        Page("VPN", "Network & internet", "ms-settings:network-vpn"),
        Page("Proxy", "Network & internet", "ms-settings:network-proxy"),
        Page("Airplane mode", "Network & internet", "ms-settings:network-airplanemode", "flight mode"),
        Page("Mobile hotspot", "Network & internet", "ms-settings:network-mobilehotspot", "tethering"),

        Page("Background", "Personalization", "ms-settings:personalization-background", "wallpaper", "desktop picture"),
        Page("Colors", "Personalization", "ms-settings:personalization-colors", "dark mode", "light mode", "accent color"),
        Page("Themes", "Personalization", "ms-settings:themes", "cursor", "desktop icons"),
        Page("Lock screen", "Personalization", "ms-settings:lockscreen", "screen saver"),
        Page("Start", "Personalization", "ms-settings:personalization-start", "start menu"),
        Page("Taskbar", "Personalization", "ms-settings:taskbar", "tray", "system tray"),
        Page("Fonts", "Personalization", "ms-settings:fonts", "typeface"),

        Page("Installed apps", "Apps", "ms-settings:appsfeatures", "uninstall", "programs", "remove app"),
        Page("Default apps", "Apps", "ms-settings:defaultapps", "file associations", "open with", "default browser"),
        Page("Startup apps", "Apps", "ms-settings:startupapps", "autostart", "run at startup"),
        Page("Optional features", "Apps", "ms-settings:optionalfeatures", "windows features"),

        Page("Your info", "Accounts", "ms-settings:yourinfo", "account", "profile picture"),
        Page("Sign-in options", "Accounts", "ms-settings:signinoptions", "password", "pin", "windows hello", "fingerprint"),
        Page("Email & accounts", "Accounts", "ms-settings:emailandaccounts", "mail"),

        Page("Date & time", "Time & language", "ms-settings:dateandtime", "clock", "time zone"),
        Page("Language & region", "Time & language", "ms-settings:regionlanguage", "keyboard layout", "locale", "format"),
        Page("Typing", "Time & language", "ms-settings:typing", "autocorrect", "spell check", "touch keyboard"),

        Page("Game Mode", "Gaming", "ms-settings:gaming-gamemode", "games"),

        Page("Accessibility", "Accessibility", "ms-settings:easeofaccess", "ease of access"),
        Page("Text size", "Accessibility", "ms-settings:easeofaccess-display", "font size", "bigger text"),

        Page("Privacy & security", "Privacy & security", "ms-settings:privacy", "permissions"),
        Page("Location", "Privacy & security", "ms-settings:privacy-location", "gps"),
        Page("Camera access", "Privacy & security", "ms-settings:privacy-webcam", "webcam"),
        Page("Microphone access", "Privacy & security", "ms-settings:privacy-microphone", "mic"),
        Page("For developers", "System", "ms-settings:developers", "developer mode", "sudo"),
        Page("Windows Security", "Privacy & security", "windowsdefender:", "antivirus", "defender", "firewall", "virus"),

        Page("Windows Update", "Windows Update", "ms-settings:windowsupdate", "updates", "check for updates", "patch"),
    ];

    private static SettingsEntry Page(string title, string section, string link, params string[] keywords) =>
        new(link, title, $"Settings › {section}", keywords, () => Open(link));

    private static void Open(string link)
    {
        using var process = Process.Start(new ProcessStartInfo(link) { UseShellExecute = true });
    }
}
