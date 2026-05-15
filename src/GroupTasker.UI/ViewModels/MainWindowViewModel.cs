using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GroupTasker.Application.Services;
using GroupTasker.UI;

namespace GroupTasker.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly GroupService _groupService;
    private readonly GroupConfiguratorViewModelFactory _configuratorFactory;

    [ObservableProperty]
    private ObservableCollection<GroupCardViewModel> _groups = [];

    /// <summary>"v1.1.0" — bound by the header and the window title.</summary>
    public string VersionLabel => AppInfo.VersionLabel;

    /// <summary>"GroupTasker v1.1.0".</summary>
    public string TitleWithVersion => AppInfo.TitleWithVersion;

    public MainWindowViewModel(GroupService groupService, GroupConfiguratorViewModelFactory configuratorFactory)
    {
        _groupService = groupService;
        _configuratorFactory = configuratorFactory;
    }

    [RelayCommand]
    private async Task LoadGroups()
    {
        var all = await _groupService.GetAllGroupsAsync();
        Groups = new ObservableCollection<GroupCardViewModel>(
            all.Select(g => new GroupCardViewModel(g, _groupService)));
    }

    [RelayCommand]
    private async Task AddGroup()
    {
        var owner = GetMainWindow();
        if (owner is null) return;

        var configVm = _configuratorFactory.CreateForNewGroup();
        var window = new Views.GroupConfiguratorWindow { DataContext = configVm };
        var result = await window.ShowDialog<bool>(owner);
        if (result && configVm.SavedGroup is not null)
            Groups.Add(new GroupCardViewModel(configVm.SavedGroup, _groupService));
    }

    [RelayCommand]
    private async Task EditGroup(GroupCardViewModel? card)
    {
        if (card is null) return;
        var owner = GetMainWindow();
        if (owner is null) return;

        var configVm = _configuratorFactory.CreateForExisting(card.DomainGroup);
        var window = new Views.GroupConfiguratorWindow { DataContext = configVm };
        await window.ShowDialog<bool>(owner);
        card.Refresh();
    }

    [RelayCommand]
    private async Task DeleteGroup(GroupCardViewModel? card)
    {
        if (card is null) return;
        var owner = GetMainWindow();
        if (owner is null) return;

        var dialog = new Views.ConfirmDialog(
            "Delete Group",
            $"Are you sure you want to delete '{card.Name}'?");

        var result = await dialog.ShowDialog<bool>(owner);
        if (result)
        {
            await _groupService.DeleteGroupAsync(card.DomainGroup.Id);
            Groups.Remove(card);
        }
    }

    [RelayCommand]
    private async Task PinToTaskbar(GroupCardViewModel? card)
    {
        if (card is null) return;
        await card.PinToTaskbarAsync();
    }

    public async Task RefreshAll() => await LoadGroups();

    private static Window? GetMainWindow() =>
        Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}
