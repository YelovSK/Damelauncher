# Architecture

Damelauncher is a keyboard launcher for Windows. It uses Win32 for the window and message loop and Direct2D for rendering its custom UI, on the same infrastructure as Dameview.

## Project boundaries

- `Dame.Win32` contains low-level Windows mechanisms such as windowing, input translation, global hotkeys, and shell integration.
- `Dame.UI` is the app-independent UI framework: the element tree, layout, animation, and generic controls. It draws with Direct2D and takes input from Win32.
- `Damelauncher.Plugins` is the contract between the launcher and its plugins: `IResultProvider`, `LaunchResult`, `ScoredResult`, and the `FuzzyMatcher` every provider scores with. It depends on nothing else.
- `Damelauncher.Plugins.*` are the plugins, one project each. A plugin references the contract and, when it needs Windows mechanisms, `Dame.Win32`, but never the UI, the executable, or another plugin.
- `Damelauncher` is the executable. It contains the renderer, the launcher UI, and the search that runs the plugins.

```text
Damelauncher -> Damelauncher.Plugins.* -> Damelauncher.Plugins
                                       -> Dame.Win32
Damelauncher -> Dame.UI -> Dame.Win32
```

## Runtime flow

`LauncherApp` is the composition root. It creates a hidden popup `AppWindow`, the `D2DRenderer`, `LauncherUi`, and `ResultSearch` with the list of providers, and registers a global hotkey on the window. The hotkey toggles the window, which is centered on the monitor under the cursor each time it opens. Dismissing it, losing focus, or running a result hides it again; the process keeps running in the background. A `TrayIcon` opens it too, and its menu can quit the launcher.

Rendering is demand-driven, as in Dameview: input and state changes request frames, and nothing renders while nothing changes.

Each window has its own `AppWindow` and `D2DRenderer`, all on one thread. The launcher's window runs the message loop, which serves every window: between messages, it waits for input together with the swap chain of each window that wants a frame, and renders whichever is ready. Frames never render inside a message, so animating can't hold up input. Closing any window but the launcher's leaves the loop running.

## Settings

`SettingsService` reads `%LocalAppData%\Damelauncher\settings.ini` once at startup and writes it on every change, keeping comments and keys it doesn't know. It holds the theme, the hotkey, how many results show, and each plugin's settings (whether it's enabled, its keyword, whether normal searches include it, and how many of its results show), in a section per plugin. The themes are Dameview's, and a change of theme applies to every open window at once. Starting with Windows is read straight from the registry's Run key instead, which is the record of it.

`SettingsWindow` opens from the tray menu or the "Damelauncher Settings" result and is rebuilt each time. Its `SettingsUi` changes nothing itself: it asks `LauncherApp` through `ISettingsActions`, which applies and saves each change at once. A new hotkey only replaces the old one once it registers, since another app may hold it, and while one is being recorded the current one is released, so pressing it reaches the settings window. When the hotkey can't be registered at startup, the settings open instead of the launcher, saying why.

Tapping the Windows key can open the launcher too, as a separate setting next to the hotkey, since the system can't register a modifier on its own as a hotkey. `WindowsKeyTap` watches every key through a low-level keyboard hook, and when Windows is pressed and released with nothing in between, it sends an unassigned key before the release, so the shell takes the tap for a combination and leaves Start closed. Windows holds up all keyboard input until the hook returns, and removes a hook that keeps it waiting, so the hook runs on a thread of its own with a message loop that does nothing else, never on the window thread, which can be busy searching. It only posts the tap to the window thread.

## Installation

The executable installs itself for the current user, so it needs no installer or administrator rights. `AppInstallation` copies it to `%LocalAppData%\Programs\Damelauncher`, adds a Start menu shortcut, an entry in installed apps whose uninstall command runs the installed executable with `--uninstall`, and the Run key entry. A copy started from anywhere else asks once whether to install, before the launcher starts; declining is saved in the settings, and the settings window offers it again. When a copy is installed already, it always asks, as that's an update. Debug builds never ask.

Only one copy can hold the hotkey, so installing and uninstalling first close every other running copy, through `AppWindow.CloseInOtherProcesses`, which finds their launcher windows by class and title, and wait for them to exit. The installed copy starts only after the current one has closed its window. Uninstalling leaves the settings and history, and a command deletes the executable once the process has exited, since a running executable can't delete itself.


A plugin is an `IResultProvider`. `ResultSearch` initializes every provider once at startup, then sends each query to the providers its `ProviderRouting` lets through. A query starting with a provider's keyword and a space goes only to that provider, without the keyword; any other query goes to every enabled provider included in normal searches. Each plugin suggests a default keyword, which its settings can change or clear, and whether normal searches include it. How many results show is limited overall and, optionally, per provider: `ResultSearch` keeps each provider's best within its limit, counting launch history, then the best of those overall, and tells each provider its limit so one that fetches from elsewhere can ask for less. A new query cancels the previous one. Results merge by score as each provider answers, so a slow provider doesn't hold back a fast one, and the previous results stay on screen until the first answer to a new query arrives.

Providers are used on the window thread, which awaits resume on through `WindowSynchronizationContext`. A provider that needs more than a moment, to index or to ask another process, does that work elsewhere, like `ProgramsProvider` indexing on a COM worker. A provider whose results change on their own raises `Changed`, and `ResultSearch` asks the current query again. `ProgramsProvider` indexes once at startup and again only when the Apps folder changes, since indexing is slow. The shell reports changes to shortcuts through `ShellChangeWatcher`, but not packaged apps, so `PackageCatalogWatcher` listens to the package catalog for those, through hand-declared WinRT interfaces rather than the projection, which would add a lot to the executable. `ProcessesProvider` lists the running processes on a worker when its keyword is typed alone, and filters that list as the query grows until it's a few seconds old.

A `LaunchResult` is what a result is, apart from any query: its provider, id, text, icon, and the action that runs it, which the provider supplies as a closure. A query answers with `ScoredResult`s, pairing a result with how well it matches, scored with the shared `FuzzyMatcher` so scores from different providers compare on one scale.

This split is what keeps typing cheap, since search runs on every keystroke. Providers that know their results ahead, like programs and settings, build them once and answer each query by refilling the same list, so a keystroke doesn't allocate in them at all. `ResultSearch` merges into a fixed buffer that keeps only the best results instead of sorting every match, and arms its deadline timer only when a provider hasn't answered yet. `tests/Damelauncher.Benchmarks` measures the time and allocations per keystroke.

A result names its icon rather than carrying a bitmap, so providers never touch rendering: a `ShellIcon` by where the shell finds it, such as a file path or `shell:AppsFolder\…`, or a `GlyphIcon` by a symbol from the icon font, for results like Sleep that have no icon of their own. `ResultIcons` loads the icons of the rows in sight on COM workers through `ShellIcons`, uploads them to Direct2D on the window thread, and keeps the most recently used ones. Rows draw without their icon until it arrives.

A result can also have an actions menu: other things to do with it, like running it as administrator or copying its path. Providers offer them through `IResultProvider.GetActions`, asked only when the menu opens, and `ResultSearch` adds forgetting a result's launches. Actions are `LaunchResult`s too, shown in place of the search results until the user goes back or types. Running one doesn't count as launching the result.

## Ranking

A result's rank is its match score plus a boost for how often and how recently it was launched. `UsageHistory` keys each result by its provider's id and its own, both strings the result already holds, so the lookup allocates nothing. The history keeps one score per result, which grows by one with every launch and halves every two weeks, and saves it to `%LocalAppData%\Damelauncher\history.txt` as one tab-separated line per result. The boost grows with the logarithm of that score and is capped, so habit can lift a decent match but not a poor one.

## Serve mode

`Damelauncher.exe --serve` runs the search without the launcher's UI, for another program, such as a desktop shell, that starts it as a child process and draws the results itself. `ServeApp` takes `LauncherApp`'s place as the composition root: it reads the same settings and history, so ranking is shared, but has no visible window, tray icon, hotkey, or install prompt, and leaves out the launcher's own commands. A hidden `AppWindow` with a title of its own runs the message loop the providers need, so installing the launcher doesn't close it. It reads commands from standard input on a thread of its own, posts them to the window thread, and exits when the input ends or on `quit`. When the Windows-key setting is on, the tap is reported instead of opening anything.

Both directions are UTF-8 lines of tab-separated fields; tabs and line breaks inside a field become spaces. `ServeCommand` reads the input and `ServeOutput` writes the output, flushing each message, or a whole batch of results, at once.

```text
→ query  <seq> <text>              seq increases; a newer query cancels this one
→ run    <provider> <id>           runs one of the results last sent, counting it as a launch
→ quit
← ready                            once every provider has initialized
← begin  <seq>                     all of the current results for query <seq>, best first,
← result <provider> <id> <title> <subtitle> <icon>
← end    <seq>
← icon   <provider> <id> <path>    an icon that arrived after its result was sent
← toggle                           the Windows key was tapped
```

Results are sent again whenever they change, as they would update on screen: as providers answer, and when one raises `Changed`. A query that a newer one replaced before any answer arrived gets none. An icon is a PNG's path, `glyph:` and a symbol from the icon font, or empty while it loads or when there is none. The other program can't load shell icons itself, so `ServeIcons` saves them as PNGs to `%LocalAppData%\Damelauncher\icons` through `PngWriter` on COM workers, and reuses them across runs. Most files show their type's icon, so files share one PNG per extension and folders share one, while programs, shortcuts, icon files and the like, which carry icons of their own, get one per path. Each PNG is named by a hash of that key. Using a PNG marks it by its last-write time, and serve mode deletes those unused for a month when it starts, so the cache stays small and an updated app's icon is eventually extracted again.
