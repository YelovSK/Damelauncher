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

`LauncherApp` is the composition root. It creates a hidden popup `AppWindow`, the `D2DRenderer`, `LauncherUi`, and `ResultSearch` with the list of providers, and registers a global hotkey on the window. The hotkey toggles the window, which is centered on the monitor under the cursor each time it opens. Escape, losing focus, or running a result hides it again; the process keeps running in the background. A `TrayIcon` opens it too, and its menu can quit the launcher.

Rendering is demand-driven, as in Dameview: input and state changes request frames, and nothing renders while nothing changes.

## Plugins

A plugin is an `IResultProvider`. `ResultSearch` initializes every provider once at startup, then sends each query to all of them. A new query cancels the previous one. Results merge by score as each provider answers, so a slow provider doesn't hold back a fast one, and the previous results stay on screen until the first answer to a new query arrives.

Providers are used on the window thread, which awaits resume on through `WindowSynchronizationContext`. A provider that needs more than a moment, to index or to ask another process, does that work elsewhere, like `ProgramsProvider` indexing on a COM worker.

A `LaunchResult` is plain data plus the action that runs it, which the provider supplies as a closure. Results score with the shared `FuzzyMatcher`, so scores from different providers compare on one scale.

## Ranking

A result's rank is its match score plus a boost for how often and how recently it was launched. Each `LaunchResult` carries an id that's stable within its provider, and `ResultSearch` qualifies it with the provider's id to look it up in `UsageHistory`. The history keeps one score per result, which grows by one with every launch and halves every two weeks, and saves it to `%LocalAppData%\Damelauncher\history.txt` as one tab-separated line per result. The boost grows with the logarithm of that score and is capped, so habit can lift a decent match but not a poor one.
