# GroupTasker

A Windows taskbar group launcher — pin app groups to your taskbar and launch multiple shortcuts from a single icon flyout.

## Features

- **Group launchers** — create groups of shortcuts (apps, folders, URLs, store apps, and **live / auto-updating apps**) and pin them to the taskbar as a single icon
- **Live / auto-updating app support** — add Microsoft Store / MSIX apps (Claude Desktop, Codex, etc.) by their `AppUserModelId`. The AUMI is stable across updates, so the shortcut keeps working when the app version-folder changes (e.g. `WindowsApps\Claude_1.12603.1.0_…` → `Claude_1.12700.0.0_…`)
- **App picker** — search across running apps, pinned taskbar items, and every Microsoft Store app on the system (~300+ items from `shell:AppsFolder`)
- **Proper `.lnk` icon extraction** — reads `IconLocation` from `IShellLinkW` so separate `.ico` files (Ollama `app.ico`, Claude, Codex) get the right colourful icon
- **Lightweight flyout** — click the pinned icon to open a compact grid of shortcuts; stays open for multi-launch
- **Shortcut reorder** — drag-and-drop shortcuts in the editor, or long-press-and-drag directly in the flyout; changes persist immediately
- **Dead shortcut detection** — missing targets are dimmed with a "Not found" tooltip
- **Persistent position** — the flyout remembers its last position across launches
- **Single-instance** — clicking a pinned shortcut instantly opens the flyout via a background listener

## Requirements

- Windows 10 1809+ (or Windows 11)
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (to build)

## Quick start

```powershell
# Clone
git clone https://github.com/Ganron007/GroupTasker.git
cd GroupTasker

# Build
dotnet build src\GroupTasker.UI\GroupTasker.UI.csproj

# Run
dotnet run --project src\GroupTasker.UI\GroupTasker.UI.csproj

# Publish self-contained (bundles the .NET 9 runtime — ~109 MB; runs on any Windows 10/11 x64)
dotnet publish src\GroupTasker.UI\GroupTasker.UI.csproj -c Release -r win-x64 --self-contained true -o out\GroupTasker
dotnet publish src\GroupTasker.Launcher\GroupTasker.Launcher.csproj -c Release -o out\GroupTasker

# Publish framework-dependent (~37 MB; target needs .NET 9 Desktop Runtime)
dotnet publish src\GroupTasker.UI\GroupTasker.UI.csproj -c Release -r win-x64 --no-self-contained -o out\GroupTasker
dotnet publish src\GroupTasker.Launcher\GroupTasker.Launcher.csproj -c Release -o out\GroupTasker
```

The published folder is run with `GroupTasker.exe` — that's a .NET Framework 4.8
launcher shim that ensures the .NET 9 Desktop Runtime is present before
launching `GroupTasker.App.exe` (the Avalonia app). On the framework-dependent
build, the shim shows a download link popup if the runtime is missing.

## Releases

Pre-built zips live under `out/release\vX.Y.Z\`:

- `GroupTasker-vX.Y.Z-win-x64.zip` — self-contained (≈109 MB)
- `GroupTasker-vX.Y.Z-fxdep-win-x64.zip` — framework-dependent (≈37 MB)
- `RELEASE-vX.Y.Z.md` — release notes for that version
- `*.md5` — MD5 checksums

See [CHANGELOG.md](CHANGELOG.md) for the full version history.

## Usage

1. Launch GroupTasker — the configurator window opens
2. Create a group and add shortcuts (click **+ App**, **+ Folder**, **+ URL**, or **⚡ Live** for a Microsoft Store app)
3. Click **Pin to taskbar** — a `.lnk` shortcut is created in the `shortcut/` folder and Explorer opens so you can drag it to your taskbar
4. Click the pinned icon to open the flyout; click shortcuts to launch them; click away to close

## Project structure

```
src/
├── GroupTasker.Domain/        — Core entities, interfaces
├── GroupTasker.Application/   — Orchestration services
├── GroupTasker.Infrastructure/ — COM interop, icon extraction, persistence
├── GroupTasker.UI/            — Avalonia desktop UI (views + view-models)
└── GroupTasker.Launcher/      — .NET Framework 4.8 launcher shim (ensures .NET 9 runtime is present)
tests/
└── GroupTasker.UnitTests/     — xUnit unit tests
```

## Build notes

- Built with Avalonia 12.0.2 and .NET 9
- Uses Clean Architecture (Domain → Application → Infrastructure → UI)
- Self-contained publish: `--self-contained true` bundles the .NET 9 runtime (≈109 MB). No trimming — COM interop breaks under `PublishTrimmed`.
- Framework-dependent publish: `--no-self-contained` for a smaller footprint (≈37 MB); requires the .NET 9 Desktop Runtime on the target machine.
- A .NET Framework 4.8 launcher shim (`src\GroupTasker.Launcher\`) is published into the output folder as `GroupTasker.exe`. It launches the Avalonia app and (on the framework-dependent build) shows a download-link popup if the .NET 9 runtime is missing.
- Version is managed in `Directory.Build.props`; update it and add a section to `CHANGELOG.md` for each release

## License

This project is licensed under the MIT License.
