using System.Globalization;

namespace SIL.Motif.App.ViewModels;

/// <summary>Which sides of a before-and-after number have a value.</summary>
internal enum PairedSide
{
    Both,
    TodayOnly,
    ProposalOnly,
}

/// <summary>The tooltip and accessible name that say which side of a before-and-after number is missing.</summary>
internal static class PairedSideNote
{
    public static string? For(PairedSide side, string shown) => side switch
    {
        PairedSide.TodayOnly => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.PairedProposalNotMeasuredNote, shown),
        PairedSide.ProposalOnly => string.Format(CultureInfo.CurrentCulture, MotifAppStrings.PairedTodayNotMeasuredNote, shown),
        _ => null,
    };
}
