using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    private readonly string _groupName;
    private Group? _loadedGroup;

    [ObservableProperty] private string _title = "Loading\u2026";
    [ObservableProperty] private ObservableCollection<LauncherShortcutViewModel> _shortcuts = [];
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _groupIcon;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _isReorderMode;

    public Guid GroupId => _loadedGroup?.Id ?? Guid.Empty;

    public LauncherViewModel(string groupName, GroupService groupService, IShortcutService shortcutService)
    {
        _groupName = groupName;
        _groupService = groupService;
        _shortcutService = shortcutService;
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

            Shortcuts = new ObservableCollection<LauncherShortcutViewModel>(
                group.Shortcuts
                    .Where(s => s.IsVisible)
                    .OrderBy(s => s.SortOrder)
                    .Select(s => new LauncherShortcutViewModel(s, _shortcutService)));

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
