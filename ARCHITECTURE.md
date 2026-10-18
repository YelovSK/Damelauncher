# Architecture

Damelauncher is a keyboard launcher for Windows. It uses Win32 for the window and message loop and Direct2D for rendering its custom UI, on the same infrastructure as Dameview.

## Project boundaries

- `Dame.Win32` contains low-level Windows mechanisms such as windowing, input translation, global hotkeys, and shell integration.
- `Dame.UI` is the app-independent UI framework: the element tree, layout, animation, and generic controls. It draws with Direct2D and takes input from Win32.
- `Damelauncher.Plugins` is the contract between the launcher and its plugins: `IResultProvider`, `LaunchResult`, and the `FuzzyMatcher` every provider scores with. It depends on nothing else.
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

`SettingsService` reads `%LocalAppData%\Damelauncher\settings.ini` once at startup and writes it on every change, keeping comments and keys it doesn't know. It holds the theme, the hotkey and the disabled plugins. The themes are Dameview's, and a change of theme applies to every open window at once. Starting with Windows is read straight from the registry's Run key instead, which is the record of it.

`SettingsWindow` opens from the tray menu or the "Damelauncher Settings" result and is rebuilt each time. Its `SettingsUi` changes nothing itself: it asks `LauncherApp` through `ISettingsActions`, which applies and saves each change at once. A new hotkey only replaces the old one once it registers, since another app may hold it, and while one is being recorded the current one is released, so pressing it reaches the settings window. When the hotkey can't be registered at startup, the settings open instead of the launcher, saying why.

Tapping the Windows key can open the launcher too, as a separate setting next to the hotkey, since the system can't register a modifier on its own as a hotkey. `WindowsKeyTap` watches every key through a low-level keyboard hook, and when Windows is pressed and released with nothing in between, it sends an unassigned key before the release, so the shell takes the tap for a combination and leaves Start closed. The hook runs on the window thread's message loop and only posts the tap, since Windows removes a hook that keeps the keyboard waiting.

## Plugins

A plugin is an `IResultProvider`. `ResultSearch` initializes every provider once at startup, then sends each query to all of them. A new query cancels the previous one. Results merge by score as each provider answers, so a slow provider doesn't hold back a fast one, and the previous results stay on screen until the first answer to a new query arrives.

Providers are used on the window thread, which awaits resume on through `WindowSynchronizationContext`. A provider that needs more than a moment, to index or to ask another process, does that work elsewhere, like `ProgramsProvider` indexing on a COM worker.

A `LaunchResult` is plain data plus the action that runs it, which the provider supplies as a closure. Results score with the shared `FuzzyMatcher`, so scores from different providers compare on one scale.

A result names its icon by where the shell finds it, such as a file path or `shell:AppsFolder\…`, rather than carrying a bitmap, so providers never touch rendering. `ResultIcons` loads the icons of the rows in sight on COM workers through `ShellIcons`, uploads them to Direct2D on the window thread, and keeps the most recently used ones. Rows draw without their icon until it arrives.

A result can also have an actions menu: other things to do with it, like running it as administrator or copying its path. Providers offer them through `IResultProvider.GetActions`, asked only when the menu opens, and `ResultSearch` adds forgetting a result's launches. Actions are `LaunchResult`s too, shown in place of the search results until the user goes back or types. Running one doesn't count as launching the result.

## Ranking

A result's rank is its match score plus a boost for how often and how recently it was launched. Each `LaunchResult` carries an id that's stable within its provider, and `ResultSearch` qualifies it with the provider's id to look it up in `UsageHistory`. The history keeps one score per result, which grows by one with every launch and halves every two weeks, and saves it to `%LocalAppData%\Damelauncher\history.txt` as one tab-separated line per result. The boost grows with the logarithm of that score and is capped, so habit can lift a decent match but not a poor one.
