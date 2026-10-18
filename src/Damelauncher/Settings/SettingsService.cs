using System.Diagnostics;
using Damelauncher.Serialization;

namespace Damelauncher.Settings;

/// <summary>The settings, read once at startup and written back on every change.</summary>
/// <remarks>
/// Saved to an INI file that can be edited by hand while the launcher isn't running. Comments
/// and keys it doesn't know survive a save.
/// </remarks>
internal sealed class SettingsService
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Damelauncher",
        "settings.ini");

    private SettingsService(LauncherSettings current) => Current = current;

    internal LauncherSettings Current { get; private set; }

    internal static SettingsService Load()
    {
        try
        {
            (LauncherSettings settings, IReadOnlyList<string> ignored) =
                SettingsIniSerializer.Read(File.ReadAllText(FilePath));
            foreach (string value in ignored)
            {
                Debug.WriteLine($"Ignored unreadable setting {value}.");
            }

            return new SettingsService(settings);
        }
        catch (Exception exception) when (IsSettingsError(exception))
        {
            // A missing file is the first run, and a broken one falls back to the defaults
            // until the next change rewrites it.
            return new SettingsService(new LauncherSettings());
        }
    }

    internal void Update(LauncherSettings settings)
    {
        Current = settings;
        try
        {
            Save();
        }
        catch (Exception exception) when (IsSettingsError(exception))
        {
            // The change still applies for this session.
            Debug.WriteLine($"Could not save settings: {exception.Message}");
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string existing = File.Exists(FilePath) ? File.ReadAllText(FilePath) : string.Empty;
        // Written aside and swapped in, so a crash halfway through can't lose the file.
        string temporaryPath = FilePath + ".tmp";
        File.WriteAllText(temporaryPath, SettingsIniSerializer.Write(Current, existing));
        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    private static bool IsSettingsError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or IniFormatException;
}
