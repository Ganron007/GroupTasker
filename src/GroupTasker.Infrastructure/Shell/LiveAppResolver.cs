using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using GroupTasker.Domain.Interfaces;
using Microsoft.Win32;

namespace GroupTasker.Infrastructure.Shell;

/// <summary>
/// Resolves the current .exe path for a "live" shortcut stored by AUMI or
/// process name. Survives app updates because it locates the running
/// instance or queries the App Paths registry rather than relying on a
/// stored path.
/// </summary>
public sealed class LiveAppResolver : ILiveAppResolver
{
    // Matches a WindowsApps package folder: {Name}_{Version}_{Arch}__{FamilyName}
    // The "double underscore" before FamilyName is the key signal.
    private static readonly Regex WindowsAppsPackagePattern = new(
        @"\\WindowsApps\\[^\\]+__(?<family>[A-Za-z0-9]+)\\",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string? Resolve(string aumiOrProcessName)
    {
        if (string.IsNullOrWhiteSpace(aumiOrProcessName)) return null;

        // 1) If a process is running with that name, use its current .exe path
        //    (filtered to prefer WindowsApps over AppData).
        var fromRunning = ResolveFromRunningProcess(aumiOrProcessName);
        if (fromRunning is not null) return fromRunning;

        // 2) If the input is an AUMI with a family name, scan WindowsApps
        //    for the latest installed version. Works even when the app is
        //    not currently running.
        if (aumiOrProcessName.Contains('!'))
        {
            var fromWindowsApps = ResolveFromWindowsApps(aumiOrProcessName);
            if (fromWindowsApps is not null) return fromWindowsApps;
        }

        // 3) Check the App Paths registry hive
        var fromAppPaths = ResolveFromAppPathsRegistry(aumiOrProcessName);
        if (fromAppPaths is not null) return fromAppPaths;

        // 4) Search PATH
        return ResolveFromPath(aumiOrProcessName);
    }

    private static string? ResolveFromRunningProcess(string name)
    {
        try
        {
            var processName = StripAumiToProcessName(name);

            var processes = Process.GetProcessesByName(processName);
            if (processes.Length == 0) return null;

            // Prefer processes running under C:\Program Files\WindowsApps\
            // over AppData installs.
            var windowsApps = new List<string>();
            var appData = new List<string>();

            foreach (var p in processes)
            {
                try
                {
                    var path = p.MainModule?.FileName;
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) continue;

                    if (path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
                        windowsApps.Add(path);
                    else
                        appData.Add(path);
                }
                catch
                {
                    // Some system processes deny MainModule access
                }
                finally
                {
                    p.Dispose();
                }
            }

            if (windowsApps.Count > 0)
                return windowsApps.OrderBy(p => p.Length).First();
            if (appData.Count > 0)
                return appData.First();
        }
        catch
        {
        }
        return null;
    }

    /// <summary>
    /// For an AUMI like "Claude_pzs8sxrjxfjjc!Claude", find the latest
    /// installed package under C:\Program Files\WindowsApps\ and return the
    /// path to its main executable. Works whether or not the app is
    /// currently running — the WindowsApps folder is the canonical install
    /// location for MSIX apps and persists across updates.
    /// </summary>
    private static string? ResolveFromWindowsApps(string aumi)
    {
        try
        {
            // Extract the family name (everything before the "!" in the AUMI).
            var family = aumi.Split('!')[0];
            if (string.IsNullOrEmpty(family)) return null;

            // The AUMI family has the form "Name_PublisherId" (e.g.
            // "Claude_pzs8sxrjxfjjc"). The WindowsApps folder name is
            // "Name_Version_Arch__PublisherId". The AppId is the bit after
            // the "!" which the manifest stores in AppxManifest.xml. Since
            // we don't have a guarantee the AppId is "App", we accept any
            // *.exe under app/ that matches the family.
            var appsDir = @"C:\Program Files\WindowsApps";
            if (!Directory.Exists(appsDir)) return null;

            // Find package folders that end in the family suffix.
            // Format: {Name}_{Version}_{Arch}__{FamilyName}
            var candidates = Directory.EnumerateDirectories(appsDir)
                .Where(d =>
                {
                    var name = Path.GetFileName(d);
                    return name.EndsWith("__" + family, StringComparison.OrdinalIgnoreCase);
                })
                .OrderByDescending(d => d)  // newest version first
                .ToList();

            if (candidates.Count == 0) return null;

            // Look in the first candidate for app\*.exe
            foreach (var dir in candidates)
            {
                var appSubdir = Path.Combine(dir, "app");
                if (!Directory.Exists(appSubdir)) continue;

                try
                {
                    var exe = Directory.EnumerateFiles(appSubdir, "*.exe")
                        .OrderByDescending(f => new FileInfo(f).Length)  // main exe is largest
                        .FirstOrDefault();
                    if (exe is not null && File.Exists(exe)) return exe;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private static string? ResolveFromAppPathsRegistry(string name)
    {
        try
        {
            var exeName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name
                : name + ".exe";

            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}");
            var path = key?.GetValue(null) as string;
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;

            using var userKey = Registry.CurrentUser.OpenSubKey(
                $@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exeName}");
            var userPath = userKey?.GetValue(null) as string;
            if (!string.IsNullOrEmpty(userPath) && File.Exists(userPath)) return userPath;
        }
        catch
        {
        }
        return null;
    }

    private static string? ResolveFromPath(string name)
    {
        try
        {
            var exeName = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? name
                : name + ".exe";

            var pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (string.IsNullOrEmpty(pathEnv)) return null;

            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                try
                {
                    var candidate = Path.Combine(dir, exeName);
                    if (File.Exists(candidate)) return candidate;
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private static string StripAumiToProcessName(string aumi)
    {
        if (!aumi.Contains('!')) return aumi;
        return aumi.Split('!')[0].Split('_')[0];
    }
}
