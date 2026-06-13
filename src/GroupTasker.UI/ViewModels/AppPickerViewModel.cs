using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GroupTasker.Domain.Entities;
using GroupTasker.Domain.Interfaces;

namespace GroupTasker.UI.ViewModels;

/// <summary>
/// Backs the "Add from running apps" picker dialog. Shows discovered apps
/// (running windows + pinned taskbar items) and lets the user pick one.
/// </summary>
public partial class AppPickerViewModel : ViewModelBase
{
    private readonly List<DiscoveredAppViewModel> _allApps;

    [ObservableProperty] private ObservableCollection<DiscoveredAppViewModel> _apps = [];
    [ObservableProperty] private DiscoveredAppViewModel? _selectedApp;
    [ObservableProperty] private string _filter = "";

    /// <summary>Result of the dialog — set by AddCommand, consumed by the caller.</summary>
    public DiscoveredApp? DialogResult { get; private set; }

    public AppPickerViewModel(IReadOnlyList<DiscoveredApp> apps)
    {
        _allApps = apps.Select(a => new DiscoveredAppViewModel(a)).ToList();
        Apps = new ObservableCollection<DiscoveredAppViewModel>(_allApps);
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    /// <summary>Apply the current filter to the Apps collection.</summary>
    private void ApplyFilter()
    {
        var filter = Filter?.Trim() ?? "";
        Apps.Clear();
        foreach (var app in _allApps)
        {
            var matches = string.IsNullOrEmpty(filter)
                || app.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (app.ProcessName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
            if (matches) Apps.Add(app);
        }
    }

    [RelayCommand]
    private void Add()
    {
        if (SelectedApp is null) return;
        DialogResult = SelectedApp.Source;
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = null;
        CloseRequested?.Invoke(this, false);
    }

    public event EventHandler<bool>? CloseRequested;
}

/// <summary>VM wrapper for the picker list. Carries the source <see cref="DiscoveredApp"/>.</summary>
public partial class DiscoveredAppViewModel : ViewModelBase
{
    public DiscoveredApp Source { get; }

    public string DisplayName => Source.DisplayName;
    public string? ProcessName => Source.ProcessName;
    public string? ExecutablePath => Source.ExecutablePath;
    public string SourceLabel => Source.Source switch
    {
        DiscoveredAppSource.PinnedTaskbar => "📌 Taskbar",
        DiscoveredAppSource.RunningWindow => "▶ Running",
        DiscoveredAppSource.StoreApp => "⊞ Store",
        _ => ""
    };

    public DiscoveredAppViewModel(DiscoveredApp app)
    {
        Source = app;
    }
}
