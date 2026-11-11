using System.Runtime.InteropServices;
using Windows.Win32.Foundation;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Variant;
using Windows.Win32.UI.Shell.PropertiesSystem;
using static Windows.Win32.PInvoke;

namespace Dame.Win32;

/// <summary>Reads a file's properties through the Windows property system, as Explorer shows them.</summary>
internal static partial class ShellProperties
{
    /// <param name="names">Canonical property names, such as <c>System.Photo.FNumber</c>.</param>
    /// <returns>Each property formatted for display, or <see langword="null"/> where the file has none.</returns>
    /// <remarks>Runs on a COM-initialized thread.</remarks>
    internal static unsafe string?[] Read(string path, IReadOnlyList<string> names)
    {
        Guid iid = typeof(IPropertyStore).GUID;
        void* instance;
        fixed (char* pathChars = path)
        {
            Marshal.ThrowExceptionForHR(SHGetPropertyStoreFromParsingName(
                pathChars, null, GETPROPERTYSTOREFLAGS.GPS_BESTEFFORT, &iid, &instance));
        }

        // Released at once, so the store doesn't keep the file open until a garbage collection.
        IPropertyStore store = UniqueComObjects.Own<IPropertyStore>(instance);
        try
        {
            string?[] values = new string?[names.Count];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = ReadValue(store, names[index]);
            }

            return values;
        }
        finally
        {
            UniqueComObjects.Release(store);
        }
    }

    private static unsafe string? ReadValue(IPropertyStore store, string name)
    {
        PSGetPropertyKeyFromName(name, out PROPERTYKEY key).ThrowOnFailure();
        store.GetValue(&key, out PROPVARIANT value);
        try
        {
            if (value.vt == VARENUM.VT_EMPTY
                || PSFormatForDisplayAlloc(key, value, PROPDESC_FORMAT_FLAGS.PDFF_DEFAULT, out PWSTR display).Failed)
            {
                return null;
            }

            try
            {
                string text = display.ToString();
                return text.Length == 0 ? null : text;
            }
            finally
            {
                Marshal.FreeCoTaskMem((nint)display.Value);
            }
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    [LibraryImport("shell32")]
    private static unsafe partial int SHGetPropertyStoreFromParsingName(
        char* pszPath,
        void* pbc,
        GETPROPERTYSTOREFLAGS flags,
        Guid* riid,
        void** ppv);
}
