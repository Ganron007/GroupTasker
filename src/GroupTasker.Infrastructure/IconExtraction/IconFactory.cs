using System.Drawing;
using System.Drawing.Imaging;

namespace GroupTasker.Infrastructure.IconExtraction;

/// <summary>
/// Pure algorithm: converts multiple PNG bitmaps into a single multi-resolution .ico file.
/// Ported from the original TaskbarGroups IconFactory.cs, modernised with null-guards and Span.
/// </summary>
public static class IconFactory
{
    public const int MaxIconSize = 256;

    private const ushort HeaderReserved = 0;
    private const ushort HeaderIconType = 1;
    private const byte HeaderLength = 6;
    private const byte EntryReserved = 0;
    private const byte EntryLength = 16;
    private const byte PngColorsInPalette = 0;
    private const ushort PngColorPlanes = 1;

    /// <summary>
    /// Write a multi-resolution .ico file from an ordered collection of PNG bitmaps.
    /// Images must be Format32bppArgb and ≤ 256×256.
    /// </summary>
    public static void SavePngsAsIcon(IReadOnlyList<Bitmap> images, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(stream);

        if (images.Count == 0)
            throw new ArgumentException("At least one image is required.", nameof(images));

        var ordered = images
            .OrderBy(i => i.Width)
            .ThenBy(i => i.Height)
            .ToArray();

        using var writer = new BinaryWriter(stream);

        // ICO header
        writer.Write(HeaderReserved);
        writer.Write(HeaderIconType);
        writer.Write((ushort)ordered.Length);

        // Build image buffers and directory entries
        var buffers = new Dictionary<uint, byte[]>();
        uint lengthSum = 0;
        uint baseOffset = HeaderLength + (uint)(EntryLength * ordered.Length);

        for (var i = 0; i < ordered.Length; i++)
        {
            var image = ordered[i];
            var buffer = CreatePngBuffer(image);
            var offset = baseOffset + lengthSum;

            writer.Write(GetIconDimension(image.Width));
            writer.Write(GetIconDimension(image.Height));
            writer.Write(PngColorsInPalette);
            writer.Write(EntryReserved);
            writer.Write(PngColorPlanes);
            writer.Write((ushort)Image.GetPixelFormatSize(image.PixelFormat));
            writer.Write((uint)buffer.Length);
            writer.Write(offset);

            lengthSum += (uint)buffer.Length;
            buffers.Add(offset, buffer);
        }

        // Write image data at correct offsets
        foreach (var (offset, buffer) in buffers)
        {
            writer.BaseStream.Seek(offset, SeekOrigin.Begin);
            writer.Write(buffer);
        }
    }

    private static byte GetIconDimension(int size) =>
        size >= MaxIconSize ? (byte)0 : (byte)size;

    private static byte[] CreatePngBuffer(Bitmap image)
    {
        using var ms = new MemoryStream();
        image.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    /// <summary>Resize an image with high-quality bicubic interpolation.</summary>
    public static Bitmap ResizeImage(Image source, int width, int height)
    {
        var dest = new Bitmap(width, height);
        dest.SetResolution(source.HorizontalResolution, source.VerticalResolution);

        using var g = Graphics.FromImage(dest);
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
        g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        using var wrap = new ImageAttributes();
        wrap.SetWrapMode(System.Drawing.Drawing2D.WrapMode.TileFlipXY);
        g.DrawImage(source,
            new Rectangle(0, 0, width, height),
            0, 0, source.Width, source.Height,
            GraphicsUnit.Pixel, wrap);

        return dest;
    }
}
