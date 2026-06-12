using System.Diagnostics;
using System.Runtime.InteropServices;
using GroupTasker.Domain;
using GroupTasker.Domain.Interfaces;
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

    public WindowsShortcutService(IconExtractor extractor, IConfigPathProvider paths, string exePath)
    {
        _extractor = extractor;
        _paths = paths;
        _exePath = exePath;
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
        catch
        {
            shortcut.Type = DomainShortcutType.Unknown;
            shortcut.DisplayName = Path.GetFileNameWithoutExtension(sourcePath);
        }

        return shortcut;
    }

    private static DomainShortcut ResolveLink(DomainShortcut shortcut, string lnkPath)
    {
        try
        {
            var (target, args, workDir) = ShellLinkInterop.ReadShortcut(lnkPath);

            shortcut.Arguments = string.IsNullOrEmpty(args) ? null : args;
            shortcut.WorkingDirectory = string.IsNullOrEmpty(workDir) ? null : workDir;

            if (string.IsNullOrEmpty(target))
            {
                shortcut.Type = DomainShortcutType.StoreApp;
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
        catch
        {
            shortcut.Type = DomainShortcutType.Unknown;
            shortcut.DisplayName = Path.GetFileNameWithoutExtension(lnkPath);
            return shortcut;
        }
    }

    public void Launch(DomainShortcut shortcut)
    {
        var target = shortcut.TargetPath ?? shortcut.SourcePath;

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

            default:
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                break;
        }
    }

    public string CreateGroupLauncherLink(DomainGroup group, string iconPath)
    {
        Directory.CreateDirectory(_paths.ShortcutFolder);
        var linkPath = Path.Combine(_paths.ShortcutFolder, $"{FileNameSanitizer.Sanitize(group.Name)}.lnk");

        ShellLinkInterop.CreateShortcut(
            targetPath: _exePath,
            appUserModelId: $"grouptasker.local.group.{group.Id:N}",
            description: $"GroupTasker — {group.Name}",
            workingDirectory: Path.GetDirectoryName(_exePath) ?? "",
            iconLocation: iconPath,
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
        catch
        {
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
