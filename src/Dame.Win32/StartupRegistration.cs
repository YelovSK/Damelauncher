using Microsoft.Win32;

namespace Dame.Win32;

/// <summary>Starting a program when the user signs in, through the current user's Run key.</summary>
/// <remarks>
/// Read from the key rather than kept in settings, so it stays right when something else
/// removes the entry. Task Manager's startup toggle doesn't, though: it keeps a separate
/// approval flag, which this doesn't look at.
/// </remarks>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    internal static bool IsRegistered(string name)
    {
        using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return runKey?.GetValue(name) is string;
    }

    internal static void Register(string name, string executablePath)
    {
        using RegistryKey runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        runKey.SetValue(name, $"\"{executablePath}\"");
    }

    internal static void Unregister(string name)
    {
        using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        runKey?.DeleteValue(name, throwOnMissingValue: false);
    }
}
