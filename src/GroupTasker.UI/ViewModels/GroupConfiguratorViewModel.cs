using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GroupTasker.Application.Services;
using GroupTasker.Domain.Entities;
using GroupTasker.Domain.Interfaces;
using GroupTasker.UI.Views;

namespace GroupTasker.UI.ViewModels;

/// <summary>
/// Factory for <see cref="GroupConfiguratorViewModel"/>. Registered in DI so the
/// view model can be constructed with all its services without the VM having to
/// reach into a static service locator.
/// </summary>
public sealed class GroupConfiguratorViewModelFactory
{
    private readonly GroupService _groupService;
    private readonly IShortcutService _shortcutService;
    private readonly ITaskbarEnumerator _taskbarEnumerator;
    private readonly IAppActivator _activator;
    private readonly ILiveAppResolver _liveResolver;
    private readonly IShellGateway _shell;
    private readonly ILogger _logger;

    public GroupConfiguratorViewModelFactory(
        GroupService groupService,
        IShortcutService shortcutService,
        ITaskbarEnumerator taskbarEnumerator,
        IAppActivator activator,
        ILiveAppResolver liveResolver,
        IShellGateway shell,
        ILogger logger)
    {
        _groupService = groupService;
        _shortcutService = shortcutService;
        _taskbarEnumerator = taskbarEnumerator;
        _activator = activator;
        _liveResolver = liveResolver;
        _shell = shell;
        _logger = logger;
    }

    public GroupConfiguratorViewModel CreateForNewGroup() =>
        new(null, _groupService, _shortcutService, _taskbarEnumerator, _shell, _logger);

    public GroupConfiguratorViewModel CreateForExisting(Group existing) =>
        new(existing, _groupService, _shortcutService, _taskbarEnumerator, _shell, _logger);
}

public partial class GroupConfiguratorViewModel : ViewModelBase
{
    private readonly GroupService _groupService;
    private readonly IShortcutService _shortcutService;
    private readonly ITaskbarEnumerator _taskbarEnumerator;
    private readonly IShellGateway _shell;
    private readonly ILogger _logger;
    private readonly Group? _editingGroup;

    public Group? SavedGroup { get; private set; }

    [ObservableProperty] private string _groupName = "New Group";
    [ObservableProperty] private string? _iconSourcePath;
    [ObservableProperty] private string? _customIconPath;
    [ObservableProperty] private string _accentColor = "";
    [ObservableProperty] private ObservableCollection<ShortcutViewModel> _shortcuts = [];
    [ObservableProperty] private bool _isNewGroup = true;
    [ObservableProperty] private string? _errorMessage;

    /// <summary>Free-form input for "Add by path/URL": any file path, protocol URI, or Store app ID.</summary>
    [ObservableProperty] private string _manualPath = "";

    /// <summary>
    /// The dialog window hosting this VM. Set by the View on attach so the VM can
    /// close itself without enumerating all open windows looking for the right one.
    /// </summary>
    public Window? HostWindow { get; set; }

    public GroupConfiguratorViewModel(
        Group? existingGroup,
        GroupService groupService,
        IShortcutService shortcutService,
        ITaskbarEnumerator taskbarEnumerator,
        IShellGateway shell,
        ILogger logger)
    {
        _groupService = groupService;
        _shortcutService = shortcutService;
        _taskbarEnumerator = taskbarEnumerator;
        _shell = shell;
        _logger = logger;

        if (existingGroup is not null)
        {
            _editingGroup = existingGroup;
            GroupName = existingGroup.Name;
            IconSourcePath = existingGroup.IconPath;
            CustomIconPath = existingGroup.CustomIconPath;
            AccentColor = existingGroup.AccentColor ?? "";
            IsNewGroup = false;

            Shortcuts = new ObservableCollection<ShortcutViewModel>(
                existingGroup.Shortcuts.Select(s => new ShortcutViewModel(s)));
        }
    }

    [RelayCommand]
    private async Task AddShortcut()
    {
        if (HostWindow is null) return;

        try
        {
            var topLevel = TopLevel.GetTopLevel(HostWindow);
            if (topLevel is null) return;

            var options = new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Select applications or shortcuts (multi-select: hold Ctrl or Shift)",
                AllowMultiple = true,
                FileTypeFilter =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType("Apps, Shortcuts, Scripts")
                    {
                        // *.url = internet shortcuts (steam://rungameid/…, com.epicgames.launcher://…);
                        // *.appref-ms = ClickOnce shortcuts; *.website = pinned sites; scripts too.
                        Patterns = ["*.exe", "*.lnk", "*.url", "*.appref-ms", "*.website", "*.bat", "*.cmd", "*.ps1", "*.vbs", "*.msc"]
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("All files")
                    {
                        Patterns = ["*.*"]
                    }
                ]
            };

            // Open the picker on the Desktop — that's where launchers and game
            // shortcuts live, and users expect to see them immediately.
            try
            {
                options.SuggestedStartLocation =
                    await topLevel.StorageProvider.TryGetWellKnownFolderAsync(
                        Avalonia.Platform.Storage.WellKnownFolder.Desktop);
            }
            catch
            {
                // Non-fatal: the picker falls back to its default location.
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);
            if (files is null || files.Count == 0) return;

            foreach (var file in files)
            {
                try
                {
                    var resolved = _shortcutService.Resolve(file.Path.LocalPath);
                    Shortcuts.Add(new ShortcutViewModel(resolved));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Skipping unresolvable file {file.Path}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"File picker error: {ex.Message}";
        }
    }

    /// <summary>
    /// Open the "Add from running apps" picker. Discovers pinned taskbar items
    /// and currently-running windows so the user can include auto-updating
    /// apps (Claude Desktop, Codex, etc.) that don't have stable desktop
    /// shortcuts. Selected apps are added as <see cref="ShortcutType.LiveApplication"/>
    /// which resolves the current .exe at launch time.
    /// </summary>
    [RelayCommand]
    private async Task AddRunningApp()
    {
        if (HostWindow is null) return;

        try
        {
            var apps = _taskbarEnumerator.Enumerate();
            if (apps.Count == 0)
            {
                ErrorMessage = "No running apps, pinned taskbar items, or installed games found.";
                return;
            }

            var picker = new AppPickerDialog
            {
                DataContext = new AppPickerViewModel(apps, _shell, _logger)
            };

            // The dialog returns the selected DiscoveredApps (or null if cancelled).
            // Multi-select is supported: Ctrl+click / Shift+click in the list.
            var results = await picker.ShowPickerAsync(HostWindow);
            if (results is null || results.Count == 0) return;

            foreach (var selected in results)
            {
                Shortcuts.Add(new ShortcutViewModel(BuildShortcutFromDiscoveredApp(selected)));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Discovery failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Convert a discovered app into a domain shortcut:
    /// <list type="bullet">
    /// <item>.lnk-backed entries (pinned or Start Menu / desktop launcher games)
    /// become <see cref="ShortcutType.Link"/> launched via the .lnk, so the
    /// launcher's arguments open the game, not the launcher;</item>
    /// <item>exe-backed game-library entries become <see cref="ShortcutType.Application"/>;</item>
    /// <item>everything else becomes <see cref="ShortcutType.LiveApplication"/>.</item>
    /// </list>
    /// IconPath is deliberately left null so <c>BuildIconsIfDirtyAsync</c> extracts
    /// a fresh icon and caches it as a stable PNG.
    /// </summary>
    private static Shortcut BuildShortcutFromDiscoveredApp(DiscoveredApp selected)
    {
        var launchKey = selected.Aumi ?? selected.ProcessName ?? selected.ExecutablePath ?? selected.DisplayName;

        if (!string.IsNullOrEmpty(selected.LnkPath) && string.IsNullOrEmpty(selected.Aumi))
        {
            return new Shortcut
            {
                SourcePath = selected.LnkPath,
                TargetPath = selected.LnkPath,
                Type = ShortcutType.Link,
                DisplayName = selected.DisplayName,
                IconSourcePath = selected.LnkPath,
                IconPath = null
            };
        }

        if (selected.Source == DiscoveredAppSource.GameLibrary &&
            !string.IsNullOrEmpty(selected.ExecutablePath))
        {
            return new Shortcut
            {
                SourcePath = selected.ExecutablePath,
                TargetPath = selected.ExecutablePath,
                Type = ShortcutType.Application,
                DisplayName = selected.DisplayName,
                IconPath = null
            };
        }

        return new Shortcut
        {
            SourcePath = launchKey,
            TargetPath = selected.Aumi,
            Type = ShortcutType.LiveApplication,
            DisplayName = selected.DisplayName,
            IconPath = null
        };
    }

    /// <summary>
    /// Add a shortcut from free-form input: any file path (exe, lnk, url,
    /// script, document), protocol URI (steam://rungameid/730, ms-settings:…),
    /// or Store app ID. <see cref="IShortcutService.Resolve"/> classifies it.
    /// </summary>
    [RelayCommand]
    private void AddManualShortcut()
    {
        var input = ManualPath?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            ErrorMessage = "Enter a path, protocol URI (e.g. steam://rungameid/730), or Store app ID first.";
            return;
        }

        try
        {
            var resolved = _shortcutService.Resolve(input);
            Shortcuts.Add(new ShortcutViewModel(resolved));
            ManualPath = "";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't add '{input}': {ex.Message}";
        }
    }

    [RelayCommand]
    private void RemoveShortcut(ShortcutViewModel? shortcut)
    {
        if (shortcut is not null)
            Shortcuts.Remove(shortcut);
    }

    [RelayCommand]
    private void MoveUp(ShortcutViewModel? shortcut)
    {
        if (shortcut is null) return;
        var index = Shortcuts.IndexOf(shortcut);
        if (index > 0)
            Shortcuts.Move(index, index - 1);
    }

    [RelayCommand]
    private void MoveDown(ShortcutViewModel? shortcut)
    {
        if (shortcut is null) return;
        var index = Shortcuts.IndexOf(shortcut);
        if (index >= 0 && index < Shortcuts.Count - 1)
            Shortcuts.Move(index, index + 1);
    }

    public void MoveToIndex(ShortcutViewModel shortcut, int newIndex)
    {
        var oldIndex = Shortcuts.IndexOf(shortcut);
        if (oldIndex < 0) return;

        // newIndex is the slot the drop-indicator pointed at: insert *before* the item
        // currently there, or Count = drop after the last item. ObservableCollection.Move
        // removes the item first, which shifts every later index down by one — so when
        // dragging DOWN we subtract one, otherwise the item lands one slot too low and
        // no longer matches the indicator. Dragging up needs no adjustment.
        var target = oldIndex < newIndex ? newIndex - 1 : newIndex;
        target = Math.Clamp(target, 0, Shortcuts.Count - 1);
        if (oldIndex != target)
            Shortcuts.Move(oldIndex, target);
    }

    [RelayCommand]
    private async Task PickCustomIcon()
    {
        if (HostWindow is null) return;
        var topLevel = TopLevel.GetTopLevel(HostWindow);
        if (topLevel is null) return;

        var options = new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Choose a custom group icon",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new Avalonia.Platform.Storage.FilePickerFileType("Icons")
                {
                    Patterns = ["*.ico", "*.png", "*.bmp", "*.jpg", "*.jpeg"]
                },
                new Avalonia.Platform.Storage.FilePickerFileType("All files")
                {
                    Patterns = ["*.*"]
                }
            ]
        };

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(options);
        if (files is not null && files.Count > 0)
            CustomIconPath = files[0].Path.LocalPath;
    }

    [RelayCommand]
    private void ClearCustomIcon() => CustomIconPath = null;

    [RelayCommand]
    private async Task Save()
    {
        try
        {
            // Normalise custom-icon / accent inputs.
            var customIcon = string.IsNullOrWhiteSpace(CustomIconPath) ? null : CustomIconPath;
            var accent = string.IsNullOrWhiteSpace(AccentColor) ? null : AccentColor;

            if (_editingGroup is not null)
            {
                _editingGroup.Name = GroupName;
                _editingGroup.CustomIconPath = customIcon;
                _editingGroup.AccentColor = accent;
                _editingGroup.ReplaceShortcuts(Shortcuts.Select(s => s.DomainShortcut));
                await _groupService.SaveGroupAsync(_editingGroup);
                SavedGroup = _editingGroup;
            }
            else
            {
                // Single save: build the entity with the custom icon + accent set BEFORE
                // CreateGroupAsync runs, so the first (and only) disk write is correct.
                var newGroup = new Domain.Entities.Group
                {
                    Name = GroupName,
                    CustomIconPath = customIcon,
                    AccentColor = accent
                };
                foreach (var s in Shortcuts)
                    newGroup.AddShortcut(s.DomainShortcut);
                await _groupService.SaveNewGroupAsync(newGroup);
                SavedGroup = newGroup;
            }

            CloseWindow(true);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Save failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void Cancel() => CloseWindow(false);

    private void CloseWindow(bool result) => HostWindow?.Close(result);
}

/// <summary>Thin VM wrapper for a domain shortcut in the configurator list.</summary>
public partial class ShortcutViewModel : ViewModelBase
{
    public Shortcut DomainShortcut { get; }

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string _typeLabel;

    public ShortcutViewModel(Shortcut shortcut)
    {
        DomainShortcut = shortcut;
        _displayName = shortcut.DisplayName;
        _typeLabel = shortcut.Type switch
        {
            ShortcutType.Application => "App",
            ShortcutType.Folder => "Folder",
            ShortcutType.StoreApp => "Store",
            ShortcutType.Link => "Shortcut",
            ShortcutType.LiveApplication => "Live",
            _ => "File"
        };
    }
}
