using System.ComponentModel;
using Dame.Win32;
using Dame.Win32.Input;
using Damelauncher.Plugins;
using Damelauncher.Plugins.Everything;
using Damelauncher.Plugins.Programs;
using Damelauncher.Plugins.WindowsSettings;
using Damelauncher.Rendering;
using Damelauncher.Search;
using Damelauncher.Settings;
using Damelauncher.UI;

namespace Damelauncher;

internal sealed class LauncherApp : ISettingsActions, IDisposable
{
    private const string AppName = "Damelauncher";
    private const int ToggleHotKeyId = 1;

    private readonly SettingsService _settings;
    private readonly AppWindow _window;
    private readonly D2DRenderer _renderer;
    private readonly LauncherUi _ui;
    private readonly IResultProvider[] _plugins;
    private readonly ResultSearch _search;
    private readonly TrayIcon _trayIcon;
    private SettingsWindow? _settingsWindow;
    private WindowsKeyTap? _windowsKeyTap;
    private bool _hotKeyRegistered;

    internal LauncherApp()
    {
        _settings = SettingsService.Load();
        _window = new AppWindow(
            AppName,
            (int)LauncherUi.WindowWidth,
            (int)LauncherUi.MaxWindowHeight,
            frame: AppWindowFrame.Popup);
        // Providers' awaits resume on the window thread, where the results are shown.
        SynchronizationContext.SetSynchronizationContext(new WindowSynchronizationContext(_window.Post));
        _renderer = new D2DRenderer(_window.Handle, _window.ClientWidth, _window.ClientHeight, _window.Dpi);
        _window.FrameLatencyWaitHandle = () => _renderer.FrameLatencyWaitHandle;
        _ui = new LauncherUi(
            _renderer.DeviceContext,
            _renderer.DirectWriteFactory,
            _window.Dpi,
            Themes.Get(_settings.Current.Theme).Palette);
        _plugins = [new ProgramsProvider(), new WindowsSettingsProvider(), new EverythingProvider()];
        _search = new ResultSearch(
            UsageHistory.Load(UsageHistory.DefaultPath),
            [.. _plugins, new LauncherCommandsProvider(OpenSettings, _window.Close)])
        {
            DisabledProviders = _settings.Current.DisabledPlugins,
        };
        _ui.QueryChanged += _search.Search;
        _search.ResultsChanged += ShowResults;
        _ui.Invalidated += _window.RequestRepaint;
        _ui.CursorChanged += _window.ApplyCursor;
        _ui.ResultInvoked += Launch;
        _ui.ActionsRequested += ShowActions;
        _window.RenderFrame += HandleRenderFrame;
        _window.Resized += HandleResize;
        _window.DpiChanged += HandleDpiChanged;
        _window.KeyPressed += HandleKeyPress;
        _window.TextInput += HandleTextInput;
        _window.PointerInput += HandlePointerInput;
        _window.HotKeyPressed += HandleHotKey;
        _window.Deactivated += _window.Hide;
        RegisterHotKey(_settings.Current.HotKey);
        WatchWindowsKey(_settings.Current.OpenWithWindowsKey);
        // Posted to the window, which reports a failure the way it does its own, instead of it
        // ending the process from the tray icon's window procedure.
        _trayIcon = new TrayIcon(
            AppName,
            () => _window.Post(Show),
            [
                new TrayMenuItem("Open", () => _window.Post(Show)),
                new TrayMenuItem("Settings", () => _window.Post(OpenSettings)),
                new TrayMenuItem("Quit", () => _window.Post(_window.Close)),
            ]);
    }

    LauncherSettings ISettingsActions.Settings => _settings.Current;

    ThemeId ISettingsActions.Theme
    {
        set
        {
            _settings.Update(_settings.Current with { Theme = value });
            Theme theme = Themes.Get(value);
            _ui.Palette = theme.Palette;
            _settingsWindow?.ApplyTheme(theme);
        }
    }

    IReadOnlyList<IResultProvider> ISettingsActions.Plugins => _plugins;

    bool ISettingsActions.IsHotKeyRegistered => _hotKeyRegistered;

    bool ISettingsActions.StartsWithWindows
    {
        get => StartupRegistration.IsRegistered(AppName);
        set
        {
            if (value)
            {
                StartupRegistration.Register(AppName, Environment.ProcessPath!);
            }
            else
            {
                StartupRegistration.Unregister(AppName);
            }
        }
    }

    bool ISettingsActions.OpensWithWindowsKey
    {
        get => _settings.Current.OpenWithWindowsKey;
        set
        {
            _settings.Update(_settings.Current with { OpenWithWindowsKey = value });
            WatchWindowsKey(value);
        }
    }

    internal void Run()
    {
        // Opens once at startup too, so launching it shows that it's running. Without its hotkey,
        // the settings open instead, which say why and let another one be picked.
        _window.Post(_hotKeyRegistered ? Show : OpenSettings);
        _window.Run();
    }

    bool ISettingsActions.TrySetHotKey(HotKey hotKey)
    {
        if (!RegisterHotKey(hotKey))
        {
            return false;
        }

        _settings.Update(_settings.Current with { HotKey = hotKey });
        return true;
    }

    void ISettingsActions.SuspendHotKey(bool suspended)
    {
        if (suspended)
        {
            UnregisterHotKey();
        }
        else if (!_hotKeyRegistered)
        {
            RegisterHotKey(_settings.Current.HotKey);
        }
    }

    void ISettingsActions.SetPluginEnabled(string id, bool enabled)
    {
        var disabled = new HashSet<string>(_settings.Current.DisabledPlugins);
        if (enabled)
        {
            disabled.Remove(id);
        }
        else
        {
            disabled.Add(id);
        }

        _settings.Update(_settings.Current with { DisabledPlugins = disabled });
        _search.DisabledProviders = disabled;
    }

    public void Dispose()
    {
        _ui.Invalidated -= _window.RequestRepaint;
        _ui.CursorChanged -= _window.ApplyCursor;
        _ui.ResultInvoked -= Launch;
        _ui.ActionsRequested -= ShowActions;
        _ui.QueryChanged -= _search.Search;
        _search.ResultsChanged -= ShowResults;
        _window.RenderFrame -= HandleRenderFrame;
        _window.Resized -= HandleResize;
        _window.DpiChanged -= HandleDpiChanged;
        _window.KeyPressed -= HandleKeyPress;
        _window.TextInput -= HandleTextInput;
        _window.PointerInput -= HandlePointerInput;
        _window.HotKeyPressed -= HandleHotKey;
        _window.Deactivated -= _window.Hide;
        _settingsWindow?.Dispose();
        _windowsKeyTap?.Dispose();
        _trayIcon.Dispose();
        _search.Dispose();
        _ui.Dispose();
        _renderer.Dispose();
        _window.Dispose();
    }

    private void Show()
    {
        _ui.Reset();
        // Placed for its tallest, so the query box stays put as the results resize the window below it.
        _window.CenterOnCursorMonitor(LauncherUi.WindowWidth, LauncherUi.MaxWindowHeight);
        _window.SetSize(LauncherUi.WindowWidth, _ui.WindowHeight);
        _window.Show();
    }

    private void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            // Posted, since the window is still being destroyed when it says it closed.
            _settingsWindow.Closed += () => _window.Post(CloseSettings);
        }

        _settingsWindow.Show();
    }

    private void CloseSettings()
    {
        _settingsWindow?.Dispose();
        _settingsWindow = null;
        // Closing the window in the middle of recording a hotkey never resumes the current one.
        if (!_hotKeyRegistered)
        {
            RegisterHotKey(_settings.Current.HotKey);
        }
    }

    private void WatchWindowsKey(bool watch)
    {
        if (watch)
        {
            // The tap arrives inside the keyboard hook, which mustn't keep the keyboard waiting.
            _windowsKeyTap ??= new WindowsKeyTap(() => _window.Post(Toggle));
        }
        else
        {
            _windowsKeyTap?.Dispose();
            _windowsKeyTap = null;
        }
    }

    private bool RegisterHotKey(HotKey hotKey)
    {
        UnregisterHotKey();
        _hotKeyRegistered = _window.TryRegisterHotKey(ToggleHotKeyId, hotKey.Modifiers, hotKey.Key);
        return _hotKeyRegistered;
    }

    private void UnregisterHotKey()
    {
        _window.UnregisterHotKey(ToggleHotKeyId);
        _hotKeyRegistered = false;
    }

    private void ShowResults(IReadOnlyList<LaunchResult> results)
    {
        _ui.SetResults(results);
        _window.SetSize(LauncherUi.WindowWidth, _ui.WindowHeight);
    }

    private void ShowActions(LaunchResult result)
    {
        IReadOnlyList<LaunchResult> actions = _search.GetActions(result);
        if (actions.Count > 0)
        {
            _ui.ShowActions(result, actions);
            _window.SetSize(LauncherUi.WindowWidth, _ui.WindowHeight);
        }
    }

    private void HideActions()
    {
        _ui.HideActions();
        _window.SetSize(LauncherUi.WindowWidth, _ui.WindowHeight);
    }

    private void Launch(LaunchResult result)
    {
        // Only opening a result itself counts toward its ranking, not copying its path and the like.
        bool isAction = _ui.ActionsTarget is not null;
        _window.Hide();
        try
        {
            result.Execute();
            if (!isAction)
            {
                _search.RecordLaunch(result);
            }
        }
        catch (Win32Exception)
        {
            // A program that isn't installed isn't worth taking the launcher down for.
        }
    }

    private void HandleHotKey(int id)
    {
        if (id == ToggleHotKeyId)
        {
            Toggle();
        }
    }

    private void Toggle()
    {
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
            if (_ui.ActionsTarget is not null)
            {
                HideActions();
            }
            else
            {
                _window.Hide();
            }

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
        // The launcher is one floating panel, so it has a surface's color rather than a window's.
        _renderer.Render(_ui.DrawFrame, _ui.Palette.Surface);
        if (animationContinues)
        {
            _window.RequestRepaint();
        }
    }
}
