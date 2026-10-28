using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.System.WinRT;
using static Windows.Win32.PInvoke;

namespace Dame.Win32;

/// <summary>
/// Reports when the current user's packaged apps finish installing, updating, or uninstalling,
/// which the shell doesn't announce. Its reports arrive through the synchronization context of
/// the thread that created it.
/// </summary>
/// <remarks>
/// Uses the WinRT <c>PackageCatalog</c> through hand-declared interfaces, as the generated
/// projection would add far more to the executable than these few calls need.
/// </remarks>
internal sealed class PackageCatalogWatcher : IDisposable
{
    private const string ClassName = "Windows.ApplicationModel.PackageCatalog";

    private readonly IPackageCatalog _catalog;
    private readonly long _installing;
    private readonly long _updating;
    private readonly long _uninstalling;

    internal PackageCatalogWatcher(Action changed)
    {
        SynchronizationContext context = SynchronizationContext.Current
            ?? throw new InvalidOperationException("Package changes are reported through a synchronization context, and this thread has none.");
        _catalog = GetStatics().OpenForCurrentUser();
        var handler = new PackageCatalogHandler(() => context.Post(static state => ((Action)state!)(), changed));
        _installing = _catalog.AddPackageInstalling(handler);
        _updating = _catalog.AddPackageUpdating(handler);
        _uninstalling = _catalog.AddPackageUninstalling(handler);
    }

    public void Dispose()
    {
        _catalog.RemovePackageInstalling(_installing);
        _catalog.RemovePackageUpdating(_updating);
        _catalog.RemovePackageUninstalling(_uninstalling);
    }

    private static IPackageCatalogStatics GetStatics()
    {
        WindowsCreateString(ClassName, (uint)ClassName.Length, out WindowsDeleteStringSafeHandle className).ThrowOnFailure();
        using (className)
        {
            RoGetActivationFactory(className, out IPackageCatalogStatics statics).ThrowOnFailure();
            return statics;
        }
    }
}

// Agile, so the catalog calls it from its own threads instead of marshaling into the creating apartment.
[GeneratedComClass]
internal sealed partial class PackageCatalogHandler(Action completed)
    : IPackageInstallingHandler, IPackageUpdatingHandler, IPackageUninstallingHandler, IAgileObject
{
    void IPackageInstallingHandler.Invoke(nint sender, IPackageInstallingEventArgs args) => Report(args.GetIsComplete());

    void IPackageUpdatingHandler.Invoke(nint sender, IPackageUpdatingEventArgs args) => Report(args.GetIsComplete());

    void IPackageUninstallingHandler.Invoke(nint sender, IPackageUninstallingEventArgs args) => Report(args.GetIsComplete());

    // Progress is reported along the way too, and only the end changes which apps there are.
    private void Report(bool isComplete)
    {
        if (isComplete)
        {
            completed();
        }
    }
}

// The interfaces below mirror windows.applicationmodel.h. Only the order of their methods matters
// to the calls, so members that are never called keep simplified signatures.

[GeneratedComInterface]
[Guid("a18c9696-e65b-4634-ba21-5e63eb7244a7")]
internal unsafe partial interface IPackageCatalogStatics : IInspectable
{
    public IPackageCatalog OpenForCurrentPackage();

    public IPackageCatalog OpenForCurrentUser();
}

[GeneratedComInterface]
[Guid("230a3751-9de3-4445-be74-91fb325abefe")]
internal unsafe partial interface IPackageCatalog : IInspectable
{
    public long AddPackageStaging(nint handler);

    public void RemovePackageStaging(long token);

    public long AddPackageInstalling(IPackageInstallingHandler handler);

    public void RemovePackageInstalling(long token);

    public long AddPackageUpdating(IPackageUpdatingHandler handler);

    public void RemovePackageUpdating(long token);

    public long AddPackageUninstalling(IPackageUninstallingHandler handler);

    public void RemovePackageUninstalling(long token);
}

[GeneratedComInterface]
[Guid("a8a900c6-da0b-5bcc-a71a-be0b9265d87a")]
internal unsafe partial interface IPackageInstallingHandler
{
    public void Invoke(nint sender, IPackageInstallingEventArgs args);
}

[GeneratedComInterface]
[Guid("c23e15f6-c618-522a-82ab-4fab36665ce5")]
internal unsafe partial interface IPackageUpdatingHandler
{
    public void Invoke(nint sender, IPackageUpdatingEventArgs args);
}

[GeneratedComInterface]
[Guid("bd636cf1-541f-53ea-8efc-e1604a395b1a")]
internal unsafe partial interface IPackageUninstallingHandler
{
    public void Invoke(nint sender, IPackageUninstallingEventArgs args);
}

[GeneratedComInterface]
[Guid("97741eb7-ab7a-401a-8b61-eb0e7faff237")]
internal unsafe partial interface IPackageInstallingEventArgs : IInspectable
{
    public Guid GetActivityId();

    public nint GetPackage();

    public double GetProgress();

    [return: MarshalAs(UnmanagedType.U1)]
    public bool GetIsComplete();
}

[GeneratedComInterface]
[Guid("cd7b4228-fd74-443e-b114-23e677b0e86f")]
internal unsafe partial interface IPackageUpdatingEventArgs : IInspectable
{
    public Guid GetActivityId();

    public nint GetSourcePackage();

    public nint GetTargetPackage();

    public double GetProgress();

    [return: MarshalAs(UnmanagedType.U1)]
    public bool GetIsComplete();
}

[GeneratedComInterface]
[Guid("4443aa52-ab22-44cd-82bb-4ec9b827367a")]
internal unsafe partial interface IPackageUninstallingEventArgs : IInspectable
{
    public Guid GetActivityId();

    public nint GetPackage();

    public double GetProgress();

    [return: MarshalAs(UnmanagedType.U1)]
    public bool GetIsComplete();
}
