using Dame.Win32;
using Dame.Win32.Input;
using Damelauncher.Rendering;
using Damelauncher.UI;

namespace Damelauncher;

/// <summary>A window of its own for the settings, with its own renderer, made each time it opens.</summary>
internal sealed class SettingsWindow : IDisposable
{
    private readonly AppWindow _window;
    private readonly D2DRenderer _renderer;
    private readonly SettingsUi _ui;

    internal SettingsWindow(ISettingsActions actions)
    {
        _window = new AppWindow(
            "Damelauncher Settings",
            (int)SettingsUi.WindowWidth,
            (int)SettingsUi.WindowHeight);
        _window.CenterOnCursorMonitor(SettingsUi.WindowWidth, SettingsUi.WindowHeight);
        _window.SetTitleBarTheme(dark: true, Themes.Dark.WindowCaptionColor, Themes.Dark.WindowTextColor);
        _renderer = new D2DRenderer(_window.Handle, _window.ClientWidth, _window.ClientHeight, _window.Dpi);
        _window.FrameLatencyWaitHandle = () => _renderer.FrameLatencyWaitHandle;
        _ui = new SettingsUi(_renderer.DeviceContext, _renderer.DirectWriteFactory, _window.Dpi, actions);
        _ui.Invalidated += _window.RequestRepaint;
        _ui.CursorChanged += _window.ApplyCursor;
        _window.RenderFrame += HandleRenderFrame;
        _window.Resized += HandleResize;
        _window.DpiChanged += HandleDpiChanged;
        _window.KeyPressed += HandleKeyPress;
        _window.PointerInput += HandlePointerInput;
        _window.Closed += HandleClosed;
    }

    /// <summary>The user closed the window, after which this is only good for disposing.</summary>
    internal event Action? Closed;

    /// <summary>Shows the window, or brings it to the front when it already is, minimized or not.</summary>
    internal void Show()
    {
        _window.Show();
        _ = _window.Activate();
    }

    public void Dispose()
    {
        _ui.Invalidated -= _window.RequestRepaint;
        _ui.CursorChanged -= _window.ApplyCursor;
        _window.RenderFrame -= HandleRenderFrame;
        _window.Resized -= HandleResize;
        _window.DpiChanged -= HandleDpiChanged;
        _window.KeyPressed -= HandleKeyPress;
        _window.PointerInput -= HandlePointerInput;
        _window.Closed -= HandleClosed;
        _ui.Dispose();
        _renderer.Dispose();
        _window.Dispose();
    }

    private void HandleClosed() => Closed?.Invoke();

    private void HandleKeyPress(WindowKeyEvent input)
    {
        if (_ui.HandleKey(input))
        {
            _window.RequestRepaint();
        }
        else if (input.Key == WindowKey.Escape)
        {
            _window.Close();
        }
    }

    private void HandleDpiChanged(float dpi)
    {
        _renderer.SetDpi(dpi);
        _ui.SetDpi(dpi);
    }

    private void HandleResize(int width, int height)
    {
        _renderer.Resize(width, height);
        _window.RequestRepaint();
    }

    private void HandlePointerInput(WindowPointerEvent input) => _ui.HandlePointer(input);

    private void HandleRenderFrame()
    {
        bool animationContinues = _ui.Update();
        _renderer.Render(_ui.DrawFrame, _ui.Palette.Background);
        if (animationContinues)
        {
            _window.RequestRepaint();
        }
    }
}
