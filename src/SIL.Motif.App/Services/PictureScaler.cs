using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SIL.Motif.Commands;
using SkiaSharp;

namespace SIL.Motif.App.Services;

/// <summary>
/// Prepares a picture for a Proposal with Avalonia's imaging, the only place a picture is decoded: upright by its EXIF
/// orientation, at most <see cref="LongestSide"/> pixels on the long side (never enlarged), stripped of metadata by
/// being re-drawn, and written as WebP, lossless for a PNG and lossy at quality 80 for a JPEG. The original is not kept.
/// </summary>
public static class PictureScaler
{
    /// <summary>The longest side a scaled picture keeps, in pixels.</summary>
    public const int LongestSide = ProposalPictures.MaxSide;

    /// <summary>The quality of a lossy WebP, and of the JPEG written when WebP cannot be.</summary>
    public const int LossyQuality = 80;

    // Leaves room under the cap so one more encode is never the one that overflows.
    private const double Target = 0.9;
    private const int MaxAttempts = 12;

    /// <summary>The most pixels a source may declare before it is decoded: about 100 megapixels, some 400 MB decoded.</summary>
    public const long MaxSourcePixels = 100_000_000;

    /// <summary>Scales <paramref name="source"/>; the result says why when it cannot.</summary>
    public static PictureIntakeResult Scale(byte[] source, long maxBytes = ProposalPictures.MaxBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!LooksLikeAPicture(source)) return new PictureIntakeResult(PictureIntakeStatus.Unsupported);
        // The header says how big the picture is; a small file can declare a size no machine should decode.
        if (DeclaredPixels(source) > MaxSourcePixels) return new PictureIntakeResult(PictureIntakeStatus.TooLarge);
        try
        {
            using var decoded = new Bitmap(new MemoryStream(source, writable: false));
            var size = decoded.PixelSize;
            if (size.Width < 1 || size.Height < 1) return new PictureIntakeResult(PictureIntakeStatus.Unreadable);
            var lossless = !IsLossy(source);
            // A phone photograph is stored sideways with a tag saying how to turn it; Avalonia ignores the tag.
            var orientation = source[0] == 0xFF ? ExifOrientation(source) : 1;
            var scale = Math.Min(1.0, (double)LongestSide / Math.Max(size.Width, size.Height));
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var width = Math.Max(1, (int)Math.Round(size.Width * scale));
                var height = Math.Max(1, (int)Math.Round(size.Height * scale));
                using var scaled = width == size.Width && height == size.Height
                    ? null : decoded.CreateScaledBitmap(new PixelSize(width, height), BitmapInterpolationMode.HighQuality);
                using var upright = orientation == 1 ? null : Turned(scaled ?? decoded, orientation);
                var encoded = Encode(upright ?? scaled ?? decoded, lossless);
                if (encoded.Length <= maxBytes * Target) return new PictureIntakeResult(PictureIntakeStatus.Ready, encoded);
                if (width == 1 && height == 1) break;
                // Size follows pixel count, so shrink the sides by the square root of the overshoot.
                scale *= Math.Min(0.85, Math.Sqrt(maxBytes * Target / encoded.Length));
            }
            return new PictureIntakeResult(PictureIntakeStatus.TooLarge);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException
                                              or NotSupportedException)
        {
            return new PictureIntakeResult(PictureIntakeStatus.Unreadable);
        }
    }

    // A JPEG, and a WebP that is not the lossless kind, are photographs; PNG, GIF and BMP are drawn pictures.
    private static bool IsLossy(byte[] b) =>
        b[0] == 0xFF || b[0] == 'R' && b.Length >= 16 && !(b[12] == 'V' && b[13] == 'P' && b[14] == '8' && b[15] == 'L');

    // WebP through the SkiaSharp that Avalonia already bundles; PNG or JPEG q80 when this build cannot encode WebP.
    private static byte[] Encode(Bitmap picture, bool lossless)
    {
        var size = picture.PixelSize;
        var info = new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var pixels = new byte[info.RowBytes * info.Height];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            picture.CopyPixels(new PixelRect(size), handle.AddrOfPinnedObject(), pixels.Length, info.RowBytes);
            using var pixmap = new SKPixmap(info, handle.AddrOfPinnedObject(), info.RowBytes);
            using var stream = new SKDynamicMemoryWStream();
            var options = new SKWebpEncoderOptions(
                lossless ? SKWebpEncoderCompression.Lossless : SKWebpEncoderCompression.Lossy, lossless ? 100 : LossyQuality);
            if (pixmap.Encode(stream, options)) return stream.CopyToData().ToArray();
        }
        finally
        {
            handle.Free();
        }
        using var fallback = new MemoryStream();
        picture.Save(fallback, lossless ? PngBitmapEncoderOptions.Default : new JpegBitmapEncoderOptions { Quality = LossyQuality });
        return fallback.ToArray();
    }

    // The pixels a PNG, JPEG, GIF, BMP or WebP header declares, or 0 when it declares none this can read.
    private static long DeclaredPixels(byte[] b)
    {
        if (PictureSniffer.ReadDeclared(b) is { } known) return (long)known.Width * known.Height;
        if (b[0] == 'G') return (long)(b[6] | b[7] << 8) * (b[8] | b[9] << 8);
        if (b[0] == 'B' && b.Length >= 26)
            return Math.Abs((long)BitConverter.ToInt32(b, 18)) * Math.Abs((long)BitConverter.ToInt32(b, 22));
        return 0;
    }

    /// <summary>The EXIF orientation (1 to 8) of a JPEG, 1 when it has none or it cannot be read.</summary>
    public static int ExifOrientation(byte[] jpeg)
    {
        var at = 2;
        while (at + 4 <= jpeg.Length && jpeg[at] == 0xFF)
        {
            var marker = jpeg[at + 1];
            if (marker == 0xFF) { at++; continue; }
            if (marker is 0x01 or (>= 0xD0 and <= 0xD8)) { at += 2; continue; }
            if (marker is 0xDA or 0xD9) return 1;
            var length = jpeg[at + 2] << 8 | jpeg[at + 3];
            var start = at + 4;
            if (marker == 0xE1 && length >= 8 && start + 6 <= jpeg.Length && jpeg[start] == 'E' && jpeg[start + 1] == 'x' &&
                jpeg[start + 2] == 'i' && jpeg[start + 3] == 'f' && jpeg[start + 4] == 0)
                return OrientationInTiff(jpeg, start + 6, Math.Min(jpeg.Length, at + 2 + length));
            if (length < 2) return 1;
            at += 2 + length;
        }
        return 1;
    }

    private static int OrientationInTiff(byte[] b, int tiff, int end)
    {
        if (tiff + 8 > end) return 1;
        var little = b[tiff] == 'I' && b[tiff + 1] == 'I';
        if (!little && !(b[tiff] == 'M' && b[tiff + 1] == 'M')) return 1;
        int U16(int at) => little ? b[at] | b[at + 1] << 8 : b[at] << 8 | b[at + 1];
        long U32(int at) => little
            ? b[at] | (long)b[at + 1] << 8 | (long)b[at + 2] << 16 | (long)b[at + 3] << 24
            : (long)b[at] << 24 | (long)b[at + 1] << 16 | (long)b[at + 2] << 8 | b[at + 3];
        var ifd = tiff + U32(tiff + 4);
        if (ifd < tiff || ifd + 2 > end) return 1;
        var count = U16((int)ifd);
        for (var index = 0; index < count; index++)
        {
            var entry = (int)ifd + 2 + index * 12;
            if (entry + 12 > end) return 1;
            if (U16(entry) != 0x0112) continue;
            var value = U16(entry + 8);
            return value is >= 1 and <= 8 ? value : 1;
        }
        return 1;
    }

    // Writes the picture turned upright for an EXIF orientation of 2 to 8.
    private static WriteableBitmap Turned(Bitmap picture, int orientation)
    {
        var width = picture.PixelSize.Width;
        var height = picture.PixelSize.Height;
        using var from = new WriteableBitmap(picture.PixelSize, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var frame = from.Lock()) picture.CopyPixels(new PixelRect(picture.PixelSize), frame.Address, frame.RowBytes * height, frame.RowBytes);
        var swapped = orientation >= 5;
        var turned = new WriteableBitmap(new PixelSize(swapped ? height : width, swapped ? width : height),
            new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using var source = from.Lock();
        using var target = turned.Lock();
        var sourcePixels = new byte[source.RowBytes * height];
        System.Runtime.InteropServices.Marshal.Copy(source.Address, sourcePixels, 0, sourcePixels.Length);
        var targetPixels = new byte[target.RowBytes * target.Size.Height];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var (dx, dy) = orientation switch
                {
                    2 => (width - 1 - x, y),
                    3 => (width - 1 - x, height - 1 - y),
                    4 => (x, height - 1 - y),
                    5 => (y, x),
                    6 => (height - 1 - y, x),
                    7 => (height - 1 - y, width - 1 - x),
                    _ => (y, width - 1 - x),
                };
                Buffer.BlockCopy(sourcePixels, y * source.RowBytes + x * 4, targetPixels, dy * target.RowBytes + dx * 4, 4);
            }
        System.Runtime.InteropServices.Marshal.Copy(targetPixels, 0, target.Address, targetPixels.Length);
        return turned;
    }

    // The kinds a person's picture files come in: PNG, JPEG, GIF, BMP and WebP.
    private static bool LooksLikeAPicture(byte[] b) =>
        b.Length >= 12 && (
            b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 ||
            b[0] == 0xFF && b[1] == 0xD8 ||
            b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8' ||
            b[0] == 'B' && b[1] == 'M' ||
            b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P');
}
