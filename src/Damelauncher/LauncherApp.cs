using System.ComponentModel;
using Dame.Win32;
using Dame.Win32.Input;
using Damelauncher.Plugins;
using Damelauncher.Plugins.Everything;
using Damelauncher.Plugins.Programs;
using Damelauncher.Plugins.WindowsSettings;
using Damelauncher.Rendering;
using Damelauncher.Search;
using Damelauncher.UI;

namespace Damelauncher;

internal sealed class LauncherApp : IDisposable
{
    private const int ToggleHotKeyId = 1;
    private const HotKeyModifiers ToggleModifiers = HotKeyModifiers.Alt | HotKeyModifiers.Shift;
    private const WindowKey ToggleKey = WindowKey.Space;

    private readonly AppWindow _window;
    private readonly D2DRenderer _renderer;
    private readonly LauncherUi _ui;
    private readonly ResultSearch _search;

    internal LauncherApp()
    {
        _window = new AppWindow(
            "Damelauncher",
            (int)LauncherUi.WindowWidth,
            (int)LauncherUi.WindowHeight,
            frame: AppWindowFrame.Popup);
        // Providers' awaits resume on the window thread, where the results are shown.
        SynchronizationContext.SetSynchronizationContext(new WindowSynchronizationContext(_window.Post));
        _renderer = new D2DRenderer(_window.Handle, _window.ClientWidth, _window.ClientHeight, _window.Dpi);
        _ui = new LauncherUi(
            _renderer.DeviceContext,
            _renderer.DirectWriteFactory,
            _window.Dpi);
        _search = new ResultSearch(
            new ProgramsProvider(),
            new WindowsSettingsProvider(),
            new EverythingProvider(),
            new LauncherCommandsProvider(_window.Close));
        _ui.QueryChanged += _search.Search;
        _search.ResultsChanged += _ui.SetResults;
        _ui.Invalidated += _window.RequestRepaint;
        _ui.CursorChanged += _window.ApplyCursor;
        _ui.ResultInvoked += Launch;
        _window.RenderFrame += HandleRenderFrame;
        _window.Resized += HandleResize;
        _window.DpiChanged += HandleDpiChanged;
        _window.KeyPressed += HandleKeyPress;
        _window.TextInput += HandleTextInput;
        _window.PointerInput += HandlePointerInput;
        _window.HotKeyPressed += HandleHotKey;
        _window.Deactivated += _window.Hide;
        _window.RegisterHotKey(ToggleHotKeyId, ToggleModifiers, ToggleKey);
    }

    internal void Run()
    {
        // Opens once at startup too, so launching it shows that it's running.
        _window.Post(Show);
        _window.Run(() => _renderer.FrameLatencyWaitHandle);
    }

    public void Dispose()
    {
        _ui.Invalidated -= _window.RequestRepaint;
        _ui.CursorChanged -= _window.ApplyCursor;
        _ui.ResultInvoked -= Launch;
        _ui.QueryChanged -= _search.Search;
        _search.ResultsChanged -= _ui.SetResults;
        _window.RenderFrame -= HandleRenderFrame;
        _window.Resized -= HandleResize;
        _window.DpiChanged -= HandleDpiChanged;
        _window.KeyPressed -= HandleKeyPress;
        _window.TextInput -= HandleTextInput;
        _window.PointerInput -= HandlePointerInput;
        _window.HotKeyPressed -= HandleHotKey;
        _window.Deactivated -= _window.Hide;
        _search.Dispose();
        _ui.Dispose();
        _renderer.Dispose();
        _window.Dispose();
    }

    private void Show()
    {
        _ui.Reset();
        _window.CenterOnCursorMonitor(LauncherUi.WindowWidth, LauncherUi.WindowHeight);
        _window.Show();
    }

    private void Launch(LaunchResult result)
    {
        _window.Hide();
        try
        {
            result.Execute();
        }
        catch (Win32Exception)
        {
            // A program that isn't installed isn't worth taking the launcher down for.
        }
    }

    private void HandleHotKey(int id)
    {
        if (id != ToggleHotKeyId)
        {
            return;
        }

        if (_window.IsVisible)
        {
            _window.Hide();
        }
        else
        {
            Show();
        }
    }

    private void HandleKeyPress(WindowKeyEvent input)
    {
        if (input.Key == WindowKey.Escape)
        {
            _window.Hide();
            return;
        }

        if (_ui.HandleKey(input))
        {
            _window.RequestRepaint();
        }
    }

    private void HandleTextInput(string text)
    {
        if (_ui.HandleTextInput(text))
        {
            _window.RequestRepaint();
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
