using Dame.Win32;

namespace Damelauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        NativeMethods.EnablePerMonitorDpiAwareness();
        NativeMethods.InitializeComApartment(ComApartment.ApartmentThreaded);

        try
        {
            using var app = new LauncherApp();
            app.Run();
        }
        finally
        {
            NativeMethods.UninitializeComApartment();
        }
    }
}
