# GroupTasker

A Windows taskbar group launcher — pin app groups to your taskbar and launch multiple shortcuts from a single icon flyout.

## Features

- **Group launchers** — create groups of shortcuts (apps, folders, URLs, store apps) and pin them to the taskbar as a single icon
- **Lightweight flyout** — click the pinned icon to open a compact grid of shortcuts; stays open for multi-launch
- **Dead shortcut detection** — missing targets are dimmed with a "Not found" tooltip
- **Persistent position** — the flyout remembers its last position across launches
- **Single-instance** — clicking a pinned shortcut instantly opens the flyout via a background listener

## Requirements

- Windows 10 1809+ (or Windows 11)
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (to build)

## Quick start

```powershell
# Clone
git clone https://github.com/YOUR_USERNAME/GroupTasker.git
cd GroupTasker

# Build
dotnet build src\GroupTasker.UI\GroupTasker.UI.csproj

# Run
dotnet run --project src\GroupTasker.UI\GroupTasker.UI.csproj

# Or publish self-contained
dotnet publish src\GroupTasker.UI\GroupTasker.UI.csproj -c Release -o out\GroupTasker --self-contained true -p:PublishReadyToRun=true
```

## Usage

1. Launch GroupTasker — the configurator window opens
2. Create a group and add shortcuts to apps, folders, or URLs
3. Click **Pin to taskbar** — a `.lnk` shortcut is created in the `shortcut/` folder and Explorer opens so you can drag it to your taskbar
4. Click the pinned icon to open the flyout; click shortcuts to launch them; click away to close

## Project structure

```
src/
├── GroupTasker.Domain/       — Core entities, interfaces
├── GroupTasker.Application/  — Orchestration services
├── GroupTasker.Infrastructure/ — COM interop, icon extraction, persistence
└── GroupTasker.UI/           — Avalonia desktop UI (views + view-models)
tests/
└── GroupTasker.UnitTests/    — xUnit unit tests
```

## Build notes

- Built with Avalonia 12.0.2 and .NET 9
- Uses Clean Architecture (Domain → Application → Infrastructure → UI)
- Self-contained publish: `--self-contained true` with ReadyToRun (no trimming — COM interop breaks under PublishTrimmed)
- Version is managed in `Directory.Build.props`; update it and add a section to `CHANGELOG.md` for each release

## License

This project is licensed under the MIT License.
