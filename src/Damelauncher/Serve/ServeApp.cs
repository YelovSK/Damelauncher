using System.ComponentModel;
using System.Text;
using Dame.Win32;
using Damelauncher.Plugins;
using Damelauncher.Search;
using Damelauncher.Settings;

namespace Damelauncher.Serve;

/// <summary>
/// Serve mode: the launcher's search without its window, for another program that shows the
/// results itself. Commands arrive on standard input, and results leave on standard output.
/// </summary>
/// <remarks>
/// It shares the settings and launch history with the launcher, but none of its window, tray
/// icon, or hotkey, so the two can run side by side.
/// </remarks>
internal sealed class ServeApp : IDisposable
{
    private readonly AppWindow _window;
    private readonly ServeOutput _output;
    private readonly ServeIcons _icons;
    private readonly ResultSearch _search;
    private readonly WindowsKeyTap? _windowsKeyTap;
    // The results last sent, which run commands pick from.
    private IReadOnlyList<LaunchResult> _results = [];
    private long _sequence;

    internal ServeApp(SettingsService settings)
    {
        // Never shown, it runs the message loop the providers rely on. Its own title keeps the
        // launcher's installer, which closes the launcher's windows by title, from closing it.
        _window = new AppWindow($"{LauncherApp.AppName} Serve", 1, 1, frame: AppWindowFrame.Popup);
        SynchronizationContext.SetSynchronizationContext(new WindowSynchronizationContext(_window.Post));
        _output = new ServeOutput(
            // Big enough for a whole batch of results, so each goes out in one write.
            new StreamWriter(
                Console.OpenStandardOutput(),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                bufferSize: 16 * 1024),
            () => _window.Post(_window.Close));
        _icons = new ServeIcons(ServeIcons.DefaultDirectory, SendIcon);
        // Without the launcher's own commands, which open its settings or quit it.
        IResultProvider[] plugins = LauncherApp.CreatePlugins();
        _search = new ResultSearch(UsageHistory.Load(UsageHistory.DefaultPath), plugins)
        {
            MaxResults = settings.Current.MaxResults,
        };
        foreach (IResultProvider plugin in plugins)
        {
            _search.SetRouting(plugin.Id, ProviderRouting.FromSettings(plugin, settings.Current.GetPlugin(plugin.Id)));
        }

        _search.ResultsChanged += SendResults;
        if (settings.Current.OpenWithWindowsKey)
        {
            // The tap arrives inside the keyboard hook, which mustn't keep the keyboard waiting.
            _windowsKeyTap = new WindowsKeyTap(() => _window.Post(_output.Toggle));
        }
    }

    internal void Run()
    {
        _ = AnnounceReadyAsync();
        new Thread(ReadCommands) { IsBackground = true, Name = "Serve input" }.Start();
        _window.Run();
    }

    public void Dispose()
    {
        _search.ResultsChanged -= SendResults;
        _windowsKeyTap?.Dispose();
        _search.Dispose();
        _icons.Dispose();
        _window.Dispose();
    }

    private async Task AnnounceReadyAsync()
    {
        // A provider that failed to initialize answers nothing, which needn't hold the others back.
        await _search.WhenInitialized().ConfigureAwait(
            ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);
        _output.Ready();
    }

    // On a thread of its own, since reading waits for the next line.
    private void ReadCommands()
    {
        try
        {
            using var reader = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            while (reader.ReadLine() is { } line)
            {
                if (ServeCommand.Parse(line) is { } command)
                {
                    _window.Post(() => Handle(command));
                }
            }
        }
        catch (IOException)
        {
            // A broken pipe ends the input the same as its end does.
        }

        // The program that started it has quit or died, so there's no one left to serve.
        _window.Post(_window.Close);
    }

    private void Handle(ServeCommand command)
    {
        switch (command)
        {
            case QueryCommand query:
                _sequence = query.Sequence;
                _search.Search(query.Text);
                break;
            case RunCommand run:
                Launch(run);
                break;
            case QuitCommand:
                _window.Close();
                break;
        }
    }

    private void Launch(RunCommand run)
    {
        foreach (LaunchResult result in _results)
        {
            if (result.Provider.Id != run.Provider || result.Id != run.Id)
            {
                continue;
            }

            try
            {
                result.Execute();
                _search.RecordLaunch(result);
            }
            catch (Win32Exception)
            {
                // A program that isn't installed isn't worth taking the search down for.
            }

            return;
        }
    }

    private void SendResults(IReadOnlyList<LaunchResult> results)
    {
        _results = results;
        _output.Results(_sequence, results, _icons.Get);
    }

    // Results that share the icon, such as files of one type, get it together.
    private void SendIcon(string path)
    {
        foreach (LaunchResult result in _results)
        {
            if (result.Icon is ShellIcon && _icons.Get(result.Icon) == path)
            {
                _output.Icon(result, path);
            }
        }
    }
}
