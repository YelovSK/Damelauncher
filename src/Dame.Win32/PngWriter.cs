using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32;
using Windows.Win32.Graphics.Imaging;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using static Windows.Win32.PInvoke;

namespace Dame.Win32;

/// <summary>Saves images as PNG files through the Windows Imaging Component.</summary>
/// <remarks>Runs on a COM-initialized thread, and releases what it used before returning.</remarks>
internal static unsafe partial class PngWriter
{
    // IWICBitmapEncoder::CreateNewFrame: after IUnknown's three methods, the encoder's eighth.
    private const int CreateNewFrameSlot = 10;

    /// <param name="pixels">Straight BGRA, top row first, four bytes per pixel with no padding.</param>
    /// <remarks>Written aside and moved into place, so a reader never sees a file half written.</remarks>
    internal static void Save(string path, int width, int height, byte[] pixels)
    {
        byte[] png = Encode(width, height, pixels);
        string temporaryPath = $"{path}.{Environment.CurrentManagedThreadId}.tmp";
        File.WriteAllBytes(temporaryPath, png);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static byte[] Encode(int width, int height, byte[] pixels)
    {
        IStream? stream = null;
        IWICBitmapEncoder? encoder = null;
        IWICBitmapFrameEncode? frame = null;
        IPropertyBag2? options = null;
        try
        {
            void* streamInstance = SHCreateMemStream(null, 0);
            if (streamInstance is null)
            {
                throw new InsufficientMemoryException("Could not create a stream to encode the PNG into.");
            }

            stream = UniqueComObjects.Own<IStream>(streamInstance);
            encoder = UniqueComObjects.Create<IWICBitmapEncoder>(CLSID_WICPngEncoder);
            encoder.Initialize(stream, WICBitmapEncoderCacheOption.WICBitmapEncoderNoCache);
            (frame, options) = CreateNewFrame(encoder);
            frame.Initialize(options!);
            frame.SetSize((uint)width, (uint)height);
            Guid pixelFormat = GUID_WICPixelFormat32bppBGRA;
            frame.SetPixelFormat(&pixelFormat);
            frame.WritePixels((uint)height, (uint)(width * 4), (uint)pixels.Length, pixels);
            frame.Commit();
            encoder.Commit();

            stream.Seek(0, SeekOrigin.Current, out ulong length);
            byte[] png = new byte[length];
            stream.Seek(0, SeekOrigin.Begin);
            stream.Read(png).ThrowOnFailure();
            return png;
        }
        finally
        {
            UniqueComObjects.Release(options);
            UniqueComObjects.Release(frame);
            UniqueComObjects.Release(encoder);
            UniqueComObjects.Release(stream);
        }
    }

    // Called through the encoder's method table, since the generated method would wrap the
    // frame in a wrapper that can't be released before a garbage collection, and the frame
    // keeps the encoder and its stream alive.
    private static (IWICBitmapFrameEncode Frame, IPropertyBag2? Options) CreateNewFrame(IWICBitmapEncoder encoder)
    {
        VirtualMethodTableInfo table = ((IUnmanagedVirtualMethodTableProvider)encoder)
            .GetVirtualMethodTableInfoForKey(typeof(IWICBitmapEncoder));
        void* frame;
        void* options;
        var createNewFrame = (delegate* unmanaged<void*, void**, void**, int>)table.VirtualMethodTable[CreateNewFrameSlot];
        Marshal.ThrowExceptionForHR(createNewFrame(table.ThisPointer, &frame, &options));
        return (
            UniqueComObjects.Own<IWICBitmapFrameEncode>(frame),
            options is null ? null : UniqueComObjects.Own<IPropertyBag2>(options));
    }

    // Declared by hand for the raw pointer, which the generated one wraps.
    [LibraryImport("shlwapi")]
    private static partial void* SHCreateMemStream(byte* pInit, uint cbInit);
}
