using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GroupTasker.Application.Services;
using GroupTasker.Domain.Entities;
using GroupTasker.Domain.Interfaces;
using GroupTasker.Domain.Logging;
using GroupTasker.Infrastructure.Configuration;
using GroupTasker.Infrastructure.Shell;

namespace GroupTasker.UI.ViewModels;

/// <summary>
/// Backs the launcher settings dialog. Lets the user pick a primary group and
/// bind a global hotkey to it.
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly LauncherSettingsService _settingsService;
    private readonly IHotkeyService _hotkeyService;
    private readonly IGroupRepository _repository;
    private readonly ILogger _logger;

    [ObservableProperty] private string? _primaryGroupId;
    [ObservableProperty] private string _hotkeyText = string.Empty;
    [ObservableProperty] private bool _showInTray = true;
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private ObservableCollection<GroupOption> _availableGroups = [];

    public SettingsViewModel(
        LauncherSettingsService settingsService,
        IHotkeyService hotkeyService,
        IGroupRepository repository,
        ILogger logger)
    {
        _settingsService = settingsService;
        _hotkeyService = hotkeyService;
        _repository = repository;
        _logger = logger;
    }

    /// <summary>Load current settings + populate the group dropdown. Call once when the dialog opens.</summary>
    public async Task LoadAsync()
    {
        var settings = _settingsService.Load();
        PrimaryGroupId = settings.PrimaryGroupId?.ToString();
        HotkeyText = settings.PrimaryGroupHotkey?.ToString() ?? string.Empty;
        ShowInTray = settings.ShowInTray ?? true;
        StartWithWindows = settings.StartWithWindows ?? false;

        var groups = await _repository.GetAllAsync();
        AvailableGroups = new ObservableCollection<GroupOption>(
            groups.Select(g => new GroupOption(g.Id.ToString(), g.Name)));
    }

    [RelayCommand]
    private void Save()
    {
        ErrorMessage = null;
        StatusMessage = null;

        var settings = _settingsService.Load();

        // Parse primary group id
        Guid? primaryId = null;
        if (!string.IsNullOrWhiteSpace(PrimaryGroupId) && Guid.TryParse(PrimaryGroupId, out var parsed))
            primaryId = parsed;
        settings.PrimaryGroupId = primaryId;

        // Parse hotkey (empty = disabled)
        HotkeyBinding? newHotkey = null;
        var hotkeyTextTrim = HotkeyText?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(hotkeyTextTrim))
        {
            if (!HotkeyBinding.TryParse(hotkeyTextTrim, out var parsedHotkey))
            {
                ErrorMessage = $"Invalid hotkey format: '{hotkeyTextTrim}'. Use e.g. 'Ctrl+Alt+G' (one or more modifiers + one key).";
                return;
            }
            newHotkey = parsedHotkey;
        }
        settings.PrimaryGroupHotkey = newHotkey;

        // Tray + auto-start
        settings.ShowInTray = ShowInTray;
        settings.StartWithWindows = StartWithWindows;

        // Try the registry write BEFORE saving settings, so a failure doesn't leave
        // the on-disk settings claiming auto-start is enabled when it isn't.
        if (StartWithWindows && !StartupService.SetAutoStart(true))
        {
            _settingsService.Save(settings);
            ErrorMessage = "Settings saved, but failed to enable Start with Windows. The registry write was blocked — try running as administrator or check the registry permissions.";
            return;
        }
        if (!StartWithWindows && !StartupService.SetAutoStart(false))
        {
            _settingsService.Save(settings);
            ErrorMessage = "Settings saved, but failed to disable Start with Windows. The registry write was blocked — you may need to remove the HKCU entry manually.";
            return;
        }

        // Persist
        _settingsService.Save(settings);
        _logger.Information("Settings updated: PrimaryGroupId={PrimaryGroupId}, Hotkey={Hotkey}, ShowInTray={ShowInTray}, StartWithWindows={StartWithWindows}",
            primaryId, newHotkey?.ToString() ?? "(none)", ShowInTray, StartWithWindows);

        // Re-register hotkey on the running service
        _hotkeyService.Unregister();
        if (newHotkey is not null)
        {
            if (!_hotkeyService.TryRegister(newHotkey))
            {
                ErrorMessage = $"Saved, but could not register hotkey '{newHotkey}' — it may be in use by another app. Disable it there and try again.";
                return;
            }
        }

        StatusMessage = "Settings saved.";
    }
}

public sealed record GroupOption(string Id, string Name);
