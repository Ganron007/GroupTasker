using System;
using System.Diagnostics;
using System.IO;
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
    private Window? _currentFlyout;
    private IServiceProvider? _provider;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        _provider = BuildServiceProvider();
        Services = _provider;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = Environment.GetCommandLineArgs();
            var launcherArg = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null;

            if (launcherArg is not null)
                HandleLauncherMode(desktop, launcherArg, _provider);
            else
                HandleConfiguratorMode(desktop, _provider);

            desktop.Exit += async (_, _) =>
            {
                if (_singleInstance is not null)
                    await _singleInstance.DisposeAsync();
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

        services.AddSingleton<IConfigPathProvider>(new ConfigPathProvider(exeDir));
        services.AddSingleton<IShellGateway, WindowsShellGateway>();
        services.AddSingleton<IconExtractor>();
        services.AddSingleton<IIconCacheService, IconCacheService>();

        services.AddSingleton<IGroupRepository>(sp =>
            new JsonGroupRepository(
                sp.GetRequiredService<IConfigPathProvider>(),
                onError: LogError));

        services.AddSingleton<IShortcutService>(sp => new WindowsShortcutService(
            sp.GetRequiredService<IconExtractor>(),
            sp.GetRequiredService<IConfigPathProvider>(),
            exePath ?? Path.Combine(exeDir, "GroupTasker.exe")));

        services.AddSingleton<LauncherSettingsService>(sp =>
            new LauncherSettingsService(sp.GetRequiredService<IConfigPathProvider>(), LogError));

        services.AddSingleton<GroupService>();

        // VMs are constructor-injected so production code never needs to reach into
        // App.Services. The string-arg LauncherViewModel uses a delegate-factory so
        // the group name can be supplied at runtime.
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<GroupConfiguratorViewModelFactory>();
        services.AddTransient<Func<string, LauncherViewModel>>(sp =>
            name => new LauncherViewModel(
                name,
                sp.GetRequiredService<GroupService>(),
                sp.GetRequiredService<IShortcutService>()));

        return services.BuildServiceProvider();
    }

    private static void LogError(string message, Exception ex) =>
        Debug.WriteLine($"[GroupTasker] {message}: {ex}");

    private void HandleConfiguratorMode(IClassicDesktopStyleApplicationLifetime desktop, IServiceProvider provider)
    {
        // No CLI arg = configurator mode. The launcher pipe is *launcher-only*; we don't
        // try to single-instance the configurator (multiple editor sessions are rare and
        // both go through the same atomic repository now).
        desktop.MainWindow = new MainWindow
        {
            DataContext = provider.GetRequiredService<MainWindowViewModel>()
        };
    }

    private void HandleLauncherMode(
        IClassicDesktopStyleApplicationLifetime desktop,
        string groupName,
        IServiceProvider provider)
    {
        if (SingleInstanceService.TryActivate(groupName))
        {
            Environment.Exit(0);
            return;
        }

        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        _settingsService = provider.GetRequiredService<LauncherSettingsService>();
        _singleInstance = new SingleInstanceService();
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
            _settingsService?.Save(new LauncherSettings
            {
                HasPosition = true,
                PositionX = flyout.Position.X,
                PositionY = flyout.Position.Y
            });
        };

        flyout.Closed += (_, _) => _currentFlyout = null;
        _currentFlyout = flyout;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt)
            lt.MainWindow = flyout;

        flyout.Show();
    }
}
