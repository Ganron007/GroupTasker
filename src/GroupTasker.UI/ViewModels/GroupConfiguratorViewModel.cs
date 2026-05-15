using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GroupTasker.Application.Services;
using GroupTasker.Domain.Entities;
using GroupTasker.Domain.Interfaces;

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

    public GroupConfiguratorViewModelFactory(GroupService groupService, IShortcutService shortcutService)
    {
        _groupService = groupService;
        _shortcutService = shortcutService;
    }

    public GroupConfiguratorViewModel CreateForNewGroup() =>
        new(null, _groupService, _shortcutService);

    public GroupConfiguratorViewModel CreateForExisting(Group existing) =>
        new(existing, _groupService, _shortcutService);
}

public partial class GroupConfiguratorViewModel : ViewModelBase
{
    private readonly GroupService _groupService;
    private readonly IShortcutService _shortcutService;
    private readonly Group? _editingGroup;

    public Group? SavedGroup { get; private set; }

    [ObservableProperty] private string _groupName = "New Group";
    [ObservableProperty] private string? _iconSourcePath;
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
        IShortcutService shortcutService)
    {
        _groupService = groupService;
        _shortcutService = shortcutService;

        if (existingGroup is not null)
        {
            _editingGroup = existingGroup;
            GroupName = existingGroup.Name;
            IconSourcePath = existingGroup.IconPath;
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

    [RelayCommand]
    private void RemoveShortcut(ShortcutViewModel? shortcut)
    {
        if (shortcut is not null)
            Shortcuts.Remove(shortcut);
    }

    [RelayCommand]
    private async Task Save()
    {
        try
        {
            if (_editingGroup is not null)
            {
                _editingGroup.Name = GroupName;
                _editingGroup.ReplaceShortcuts(Shortcuts.Select(s => s.DomainShortcut));
                await _groupService.SaveGroupAsync(_editingGroup);
                SavedGroup = _editingGroup;
            }
            else
            {
                // Single save: CreateGroupAsync builds icons + persists in one round-trip,
                // replacing the old "create empty, mutate list, save again" double-write.
                SavedGroup = await _groupService.CreateGroupAsync(
                    GroupName,
                    Shortcuts.Select(s => s.DomainShortcut));
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
            _ => "File"
        };
    }
}
