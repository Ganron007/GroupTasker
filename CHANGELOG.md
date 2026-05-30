# Changelog

All notable changes to GroupTasker are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

The canonical version lives in `Directory.Build.props` — bump `<VersionPrefix>`
there and add a new section at the top of this file for each release.

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
