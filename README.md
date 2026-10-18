<h1 align="center">Damelauncher</h1>

<p align="center">
  A fast launcher for Windows.<br>
  A single ~3 MB executable with a custom-drawn UI.<br>
</p>

## Features

- Opens with a hotkey (Alt + Space by default), or by tapping the Windows key on its own
- Fuzzy search
- Keywords to search a single plugin, e.g. `f report` for files only
- Results ranked by frequency and recency
- Plugins:
  - Programs
  - Settings pages and power commands (lock, sleep, sign out, shut down, restart)
  - Files and folders through [Everything](https://www.voidtools.com/)
- Icons for every result
- Tray icon
- Settings window with the hotkey, plugins, start with Windows, and color themes

## Tech stack

- .NET 10
- Native AOT for Windows x64
- Raw Win32 windows and message loop
- Direct2D through Vortice.Windows
- Custom-drawn UI, shared with [Dameview](https://github.com/YelovSK/Dameview)

More in [ARCHITECTURE.md](ARCHITECTURE.md).

## Motivation

Honestly, pretty much the same as [Dameview](https://github.com/YelovSK/Dameview#motivation), which is my image viewer.

There are already good launchers for Windows, e.g. Flow Launcher and Fluent Search. I was curious what it would take to make a minimal, performance-focused launcher on the same stack as Dameview. So I copied the UI and Win32 projects from Dameview, and built the launcher of top of that stack.

There is no support for 3rd party plugins. It's as bare bones as possible for my specific use case, which includes just a couple of search providers/plugins - programs, settings, and Everything search.

The code is written by AI, I have read very little of it (unlike Dameview, which I took more seriously).

## Run

```powershell
dotnet run --project src/Damelauncher
```

The launcher opens once at startup and then stays in the tray. Settings are stored in `%LOCALAPPDATA%\Damelauncher`.

## Publish

```powershell
dotnet publish src/Damelauncher -c Release
```

Code style is defined in `.editorconfig` and enforced by the built-in .NET analyzers. Use `dotnet format` to apply it.
