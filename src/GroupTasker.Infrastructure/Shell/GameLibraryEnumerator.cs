using System.Text.Json;
using System.Text.RegularExpressions;
using GroupTasker.Domain.Interfaces;
using Microsoft.Win32;

namespace GroupTasker.Infrastructure.Shell;

/// <summary>
/// Discovers installed games from all major launchers and stores:
/// <list type="bullet">
/// <item><b>Start Menu scan</b> (generic, covers the long tail): every launcher —
/// EA app, Ubisoft Connect, Rockstar Games Launcher, Battle.net, Riot, Amazon
/// Games, itch.io, … — registers its installed games and its own launcher as
/// Start Menu shortcuts. Those .lnk files carry the correct launch arguments /
/// protocol URL, so they are kept as the launch target.</item>
/// <item><b>Steam</b>: parses <c>libraryfolders.vdf</c> for every library and
/// finds each game's exe under <c>steamapps\common</c> (Steam Start Menu
/// shortcuts are skipped here because this scan is more complete).</item>
/// <item><b>Epic</b>: parses the launcher's <c>Manifests\*.item</c> JSON
/// (Epic shortcuts are disabled by default, so the Start Menu scan misses them).</item>
/// <item><b>GOG</b>: reads the <c>GOG.com\Games</c> registry install records.</item>
/// </list>
/// Never throws — every source is defensive and returns what it can.
/// </summary>
public sealed class GameLibraryEnumerator : IGameLibraryEnumerator
{
    // Start Menu folder segments that identify a game shortcut. A segment matches
    // when it equals the marker or starts with "marker " (so "ea" matches
    // "EA app" but not "EaseUS").
    private static readonly string[] GameFolderMarkers =
    [
        "epic", "electronic arts", "ea games", "ea sports", "ea",
        "ubisoft", "ubisoft connect", "uplay", "rockstar", "rockstar games",
        "battle.net", "blizzard", "blizzard entertainment", "activision",
        "riot games", "riot", "amazon games", "amazon", "gog", "gog galaxy",
        "gog.com", "itch", "itch.io", "bethesda", "2k games", "2k", "sega",
        "xbox games", "xbox", "paradox", "paradox interactive", "capcom",
        "square enix", "bandai namco", "games"
    ];

    // Folders whose shortcuts are intentionally skipped in the Start Menu scan:
    // Steam and Epic get a dedicated, more complete library scan instead.
    private static readonly string[] SkippedStartMenuFolders = ["steam", "epic games"];

    // .lnk files (in any folder) that ARE the launcher itself — these are always
    // included, so users can add the launchers to groups too.
    private static readonly string[] LauncherLnkNames =
    [
        "steam", "epic games launcher", "ea app", "ea", "origin", "ubisoft connect",
        "uplay", "rockstar games launcher", "battle.net", "gog galaxy",
        "amazon games", "itch", "itch.io", "riot client", "riot games launcher",
        "xbox", "minecraft launcher"
    ];

    // Start Menu .lnk targets under these paths are treated as games even when
    // the shortcut sits outside a recognisable launcher folder.
    private static readonly string[] TargetPathMarkers =
    [
        "steamapps", "epic games", "ea games", "electronic arts", "ubisoft",
        "rockstar games", "battle.net", "riot games", "gog games", "amazon games"
    ];

    // URL protocols used by game launchers (steam://, com.epicgames.launcher://, …).
    private static readonly string[] GameProtocols =
    [
        "steam://", "com.epicgames.launcher://", "uplay://", "ubisoftconnect://",
        "battle.net://", "ealink://", "link2ea://", "origin2://",
        "rockstarlauncher://", "com.riotgames://"
    ];

    // Exes that are launchers, not games. When a shortcut resolves to one of
    // these the game identity lives in the shortcut (its arguments / URL), so
    // the shortcut is kept as the launch target and deduped by its own path.
    private static readonly HashSet<string> LauncherExes = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam.exe", "epicgameslauncher.exe", "eadesktop.exe", "ealauncher.exe",
        "origin.exe", "upc.exe", "ubisoftconnect.exe", "rockstargameslauncher.exe",
        "launcher.exe", "battle.net.exe", "battle.net launcher.exe",
        "goggalaxy.exe", "galaxyclient.exe", "amazongames.exe", "riotclient.exe"
    };

    private static readonly string[] JunkExeNames =
    [
        "unins", "crashhandler", "crashreporter", "crashreport", "redist",
        "dxsetup", "dotnet", "report", "cefprocess"
    ];

    private static readonly string[] JunkDirPrefixes =
    [
        "redist", "_commonredist", "directx", "dotnet", "vcredist", "crashhandler"
    ];

    public IReadOnlyList<DiscoveredApp> Enumerate()
    {
        var results = new List<DiscoveredApp>();
        try { results.AddRange(EnumerateStartMenuGames()); } catch { /* never throw */ }
        try { results.AddRange(EnumerateSteamGames()); } catch { /* never throw */ }
        try { results.AddRange(EnumerateEpicGames()); } catch { /* never throw */ }
        try { results.AddRange(EnumerateGogGames()); } catch { /* never throw */ }
        return results;
    }

    /// <summary>True when the exe is a game launcher rather than a game itself.</summary>
    public static bool IsLauncherExe(string exePath)
    {
        var name = Path.GetFileName(exePath);
        return !string.IsNullOrEmpty(name) && LauncherExes.Contains(name);
    }

    // ── Start Menu scan ──────────────────────────────────────────────

    private static IEnumerable<DiscoveredApp> EnumerateStartMenuGames()
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
        }
        .Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        foreach (var root in roots)
        {
            foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
            {
                var entry = TryBuildStartMenuEntry(root, lnk);
                if (entry is not null) yield return entry;
            }
        }
    }

    private static DiscoveredApp? TryBuildStartMenuEntry(string root, string lnk)
    {
        string name;
        string[] segments;
        try
        {
            name = Path.GetFileNameWithoutExtension(lnk);
            var relDir = Path.GetDirectoryName(Path.GetRelativePath(root, lnk)) ?? "";
            segments = relDir.Split(
                Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar,
                StringSplitOptions.RemoveEmptyEntries);
        }
        catch
        {
            return null;
        }

        var isLauncher = LauncherLnkNames.Any(l => name.Equals(l, StringComparison.OrdinalIgnoreCase));
        var inSkippedFolder = segments.Any(s =>
            SkippedStartMenuFolders.Any(f => s.Equals(f, StringComparison.OrdinalIgnoreCase)));
        if (inSkippedFolder && !isLauncher) return null;

        var inGameFolder = segments.Any(s => GameFolderMarkers.Any(m =>
            s.Equals(m, StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith(m + " ", StringComparison.OrdinalIgnoreCase)));

        string? exe = null;
        string? url = null;
        if (!isLauncher)
        {
            string target;
            try
            {
                (target, _, _, _, _) = ShellLinkInterop.ReadShortcut(lnk);
            }
            catch
            {
                target = "";
            }

            if (!string.IsNullOrWhiteSpace(target))
            {
                try
                {
                    var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(target));
                    if (File.Exists(full)) exe = full;
                }
                catch { /* unreadable target — keep exe null */ }
            }
            else
            {
                url = ShellLinkInterop.TryReadTargetUrl(lnk);
            }

            if (!inGameFolder)
            {
                var exeIsGame = exe is not null &&
                    TargetPathMarkers.Any(m => exe.Contains(m, StringComparison.OrdinalIgnoreCase));
                var urlIsGame = url is not null &&
                    GameProtocols.Any(p => url.StartsWith(p, StringComparison.OrdinalIgnoreCase));
                if (!exeIsGame && !urlIsGame) return null;
            }
        }

        return new DiscoveredApp
        {
            DisplayName = name,
            ProcessName = exe is null ? null : Path.GetFileNameWithoutExtension(exe),
            ExecutablePath = exe,
            LnkPath = lnk,
            Source = DiscoveredAppSource.GameLibrary
        };
    }

    // ── Steam ───────────────────────────────────────────────────────

    private static IEnumerable<DiscoveredApp> EnumerateSteamGames()
    {
        var libraryRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string[] steamSteamapps =
        [
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steamapps")
        ];

        foreach (var root in steamSteamapps)
        {
            if (!Directory.Exists(root)) continue;
            libraryRoots.Add(root);

            // libraryfolders.vdf lists every extra library location (D:\Games, …)
            var vdfPath = Path.Combine(root, "libraryfolders.vdf");
            if (!File.Exists(vdfPath)) continue;

            try
            {
                foreach (Match m in Regex.Matches(
                    File.ReadAllText(vdfPath), "\"path\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\""))
                {
                    var lib = m.Groups[1].Value.Replace("\\\\", "\\");
                    var candidate = Path.Combine(lib, "steamapps");
                    if (Directory.Exists(candidate)) libraryRoots.Add(candidate);
                }
            }
            catch { /* unreadable vdf — keep default roots */ }
        }

        // appmanifest_<id>.acf maps "installdir" (common\<folder>) to "name".
        var namesByInstallDir = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in libraryRoots)
        {
            try
            {
                foreach (var acf in Directory.EnumerateFiles(root, "appmanifest_*.acf"))
                {
                    var text = File.ReadAllText(acf);
                    var n = Regex.Match(text, "\"name\"\\s+\"([^\"]*)\"").Groups[1].Value;
                    var d = Regex.Match(text, "\"installdir\"\\s+\"([^\"]*)\"").Groups[1].Value;
                    if (!string.IsNullOrEmpty(d)) namesByInstallDir[d] = n;
                }
            }
            catch { /* skip library */ }
        }

        foreach (var root in libraryRoots)
        {
            var common = Path.Combine(root, "common");
            if (!Directory.Exists(common)) continue;

            foreach (var gameDir in Directory.EnumerateDirectories(common))
            {
                var folderName = Path.GetFileName(gameDir);
                if (folderName.Equals("Steamworks Shared", StringComparison.OrdinalIgnoreCase))
                    continue;

                var exe = FindGameExe(gameDir);
                if (exe is null) continue;

                var displayName = namesByInstallDir.TryGetValue(folderName, out var n) &&
                                  !string.IsNullOrWhiteSpace(n)
                    ? n
                    : folderName;

                yield return new DiscoveredApp
                {
                    DisplayName = displayName,
                    ProcessName = Path.GetFileNameWithoutExtension(exe),
                    ExecutablePath = exe,
                    Source = DiscoveredAppSource.GameLibrary
                };
            }
        }
    }

    /// <summary>
    /// Pick the game's main executable inside its install folder. Recursively
    /// lists *.exe, drops launcher/redistributable junk, and keeps the largest
    /// remaining file — the main game binary is almost always the biggest.
    /// </summary>
    private static string? FindGameExe(string gameDir)
    {
        try
        {
            string? best = null;
            long bestSize = -1;

            foreach (var exe in Directory.EnumerateFiles(gameDir, "*.exe", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(gameDir, exe);
                if (JunkDirPrefixes.Any(d =>
                    rel.StartsWith(d, StringComparison.OrdinalIgnoreCase))) continue;

                var name = Path.GetFileNameWithoutExtension(exe);
                if (JunkExeNames.Any(j => name.Contains(j, StringComparison.OrdinalIgnoreCase))) continue;

                long size;
                try { size = new FileInfo(exe).Length; } catch { size = 0; }
                if (size > bestSize)
                {
                    bestSize = size;
                    best = exe;
                }
            }

            return best;
        }
        catch
        {
            return null;
        }
    }

    // ── Epic ────────────────────────────────────────────────────────

    private static IEnumerable<DiscoveredApp> EnumerateEpicGames()
    {
        var manifestsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(manifestsDir)) yield break;

        foreach (var item in Directory.EnumerateFiles(manifestsDir, "*.item"))
        {
            EpicManifest? manifest;
            try
            {
                manifest = JsonSerializer.Deserialize<EpicManifest>(File.ReadAllText(item));
            }
            catch
            {
                manifest = null;
            }

            if (manifest is null) continue;
            if (string.IsNullOrWhiteSpace(manifest.InstallLocation) ||
                string.IsNullOrWhiteSpace(manifest.LaunchExecutable) ||
                string.IsNullOrWhiteSpace(manifest.DisplayName)) continue;

            string exe;
            try
            {
                exe = Path.IsPathRooted(manifest.LaunchExecutable)
                    ? manifest.LaunchExecutable
                    : Path.GetFullPath(Path.Combine(manifest.InstallLocation, manifest.LaunchExecutable));
            }
            catch
            {
                continue;
            }
            if (!File.Exists(exe)) continue;

            yield return new DiscoveredApp
            {
                DisplayName = manifest.DisplayName,
                ProcessName = Path.GetFileNameWithoutExtension(exe),
                ExecutablePath = exe,
                Source = DiscoveredAppSource.GameLibrary
            };
        }
    }

    private sealed class EpicManifest
    {
        public string? DisplayName { get; set; }
        public string? InstallLocation { get; set; }
        public string? LaunchExecutable { get; set; }
    }

    // ── GOG ─────────────────────────────────────────────────────────

    private static IEnumerable<DiscoveredApp> EnumerateGogGames()
    {
        foreach (var baseKey in new[] { @"SOFTWARE\WOW6432Node\GOG.com\Games", @"SOFTWARE\GOG.com\Games" })
        {
            using var games = Registry.LocalMachine.OpenSubKey(baseKey);
            if (games is null) continue;

            foreach (var gameId in games.GetSubKeyNames())
            {
                using var game = games.OpenSubKey(gameId);
                if (game is null) continue;

                var name = game.GetValue("gamename") as string;
                var install = game.GetValue("path") as string;
                var exe = game.GetValue("exe") as string ?? game.GetValue("launchCommand") as string;
                if (string.IsNullOrWhiteSpace(install) || string.IsNullOrWhiteSpace(exe)) continue;

                exe = exe.Trim().Trim('"');
                string full;
                try
                {
                    full = Path.IsPathRooted(exe)
                        ? exe
                        : Path.GetFullPath(Path.Combine(install, exe));
                }
                catch
                {
                    continue;
                }
                if (!File.Exists(full)) continue;

                yield return new DiscoveredApp
                {
                    DisplayName = string.IsNullOrWhiteSpace(name)
                        ? Path.GetFileNameWithoutExtension(full)
                        : name,
                    ProcessName = Path.GetFileNameWithoutExtension(full),
                    ExecutablePath = full,
                    Source = DiscoveredAppSource.GameLibrary
                };
            }
        }
    }
}
