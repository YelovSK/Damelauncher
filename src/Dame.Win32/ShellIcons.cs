using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Shell;
using static Windows.Win32.PInvoke;

namespace Dame.Win32;

/// <param name="Pixels">Straight BGRA, top row first, four bytes per pixel with no padding.</param>
internal sealed record ShellIconImage(int Width, int Height, byte[] Pixels);

/// <summary>The icons Explorer shows for files, folders, and apps.</summary>
internal static unsafe partial class ShellIcons
{
    /// <param name="parsingName">A path, or a shell location such as <c>shell:AppsFolder\…</c>.</param>
    /// <param name="size">The icon's width and height in pixels, which it's scaled to fit.</param>
    /// <returns>The icon, or <see langword="null"/> when the item doesn't exist or has none.</returns>
    /// <remarks>Runs on a COM-initialized thread: extracting an icon can take a while.</remarks>
    internal static ShellIconImage? Load(string parsingName, int size)
    {
        Guid iid = typeof(IShellItemImageFactory).GUID;
        void* instance;
        fixed (char* name = parsingName)
        {
            if (SHCreateItemFromParsingName(name, null, &iid, &instance) < 0)
            {
                return null;
            }
        }

        IShellItemImageFactory factory = UniqueComObjects.Own<IShellItemImageFactory>(instance);
        HBITMAP bitmap;
        try
        {
            factory.GetImage(new SIZE(size, size), SIIGBF.SIIGBF_ICONONLY, &bitmap);
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            UniqueComObjects.Release(factory);
        }

        try
        {
            return ReadPixels(bitmap);
        }
        finally
        {
            _ = DeleteObject((HGDIOBJ)bitmap.Value);
        }
    }

    private static ShellIconImage? ReadPixels(HBITMAP bitmap)
    {
        BITMAP description;
        if (GetObject((HGDIOBJ)bitmap.Value, sizeof(BITMAP), &description) == 0)
        {
            return null;
        }

        int width = description.bmWidth;
        int height = description.bmHeight;
        var info = new BITMAPINFO
        {
            bmiHeader = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                // Negative for rows top to bottom, the way Direct2D takes them.
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
            },
        };

        byte[] pixels = new byte[width * height * 4];
        HDC screen = GetDC(default);
        try
        {
            fixed (byte* destination = pixels)
            {
                if (GetDIBits(screen, bitmap, 0, (uint)height, destination, &info, DIB_USAGE.DIB_RGB_COLORS) != height)
                {
                    return null;
                }
            }
        }
        finally
        {
            _ = ReleaseDC(default, screen);
        }

        MakeOpaqueWithoutAlpha(pixels);
        return new ShellIconImage(width, height, pixels);
    }

    // An icon without an alpha channel comes out with all of it zero, which means opaque rather
    // than invisible.
    private static void MakeOpaqueWithoutAlpha(Span<byte> pixels)
    {
        for (int index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] != 0)
            {
                return;
            }
        }

        for (int index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = 255;
        }
    }

    // Declared by hand for the raw pointer, which the generated one would wrap in an object
    // that's only released once the garbage collector gets to it.
    [LibraryImport("shell32")]
    private static partial int SHCreateItemFromParsingName(char* pszPath, void* pbc, Guid* riid, void** ppv);
}
