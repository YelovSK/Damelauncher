using Dame.Win32;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Damelauncher.UI;

/// <summary>
/// The shell icons shown next to results, loaded in the background on first request and kept
/// for the ones used most recently.
/// </summary>
/// <remarks>
/// Used on the window thread, where loads finish too. Icons are loaded at the size they're drawn
/// at, so a DPI change starts over.
/// </remarks>
internal sealed class ResultIcons : IDisposable
{
    internal const float Size = 32.0f;
    // Several screens of results, so scrolling back and forth or retyping a query hits the cache.
    private const int Capacity = 256;

    private static readonly BitmapProperties1 BitmapProperties = new(
        new PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied),
        96.0f,
        96.0f,
        BitmapOptions.None);

    private readonly ID2D1DeviceContext _deviceContext;
    private readonly Action _loaded;
    private readonly ComWorkerQueue<object?> _worker = new("Result icons", 2, static () => null);
    private readonly Dictionary<string, LinkedListNode<Entry>> _entries = [];
    private readonly LinkedList<Entry> _recentlyUsed = new();
    private int _pixelSize;

    /// <param name="loaded">Called when an icon that was asked for has arrived, to draw it.</param>
    internal ResultIcons(ID2D1DeviceContext deviceContext, float dpi, Action loaded)
    {
        _deviceContext = deviceContext;
        _loaded = loaded;
        _pixelSize = GetPixelSize(dpi);
    }

    /// <returns>The icon, or <see langword="null"/> while it loads or when there is none.</returns>
    internal ID2D1Bitmap1? Get(string source)
    {
        if (_entries.TryGetValue(source, out LinkedListNode<Entry>? cached))
        {
            _recentlyUsed.Remove(cached);
            _recentlyUsed.AddFirst(cached);
            return cached.Value.Bitmap;
        }

        var entry = new Entry(source);
        _entries.Add(source, _recentlyUsed.AddFirst(entry));
        Trim();
        _ = LoadAsync(entry, _pixelSize);
        return null;
    }

    internal void SetDpi(float dpi)
    {
        int pixelSize = GetPixelSize(dpi);
        if (pixelSize == _pixelSize)
        {
            return;
        }

        _pixelSize = pixelSize;
        Clear();
    }

    public void Dispose()
    {
        _worker.Dispose();
        Clear();
    }

    private async Task LoadAsync(Entry entry, int pixelSize)
    {
        ShellIconImage? image;
        try
        {
            image = await _worker.Enqueue((_, _) => ShellIcons.Load(entry.Source, pixelSize));
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Evicted meanwhile, its icon isn't wanted anymore.
        if (image is null || entry.Evicted)
        {
            return;
        }

        entry.Bitmap = Upload(image);
        _loaded();
    }

    private unsafe ID2D1Bitmap1 Upload(ShellIconImage image)
    {
        fixed (byte* pixels = image.Pixels)
        {
            return _deviceContext.CreateBitmap(
                new SizeI(image.Width, image.Height),
                (nint)pixels,
                (uint)(image.Width * 4),
                BitmapProperties);
        }
    }

    private void Trim()
    {
        while (_entries.Count > Capacity)
        {
            LinkedListNode<Entry> oldest = _recentlyUsed.Last!;
            _recentlyUsed.Remove(oldest);
            _entries.Remove(oldest.Value.Source);
            oldest.Value.Evict();
        }
    }

    private void Clear()
    {
        foreach (Entry entry in _recentlyUsed)
        {
            entry.Evict();
        }

        _recentlyUsed.Clear();
        _entries.Clear();
    }

    private static int GetPixelSize(float dpi) => (int)MathF.Round(Size * dpi / 96.0f);

    private sealed class Entry(string source)
    {
        internal string Source { get; } = source;
        internal ID2D1Bitmap1? Bitmap { get; set; }
        internal bool Evicted { get; private set; }

        internal void Evict()
        {
            Evicted = true;
            Bitmap?.Dispose();
            Bitmap = null;
        }
    }
}
