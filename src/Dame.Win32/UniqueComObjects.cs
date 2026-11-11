using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32.System.Com;

namespace Dame.Win32;

/// <summary>
/// COM objects that can be released as soon as they're done with. The generated interop shares
/// one wrapper per object and only releases it when the garbage collector finalizes it, which can
/// keep a file open or native memory in use long after.
/// </summary>
internal static unsafe partial class UniqueComObjects
{
    private static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>Creates an object of a COM class, in this process.</summary>
    internal static T Create<T>(Guid classId)
        where T : class
    {
        Guid iid = typeof(T).GUID;
        void* instance;
        Marshal.ThrowExceptionForHR(CoCreateInstance(&classId, null, CLSCTX.CLSCTX_INPROC_SERVER, &iid, &instance));
        return Own<T>(instance);
    }

    /// <summary>Wraps an interface pointer, taking over the reference the caller holds.</summary>
    internal static T Own<T>(void* instance)
        where T : class
    {
        object wrapper = Wrappers.GetOrCreateObjectForComInstance((nint)instance, CreateObjectFlags.UniqueInstance);
        // The wrapper holds a reference of its own.
        Marshal.Release((nint)instance);
        return (T)wrapper;
    }

    /// <param name="wrapper">An object from <see cref="Own{T}"/>, or <see langword="null"/>.</param>
    internal static void Release(object? wrapper) => (wrapper as ComObject)?.FinalRelease();

    // Declared by hand for the raw pointer, which the generated one wraps.
    [LibraryImport("ole32")]
    private static partial int CoCreateInstance(Guid* rclsid, void* pUnkOuter, CLSCTX dwClsContext, Guid* riid, void** ppv);
}
