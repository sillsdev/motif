using Avalonia;
using Avalonia.Media.Imaging;
using SIL.Motif.Commands;

namespace SIL.Motif.App.Services;

/// <summary>
/// Scales a picture to fit <see cref="ProposalPictures.MaxBytes"/> with Avalonia's imaging, the only place a picture
/// is decoded. A PNG or JPEG already small enough is kept byte for byte; anything else is decoded and written as a PNG
/// (a JPEG stays a JPEG), made smaller step by step until it fits.
/// </summary>
public static class PictureScaler
{
    /// <summary>The longest side a scaled picture keeps, in pixels.</summary>
    public const int LongestSide = 2048;

    // Leaves room under the cap so one more encode is never the one that overflows.
    private const double Target = 0.9;
    private const int MaxAttempts = 12;

    /// <summary>Scales <paramref name="source"/>; the result says why when it cannot.</summary>
    public static PictureIntakeResult Scale(byte[] source, long maxBytes = ProposalPictures.MaxBytes)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!LooksLikeAPicture(source)) return new PictureIntakeResult(PictureIntakeStatus.Unsupported);
        if (source.Length <= maxBytes && PictureSniffer.Read(source) is { } known &&
            Math.Max(known.Width, known.Height) <= LongestSide)
            return new PictureIntakeResult(PictureIntakeStatus.Ready, source);
        try
        {
            using var decoded = new Bitmap(new MemoryStream(source, writable: false));
            var size = decoded.PixelSize;
            if (size.Width < 1 || size.Height < 1) return new PictureIntakeResult(PictureIntakeStatus.Unreadable);
            // A photograph keeps its JPEG form, which stays small; anything else is written as a PNG.
            BitmapEncoderOptions options = source[0] == 0xFF ? new JpegBitmapEncoderOptions { Quality = 85 }
                : PngBitmapEncoderOptions.Default;
            var scale = Math.Min(1.0, (double)LongestSide / Math.Max(size.Width, size.Height));
            for (var attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var width = Math.Max(1, (int)Math.Round(size.Width * scale));
                var height = Math.Max(1, (int)Math.Round(size.Height * scale));
                using var scaled = width == size.Width && height == size.Height
                    ? null : decoded.CreateScaledBitmap(new PixelSize(width, height), BitmapInterpolationMode.HighQuality);
                using var output = new MemoryStream();
                (scaled ?? decoded).Save(output, options);
                if (output.Length <= maxBytes * Target)
                    return new PictureIntakeResult(PictureIntakeStatus.Ready, output.ToArray());
                if (width == 1 && height == 1) break;
                // PNG size follows pixel count, so shrink the sides by the square root of the overshoot.
                scale *= Math.Min(0.85, Math.Sqrt(maxBytes * Target / output.Length));
            }
            return new PictureIntakeResult(PictureIntakeStatus.TooLarge);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException
                                              or NotSupportedException)
        {
            return new PictureIntakeResult(PictureIntakeStatus.Unreadable);
        }
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
