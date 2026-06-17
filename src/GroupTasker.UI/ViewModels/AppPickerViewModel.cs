using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GroupTasker.Domain.Entities;
using GroupTasker.Domain.Interfaces;
using GroupTasker.Domain.Logging;

namespace GroupTasker.UI.ViewModels;

/// <summary>
/// Backs the "Add from running apps" picker dialog. Shows discovered apps
/// (running windows + pinned taskbar items) and lets the user pick one.
/// </summary>
public partial class AppPickerViewModel : ViewModelBase
{
    private readonly List<DiscoveredAppViewModel> _allApps;
    private readonly IShellGateway _shell;
    private readonly ILogger _logger;

    [ObservableProperty] private ObservableCollection<DiscoveredAppViewModel> _apps = [];
    [ObservableProperty] private DiscoveredAppViewModel? _selectedApp;
    [ObservableProperty] private string _filter = "";

    /// <summary>Result of the dialog — set by AddCommand, consumed by the caller.</summary>
    public DiscoveredApp? DialogResult { get; private set; }

    public AppPickerViewModel(IReadOnlyList<DiscoveredApp> apps, IShellGateway shell, ILogger logger)
    {
        _allApps = apps.Select(a => new DiscoveredAppViewModel(a)).ToList();
        Apps = new ObservableCollection<DiscoveredAppViewModel>(_allApps);
        _shell = shell;
        _logger = logger;
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    /// <summary>Apply the current filter to the Apps collection (word-prefix match).</summary>
    private void ApplyFilter()
    {
        var filter = Filter?.Trim() ?? "";
        Apps.Clear();
        foreach (var app in _allApps)
        {
            if (string.IsNullOrEmpty(filter) || MatchesFilter(app, filter))
                Apps.Add(app);
        }
    }

    private static bool MatchesFilter(DiscoveredAppViewModel app, string filter)
    {
        var words = filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var name = app.DisplayName ?? "";
        var proc = app.ProcessName ?? "";
        var path = app.ExecutablePath ?? "";
        foreach (var w in words)
        {
            if (!HasWordWithPrefix(name, w) && !HasWordWithPrefix(proc, w) && !HasWordWithPrefix(path, w))
                return false;
        }
        return true;
    }

    private static bool HasWordWithPrefix(string text, string prefix) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Any(word => word.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

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

    /// <summary>Close the running instance of the given app (only valid for Running apps).</summary>
    [RelayCommand]
    private void CloseApp(DiscoveredAppViewModel? app)
    {
        if (app is null) return;
        var name = app.ProcessName ?? app.DisplayName;
        try
        {
            if (!string.IsNullOrEmpty(app.ProcessName))
            {
                foreach (var p in Process.GetProcessesByName(app.ProcessName))
                {
                    try { p.CloseMainWindow(); if (!p.WaitForExit(500)) p.Kill(); }
                    catch { /* ignore individual process close failures */ }
                }
            }
            else if (app.Source.WindowHandle != IntPtr.Zero)
            {
                // Win32 fallback: post WM_CLOSE to the window.
                PostMessage(app.Source.WindowHandle, 0x0010 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
            }
            _logger.Information("Closed app {App} from picker", name);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to close app {App}", name);
        }
    }

    /// <summary>Open the folder containing the app's exe in Explorer.</summary>
    [RelayCommand]
    private void OpenFileLocation(DiscoveredAppViewModel? app)
    {
        if (app is null) return;
        var path = app.ExecutablePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            _shell.RevealInFileManager(folder);
    }

    public event EventHandler<bool>? CloseRequested;

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
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

    /// <summary>True if this app is currently running and can be closed.</summary>
    public bool IsCloseable =>
        Source.Source == DiscoveredAppSource.RunningWindow
        && (!string.IsNullOrEmpty(Source.ProcessName) || Source.WindowHandle != IntPtr.Zero);

    /// <summary>True if this app has a known exe path that can be revealed in Explorer.</summary>
    public bool IsOpenable => !string.IsNullOrEmpty(Source.ExecutablePath);

    public DiscoveredAppViewModel(DiscoveredApp app)
    {
        Source = app;
    }
}
