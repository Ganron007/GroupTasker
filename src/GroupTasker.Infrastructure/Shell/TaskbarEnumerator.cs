using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using GroupTasker.Domain.Entities;
using GroupTasker.Domain.Interfaces;

namespace GroupTasker.Infrastructure.Shell;

/// <summary>
/// Enumerates Windows taskbar items by reading the pinned-taskbar .lnk folder
/// and the running-window list. Built for the "Add from running apps" UI that
/// lets users include auto-updating apps (Claude Desktop, Codex, etc.) in a
/// group without needing a stable desktop shortcut.
/// </summary>
public sealed class TaskbarEnumerator : ITaskbarEnumerator
{
    private readonly IAppActivator _activator;
    private readonly IStoreAppEnumerator _storeApps;

    // Matches a WindowsApps package folder: {Name}_{Version}_{Arch}__{FamilyName}
    private static readonly Regex WindowsAppsPackagePattern = new(
        @"\\WindowsApps\\[^\\]+__(?<family>[A-Za-z0-9]+)\\",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public TaskbarEnumerator(IAppActivator activator, IStoreAppEnumerator storeApps)
    {
        _activator = activator;
        _storeApps = storeApps;
    }

    public IReadOnlyList<DiscoveredApp> Enumerate()
    {
        // Order matters: store apps are the most authoritative source because
        // they carry AUMIs. They go in first so dedup picks them over plain
        // running-process entries.
        var results = new List<DiscoveredApp>();
        results.AddRange(_storeApps.Enumerate());
        results.AddRange(EnumeratePinnedTaskbarItems());
        results.AddRange(EnumerateRunningWindows());
        results.AddRange(EnumerateAllRunningProcesses());

        // De-dup. Prefer entries that have an AUMI (more launchable) and
        // prefer store-app entries (MSIX installs) over plain Win32 entries.
        return results
            .GroupBy(a => DedupKey(a))
            .Select(g => g
                .OrderByDescending(a => a.Aumi is not null)
                .ThenByDescending(a => a.Source == DiscoveredAppSource.StoreApp)
                .ThenByDescending(a => a.Source == DiscoveredAppSource.PinnedTaskbar)
                .ThenByDescending(a => !string.IsNullOrEmpty(a.ExecutablePath))
                .First())
            .ToList();
    }

    /// <summary>
    /// Normalise the dedup key. We lower-case paths so
    /// "C:\foo\Bar.exe" and "c:\foo\bar.exe" don't show up twice. For AUMIs we
    /// strip the application-id suffix (after the "!") because the WindowsApps
    /// folder name only encodes the package family, not the app id. So a
    /// running-process entry with AUMI <c>Claude_xyz!App</c> and a store-app
    /// entry with AUMI <c>Claude_xyz!Claude</c> both collapse to the same
    /// package family.
    /// </summary>
    private static string DedupKey(DiscoveredApp a)
    {
        if (!string.IsNullOrEmpty(a.Aumi))
        {
            // Use just the family part of the AUMI (before the "!")
            var family = a.Aumi.Split('!')[0].ToLowerInvariant();
            return $"family:{family}";
        }
        if (!string.IsNullOrEmpty(a.ExecutablePath)) return $"exe:{a.ExecutablePath.ToLowerInvariant()}";
        if (!string.IsNullOrEmpty(a.ProcessName)) return $"proc:{a.ProcessName.ToLowerInvariant()}";
        return $"name:{a.DisplayName.ToLowerInvariant()}";
    }

    private IEnumerable<DiscoveredApp> EnumeratePinnedTaskbarItems()
    {
        var pinnedDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar");

        if (!Directory.Exists(pinnedDir)) yield break;

        foreach (var lnk in Directory.EnumerateFiles(pinnedDir, "*.lnk"))
        {
            DiscoveredApp? app = null;
            try
            {
                var aumi = _activator.TryGetAumiFromLink(lnk);
                var name = Path.GetFileNameWithoutExtension(lnk);
                var exe = TryReadShortcutTarget(lnk);

                // Skip stale pinned items whose target no longer exists on disk.
                // These are leftover .lnk files from apps the user has since
                // uninstalled. Including them only clutters the picker.
                if (!string.IsNullOrEmpty(exe) && !File.Exists(exe) && string.IsNullOrEmpty(aumi))
                    continue;

                app = new DiscoveredApp
                {
                    DisplayName = name,
                    Aumi = aumi,
                    ProcessName = exe is null ? null : Path.GetFileNameWithoutExtension(exe),
                    ExecutablePath = exe,
                    Source = DiscoveredAppSource.PinnedTaskbar
                };
            }
            catch
            {
                // skip unreadable .lnk
            }

            if (app is not null) yield return app;
        }
    }

    private IEnumerable<DiscoveredApp> EnumerateRunningWindows()
    {
        // Snapshot window list once via EnumWindows. We deliberately do NOT
        // filter by IsWindowVisible here — many UWP / Electron apps (Claude
        // Desktop, Codex, Discord, Slack, VS Code) keep their top-level window
        // hidden when minimised to the tray or when first launched.
        var windows = new List<IntPtr>();
        EnumWindows((hWnd, _) =>
        {
            var owner = GetWindow(hWnd, GW_OWNER);
            // Skip windows owned by other top-level windows (we only want top-level)
            if (owner != IntPtr.Zero) return true;
            windows.Add(hWnd);
            return true;
        }, IntPtr.Zero);

        foreach (var hWnd in windows)
        {
            GetWindowThreadProcessId(hWnd, out var pid);
            if (pid == 0) continue;

            DiscoveredApp? app = null;
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                var exe = proc.MainModule?.FileName;
                if (string.IsNullOrEmpty(exe)) continue;

                var length = GetWindowTextLength(hWnd);
                string title = "";
                if (length > 0)
                {
                    var sb = new StringBuilder(length + 1);
                    GetWindowText(hWnd, sb, sb.Capacity);
                    title = sb.ToString();
                }
                var displayName = !string.IsNullOrWhiteSpace(title) ? title : proc.ProcessName;

                app = new DiscoveredApp
                {
                    DisplayName = displayName,
                    Aumi = TryDeriveAumiFromWindowsAppsPath(exe),
                    ProcessName = proc.ProcessName,
                    ExecutablePath = exe,
                    WindowHandle = hWnd,
                    Source = DiscoveredAppSource.RunningWindow
                };
            }
            catch
            {
                // access denied / system process — skip
            }

            if (app is not null) yield return app;
        }
    }

    /// <summary>
    /// Fallback: enumerate every process on the system. Catches background
    /// apps that don't have a top-level window (background workers, system
    /// tray apps, services) but might still be the target the user wants to
    /// add. We dedup with the running-windows list and the pinned folder so
    /// each process only shows up once.
    /// </summary>
    private IEnumerable<DiscoveredApp> EnumerateAllRunningProcesses()
    {
        Process[] all;
        try
        {
            all = Process.GetProcesses();
        }
        catch
        {
            yield break;
        }

        foreach (var proc in all)
        {
            DiscoveredApp? app = null;
            try
            {
                var exe = proc.MainModule?.FileName;
                if (string.IsNullOrEmpty(exe)) continue;

                if (IsSystemProcess(proc.ProcessName)) continue;

                app = new DiscoveredApp
                {
                    DisplayName = proc.ProcessName,
                    Aumi = TryDeriveAumiFromWindowsAppsPath(exe),
                    ProcessName = proc.ProcessName,
                    ExecutablePath = exe,
                    Source = DiscoveredAppSource.RunningWindow
                };
            }
            catch
            {
                // access denied / system process
            }
            finally
            {
                proc.Dispose();
            }

            if (app is not null) yield return app;
        }
    }

    /// <summary>
    /// If the executable lives under <c>C:\Program Files\WindowsApps\{Name}_*__{Family}\</c>
    /// we can derive the AUMI's package family name from the path. The
    /// ApplicationId is <c>App</c> by convention for single-app packages but
    /// may be a custom name (e.g. <c>Claude</c>). We return just the family
    /// for now — if a more specific AUMI is needed at launch time, the
    /// <see cref="WindowsAppActivator"/> uses an "App" suffix as a fallback.
    /// </summary>
    private static string? TryDeriveAumiFromWindowsAppsPath(string exePath)
    {
        var match = WindowsAppsPackagePattern.Match(exePath);
        if (!match.Success) return null;
        var family = match.Groups["family"].Value;
        return string.IsNullOrEmpty(family) ? null : $"{family}!App";
    }

    private static bool IsSystemProcess(string name)
    {
        return name.Equals("svchost", StringComparison.OrdinalIgnoreCase)
            || name.Equals("csrss", StringComparison.OrdinalIgnoreCase)
            || name.Equals("lsass", StringComparison.OrdinalIgnoreCase)
            || name.Equals("wininit", StringComparison.OrdinalIgnoreCase)
            || name.Equals("services", StringComparison.OrdinalIgnoreCase)
            || name.Equals("System", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Idle", StringComparison.OrdinalIgnoreCase)
            || name.Equals("dwm", StringComparison.OrdinalIgnoreCase)
            || name.Equals("fontdrvhost", StringComparison.OrdinalIgnoreCase)
            || name.Equals("WmiPrvSe", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryReadShortcutTarget(string lnkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return null;
            var shellApp = Activator.CreateInstance(shellType);
            if (shellApp is null) return null;

            var folder = shellType.InvokeMember(
                "NameSpace",
                BindingFlags.InvokeMethod,
                null,
                shellApp,
                new object[] { Path.GetDirectoryName(lnkPath) ?? "" });
            if (folder is null) return null;

            var items = folder.GetType().InvokeMember(
                "Items",
                BindingFlags.InvokeMethod,
                null,
                folder,
                null);
            if (items is null) return null;

            var item = items.GetType().InvokeMember(
                "Item",
                BindingFlags.InvokeMethod,
                null,
                items,
                new object[] { Path.GetFileName(lnkPath) });
            if (item is null) return null;

            var link = item.GetType().InvokeMember(
                "GetLink",
                BindingFlags.GetProperty,
                null,
                item,
                null);
            if (link is null) return null;

            var target = link.GetType().InvokeMember(
                "Path",
                BindingFlags.GetProperty,
                null,
                link,
                null) as string;

            return string.IsNullOrEmpty(target) ? null : target;
        }
        catch
        {
            return null;
        }
    }

    // --- Win32 imports ---

    private const uint GW_OWNER = 4;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
}
