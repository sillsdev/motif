using System.Globalization;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Writes PanGloss's measured parse times the same way on every page, so the Overview's Speed tile and Timing's
/// headline read as one number.
/// </summary>
public static class SpeedText
{
    /// <summary>A total parse time: milliseconds under a tenth of a second, then seconds, to one decimal under ten.</summary>
    /// <param name="milliseconds">The summed per-word parse time.</param>
    public static string Duration(double milliseconds) => milliseconds switch
    {
        < 100 => milliseconds.ToString("N0", CultureInfo.CurrentCulture) + " ms",
        < 10_000 => (milliseconds / 1000).ToString("N1", CultureInfo.CurrentCulture) + " s",
        _ => (milliseconds / 1000).ToString("N0", CultureInfo.CurrentCulture) + " s",
    };

    /// <summary>One word's parse time: to one decimal under ten milliseconds unless it is whole.</summary>
    /// <param name="milliseconds">A per-word parse time or percentile.</param>
    public static string PerWord(double milliseconds) =>
        (milliseconds >= 10 || Math.Abs(milliseconds - Math.Round(milliseconds)) < 0.05
            ? milliseconds.ToString("N0", CultureInfo.CurrentCulture)
            : milliseconds.ToString("N1", CultureInfo.CurrentCulture)) + " ms";

    /// <summary>A count with its noun, singular for one.</summary>
    /// <param name="count">How many.</param>
    /// <param name="one">The noun for one.</param>
    /// <param name="many">The noun for any other count.</param>
    public static string Count(int count, string one, string many) =>
        count.ToString("N0", CultureInfo.CurrentCulture) + " " + (count == 1 ? one : many);
}
