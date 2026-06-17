using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using GroupTasker.Application.Services;
using GroupTasker.Domain.Interfaces;
using GroupTasker.Infrastructure.Configuration;
using GroupTasker.Infrastructure.Data;
using GroupTasker.Infrastructure.IconExtraction;
using GroupTasker.Infrastructure.Logging;
using GroupTasker.Infrastructure.Shell;
using GroupTasker.UI.ViewModels;
using GroupTasker.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace GroupTasker.UI;

public partial class App : Avalonia.Application
{
    /// <summary>
    /// Composition root. Exposed only as a fall-back for the XAML-instantiated
    /// designer data context; production code receives its dependencies via the constructor.
    /// </summary>
    internal static IServiceProvider? Services { get; private set; }

    private SingleInstanceService? _singleInstance;
    private LauncherSettingsService? _settingsService;
    private IHotkeyService? _hotkeyService;
    private ITrayIconService? _trayService;
    private Window? _currentFlyout;
    private Window? _configuratorWindow;
    private IServiceProvider? _provider;
    private bool _trayMode;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        _provider = BuildServiceProvider();
        Services = _provider;

        var crashLogger = _provider.GetRequiredService<ILogger>();
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                crashLogger.Error(ex, "Unhandled AppDomain exception — application terminating");
            else
                crashLogger.Error("Unhandled AppDomain exception (non-Exception object of type {ObjectType})", e.ExceptionObject?.GetType().Name ?? "null");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            crashLogger.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = Environment.GetCommandLineArgs();
            _trayMode = args.Length > 1 && args[1] == "--tray";
            // --no-tray: spawn a configurator from the tray without making it add its own
            // tray icon. Prevents the "click Open Configurator → two tray icons" loop.
            var noTray = args.Length > 1 && args[1] == "--no-tray";
            var launcherArg = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null;

            if (launcherArg is not null)
                HandleLauncherMode(desktop, launcherArg, _provider);
            else if (_trayMode)
                HandleTrayMode(desktop, _provider);
            else
                HandleConfiguratorMode(desktop, _provider);

            RegisterHotkeyIfConfigured();
            if (!noTray) SetupTrayIcon();

            desktop.Exit += async (_, _) =>
            {
                if (_singleInstance is not null)
                    await _singleInstance.DisposeAsync();
                _hotkeyService?.Dispose();
                _trayService?.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();

        // Resolve the directory the .exe actually lives in. The old code did
        // Path.GetDirectoryName(exePath) and fell back to AppContext.BaseDirectory
        // (which is a directory, not a file) — yielding the *parent* folder.
        var exePath = Environment.ProcessPath;
        var exeDir = !string.IsNullOrEmpty(exePath)
            ? Path.GetDirectoryName(exePath)!
            : AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);

        var logger = SerilogBootstrap.CreateLogger();
        services.AddSingleton<ILogger>(logger);
        logger.Information("GroupTasker starting up");

        services.AddSingleton<IConfigPathProvider>(new ConfigPathProvider(exeDir));
        services.AddSingleton<IShellGateway, WindowsShellGateway>();
        services.AddSingleton<IconExtractor>();
        services.AddSingleton<IAppActivator, WindowsAppActivator>();
        services.AddSingleton<ILiveAppResolver, LiveAppResolver>();
        services.AddSingleton<IStoreAppEnumerator, ShellAppsFolderEnumerator>();
        services.AddSingleton<ITaskbarEnumerator>(sp => new TaskbarEnumerator(
            sp.GetRequiredService<IAppActivator>(),
            sp.GetRequiredService<IStoreAppEnumerator>()));
        services.AddSingleton<IIconCacheService>(sp => new IconCacheService(
            sp.GetRequiredService<IconExtractor>(),
            sp.GetRequiredService<ILiveAppResolver>(),
            sp.GetRequiredService<ILogger>()));

        services.AddSingleton<IGroupRepository>(sp =>
            new JsonGroupRepository(
                sp.GetRequiredService<IConfigPathProvider>(),
                sp.GetRequiredService<ILogger>()));

        services.AddSingleton<IShortcutService>(sp => new WindowsShortcutService(
            sp.GetRequiredService<IconExtractor>(),
            sp.GetRequiredService<IConfigPathProvider>(),
            exePath ?? Path.Combine(exeDir, "GroupTasker.App.exe"),
            sp.GetRequiredService<IAppActivator>(),
            sp.GetRequiredService<ILiveAppResolver>(),
            sp.GetRequiredService<ILogger>()));

        services.AddSingleton<LauncherSettingsService>(sp =>
            new LauncherSettingsService(
                sp.GetRequiredService<IConfigPathProvider>(),
                sp.GetRequiredService<ILogger>()));

        services.AddSingleton<IHotkeyService>(sp => new HotkeyService(
            sp.GetRequiredService<ILogger>()));

        services.AddSingleton<ITrayIconService>(sp => new TrayIconService(
            sp.GetRequiredService<ILogger>()));

        services.AddSingleton<GroupService>(sp => new GroupService(
            sp.GetRequiredService<IGroupRepository>(),
            sp.GetRequiredService<IIconCacheService>(),
            sp.GetRequiredService<IShortcutService>(),
            sp.GetRequiredService<IConfigPathProvider>(),
            sp.GetRequiredService<IShellGateway>(),
            sp.GetRequiredService<ILogger>()));

        // VMs are constructor-injected so production code never needs to reach into
        // App.Services. The string-arg LauncherViewModel uses a delegate-factory so
        // the group name can be supplied at runtime.
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<GroupConfiguratorViewModelFactory>();
        services.AddTransient<Func<string, LauncherViewModel>>(sp =>
            name => new LauncherViewModel(
                name,
                sp.GetRequiredService<GroupService>(),
                sp.GetRequiredService<IShortcutService>(),
                sp.GetRequiredService<IShellGateway>()));

        return services.BuildServiceProvider();
    }

    private void HandleConfiguratorMode(IClassicDesktopStyleApplicationLifetime desktop, IServiceProvider provider)
    {
        // No CLI arg = configurator mode. The launcher pipe is *launcher-only*; we don't
        // try to single-instance the configurator (multiple editor sessions are rare and
        // both go through the same atomic repository now).
        _configuratorWindow = new MainWindow
        {
            DataContext = provider.GetRequiredService<MainWindowViewModel>()
        };
        desktop.MainWindow = _configuratorWindow;
    }

    private void HandleTrayMode(IClassicDesktopStyleApplicationLifetime desktop, IServiceProvider provider)
    {
        // --tray mode: no configurator window. The app lives in the tray.
        // The tray icon (set up by SetupTrayIcon) provides the entry point.
        // A hidden dummy window keeps the app alive without a visible main window.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
    }

    private void SetupTrayIcon()
    {
        if (_provider is null) return;

        var settings = _provider.GetRequiredService<LauncherSettingsService>().Load();
        // Default: show in tray unless explicitly disabled.
        var showInTray = settings.ShowInTray ?? true;
        if (!showInTray) return;

        _trayService = _provider.GetRequiredService<ITrayIconService>();
        _trayService.IconClicked += () => OpenPrimaryGroupFlyout();
        _trayService.MenuAction += OnTrayMenuAction;
        _trayService.Show("GroupTasker");
        RefreshTrayMenu();
    }

    private async void RefreshTrayMenu()
    {
        if (_trayService is null || _provider is null) return;

        var groups = await _provider.GetRequiredService<IGroupRepository>().GetAllAsync();
        var settings = _provider.GetRequiredService<LauncherSettingsService>().Load();
        var primaryId = settings.PrimaryGroupId;

        var items = new List<TrayMenuItem>();
        foreach (var g in groups)
        {
            var isChecked = g.Id == primaryId;
            items.Add(new TrayMenuItem(g.Name, "group:" + g.Id) { IsChecked = isChecked });
        }
        if (items.Count > 0)
            items.Add(new TrayMenuItem("", "")); // separator
        items.Add(new TrayMenuItem("Open", "open-configurator"));
        items.Add(new TrayMenuItem("Exit", "quit"));

        _trayService.SetMenu(items);
    }

    private void OnTrayMenuAction(string actionKey)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (actionKey == "open-configurator")
            {
                ShowConfiguratorWindow();
            }
            else if (actionKey == "quit")
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.Shutdown();
            }
            else if (actionKey.StartsWith("group:"))
            {
                var groupId = actionKey["group:".Length..];
                if (Guid.TryParse(groupId, out var id) && _provider is not null)
                {
                    _ = ShowGroupFlyoutById(id);
                }
            }
        });
    }

    /// <summary>
    /// Show the configurator window. In tray mode we create + show it in the
    /// CURRENT process (standard tray-app pattern: no duplicate process, no
    /// duplicate tray icon). In configurator mode the window already exists
    /// from startup, so this just brings it to the foreground.
    /// </summary>
    private void ShowConfiguratorWindow()
    {
        if (_provider is null) return;
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop) return;

        if (_configuratorWindow is null)
        {
            var mainWindow = new MainWindow
            {
                DataContext = _provider.GetRequiredService<MainWindowViewModel>()
            };
            // In tray mode, closing the window should hide to tray, not exit the app.
            // In configurator mode (the user started the app with no args), the
            // window closes the app as usual.
            mainWindow.CloseHidesToTray = _trayMode;
            _configuratorWindow = mainWindow;
            // In tray mode the ShutdownMode is OnExplicitShutdown, so closing the
            // window won't exit the app — the user has to click Exit on the tray.
            // We just need to make sure the window is the active MainWindow so it
            // shows up and is correctly restored.
            desktop.MainWindow = _configuratorWindow;
        }

        if (!_configuratorWindow.IsVisible)
            _configuratorWindow.Show();
        _configuratorWindow.Activate();
        if (_configuratorWindow.WindowState == WindowState.Minimized)
            _configuratorWindow.WindowState = WindowState.Normal;
    }

    private async Task ShowGroupFlyoutById(Guid groupId)
    {
        if (_provider is null) return;
        var group = await _provider.GetRequiredService<IGroupRepository>().GetByIdAsync(groupId);
        if (group is not null)
            ShowFlyout(group.Name, _provider);
    }

    private void RegisterHotkeyIfConfigured()
    {
        if (_provider is null) return;
        var settings = _provider.GetRequiredService<LauncherSettingsService>().Load();
        if (settings.PrimaryGroupHotkey is null) return;

        _hotkeyService = _provider.GetRequiredService<IHotkeyService>();
        if (!_hotkeyService.TryRegister(settings.PrimaryGroupHotkey))
        {
            _hotkeyService = null;
            return;
        }

        _hotkeyService.HotkeyPressed += () => OpenPrimaryGroupFlyout();
    }

    private void OpenPrimaryGroupFlyout()
    {
        if (_provider is null) return;
        Dispatcher.UIThread.Post(async () =>
        {
            var settings = _provider.GetRequiredService<LauncherSettingsService>().Load();
            var groups = await _provider.GetRequiredService<IGroupRepository>().GetAllAsync();
            var primaryId = settings.PrimaryGroupId;
            var group = primaryId is { } id
                ? groups.FirstOrDefault(g => g.Id == id) ?? groups.FirstOrDefault()
                : groups.FirstOrDefault();
            if (group is not null)
                ShowFlyout(group.Name, _provider);
        });
    }

    private void HandleLauncherMode(
        IClassicDesktopStyleApplicationLifetime desktop,
        string groupName,
        IServiceProvider provider)
    {
        var logger = provider.GetRequiredService<ILogger>();
        if (SingleInstanceService.TryActivate(groupName, logger))
        {
            Environment.Exit(0);
            return;
        }

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _settingsService = provider.GetRequiredService<LauncherSettingsService>();
        _singleInstance = new SingleInstanceService(logger);
        _singleInstance.OnShowGroup += n => ShowFlyout(n, provider);
        _singleInstance.Start();

        ShowFlyout(groupName, provider);
    }

    private void ShowFlyout(string groupName, IServiceProvider provider)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ShowFlyout(groupName, provider));
            return;
        }

        if (_currentFlyout is not null)
        {
            _currentFlyout.Close();
            _currentFlyout = null;
        }

        var factory = provider.GetRequiredService<Func<string, LauncherViewModel>>();
        var vm = factory(groupName);
        var flyout = new LauncherWindow { DataContext = vm };

        var settings = _settingsService?.Load();
        if (settings is { HasPosition: true })
        {
            flyout.Position = new PixelPoint(settings.PositionX, settings.PositionY);
        }
        else
        {
            var (x, y) = TaskbarHelper.GetDefaultPosition(320, 200);
            flyout.Position = new PixelPoint(x, y);
        }

        flyout.Closing += (_, _) =>
        {
            // Preserve other settings (e.g. PrimaryGroupHotkey) when persisting the new position.
            var current = _settingsService?.Load() ?? new LauncherSettings();
            current.HasPosition = true;
            current.PositionX = flyout.Position.X;
            current.PositionY = flyout.Position.Y;
            _settingsService?.Save(current);
        };

        flyout.Closed += (_, _) => _currentFlyout = null;
        _currentFlyout = flyout;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt)
            lt.MainWindow = flyout;

        flyout.Show();
    }
}
