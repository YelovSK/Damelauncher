# Architecture

Damelauncher is a keyboard launcher for Windows. It uses Win32 for the window and message loop and Direct2D for rendering its custom UI, on the same infrastructure as Dameview.

## Project boundaries

- `Dame.Win32` contains low-level Windows mechanisms such as windowing, input translation, global hotkeys, and shell integration.
- `Dame.UI` is the app-independent UI framework: the element tree, layout, animation, and generic controls. It draws with Direct2D and takes input from Win32.
- `Damelauncher` is the executable. It contains the renderer, the launcher UI, and the results.

```text
Damelauncher -> Dame.UI -> Dame.Win32
Damelauncher -> Dame.Win32
```

## Runtime flow

`LauncherApp` is the composition root. It creates a hidden popup `AppWindow`, the `D2DRenderer`, and `LauncherUi`, and registers a global hotkey on the window. The hotkey toggles the window, which is centered on the monitor under the cursor each time it opens. Escape, losing focus, or running a result hides it again; the process keeps running in the background.

Typing updates the query, which `ResultCatalog` searches. `ResultList` draws the matches and runs the selected one on Enter or a click.

Rendering is demand-driven, as in Dameview: input and state changes request frames, and nothing renders while nothing changes.
