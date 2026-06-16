using System.Diagnostics;
using System.Runtime.InteropServices;
using GroupTasker.Domain;
using GroupTasker.Domain.Interfaces;
using GroupTasker.Domain.Logging;
using GroupTasker.Infrastructure.IconExtraction;
using DomainGroup = GroupTasker.Domain.Entities.Group;
using DomainShortcut = GroupTasker.Domain.Entities.Shortcut;
using DomainShortcutType = GroupTasker.Domain.Entities.ShortcutType;

namespace GroupTasker.Infrastructure.Shell;

/// <summary>
/// Windows implementation of <see cref="IShortcutService"/>. Handles launching, resolving,
/// temp-link creation, and taskbar pinning via the typed <c>IShellLinkW</c> COM interface
/// and (only as a fallback) <c>Shell.Application</c> verbs.
/// </summary>
public sealed class WindowsShortcutService : IShortcutService
{
    private readonly IconExtractor _extractor;
    private readonly IConfigPathProvider _paths;
    private readonly string _exePath;
    private readonly IAppActivator _activator;
    private readonly ILiveAppResolver _liveResolver;
    private readonly ILogger _logger;

    public WindowsShortcutService(
        IconExtractor extractor,
        IConfigPathProvider paths,
        string exePath,
        IAppActivator? activator = null,
        ILiveAppResolver? liveResolver = null,
        ILogger? logger = null)
    {
        _extractor = extractor;
        _paths = paths;
        _exePath = exePath;
        _activator = activator ?? new WindowsAppActivator();
        _liveResolver = liveResolver ?? new LiveAppResolver();
        _logger = logger ?? NullLogger.Instance;
    }

    public DomainShortcut Resolve(string sourcePath)
    {
        var shortcut = new DomainShortcut { SourcePath = sourcePath };

        try
        {
            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();

            switch (ext)
            {
                case ".lnk":
                    return ResolveLink(shortcut, sourcePath);
                case ".exe":
                    shortcut.Type = DomainShortcutType.Application;
                    shortcut.TargetPath = sourcePath;
                    shortcut.DisplayName = Path.GetFileNameWithoutExtension(sourcePath);
                    break;
                default:
                    if (Directory.Exists(sourcePath))
                    {
                        shortcut.Type = DomainShortcutType.Folder;
                        shortcut.TargetPath = sourcePath;
                        shortcut.WorkingDirectory = sourcePath;
                        shortcut.DisplayName = new DirectoryInfo(sourcePath).Name;
                    }
                    else if (sourcePath.Contains('!'))
                    {
                        shortcut.Type = DomainShortcutType.StoreApp;
                        shortcut.TargetPath = sourcePath;
                        shortcut.DisplayName = _extractor.GetStoreAppName(sourcePath);
                    }
                    else
                    {
                        shortcut.Type = DomainShortcutType.Unknown;
                        shortcut.DisplayName = Path.GetFileNameWithoutExtension(sourcePath);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to resolve shortcut {SourcePath}", sourcePath);
            shortcut.Type = DomainShortcutType.Unknown;
            shortcut.DisplayName = Path.GetFileNameWithoutExtension(sourcePath);
        }

        return shortcut;
    }

    private DomainShortcut ResolveLink(DomainShortcut shortcut, string lnkPath)
    {
        try
        {
            var (target, args, workDir, iconLocation, _) = ShellLinkInterop.ReadShortcut(lnkPath);

            shortcut.Arguments = string.IsNullOrEmpty(args) ? null : args;
            shortcut.WorkingDirectory = string.IsNullOrEmpty(workDir) ? null : workDir;

            // Store the icon location from the .lnk metadata. Many installers
            // (Ollama, Claude, Codex) point the shortcut at a separate .ico
            // file (e.g. app.ico) rather than the .exe itself, so we must
            // honour that reference. Format: "C:\path\to\file.ico,0" where
            // ",0" is the icon index. The trailing index is stripped because
            // .ico files contain multiple icons and we just want the largest.
            if (!string.IsNullOrEmpty(iconLocation))
            {
                var commaIdx = iconLocation.LastIndexOf(',');
                shortcut.IconSourcePath = commaIdx > 0
                    ? iconLocation[..commaIdx]
                    : iconLocation;
            }

            if (string.IsNullOrEmpty(target))
            {
                // Some installers (e.g. Ollama) create desktop shortcuts whose
                // resolved target path is empty when read via Shell COM.
                // Categorize as a plain Link and keep the .lnk itself as the
                // launch target — Windows will resolve the shortcut at launch
                // time via UseShellExecute. This used to be miscategorised as
                // StoreApp, which caused GroupTasker to launch
                // `explorer.exe shell:appsFolder\` (empty AUMI) — opening the
                // user's Documents folder instead of the app.
                shortcut.Type = DomainShortcutType.Link;
                shortcut.TargetPath = lnkPath;
                shortcut.DisplayName = Path.GetFileNameWithoutExtension(lnkPath);
                return shortcut;
            }

            var resolvedPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(target));
            shortcut.TargetPath = resolvedPath;

            if (resolvedPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                shortcut.Type = DomainShortcutType.Application;
                shortcut.DisplayName = Path.GetFileNameWithoutExtension(resolvedPath);
            }
            else if (resolvedPath.Contains('!'))
            {
                shortcut.Type = DomainShortcutType.StoreApp;
                shortcut.DisplayName = Path.GetFileNameWithoutExtension(lnkPath);
            }
            else
            {
                shortcut.Type = DomainShortcutType.Link;
                shortcut.DisplayName = Path.GetFileNameWithoutExtension(lnkPath);
            }

            return shortcut;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to resolve .lnk {LinkPath}", lnkPath);
            shortcut.Type = DomainShortcutType.Unknown;
            shortcut.DisplayName = Path.GetFileNameWithoutExtension(lnkPath);
            return shortcut;
        }
    }

    public void Launch(DomainShortcut shortcut)
    {
        var target = shortcut.TargetPath ?? shortcut.SourcePath;
        _logger.Information("Launching shortcut {ShortcutName} ({ShortcutType}) from {Target}", shortcut.DisplayName, shortcut.Type, target);

        switch (shortcut.Type)
        {
            case DomainShortcutType.Application:
            case DomainShortcutType.Link:
                Process.Start(new ProcessStartInfo(target)
                {
                    Arguments = shortcut.Arguments ?? "",
                    WorkingDirectory = shortcut.WorkingDirectory ?? Path.GetDirectoryName(target) ?? "",
                    UseShellExecute = true
                });
                break;

            case DomainShortcutType.Folder:
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
                break;

            case DomainShortcutType.StoreApp:
                Process.Start(new ProcessStartInfo("explorer.exe", $"shell:appsFolder\\{target}") { UseShellExecute = true });
                break;

            case DomainShortcutType.LiveApplication:
                LaunchLiveApplication(shortcut);
                break;

            default:
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                break;
        }
    }

    /// <summary>
    /// Launch a LiveApplication shortcut. Tries AUMI first (most reliable),
    /// then falls back to resolving the current .exe and starting it.
    /// </summary>
    private void LaunchLiveApplication(DomainShortcut shortcut)
    {
        var source = shortcut.SourcePath ?? "";
        var aumi = shortcut.TargetPath ?? source;

        // 1) If we have a valid AUMI (contains "!"), try to activate by AUMI
        if (aumi.Contains('!') && _activator.ActivateByAumi(aumi)) return;

        // 2) Try to resolve the current .exe path
        var resolved = _liveResolver.Resolve(source);
        if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
        {
            Process.Start(new ProcessStartInfo(resolved)
            {
                UseShellExecute = true
            });
            return;
        }

        // 3) Last-ditch: shell-execute the source string
        Process.Start(new ProcessStartInfo(source) { UseShellExecute = true });
    }

    /// <summary>
    /// Build a <see cref="DomainShortcutType.LiveApplication"/> from a discovered
    /// app (pinned taskbar item or running window). Stores the AUMI when known,
    /// the process name as a fallback.
    /// </summary>
    public static DomainShortcut FromDiscoveredApp(DiscoveredApp app)
    {
        var launchKey = app.Aumi ?? app.ProcessName ?? app.ExecutablePath ?? app.DisplayName;
        return new DomainShortcut
        {
            SourcePath = launchKey,
            TargetPath = app.Aumi,
            Type = DomainShortcutType.LiveApplication,
            DisplayName = app.DisplayName,
            IconPath = app.ExecutablePath
        };
    }

    public string CreateGroupLauncherLink(DomainGroup group, string iconPath)
    {
        Directory.CreateDirectory(_paths.ShortcutFolder);
        var linkPath = Path.Combine(_paths.ShortcutFolder, $"{FileNameSanitizer.Sanitize(group.Name)}.lnk");

        // If the user set a custom icon, use it instead of the auto-generated composite.
        var effectiveIcon = !string.IsNullOrEmpty(group.CustomIconPath) && File.Exists(group.CustomIconPath)
            ? group.CustomIconPath
            : iconPath;

        ShellLinkInterop.CreateShortcut(
            targetPath: _exePath,
            appUserModelId: $"grouptasker.local.group.{group.Id:N}",
            description: $"GroupTasker — {group.Name}",
            workingDirectory: Path.GetDirectoryName(_exePath) ?? "",
            iconLocation: effectiveIcon,
            savePath: linkPath,
            arguments: $"\"{group.Name}\"");

        return linkPath;
    }

    /// <summary>
    /// Try Windows' "Pin to taskbar" verb. Removed in Win10/11 in many SKUs, so callers must
    /// handle the <c>false</c> path gracefully (the .lnk is still on disk, just not pinned).
    /// </summary>
    public bool PinToTaskbar(DomainGroup group, string launcherPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return false;

            var shellApp = Activator.CreateInstance(shellType);
            if (shellApp is null) return false;

            // Late-binding via Type.InvokeMember keeps us off the DLR (more AOT-friendly than dynamic).
            var folder = shellType.InvokeMember("NameSpace",
                System.Reflection.BindingFlags.InvokeMethod, null, shellApp,
                new object[] { Path.GetDirectoryName(launcherPath)! });
            if (folder is null) return false;

            var items = folder.GetType().InvokeMember("Items",
                System.Reflection.BindingFlags.InvokeMethod, null, folder, null);
            if (items is null) return false;

            var item = items.GetType().InvokeMember("Item",
                System.Reflection.BindingFlags.InvokeMethod, null, items,
                new object[] { Path.GetFileName(launcherPath) });
            if (item is null) return false;

            var verbs = item.GetType().InvokeMember("Verbs",
                System.Reflection.BindingFlags.InvokeMethod, null, item, null);
            if (verbs is null) return false;

            var count = (int)verbs.GetType().InvokeMember("Count",
                System.Reflection.BindingFlags.GetProperty, null, verbs, null)!;

            for (int i = 0; i < count; i++)
            {
                var verb = verbs.GetType().InvokeMember("Item",
                    System.Reflection.BindingFlags.InvokeMethod, null, verbs, new object[] { i });
                if (verb is null) continue;

                var name = (string?)verb.GetType().InvokeMember("Name",
                    System.Reflection.BindingFlags.GetProperty, null, verb, null);
                if (name is null) continue;

                if (name.Replace("&", "").Contains("pin to taskbar", StringComparison.OrdinalIgnoreCase))
                {
                    verb.GetType().InvokeMember("DoIt",
                        System.Reflection.BindingFlags.InvokeMethod, null, verb, null);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Taskbar pinning failed for {LauncherPath}", launcherPath);
            // Taskbar pinning not available or failed — caller decides what to tell the user.
        }
        finally
        {
            // Release any remaining COM RCWs we touched.
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        return false;
    }
}
