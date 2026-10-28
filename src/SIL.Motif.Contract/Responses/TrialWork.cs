namespace SIL.Motif.Contract.Responses;

/// <summary>A word whose completed Trial required more parser work than its Baseline.</summary>
public sealed record TrialWorkIncrease(string Word, long BaselineWork, long TrialWork, double PercentMore);

/// <summary>Parser work for the Trial's word list and the limits that stopped its searches.</summary>
public sealed record TrialWork(
    int WordCount, long? BaselineWork, long? TrialWorkSteps, double? PercentChange,
    IReadOnlyList<TrialWorkIncrease> IncreasedWords, int WorkLimitStops, int TimeSafetyNetStops)
{
    /// <summary>Trial words stopped by the work or attempt cap.</summary>
    public IReadOnlyList<string> WorkLimitWords { get; init; } = [];
    /// <summary>Trial words stopped by the time safety net.</summary>
    public IReadOnlyList<string> TimeSafetyNetWords { get; init; } = [];
}
