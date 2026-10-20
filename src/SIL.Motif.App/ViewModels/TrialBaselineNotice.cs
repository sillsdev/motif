namespace SIL.Motif.App.ViewModels;

/// <summary>The window's words for a Trial that was queued without a Baseline to compare against.</summary>
public static class TrialBaselineNotice
{
    /// <summary>
    /// The notice for <paramref name="baselineWarning"/>, the reason the shared Trial workflow gave, or
    /// <see langword="null"/> when there is no warning and so nothing to show.
    /// </summary>
    public static string? For(string? baselineWarning) =>
        string.IsNullOrWhiteSpace(baselineWarning)
            ? null
            : "This Trial was not compared against a Baseline, so it will show no Difference. " +
              baselineWarning.Trim();
}
