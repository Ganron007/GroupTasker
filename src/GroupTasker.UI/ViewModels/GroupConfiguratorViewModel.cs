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

    public GroupConfiguratorViewModelFactory(
        GroupService groupService,
        IShortcutService shortcutService,
        ITaskbarEnumerator taskbarEnumerator,
        IAppActivator activator,
        ILiveAppResolver liveResolver)
    {
        _groupService = groupService;
        _shortcutService = shortcutService;
        _taskbarEnumerator = taskbarEnumerator;
        _activator = activator;
        _liveResolver = liveResolver;
    }

    public GroupConfiguratorViewModel CreateForNewGroup() =>
        new(null, _groupService, _shortcutService, _taskbarEnumerator);

    public GroupConfiguratorViewModel CreateForExisting(Group existing) =>
        new(existing, _groupService, _shortcutService, _taskbarEnumerator);
}

public partial class GroupConfiguratorViewModel : ViewModelBase
{
    private readonly GroupService _groupService;
    private readonly IShortcutService _shortcutService;
    private readonly ITaskbarEnumerator _taskbarEnumerator;
    private readonly Group? _editingGroup;

    public Group? SavedGroup { get; private set; }

    [ObservableProperty] private string _groupName = "New Group";
    [ObservableProperty] private string? _iconSourcePath;
    [ObservableProperty] private string? _customIconPath;
    [ObservableProperty] private string _accentColor = "";
    [ObservableProperty] private ObservableCollection<ShortcutViewModel> _shortcuts = [];
    [ObservableProperty] private bool _isNewGroup = true;
    [ObservableProperty] private string? _errorMessage;

    /// <summary>
    /// The dialog window hosting this VM. Set by the View on attach so the VM can
    /// close itself without enumerating all open windows looking for the right one.
    /// </summary>
    public Window? HostWindow { get; set; }

    public GroupConfiguratorViewModel(
        Group? existingGroup,
        GroupService groupService,
        IShortcutService shortcutService,
        ITaskbarEnumerator taskbarEnumerator)
    {
        _groupService = groupService;
        _shortcutService = shortcutService;
        _taskbarEnumerator = taskbarEnumerator;

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
                    new Avalonia.Platform.Storage.FilePickerFileType("Applications and Shortcuts")
                    {
                        Patterns = ["*.exe", "*.lnk"]
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("All files")
                    {
                        Patterns = ["*.*"]
                    }
                ]
            };

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
                ErrorMessage = "No running apps or pinned taskbar items found.";
                return;
            }

            var picker = new AppPickerDialog
            {
                DataContext = new AppPickerViewModel(apps)
            };

            // The dialog returns the selected DiscoveredApp (or null if cancelled)
            var selected = await picker.ShowPickerAsync(HostWindow);
            if (selected is null) return;

            var launchKey = selected.Aumi ?? selected.ProcessName ?? selected.ExecutablePath ?? selected.DisplayName;
            // Don't set IconPath here — leave it null so BuildIconsIfDirtyAsync
            // extracts a fresh icon and stores it as a stable cached PNG.
            // Setting it to selected.ExecutablePath would make the icon stale
            // the moment the underlying app updates to a new version folder.
            var shortcut = new Shortcut
            {
                SourcePath = launchKey,
                TargetPath = selected.Aumi,
                Type = ShortcutType.LiveApplication,
                DisplayName = selected.DisplayName,
                IconPath = null
            };
            Shortcuts.Add(new ShortcutViewModel(shortcut));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Discovery failed: {ex.Message}";
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
        var clamped = Math.Clamp(newIndex, 0, Shortcuts.Count - 1);
        if (oldIndex != clamped)
            Shortcuts.Move(oldIndex, clamped);
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
            if (_editingGroup is not null)
            {
                _editingGroup.Name = GroupName;
                _editingGroup.CustomIconPath = string.IsNullOrWhiteSpace(CustomIconPath) ? null : CustomIconPath;
                _editingGroup.AccentColor = string.IsNullOrWhiteSpace(AccentColor) ? null : AccentColor;
                _editingGroup.ReplaceShortcuts(Shortcuts.Select(s => s.DomainShortcut));
                await _groupService.SaveGroupAsync(_editingGroup);
                SavedGroup = _editingGroup;
            }
            else
            {
                SavedGroup = await _groupService.CreateGroupAsync(
                    GroupName,
                    Shortcuts.Select(s => s.DomainShortcut));
                SavedGroup.CustomIconPath = string.IsNullOrWhiteSpace(CustomIconPath) ? null : CustomIconPath;
                SavedGroup.AccentColor = string.IsNullOrWhiteSpace(AccentColor) ? null : AccentColor;
                await _groupService.SaveGroupAsync(SavedGroup);
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
