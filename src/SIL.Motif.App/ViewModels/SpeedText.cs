using System.Globalization;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Writes the counts the Overview's Speed tile and Timing's headline share the same way on every page, so they
/// read as one number.
/// </summary>
public static class SpeedText
{
    /// <summary>A count with its noun, singular for one.</summary>
    /// <param name="count">How many.</param>
    /// <param name="one">The noun for one.</param>
    /// <param name="many">The noun for any other count.</param>
    public static string Count(int count, string one, string many) =>
        count.ToString("N0", CultureInfo.CurrentCulture) + " " + (count == 1 ? one : many);
}
