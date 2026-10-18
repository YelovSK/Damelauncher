namespace Dame.Win32;

internal enum AppWindowFrame
{
    Standard,
    /// <summary>
    /// Leaves out the system title bar, so the app draws its own in the client area and says
    /// through <see cref="AppWindow.IsDragArea"/> where it drags the window.
    /// </summary>
    CustomTitleBar,
    /// <summary>
    /// A borderless, rounded, always-on-top window kept off the taskbar, which starts hidden
    /// and is shown with <see cref="AppWindow.Show"/>.
    /// </summary>
    Popup,
}
