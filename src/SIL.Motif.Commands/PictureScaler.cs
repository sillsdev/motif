using SkiaSharp;

namespace SIL.Motif.Commands;

/// <summary>Why a picture could not be scaled.</summary>
public enum PictureScaleStatus
{
    /// <summary>The picture was scaled; its bytes are in the result.</summary>
    Ready,
    /// <summary>The bytes are not a kind of picture Motif can use.</summary>
    Unsupported,
    /// <summary>The bytes look like a picture but cannot be decoded.</summary>
    Unreadable,
    /// <summary>The picture declares more pixels than are decoded, or cannot be made small enough.</summary>
    TooLarge,
}

/// <summary>The outcome of scaling a picture.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Bytes">The WebP copy; null unless <paramref name="Status"/> is Ready.</param>
public sealed record PictureScaleResult(PictureScaleStatus Status, byte[]? Bytes = null);

/// <summary>
/// Prepares a picture for a Proposal, the one place a picture is decoded (window and commands alike): upright by its
/// EXIF orientation, at most <see cref="LongestSide"/> pixels on the long side (never enlarged), stripped of metadata
/// by being re-drawn, and written as WebP, lossless for a PNG and lossy at quality 80 for a JPEG. The original is not kept.
/// </summary>
public static class PictureScaler
{
    /// <summary>The longest side a scaled picture keeps, in pixels.</summary>
    public const int LongestSide = ProposalPictures.MaxSide;

    /// <summary>The quality of a lossy WebP, and of the JPEG written when WebP cannot be.</summary>
    public const int LossyQuality = 80;

    /// <summary>The largest picture file read at all; a bigger one is refused as too large without being decoded.</summary>
    public const long MaxSourceBytes = 64L * 1024 * 1024;

    /// <summary>The most pixels a source may declare before it is decoded: about 100 megapixels, some 400 MB decoded.</summary>
    public const long MaxSourcePixels = 100_000_000;

    // Leaves room under the cap so one more encode is never the one that overflows.
    private const double Target = 0.9;
    private const int MaxAttempts = 12;

    /// <summary>Checks a source's type and declared size before decoding it.</summary>
    public static PictureScaleStatus ValidateSource(byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!LooksLikeAPicture(source)) return PictureScaleStatus.Unsupported;
        if (source.Length > MaxSourceBytes || DeclaredPixels(source) > MaxSourcePixels) return PictureScaleStatus.TooLarge;
        return PictureScaleStatus.Ready;
    }

    /// <summary>Scales <paramref name="source"/>; the result says why when it cannot.</summary>
    public static PictureScaleResult Scale(byte[] source, long maxBytes = ProposalPictures.MaxBytes)
    {
        var validation = ValidateSource(source);
        if (validation != PictureScaleStatus.Ready) return new PictureScaleResult(validation);
        try
        {
            using var codec = SKCodec.Create(SKData.CreateCopy(source));
            if (codec is null || codec.Info.Width < 1 || codec.Info.Height < 1)
                return new PictureScaleResult(PictureScaleStatus.Unreadable);
            if ((long)codec.Info.Width * codec.Info.Height > MaxSourcePixels)
                return new PictureScaleResult(PictureScaleStatus.TooLarge);
            using var decoded = SKBitmap.Decode(codec, new SKImageInfo(codec.Info.Width, codec.Info.Height,
                SKColorType.Bgra8888, SKAlphaType.Premul));
            if (decoded is null) return new PictureScaleResult(PictureScaleStatus.Unreadable);
            using var image = SKImage.FromBitmap(decoded);
            var lossless = !IsLossy(source);
            // A phone photograph is stored sideways with a tag saying how to turn it; Skia's decoder ignores the tag.
            var orientation = source[0] == 0xFF ? ExifOrientation(source) : 1;
            var swapped = orientation >= 5;
            var uprightWidth = swapped ? decoded.Height : decoded.Width;
            var uprightHeight = swapped ? decoded.Width : decoded.Height;
            var scale = Math.Min(1.0, (double)LongestSide / Math.Max(uprightWidth, uprightHeight));
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var width = Math.Max(1, (int)Math.Round(uprightWidth * scale));
                var height = Math.Max(1, (int)Math.Round(uprightHeight * scale));
                var encoded = Encode(image, orientation, uprightWidth, uprightHeight, width, height, lossless);
                if (encoded.Length <= maxBytes * Target) return new PictureScaleResult(PictureScaleStatus.Ready, encoded);
                if (width == 1 && height == 1) break;
                // Size follows pixel count, so shrink the sides by the square root of the overshoot.
                scale *= Math.Min(0.85, Math.Sqrt(maxBytes * Target / encoded.Length));
            }
            return new PictureScaleResult(PictureScaleStatus.TooLarge);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException
                                              or NotSupportedException or OutOfMemoryException or DllNotFoundException
                                              or TypeInitializationException)
        {
            return new PictureScaleResult(PictureScaleStatus.Unreadable);
        }
    }

    // A JPEG, and a WebP that is not the lossless kind, are photographs; PNG, GIF and BMP are drawn pictures.
    private static bool IsLossy(byte[] b) =>
        b[0] == 0xFF || b[0] == 'R' && b.Length >= 16 && !(b[12] == 'V' && b[13] == 'P' && b[14] == '8' && b[15] == 'L');

    // Draws the picture upright and scaled, then encodes it as WebP (PNG or JPEG q80 if WebP is unavailable).
    private static byte[] Encode(SKImage image, int orientation, int uprightWidth, int uprightHeight, int width, int height,
        bool lossless)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul))
            ?? throw new InvalidOperationException("No surface for a picture of this size.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale((float)width / uprightWidth, (float)height / uprightHeight);
        canvas.Concat(Upright(orientation, image.Width, image.Height));
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var drawn = surface.Snapshot();
        using var pixmap = drawn.PeekPixels();
        using var stream = new SKDynamicMemoryWStream();
        var options = new SKWebpEncoderOptions(
            lossless ? SKWebpEncoderCompression.Lossless : SKWebpEncoderCompression.Lossy, lossless ? 100 : LossyQuality);
        if (pixmap.Encode(stream, options)) return stream.CopyToData().ToArray();
        using var fallback = drawn.Encode(lossless ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg,
            lossless ? 100 : LossyQuality);
        return fallback.ToArray();
    }

    // The matrix that carries a w x h picture to upright for an EXIF orientation of 1 to 8.
    private static SKMatrix Upright(int orientation, int w, int h) => orientation switch
    {
        2 => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
        3 => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
        4 => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
        5 => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        6 => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
        7 => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
        8 => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
        _ => SKMatrix.Identity,
    };

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

    // The kinds a person's picture files come in: PNG, JPEG, GIF, BMP and WebP.
    private static bool LooksLikeAPicture(byte[] b) =>
        b.Length >= 12 && (
            b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 ||
            b[0] == 0xFF && b[1] == 0xD8 ||
            b[0] == 'G' && b[1] == 'I' && b[2] == 'F' && b[3] == '8' ||
            b[0] == 'B' && b[1] == 'M' ||
            b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F' && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P');
}
