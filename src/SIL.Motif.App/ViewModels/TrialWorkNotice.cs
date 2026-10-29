using System.Globalization;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The window's note that a Trial makes parsing more work than the Baseline. It is shown only when the rise
/// passes the threshold that <see cref="TrialWork.WorkIncreaseWarning"/> sets, and it never blocks or
/// disables anything.
/// </summary>
public static class TrialWorkNotice
{
    /// <summary>
    /// The note for <paramref name="work"/>, with the rise rounded up to a whole percent, or
    /// <see langword="null"/> when there is nothing to show: no Trial work, a missing total, or a rise that
    /// does not pass the threshold.
    /// </summary>
    /// <param name="work">
    /// The Trial's parsing work measured against the Baseline's, if the window has it.
    /// </param>
    public static string? For(TrialWork? work)
    {
        if (work is not { BaselineWork: not null, TrialWorkSteps: not null, PercentChange: { } change })
            return null;
        if (!work.WorkIncreaseWarning) return null;
        return string.Format(CultureInfo.CurrentCulture, MotifAppStrings.TrialWorkNotice,
            (int)Math.Ceiling(change));
    }
}
