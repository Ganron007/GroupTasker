using System.Collections.Concurrent;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml;

namespace GroupTasker.Infrastructure.IconExtraction;

/// <summary>
/// Extracts icons from executables, shortcuts, folders, and Windows Store apps.
/// Thread-safe — the package-folder cache uses <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// so concurrent calls during a bulk rebuild don't race.
/// </summary>
public sealed class IconExtractor
{
    private readonly ConcurrentDictionary<string, string> _packageDirCache = new();

    public Bitmap ExtractIcon(string sourcePath)
    {
        try
        {
            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();

            return ext switch
            {
                ".lnk" => ExtractFromLink(sourcePath),
                ".exe" => ExtractFromExe(sourcePath),
                _ when IsStoreApp(sourcePath) => ExtractFromStoreApp(sourcePath),
                _ when Directory.Exists(sourcePath) => ExtractFromFolder(sourcePath),
                _ => ExtractFromExe(sourcePath)
            };
        }
        catch
        {
            return SafeExtract(sourcePath);
        }
    }

    private Bitmap ExtractFromLink(string filePath)
    {
        try
        {
            // Use typed IShellLinkW via ShellLinkInterop rather than late-bound WScript.Shell.
            var (target, _, _) = Shell.ShellLinkInterop.ReadShortcut(filePath);

            if (!string.IsNullOrEmpty(target))
            {
                var resolvedTarget = Path.GetFullPath(Environment.ExpandEnvironmentVariables(target));
                if (File.Exists(resolvedTarget))
                {
                    using var icon = Icon.ExtractAssociatedIcon(resolvedTarget);
                    if (icon is not null) return icon.ToBitmap();
                }
            }

            return ExtractFromStoreApp(filePath);
        }
        catch
        {
            return SafeExtract(filePath);
        }
    }

    private static Bitmap ExtractFromExe(string filePath)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(Path.GetFullPath(filePath));
            return icon?.ToBitmap() ?? SafeExtract(filePath);
        }
        catch
        {
            return SafeExtract(filePath);
        }
    }

    private static Bitmap ExtractFromFolder(string folderPath)
    {
        var flags = SHGFI_ICON | SHGFI_LARGEICON;
        var shfi = new SHFILEINFO();
        var res = SHGetFileInfo(folderPath, FILE_ATTRIBUTE_DIRECTORY, out shfi,
            (uint)Marshal.SizeOf<SHFILEINFO>(), flags);

        if (res == IntPtr.Zero || shfi.hIcon == IntPtr.Zero)
            return SafeExtract(folderPath);

        try
        {
            using var icon = (Icon)Icon.FromHandle(shfi.hIcon).Clone();
            return icon.ToBitmap();
        }
        finally
        {
            DestroyIcon(shfi.hIcon);
        }
    }

    public Bitmap ExtractFromStoreApp(string appId)
    {
        try
        {
            var name = appId.Contains('!') ? appId : ResolveStoreLinkTarget(appId);
            var subAppName = name.Split('!')[0];
            var appPath = FindPackageFolder(subAppName);

            if (string.IsNullOrEmpty(appPath) || !File.Exists(Path.Combine(appPath, "AppxManifest.xml")))
                return SafeExtract(appId);

            var manifest = new XmlDocument();
            manifest.Load(Path.Combine(appPath, "AppxManifest.xml"));

            var nsmgr = new XmlNamespaceManager(new NameTable());
            nsmgr.AddNamespace("sm", "http://schemas.microsoft.com/appx/manifest/foundation/windows10");

            var logoNode = manifest.SelectSingleNode("/sm:Package/sm:Properties/sm:Logo", nsmgr);
            if (logoNode is null) return SafeExtract(appId);

            var logoRelPath = logoNode.InnerText.Replace('\\', Path.DirectorySeparatorChar);
            var logoDir = Path.GetDirectoryName(logoRelPath) ?? "";
            var logoDirFull = Path.GetFullPath(Path.Combine(appPath, logoDir));

            if (!Directory.Exists(logoDirFull)) return SafeExtract(appId);

            var dir = new DirectoryInfo(logoDirFull);
            var files = FindLogoFiles(dir, "StoreLogo") ?? FindLogoFiles(dir, "scale-200");

            if (files is { Length: > 0 })
            {
                var logoPath = files[^1].FullName;
                using var ms = new MemoryStream(File.ReadAllBytes(logoPath));
                return IconFactory.ResizeImage(new Bitmap(ms), 64, 64);
            }

            return SafeExtract(appId);
        }
        catch
        {
            return SafeExtract(appId);
        }
    }

    public string GetStoreAppName(string appId)
    {
        try
        {
            var subAppName = appId.Split('!')[0];
            var appPath = FindPackageFolder(subAppName);

            if (string.IsNullOrEmpty(appPath)) return subAppName;

            var manifest = new XmlDocument();
            manifest.Load(Path.Combine(appPath, "AppxManifest.xml"));

            var nsmgr = new XmlNamespaceManager(new NameTable());
            nsmgr.AddNamespace("sm", "http://schemas.microsoft.com/appx/manifest/foundation/windows10");
            nsmgr.AddNamespace("uap", "http://schemas.microsoft.com/appx/manifest/uap/windows10");

            var node = manifest.SelectSingleNode(
                "/sm:Package/sm:Applications/sm:Application/uap:VisualElements", nsmgr);
            var visual = node?.Attributes?["DisplayName"]?.InnerText;
            if (!string.IsNullOrEmpty(visual)) return visual;

            var propsNode = manifest.SelectSingleNode("/sm:Package/sm:Properties/sm:DisplayName", nsmgr);
            return propsNode?.InnerText ?? subAppName;
        }
        catch
        {
            return appId;
        }
    }

    private static Bitmap SafeExtract(string _)
    {
        try { return SystemIcons.Application.ToBitmap(); }
        catch { return new Bitmap(64, 64); }
    }

    private string FindPackageFolder(string subAppName) =>
        _packageDirCache.GetOrAdd(subAppName, key =>
        {
            try
            {
                var winApps = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");

                if (!Directory.Exists(winApps)) return "";

                var dir = new DirectoryInfo(winApps);
                var match = dir.GetDirectories($"{key}_*", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault();

                return match?.FullName ?? "";
            }
            catch
            {
                return "";
            }
        });

    private static FileInfo[]? FindLogoFiles(DirectoryInfo dir, string pattern)
    {
        try
        {
            var files = dir.GetFiles($"*{pattern}*.*", SearchOption.AllDirectories);
            return files.Length > 0 ? files : null;
        }
        catch { return null; }
    }

    private static bool IsStoreApp(string path) =>
        path.Contains('!', StringComparison.Ordinal) ||
        path.StartsWith("shell:appsFolder", StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolve a store app .lnk to its AppUserModelId-style path via late-bound Shell.Application.</summary>
    private static string ResolveStoreLinkTarget(string lnkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return lnkPath;

            var shellApp = Activator.CreateInstance(shellType);
            if (shellApp is null) return lnkPath;

            var fullPath = Path.GetFullPath(lnkPath);
            var folder = Invoke(shellApp, "NameSpace", Path.GetDirectoryName(fullPath)!);
            if (folder is null) return lnkPath;

            var items = Invoke(folder, "Items");
            var item = items is null ? null : Invoke(items, "Item", Path.GetFileName(fullPath));
            var link = item is null ? null : item.GetType().InvokeMember("GetLink",
                BindingFlags.GetProperty, null, item, null);
            if (link is null) return lnkPath;

            var targetObj = link.GetType().InvokeMember("Target",
                BindingFlags.GetProperty, null, link, null);
            if (targetObj is null) return lnkPath;

            var path = targetObj.GetType().InvokeMember("Path",
                BindingFlags.GetProperty, null, targetObj, null) as string;
            return string.IsNullOrEmpty(path) ? lnkPath : path;
        }
        catch
        {
            return lnkPath;
        }
    }

    private static object? Invoke(object target, string method, params object[] args) =>
        target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);

    // --- Shell32 folder icon interop ---

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;

        public SHFILEINFO()
        {
            szDisplayName = "";
            szTypeName = "";
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        out SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
}
