using System.Runtime.InteropServices;
using Dame.Win32;
using Damelauncher.Plugins;

namespace Damelauncher.Serve;

/// <summary>
/// Result icons as files another program can load: shell icons saved as PNGs and kept across
/// runs, until they go unused for a while.
/// </summary>
/// <remarks>
/// Used on the window thread, where saves finish too. Most files show their type's icon, so
/// they share one PNG per extension, and folders share one, rather than one per path.
/// </remarks>
internal sealed class ServeIcons : IDisposable
{
    private const int PixelSize = 64;
    private const string FolderKey = "folder";
    // Long enough that icons in use aren't extracted again, short enough that an updated app's
    // old icon goes away.
    private static readonly TimeSpan UnusedLifetime = TimeSpan.FromDays(30);
    private static readonly HashSet<string> OwnIconExtensions = new(
        [".exe", ".lnk", ".ico", ".url", ".dll", ".appref-ms", ".cpl", ".scr", ".msc", ".cur", ".ani", ".website"],
        StringComparer.OrdinalIgnoreCase);

    internal static readonly string DefaultDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Damelauncher",
        "icons");

    private readonly string _directory;
    private readonly Action<string> _saved;
    private readonly ComWorkerQueue<object?> _worker = new("Serve icons", 2, static () => null);
    // Each shell icon's PNG, once worked out.
    private readonly Dictionary<string, string> _files = [];
    // Whether each PNG asked for is there to send, rather than being saved or missing for good.
    private readonly Dictionary<string, bool> _ready = [];

    /// <param name="saved">Called with a PNG once it's saved.</param>
    internal ServeIcons(string directory, Action<string> saved)
    {
        _directory = directory;
        _saved = saved;
        // Before any icon is asked for, so it never deletes one about to be sent.
        Prune(_directory, DateTime.UtcNow - UnusedLifetime);
    }

    /// <returns>
    /// A shell icon's PNG file, or empty while it's saved; <c>glyph:</c> and the symbol for a
    /// glyph; empty for none.
    /// </returns>
    internal string Get(ResultIcon? icon) => icon switch
    {
        ShellIcon shellIcon => GetFile(shellIcon.Location),
        GlyphIcon glyphIcon => "glyph:" + glyphIcon.Glyph,
        _ => string.Empty,
    };

    /// <summary>
    /// What a location's icon is cached by: its extension for a file whose icon comes from its
    /// type, one key for every folder, and the location itself for anything else.
    /// </summary>
    internal static string GetKey(string location, Func<string, bool> isFolder)
    {
        if (!Path.IsPathFullyQualified(location) || Path.GetPathRoot(location) == location)
        {
            return location;
        }

        string extension = Path.GetExtension(location);
        if (OwnIconExtensions.Contains(extension))
        {
            return location;
        }

        return isFolder(location) ? FolderKey : "ext:" + extension.ToLowerInvariant();
    }

    /// <summary>Deletes the PNGs, and files left from saving them, last written before the cutoff.</summary>
    internal static void Prune(string directory, DateTime cutoffUtc)
    {
        var folder = new DirectoryInfo(directory);
        if (!folder.Exists)
        {
            return;
        }

        // The listing carries each file's last write, so checking it costs no call of its own.
        foreach (FileInfo file in folder.EnumerateFiles())
        {
            if (file.LastWriteTimeUtc >= cutoffUtc)
            {
                continue;
            }

            try
            {
                file.Delete();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Tried again next run.
            }
        }
    }

    public void Dispose() => _worker.Dispose();

    private string GetFile(string location)
    {
        if (!_files.TryGetValue(location, out string? file))
        {
            string key = GetKey(location, Directory.Exists);
            file = Path.Combine(_directory, $"{Hash(key):x16}.png");
            _files.Add(location, file);
            if (!_ready.ContainsKey(file))
            {
                Load(file, key == FolderKey ? _directory : location);
            }
        }

        return _ready[file] ? file : string.Empty;
    }

    private void Load(string file, string source)
    {
        bool exists = File.Exists(file);
        _ready.Add(file, exists);
        if (exists)
        {
            MarkUsed(file);
        }
        else
        {
            _ = SaveAsync(file, source);
        }
    }

    // So pruning keeps it in later runs.
    private static void MarkUsed(string file)
    {
        try
        {
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Saved again once a later run prunes it.
        }
    }

    private async Task SaveAsync(string file, string source)
    {
        try
        {
            bool saved = await _worker.Enqueue((_, _) =>
            {
                // The folder icon comes from the cache's own folder, a plain one that exists.
                Directory.CreateDirectory(_directory);
                ShellIconImage? image = ShellIcons.Load(source, PixelSize);
                if (image is null)
                {
                    return false;
                }

                PngWriter.Save(file, image.Width, image.Height, image.Pixels);
                return true;
            });
            if (saved)
            {
                _ready[file] = true;
                _saved(file);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or UnauthorizedAccessException or COMException)
        {
            // The result goes on without an icon.
        }
    }

    // FNV-1a, which names the same key the same file in every run, unlike string.GetHashCode.
    private static ulong Hash(string text)
    {
        ulong hash = 14695981039346656037;
        foreach (char character in text)
        {
            hash = (hash ^ character) * 1099511628211;
        }

        return hash;
    }
}
