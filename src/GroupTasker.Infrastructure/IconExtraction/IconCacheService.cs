using System.Drawing;
using System.Drawing.Imaging;
using GroupTasker.Domain.Interfaces;
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

    public IconCacheService(IconExtractor extractor)
    {
        _extractor = extractor;
    }

    public async Task<string> GetIconPathAsync(DomainShortcut shortcut, string groupPath, CancellationToken ct = default)
    {
        var iconsDir = Path.Combine(groupPath, "Icons");
        Directory.CreateDirectory(iconsDir);

        // Key the cache by Shortcut.Id so identically-named binaries from different
        // folders cannot collide. The old name-based key silently aliased them.
        var iconPath = Path.Combine(iconsDir, $"{shortcut.Id:N}.png");

        if (File.Exists(iconPath))
            return iconPath;

        var sourcePath = shortcut.TargetPath ?? shortcut.SourcePath;

        await Task.Run(() =>
        {
            try
            {
                using var bitmap = _extractor.ExtractIcon(sourcePath);
                using var resized = IconFactory.ResizeImage(bitmap, 64, 64);
                SaveAtomic(resized, iconPath);
            }
            catch
            {
                using var fallback = new Bitmap(64, 64);
                using (var g = Graphics.FromImage(fallback))
                    g.Clear(Color.FromArgb(55, 55, 55));
                SaveAtomic(fallback, iconPath);
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
            catch
            {
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

    private Bitmap BuildCompositeIcon(DomainGroup group, int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.FromArgb(55, 55, 55));

        if (group.Shortcuts.Count == 0)
            return bmp;

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
                // Reuse the per-shortcut PNG when it already exists — avoids re-running
                // Shell/COM extraction every time the composite is regenerated.
                using var src = LoadCachedOrExtract(shortcut);
                g.DrawImage(src, x, y, sqSize, sqSize);
            }
            catch
            {
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
