# Changelog

All notable changes to GroupTasker are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The canonical version lives in `Directory.Build.props` — bump `<VersionPrefix>`
there and add a new section at the top of this file for each release.

## [Unreleased]

### Added

- **CI pipeline** (`.github/workflows/ci.yml`) — builds, tests, and smoke-publishes
  on every push/PR to `main` using GitHub Actions on `windows-latest`.
- **Release pipeline** (`.github/workflows/release.yml`) — publishes self-contained
  and framework-dependent builds on every `v*` tag and uploads the resulting zips
  as workflow artifacts.
- **Structured logging** via Serilog. New `ILogger` seam in the Domain layer,
  `SerilogLogger` implementation in Infrastructure, and `SerilogBootstrap` that
  writes to `%APPDATA%\GroupTasker\logs\log-.txt` (rolling daily, 7 days retained)
  plus Debug output. Logger creation failures fall back to a no-op logger so the
  app always starts.

### Changed

- `JsonGroupRepository` and `LauncherSettingsService` now accept `ILogger` instead
  of the previous `Action<string, Exception>?` error callback.
- `GroupService`, `IconCacheService`, `WindowsShortcutService`, and
  `SingleInstanceService` are now instrumented with informational and error logs
  for group operations, shortcut launches, icon extraction failures, and named-pipe
  communication errors.

## [1.5.0] — 2026-06-15

### Added

- **System tray icon** — `ITrayIconService` (Domain) + `TrayIconService`
  (Infrastructure) wrap `Shell_NotifyIconW` against a hidden message-only
  window on a dedicated background thread. The icon is extracted from the
  running exe via `ExtractIcon`. The context menu lists every group (with
  a checkmark on the primary), then "Open configurator" and "Quit". Left-click
  on the icon opens the primary group's flyout.
- **Auto-start with Windows** — new `StartupService.SetAutoStart(bool)` writes
  `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\GroupTasker` with
  `"exePath" --tray`. When enabled, Windows launches the app hidden in the
  tray on login. The new `--tray` CLI arg sets `ShutdownMode.OnExplicitShutdown`
  so the process keeps the single-instance pipe + global hotkey alive without
  showing any window. The Settings dialog gets two new checkboxes:
  "Show tray icon while running" (default on) and "Start with Windows
  (hidden in tray)" (default off).
- **Per-group custom icon** — `Group.CustomIconPath` (string?). The
  configurator gets "Browse…" / "Clear" buttons for picking a `.ico`/PNG/BMP/JPG.
  When set, `WindowsShortcutService.CreateGroupLauncherLink` uses it as the
  `.lnk` icon location, overriding the auto-generated composite.
- **Per-group accent colour** — `Group.AccentColor` (string, hex like
  `#4A9EFF`). The `LauncherWindow` border brush binds to it (parsed as
  `IBrush` via `Color.Parse` in the VM). Empty = default `#3A3A3A`.
- **Export / import groups** — `GroupService.ExportGroupsAsync(path)` serializes
  all groups to a single JSON file; `GroupService.ImportGroupsAsync(path)`
  deserializes and saves each via the repository (overwrites by Id). The main
  window gets "Export" and "Import" buttons wired to file-picker dialogs.
  Import reloads the group list.

### Notes

- 76 unit tests pass (unchanged from v1.4.0). 0 warnings, 0 errors on a full
  clean rebuild. The tray icon + auto-start are P/Invoke / registry so they
  can't be unit-tested without a Windows session; manual smoke test is the
  verification path.
- **Known limitation:** the tray icon service is only active while the
  configurator or launcher process is running. The "Start with Windows" toggle
  ensures it starts on login. There is no "minimise to tray" behaviour when
  the user closes the configurator window — closing the configurator exits
  the app (intentional, matches v1.4.0 behaviour).

## [1.4.0] — 2026-06-15

### Added

- **Global hotkey for the primary group** — register a system-wide hotkey (default
  `Ctrl+Alt+G`, configurable) that opens a chosen group from anywhere. The new
  `HotkeyBinding` value object (Domain) parses/serialises combos like
  `Ctrl+Shift+F12`; `IHotkeyService` / `HotkeyService` (Infrastructure) wraps the
  Win32 `RegisterHotKey` P/Invoke against a message-only window on a dedicated
  background thread. `LauncherSettings.PrimaryGroupHotkey` + `PrimaryGroupId`
  are persisted in `launcher.json` via a custom `HotkeyBindingJsonConverter`. A
  new **Settings** dialog in the configurator lets the user pick the primary
  group and edit the binding; saving immediately re-registers the hotkey on the
  running service. Falls back to the first group by `CreatedAt` when no
  `PrimaryGroupId` is set. **30 new unit tests** for `HotkeyBinding` (parse,
  format, equality, key validation, round-trip).
- **Search/filter inside the launcher flyout** — type-to-filter textbox at the top
  of `LauncherWindow`. Filter is a case-insensitive `Contains` match on shortcut
  name + target path, reusing the same pattern as the app picker. Esc clears the
  filter and keeps focus on the textbox; a second Esc closes the flyout. Down
  arrow moves focus to the first match; Enter launches the first match. The
  flyout's existing arrow-key navigation continues to work on the filtered set.
  Filter textbox has a blue focus underline matching the shortcut focus ring.
- **Right-click context menu on flyout items** — five items wired to commands on
  the parent `LauncherViewModel`:
  - **Open file location** — `IShellGateway.RevealInFileManager(folder)`
  - **Edit shortcut** — relaunches the configurator exe
  - **Copy path** — Avalonia `TopLevel.Clipboard.SetTextAsync` (extension method)
  - **Properties** — `SHObjectProperties` P/Invoke (`shell32.dll`,
    `SHOP_FILEPATH = 1`) opens the native Windows Properties dialog
  - **Remove from group** — `GroupService.RemoveShortcutAsync`; updates the local
    list without a full reload
  Context menu is styled to match the dark theme.

### Fixed

- **`ShowFlyout.Closing`** now preserves other `LauncherSettings` fields
  (e.g. `PrimaryGroupHotkey`) when persisting the new position, instead of
  overwriting the whole file with a position-only record.

### Notes

- 76 unit tests pass (up from 33 in v1.3.0 / 46 after the v1.3.5 Infrastructure
  bump). 0 warnings, 0 errors on a full clean rebuild.
- The hotkey only works while the app is running. "Start with Windows" (which
  would let the hotkey work in the background) is planned for v1.5.0.

## [1.3.5] — 2026-06-15

### Security

- **`IconExtractor`** now parses `AppxManifest.xml` through a safe `XmlReader` with
  `DtdProcessing.Prohibit`, and disables the `XmlDocument.XmlResolver`. The
  previous direct `new XmlDocument().Load(...)` had default DTD processing enabled.
  Both `ExtractFromStoreApp` and `GetStoreAppName` go through the new
  `LoadManifestSafe` helper.
- **`SingleInstanceService`** named pipe is now per-user
  (`GroupTasker-Launcher-{user-SID}`) instead of global (`GroupTasker-Launcher`).
  Other users on the machine can no longer send fake "show group X" payloads.
- **`Group.ValidateName`** now rejects double-quote characters in addition to
  control characters. Prevents argument-injection into the
  `\"{group.Name}\"` embedded in the per-group `.lnk` arguments by
  `WindowsShortcutService.CreateGroupLauncherLink`.
- **`ConfigPathProvider`** no longer hard-anchors config under the executable's
  directory. New logic: if a `config/` folder already exists next to the exe
  (portable installs), keep using it for backward compatibility; otherwise anchor
  under `%LocalAppData%\{appName}` so installs under `C:\Program Files` and MSIX
  packages work without write-permission failures.

### Added

- **Crash reporting** — `AppDomain.CurrentDomain.UnhandledException` and
  `TaskScheduler.UnobservedTaskException` are now wired in `App.OnFrameworkInitializationCompleted`
  right after the DI container is built. Crashes are logged to the existing
  Serilog file sink at `%APPDATA%\GroupTasker\logs\log-{date}.txt` (7-day rolling
  retention). The task-exception handler marks the exception as observed so a
  fire-and-forget task failure can't tear the app down.
- **Accessibility — flyout keyboard navigation.** `LauncherWindow` shortcuts are
  now focusable. Arrow keys move focus through the 7-column grid (←→ for ±1,
  ↑↓ for ±7), Enter/Space launches the focused shortcut, Esc closes the flyout.
  The window is keyboard-focused on open.
- **Accessibility — focus indicator.** Focused shortcut cells show a 2px
  `#4A9EFF` border.
- **Accessibility — screen reader labels.** Every shortcut cell in the flyout has
  `AutomationProperties.Name` bound to the tooltip text (name + "Live — auto-updating
  app" or "Not found: <path>"). Configurator's symbol-based buttons
  (Move up, Move down, Remove shortcut) now have explicit `AutomationProperties.Name`
  values.

### Tests

- 13 new Infrastructure tests: 9 for `JsonGroupRepository` (round-trip, GetAll
  ordering, empty directory, non-existent ID, delete, corrupt-JSON skip,
  idempotent overwrite) and 4 for `ConfigPathProvider` (portable vs installed
  mode, group path subpath, no-op on construction). Test project now targets
  `net9.0-windows` and references the Infrastructure project.
- 46 total tests pass (up from 33). 0 warnings, 0 errors on full clean rebuild.

## [1.3.0] — 2026-06-13

### Added

- **Live / auto-updating app support** (`ShortcutType.LiveApplication`). A new
  "⚡ Live" button in the configurator lets you add Microsoft Store / MSIX
  apps (Claude Desktop, Codex, etc.) by their `AppUserModelId`. The AUMI is
  stable across updates — Windows resolves it to the current `.exe` under
  `C:\Program Files\WindowsApps\`, so the shortcut survives auto-updates that
  change the version-folder name.
- **Auto-discovery from `shell:AppsFolder`** — the picker shows every installed
  Microsoft Store app (verified: 307 items on a typical install). Source
  label in the picker is "⊞ Store" for these.
- **`IAppActivator` (proper COM `IApplicationActivationManager` interop)**.
  Replaces the broken `Type.InvokeMember` late-binding attempt that threw
  "COM target does not implement IDispatch". Uses the correct IID
  `2e941141-7f97-4756-ba1d-9decde894a3d` verified against `shobjidl_core.h`.
- **`ILiveAppResolver`** — finds the current `.exe` for a stored AUMI by
  preferring running processes under `C:\Program Files\WindowsApps\` over
  AppData installs, then App Paths registry, then PATH. Also scans
  `C:\Program Files\WindowsApps\` for the AUMI's family name when the app is
  not running.
- **Shell `.lnk` icon location extraction** via proper `IShellLinkW` COM
  interop. `ShellLinkInterop.ReadShortcut` now returns the icon location
  string (e.g. `C:\Users\Ganro\AppData\Local\Programs\Ollama\app.ico,0`), which
  is stored in a new `Shortcut.IconSourcePath` field. The icon cache uses
  this as the primary extraction source, so the proper colourful icon is
  pulled for Ollama, Claude, Codex, etc. (which ship separate `.ico` files
  rather than embedding icons in their `.exe`).
- **`.ico` file support** in `IconExtractor` (uses `Icon.ExtractAssociatedIcon`
  on the `.ico` directly).
- **Live application launch path** (`WindowsShortcutService.LaunchLiveApplication`):
  prefers AUMI activation via the proper COM interface, falls back to the
  resolver's `.exe` path, then to a shell-execute last-ditch.
- **Stale-fallback detection**: `IconCacheService` now treats PNGs smaller
  than 300 bytes as stale fallbacks and re-extracts them on the next save.
  Plus, `GroupService.BuildIconsIfDirtyAsync` always runs the extraction
  pipeline (was skipping when an `IconPath` was set, which left 244-byte
  fallbacks in place indefinitely).

### Changed

- **`WindowsShortcutService.ResolveLink`** now categorises empty-target `.lnk`
  files (e.g. Ollama's installer creates shortcuts with no resolvable target
  path in Shell COM) as `ShortcutType.Link` with the `.lnk` itself as the
  launch target. Previously these were miscategorised as `StoreApp`, which
  caused GroupTasker to launch `explorer.exe shell:appsFolder\` (empty AUMI)
  — opening the user's Documents folder instead of the app.
- **Launcher / ⚡ Live picker** UI: app rows now show a source label
  ("📌 Taskbar" / "▶ Running" / "⊞ Store") and a search filter for the
  300+ AppsFolder items.
- **Icon cell background** in the launcher flyout was changed from
  `Transparent` to `#2A2A2A` so missing icons show as visible dark
  cells rather than invisible transparent holes.
- **Self-contained release build** in addition to the existing
  framework-dependent one. The self-contained zip bundles the .NET
  runtime so the app runs on machines without .NET 9 installed
  (≈100 MB vs ≈37 MB for the framework-dependent build).

### Fixed

- **Launch via AUMI for Microsoft Store apps** — `IApplicationActivationManager`
  is a pure IUnknown COM interface, not IDispatch. The previous
  `Type.InvokeMember` call failed silently at runtime; the fix uses proper
  COM interop with the correct IID.
- **Auto-update survival**: storing the AUMI instead of the `.exe` path
  means the shortcut keeps working when the app version-folder changes
  (verified: Codex 26.609.3341.0 → 26.609.4994.0 auto-updated during
  testing and the existing AUMI shortcut kept working).
- **Ollama launch bug**: empty-target `.lnk` files now launch via
  `UseShellExecute = true`, which lets Windows resolve the shortcut
  normally.
- **Missing icons for app `.lnk` shortcuts** (Ollama `app.ico`,
  Claude, Codex). The `IconExtractor` now reads the `IconLocation` from
  the `.lnk` metadata (a separate `.ico` file for many installers) instead
  of always extracting from the resolved `.exe`.

## [1.2.0] — 2026-05-30

### Added

- Shortcut reorder in the group editor: drag-and-drop with visual feedback
  (drag adorner follows cursor, live reordering during drag) plus Move Up/Down
  buttons as an accessible alternative.
- Shortcut reorder in the launcher flyout: long-press (300ms) any shortcut icon
  to enter reorder mode, then drag to rearrange. Short click still launches.
  Changes persist immediately via `GroupService.ReorderShortcutAsync`.
- `GroupService.ReorderShortcutAsync` — atomic reorder-and-save that skips icon
  rebuild (cheap path, same as renames).
- `GroupConfiguratorViewModel` commands: `MoveUpCommand`, `MoveDownCommand`,
  `MoveToIndex` for editor reorder.
- `LauncherViewModel.ReorderCommand` and `GroupId` property for flyout reorder
  persistence.
- 2 new unit tests: `ReorderShortcutAsync_MovesAndSaves` and
  `ReorderShortcutAsync_ThrowsWhenGroupMissing` (32 total, up from 30).

### Changed

- `GroupConfiguratorWindow` shortcut item template: added drag handle column
  (⠿ grip), Move Up/Down buttons, and `PointerPressed/Moved/Released` handlers
  for drag-and-drop.
- `LauncherWindow` shortcut items: added `PointerMoved` and `PointerReleased`
  handlers; named `ItemsControl` for code-behind access.
- `LauncherWindow.OnDeactivated`: now suppresses close during active drag
  operations to prevent the flyout from dismissing mid-reorder.

### Fixed

- Shortcut cells in the launcher flyout now have a dark background (`#2A2A2A`
  in the `ShortcutIcon` style) instead of `Transparent`. When a shortcut icon
  cannot be loaded (null path, missing file, or format error), the cell shows
  a visible dark square rather than appearing as a completely blank/transparent
  box.

## [1.1.0] — 2026-05-13

The Avalonia / Clean-Architecture rewrite of the original TaskbarGroups app.
This is the first numbered release; the prior un-versioned build is treated as 1.0.

### Versioning

- Added `Directory.Build.props` so all five projects ship with a consistent
  `AssemblyVersion`, `FileVersion`, and `InformationalVersion` (1.1.0).
- Added `GroupTasker.UI.AppInfo` — reads the version from the entry assembly
  so the UI string never drifts from the metadata.
- Main window title is now `GroupTasker v1.1.0`, and the header shows a `v1.1.0`
  pill bound to `MainWindowViewModel.VersionLabel`.

### Added

- `IConfigPathProvider` (Domain) and `ConfigPathProvider` (Infrastructure) — single
  source of truth for the on-disk locations the app reads/writes, replacing the
  practice of threading a raw `string configRoot` through every constructor.
- `IShellGateway` (Domain) and `WindowsShellGateway` (Infrastructure) — narrow seam
  for the OS-level "reveal in file manager" action so the Application layer no
  longer references `System.Diagnostics.Process`.
- `PinResult` / `PinOutcome` (Domain) — structured return value from
  `GroupService.PinGroupToTaskbarAsync`. UI now formats the user-facing message;
  the Application layer no longer ships English strings.
- `FileNameSanitizer` (Domain) — shared utility consolidating the two near-identical
  `Sanit*FileName` helpers that used to live in `WindowsShortcutService` and
  `IconCacheService`.
- `Group.ReplaceShortcuts(IEnumerable<Shortcut>)` and `Group.MarkIconCacheClean()`
  — explicit domain methods so the editor view-model no longer mutates the
  internal `Shortcuts` list directly.
- `GroupConfiguratorViewModelFactory` — DI-registered factory so the configurator
  view-model receives its services through constructor injection instead of
  reaching into `App.Services`.
- `ShellLinkInterop.ReadShortcut` — typed `IShellLinkW` reader, replacing the
  `dynamic` / `WScript.Shell` automation path inside `WindowsShortcutService` and
  `IconExtractor`.
- Real test suite: 27 xUnit tests covering domain logic (add / remove / reorder /
  replace shortcuts, name validation), JSON round-tripping (including the
  `ShortcutType.Unknown` default), file-name sanitisation, and `GroupService`
  behaviour (no double-save, dirty-flag gating, pin outcomes).
- `CHANGELOG.md` (this file).

### Changed

- **Architecture.** `GroupTasker.Infrastructure` now references only
  `GroupTasker.Domain` instead of `GroupTasker.Application`, restoring the
  Clean-Architecture dependency direction.
- **Persistence.** `JsonGroupRepository` now writes atomically (temp file +
  `File.Move` overwrite) under a per-group `SemaphoreSlim`, uses
  `File.ReadAllTextAsync` / `File.WriteAllTextAsync`, honours the
  `CancellationToken`, and reports failures through an injected `Action<string,
  Exception>` callback instead of silently swallowing every exception. The
  repository no longer mutates `Group.ModifiedAt` on save (that invariant belongs
  to the entity).
- **Group save flow.** `GroupConfiguratorViewModel.Save` for new groups now does
  one round-trip via `GroupService.CreateGroupAsync(name, shortcuts)`; the old
  "create empty group + mutate list + save again" sequence is gone.
- **Icon cache.** Per-shortcut PNGs are now keyed by `Shortcut.Id` instead of the
  source-path filename, so two `setup.exe` files from different folders no longer
  collide. `BuildCompositeIcon` reuses the cached PNG when available instead of
  re-running Shell/COM extraction on every save.
- **Composite icon writes.** Group icons are now written via temp-file + `Move`
  so a crash mid-write can no longer leave a half-written `GroupIcon.ico`.
- **Save fast-path.** `GroupService.SaveGroupAsync` respects
  `Group.IconCacheDirty` — renames and reorders that don't change shortcut
  contents skip the multi-resolution composite rebuild.
- **Thread safety.** `IconExtractor._packageDirCache` is now a
  `ConcurrentDictionary`. Concurrent calls during a bulk rebuild can no longer
  race the underlying `Dictionary`.
- **Single-instance protocol.** `SingleInstanceService` now uses a
  length-prefixed UTF-8 wire format (4-byte little-endian length + payload)
  instead of line-delimited reads, so group names containing newlines no longer
  truncate. It also implements `IAsyncDisposable` and the dispose path awaits the
  listener task before disposing the CTS.
- **Launcher settings.** `LauncherSettingsService` writes atomically and now
  takes `IConfigPathProvider` rather than a raw path string.
- **Multi-monitor.** `TaskbarHelper` resolves screen bounds from
  `MonitorFromPoint` instead of the primary monitor's `GetSystemMetrics`, so the
  flyout positions correctly when the taskbar lives on a secondary display. The
  return value of `SHAppBarMessage` is now checked.
- **`ViewLocator`.** Replaced the reflection-based template with an explicit
  dictionary lookup, removing the `[RequiresUnreferencedCode]` annotation and
  the silent breakage that combination caused under trimming.
- **`Group.Name`.** Now validated on assignment: trims whitespace, rejects empty
  strings, rejects control characters. Makes the launcher pipe payload
  unambiguous and prevents invalid characters from reaching shortcut filenames.
- **`ShortcutType` ordinals.** `Unknown` is now `0` so a default-constructed
  `Shortcut` or a JSON document missing the field can no longer silently claim
  to be an `Application`.
- **`IIconCacheService.IconSizes`.** Now an `ImmutableArray<int>` rather than a
  publicly mutable `static readonly int[]`.
- **View-model dependencies.** `LauncherViewModel` and `GroupConfiguratorViewModel`
  receive their services through the constructor; the static `App.Services`
  locator is no longer used from production view-models (it's kept `internal`
  only for the designer fall-back path).
- **Icon loading.** `GroupCardViewModel`, `LauncherViewModel`, and
  `LauncherShortcutViewModel` now decode the icon bitmap on a background thread
  and marshal back via `Dispatcher.UIThread.InvokeAsync`, so slow disks no longer
  freeze the UI on startup.
- **`ConfirmDialog`.** Button click handlers are wired in the parameterless
  constructor, so the XAML-instantiated path is no longer inert.
- **`Path.GetDirectoryName(exePath)` fall-back.** `App.OnFrameworkInitialization`
  no longer applies `GetDirectoryName` to `AppContext.BaseDirectory` (which is
  already a directory). The config root now resolves to the executable's own
  folder.
- **`WindowsShortcutService.PinToTaskbar`.** Migrated from `dynamic` to
  `Type.InvokeMember` late binding for `Shell.Application`. Closer to
  AOT-friendly even though the verb path still relies on the legacy automation
  surface.

### Removed

- `MessageBox.Avalonia` package reference — never used by any source file.
- Empty `Models/` `<Folder Include>` from the UI project.
- Empty `UnitTest1.cs` stub from the test project.
- Dead `OnGroupCardPressed` and `OnShortcutPointerMoved` event handlers from the
  views. The drag-and-drop scaffold (`PrepareDrag`, `CreateTempLink`) is left in
  place; it is still callable from `WindowsShortcutService` but no view wires it
  up yet.
- `Design.DataContext` blocks that instantiated view-models with their required
  service constructors set to `null` — the designer threw on every open.

### Fixed

- `JsonGroupRepository` writing a `.tmp` then `Move`-ing it means a crash during
  save can no longer corrupt `group.json`.
- `IconExtractor.ExtractFromFolder` no longer leaks the original `HICON` if the
  cloned `Icon` constructor throws (`DestroyIcon` moved into `finally`).
- `IconExtractor.ExtractAssociatedIcon` results are no longer dereferenced with a
  null-forgiving `!` — null returns now fall back through `SafeExtract`.
- `GroupService.PinGroupToTaskbarAsync` returns `PinOutcome.Failed` with a
  populated `Error` field instead of conflating success and failure into a
  single English status string.
- `MainWindowViewModel.GetMainWindow` returns `Window?` instead of casting
  `null` to `Window` via `null!`.

### Security

- Group names containing control characters are now rejected at the domain
  boundary, removing a class of bug where a crafted name could truncate
  launcher pipe messages or produce surprising shortcut filenames.

### Notes

- Avalonia `12.0.2`, `AvaloniaUI.DiagnosticsSupport 2.2.1`, and the previously
  flagged `MessageBox.Avalonia 12.0.0` were all resolvable from the public NuGet
  feed; no version downgrade was necessary. Only the unused `MessageBox.Avalonia`
  reference was dropped.
- This release leaves one piece of `dynamic`/late-bound COM code path in
  `IconExtractor.ResolveStoreLinkTarget`. It is reached only when resolving a
  store-app `.lnk` whose target the typed `IShellLinkW` interface cannot read,
  and is now isolated behind `Type.InvokeMember` reflection rather than `dynamic`.
