using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GroupTasker.Application.Services;
using GroupTasker.Domain.Entities;
using GroupTasker.Domain.ValueObjects;

namespace GroupTasker.UI.ViewModels;

public partial class GroupCardViewModel : ViewModelBase
{
    private readonly GroupService _groupService;

    public Group DomainGroup { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private int _shortcutCount;
    [ObservableProperty] private Avalonia.Media.Imaging.Bitmap? _icon;
    [ObservableProperty] private string? _pinStatus;

    public GroupCardViewModel(Group group, GroupService groupService)
    {
        DomainGroup = group;
        _groupService = groupService;
        _name = group.Name;
        _shortcutCount = group.Shortcuts.Count;

        _ = LoadIconAsync();
    }

    /// <summary>
    /// Decode the icon off the UI thread. The synchronous <c>new Bitmap(path)</c> the
    /// original code used would block the UI on slow disks / network paths.
    /// </summary>
    public async Task LoadIconAsync()
    {
        var path = DomainGroup.IconPath;
        if (path is null || !File.Exists(path))
            return;

        try
        {
            var bmp = await Task.Run(() => new Avalonia.Media.Imaging.Bitmap(path));
            await Dispatcher.UIThread.InvokeAsync(() => Icon = bmp);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Icon load failed for {path}: {ex.Message}");
        }
    }

    public async Task PinToTaskbarAsync()
    {
        PinStatus = "Pinning…";
        var result = await _groupService.PinGroupToTaskbarAsync(DomainGroup.Id);
        PinStatus = result.Outcome switch
        {
            PinOutcome.Pinned => $"'{DomainGroup.Name}' pinned to taskbar!",
            PinOutcome.ShortcutCreatedManualPinRequired =>
                $"Shortcut created: {Path.GetFileName(result.LauncherPath)} — drag it to your taskbar to pin.",
            _ => $"Failed: {result.Error ?? "unknown error"}"
        };
    }

    public void Refresh()
    {
        Name = DomainGroup.Name;
        ShortcutCount = DomainGroup.Shortcuts.Count;
        _ = LoadIconAsync();
    }
}
