using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace SIL.Motif.App.Views;

/// <summary>Turns a stored picture's bytes into the thumbnail's bitmap, decoded at thumbnail width.</summary>
public sealed class PictureBytesConverter : IValueConverter
{
    /// <summary>The one instance a view uses.</summary>
    public static readonly PictureBytesConverter Instance = new();

    /// <summary>How wide, in pixels, a thumbnail is decoded; it is drawn no larger than this.</summary>
    public const int ThumbnailWidth = 220;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not byte[] bytes) return null;
        try { return Bitmap.DecodeToWidth(new MemoryStream(bytes, writable: false), ThumbnailWidth); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException
                                              or NotSupportedException)
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
