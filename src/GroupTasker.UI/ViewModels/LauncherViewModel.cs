using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GroupTasker.Application.Services;
using GroupTasker.Domain.Entities;
using GroupTasker.Domain.Interfaces;

namespace GroupTasker.UI.ViewModels;

public partial class LauncherViewModel : ViewModelBase
{
    private readonly GroupService _groupService;
    private readonly IShortcutService _shortcutService;
    private readonly IShellGateway _shell;
    private readonly string _groupName;
    private Group? _loadedGroup;
    private List<LauncherShortcutViewModel> _allShortcuts = [];

    [ObservableProperty] private string _title = "Loading\u2026";
    [ObservableProperty] private ObservableCollection<LauncherShortcutViewModel> _shortcuts = [];
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _groupIcon;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _isReorderMode;
    [ObservableProperty] private string _filter = "";

    /// <summary>Raised after the <see cref="Shortcuts"/> collection is rebuilt by the filter.
    /// The view listens to this to move keyboard focus to the first visible shortcut.</summary>
    public event EventHandler? ShortcutsFiltered;

    public Guid GroupId => _loadedGroup?.Id ?? Guid.Empty;

    public LauncherViewModel(
        string groupName,
        GroupService groupService,
        IShortcutService shortcutService,
        IShellGateway shell)
    {
        _groupName = groupName;
        _groupService = groupService;
        _shortcutService = shortcutService;
        _shell = shell;
        _ = LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        try
        {
            Title = _groupName;
            var groups = await _groupService.GetAllGroupsAsync();
            var group = groups.FirstOrDefault(g =>
                g.Name.Equals(_groupName, StringComparison.OrdinalIgnoreCase));

            if (group is null)
            {
                IsEmpty = true;
                Title = $"{_groupName} (not found)";
                return;
            }

            _loadedGroup = group;

            _allShortcuts = group.Shortcuts
                .Where(s => s.IsVisible)
                .OrderBy(s => s.SortOrder)
                .Select(s => new LauncherShortcutViewModel(s, _shortcutService))
                .ToList();

            ApplyFilter();

            if (group.IconPath is not null && File.Exists(group.IconPath))
            {
                try
                {
                    var bmp = await Task.Run(() => new Avalonia.Media.Imaging.Bitmap(group.IconPath));
                    await Dispatcher.UIThread.InvokeAsync(() => GroupIcon = bmp);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Group icon load failed: {ex.Message}");
                }
            }

            IsEmpty = Shortcuts.Count == 0;
        }
        catch (Exception ex)
        {
            Title = $"Error loading group: {ex.Message}";
            IsEmpty = true;
        }
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    /// <summary>Re-apply the current <see cref="Filter"/> to <see cref="Shortcuts"/>. Called on load and on filter change.</summary>
    private void ApplyFilter()
    {
        var filter = Filter?.Trim() ?? "";
        var matched = string.IsNullOrEmpty(filter)
            ? _allShortcuts
            : _allShortcuts.Where(s =>
                s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (s.DomainShortcut.TargetPath?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false));

        Shortcuts = new ObservableCollection<LauncherShortcutViewModel>(matched);
        IsEmpty = Shortcuts.Count == 0;
        ShortcutsFiltered?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ClearFilter() => Filter = "";

    [RelayCommand]
    private void Close()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow?.Close();
        }
    }

    [RelayCommand]
    private void OpenConfigurator()
    {
        var exePath = Environment.ProcessPath;
        if (exePath is not null)
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath) { UseShellExecute = true });
        Close();
    }

    [RelayCommand]
    private async Task ReorderAsync(object? parameter)
    {
        if (parameter is not (int oldIndex, int newIndex)) return;
        if (_loadedGroup is null) return;
        if (oldIndex < 0 || oldIndex >= Shortcuts.Count) return;
        if (newIndex < 0 || newIndex >= Shortcuts.Count) return;

        var shortcut = Shortcuts[oldIndex];
        Shortcuts.Move(oldIndex, newIndex);

        try
        {
            await _groupService.ReorderShortcutAsync(
                _loadedGroup.Id,
                shortcut.DomainShortcut.Id,
                newIndex);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reorder save failed: {ex.Message}");
        }
    }

    // --- Context menu commands ---

    [RelayCommand]
    private void OpenFileLocation(LauncherShortcutViewModel? shortcut)
    {
        if (shortcut is null) return;
        var path = shortcut.DomainShortcut.TargetPath ?? shortcut.DomainShortcut.SourcePath;
        if (string.IsNullOrEmpty(path)) return;
        var folder = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder) && System.IO.Directory.Exists(folder))
            _shell.RevealInFileManager(folder);
    }

    [RelayCommand]
    private void EditShortcut(LauncherShortcutViewModel? shortcut)
    {
        if (shortcut is null) return;
        // Open the configurator (this same exe with no args). The configurator will
        // load the group; the user can then edit the shortcut from there.
        var exePath = Environment.ProcessPath;
        if (exePath is not null)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath) { UseShellExecute = true });
            Close();
        }
    }

    [RelayCommand]
    private async Task RemoveFromGroupAsync(LauncherShortcutViewModel? shortcut)
    {
        if (shortcut is null || _loadedGroup is null) return;
        try
        {
            await _groupService.RemoveShortcutAsync(_loadedGroup.Id, shortcut.DomainShortcut.Id);
            // Update the local list without reloading from disk.
            _allShortcuts.Remove(shortcut);
            ApplyFilter();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Remove shortcut failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CopyPathAsync(LauncherShortcutViewModel? shortcut)
    {
        if (shortcut is null) return;
        var path = shortcut.DomainShortcut.TargetPath ?? shortcut.DomainShortcut.SourcePath;
        if (string.IsNullOrEmpty(path)) return;

        var topLevel = Avalonia.Application.Current?.ApplicationLifetime
            is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lt
                ? lt.MainWindow
                : null;
        if (topLevel?.Clipboard is null) return;
        await topLevel.Clipboard.SetTextAsync(path);
    }

    [RelayCommand]
    private void ShowProperties(LauncherShortcutViewModel? shortcut)
    {
        if (shortcut is null) return;
        var path = shortcut.DomainShortcut.TargetPath ?? shortcut.DomainShortcut.SourcePath;
        if (string.IsNullOrEmpty(path)) return;
        ShellInterop.ShowObjectProperties(path);
    }
}

public partial class LauncherShortcutViewModel : ViewModelBase
{
    private readonly IShortcutService _shortcutService;

    public Shortcut DomainShortcut { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _icon;

    public bool IsDead { get; private set; }
    public double IconOpacity => IsDead ? 0.35 : 1.0;

    public string TooltipText
    {
        get
        {
            if (DomainShortcut.Type == ShortcutType.LiveApplication)
                return $"{Name}\n(Live — auto-updating app)";
            if (!IsDead) return Name;
            var path = DomainShortcut.TargetPath ?? DomainShortcut.SourcePath;
            return $"Not found: {path}";
        }
    }

    public LauncherShortcutViewModel(Shortcut shortcut, IShortcutService shortcutService)
    {
        DomainShortcut = shortcut;
        _shortcutService = shortcutService;
        _name = shortcut.DisplayName;

        CheckIfDead();
        _ = LoadIconAsync();
    }

    private void CheckIfDead()
    {
        var path = DomainShortcut.TargetPath ?? DomainShortcut.SourcePath;
        if (string.IsNullOrEmpty(path))
        {
            IsDead = true;
            return;
        }

        IsDead = DomainShortcut.Type switch
        {
            ShortcutType.Folder => !Directory.Exists(path),
            ShortcutType.StoreApp => false,
            ShortcutType.LiveApplication => false,
            _ => !File.Exists(path)
        };
    }

    [RelayCommand]
    private void Launch()
    {
        if (!IsDead)
            _shortcutService.Launch(DomainShortcut);
    }

    private async Task LoadIconAsync()
    {
        if (IsDead) return;

        var iconPath = DomainShortcut.IconPath;
        if (iconPath is null || !File.Exists(iconPath)) return;

        try
        {
            var bmp = await Task.Run(() => new Avalonia.Media.Imaging.Bitmap(iconPath));
            await Dispatcher.UIThread.InvokeAsync(() => Icon = bmp);
        }
        catch
        {
            // Icon failed to load — Icon stays null, XAML background shows placeholder.
        }
    }
}
