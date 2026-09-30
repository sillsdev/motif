using System.Globalization;

namespace SIL.Motif.Worker.Jobs;

/// <summary>Formats durable job timestamps so their stored text preserves UTC time order.</summary>
public static class JobTimestamp
{
    /// <summary>Returns a seven-digit fractional UTC timestamp with a fixed-width <c>Z</c> suffix.</summary>
    public static string FormatUtc(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
