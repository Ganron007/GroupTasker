using System.Drawing;
using System.Drawing.Imaging;
using GroupTasker.Domain.Entities;
using GroupTasker.Domain.Interfaces;
using GroupTasker.Domain.Logging;
using DomainGroup = GroupTasker.Domain.Entities.Group;
using DomainShortcut = GroupTasker.Domain.Entities.Shortcut;

namespace GroupTasker.Infrastructure.IconExtraction;

/// <summary>
/// Manages the icon cache for groups and shortcuts on disk.
/// Each group gets an <c>Icons/</c> folder with one PNG per shortcut, keyed by the
/// shortcut's <see cref="DomainShortcut.Id"/> so two same-named executables from
/// different folders don't collide.
/// </summary>
public sealed class IconCacheService : IIconCacheService
{
    private readonly IconExtractor _extractor;
    private readonly ILiveAppResolver? _liveResolver;
    private readonly ILogger _logger;

    /// <summary>
    /// The minimum size (in bytes) of a real icon PNG. The fallback gray
    /// bitmap is exactly 244 bytes; anything significantly larger is a
    /// successful extraction. Used to detect stale fallbacks left in the
    /// cache by earlier broken builds so they get re-extracted on the next
    /// save.
    /// </summary>
    private const int FallbackSizeBytes = 300;

    public IconCacheService(IconExtractor extractor, ILiveAppResolver? liveResolver = null, ILogger? logger = null)
    {
        _extractor = extractor;
        _liveResolver = liveResolver;
        _logger = logger ?? NullLogger.Instance;
    }

    public async Task<string> GetIconPathAsync(DomainShortcut shortcut, string groupPath, CancellationToken ct = default)
    {
        var iconsDir = Path.Combine(groupPath, "Icons");
        Directory.CreateDirectory(iconsDir);

        // Key the cache by Shortcut.Id so identically-named binaries from different
        // folders cannot collide. The old name-based key silently aliased them.
        var iconPath = Path.Combine(iconsDir, $"{shortcut.Id:N}.png");

        // Skip only if a *real* icon already exists. The fallback gray bitmap
        // is exactly 244 bytes — anything smaller is treated as stale and
        // re-extracted. This handles the case where an earlier build left a
        // bad icon in the cache.
        if (File.Exists(iconPath) && new FileInfo(iconPath).Length > FallbackSizeBytes)
            return iconPath;

        // Resolve the source for icon extraction. LiveApplication shortcuts
        // store an AUMI or process name, not a file path.
        var sourcePath = ResolveSourceForIconExtraction(shortcut);

        await Task.Run(() =>
        {
            try
            {
                if (sourcePath is null || !File.Exists(sourcePath))
                {
                    SaveFallback(iconPath);
                    return;
                }

                using var bitmap = _extractor.ExtractIcon(sourcePath);
                using var resized = IconFactory.ResizeImage(bitmap, 64, 64);
                SaveAtomic(resized, iconPath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to extract icon for shortcut {ShortcutId} from {SourcePath}", shortcut.Id, sourcePath);
                SaveFallback(iconPath);
            }
        }, ct).ConfigureAwait(false);

        return iconPath;
    }

    public async Task<string> BuildGroupIconAsync(DomainGroup group, string groupPath, CancellationToken ct = default)
    {
        var iconPath = Path.Combine(groupPath, "GroupIcon.ico");
        Directory.CreateDirectory(groupPath);

        await Task.Run(() =>
        {
            try
            {
                var bitmaps = new List<Bitmap>();
                try
                {
                    foreach (var size in IIconCacheService.IconSizes)
                    {
                        ct.ThrowIfCancellationRequested();
                        bitmaps.Add(BuildCompositeIcon(group, size));
                    }

                    using var ms = new MemoryStream();
                    IconFactory.SavePngsAsIcon(bitmaps, ms);
                    File.WriteAllBytes(iconPath + ".tmp", ms.ToArray());
                    File.Move(iconPath + ".tmp", iconPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Failed to build composite icon for group {GroupId} ({GroupName}); using solid fallback", group.Id, group.Name);
                    // Fallback: solid-fill icon so the .lnk has *something* on disk.
                    foreach (var bmp in bitmaps) bmp.Dispose();
                    bitmaps.Clear();

                    foreach (var size in IIconCacheService.IconSizes)
                    {
                        var bmp = new Bitmap(size, size);
                        using (var g = Graphics.FromImage(bmp))
                            g.Clear(Color.FromArgb(55, 55, 55));
                        bitmaps.Add(bmp);
                    }

                    using var ms = new MemoryStream();
                    IconFactory.SavePngsAsIcon(bitmaps, ms);
                    File.WriteAllBytes(iconPath + ".tmp", ms.ToArray());
                    File.Move(iconPath + ".tmp", iconPath, overwrite: true);
                }
                finally
                {
                    foreach (var bmp in bitmaps) bmp.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Fatal failure building group icon for {GroupId} ({GroupName})", group.Id, group.Name);
            }
        }, ct).ConfigureAwait(false);

        return iconPath;
    }

    public async Task RebuildCacheAsync(DomainGroup group, string groupPath, CancellationToken ct = default)
    {
        var iconsDir = Path.Combine(groupPath, "Icons");
        if (Directory.Exists(iconsDir))
            Directory.Delete(iconsDir, recursive: true);

        Directory.CreateDirectory(iconsDir);

        foreach (var shortcut in group.Shortcuts)
        {
            ct.ThrowIfCancellationRequested();
            shortcut.IconPath = await GetIconPathAsync(shortcut, groupPath, ct).ConfigureAwait(false);
        }

        await BuildGroupIconAsync(group, groupPath, ct).ConfigureAwait(false);
        group.MarkIconCacheClean();
    }

    /// <summary>
    /// Pick the right file path to extract an icon from. For LiveApplication
    /// shortcuts the stored Source/TargetPath is an AUMI or process name, not
    /// a file, so we use <see cref="ILiveAppResolver"/> to find the current
    /// .exe. For regular shortcuts, just return the stored path.
    /// </summary>
    private string? ResolveSourceForIconExtraction(DomainShortcut shortcut)
    {
        // 1) Highest priority: an explicit icon source (.ico file or
        //    specific .exe with index) from the .lnk metadata.
        if (!string.IsNullOrEmpty(shortcut.IconSourcePath) && File.Exists(shortcut.IconSourcePath))
            return shortcut.IconSourcePath;

        // 2) For LiveApplication shortcuts, the stored Source/TargetPath is
        //    an AUMI or process name, not a file, so use the resolver.
        if (shortcut.Type == ShortcutType.LiveApplication)
        {
            if (_liveResolver is null) return null;

            var lookup = !string.IsNullOrEmpty(shortcut.TargetPath) && shortcut.TargetPath.Contains('!')
                ? shortcut.TargetPath
                : shortcut.SourcePath;

            if (string.IsNullOrEmpty(lookup)) return null;
            return _liveResolver.Resolve(lookup);
        }

        // 3) Fall back to the resolved target or original source path.
        return shortcut.TargetPath ?? shortcut.SourcePath;
    }

    private static void SaveFallback(string iconPath)
    {
        using var fallback = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(fallback))
            g.Clear(Color.FromArgb(55, 55, 55));
        SaveAtomic(fallback, iconPath);
    }

    private Bitmap BuildCompositeIcon(DomainGroup group, int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(55, 55, 55));

        if (group.Shortcuts.Count == 0) return bmp;

        var margin = Math.Max(2, size / 8);
        var gap = Math.Max(1, size / 16);
        var sqSize = (size - 2 * margin - gap) / 2;
        var count = Math.Min(group.Shortcuts.Count, 4);

        for (var i = 0; i < count; i++)
        {
            var row = i / 2;
            var col = i % 2;
            var x = margin + col * (sqSize + gap);
            var y = margin + row * (sqSize + gap);
            var shortcut = group.Shortcuts[i];

            try
            {
                using var src = LoadCachedOrExtract(shortcut);
                g.DrawImage(src, x, y, sqSize, sqSize);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to render shortcut {ShortcutId} icon into composite for group {GroupId}", shortcut.Id, group.Id);
                using var brush = new SolidBrush(Color.FromArgb(90, 90, 90));
                g.FillRectangle(brush, x, y, sqSize, sqSize);
            }
        }

        return bmp;
    }

    private Bitmap LoadCachedOrExtract(DomainShortcut shortcut)
    {
        if (!string.IsNullOrEmpty(shortcut.IconPath) && File.Exists(shortcut.IconPath))
            return new Bitmap(shortcut.IconPath);

        return _extractor.ExtractIcon(shortcut.TargetPath ?? shortcut.SourcePath);
    }

    private static void SaveAtomic(Bitmap bmp, string finalPath)
    {
        var tmp = finalPath + ".tmp";
        bmp.Save(tmp, ImageFormat.Png);
        File.Move(tmp, finalPath, overwrite: true);
    }
}
