namespace SIL.Motif.Contract.Responses;

/// <summary>A word whose completed Trial required more parser work than its Baseline.</summary>
/// <param name="Word">The word.</param>
/// <param name="BaselineWork">The work steps the Baseline spent on the word.</param>
/// <param name="TrialWork">The work steps the Trial spent on the same word.</param>
/// <param name="PercentMore">How much more work the Trial needed, as a percentage of the Baseline's.</param>
public sealed record TrialWorkIncrease(string Word, long BaselineWork, long TrialWork, double PercentMore)
{
    /// <summary>The Trial's work divided by the Baseline's, such as 1.18 for 18% more work.</summary>
    public double Ratio => BaselineWork > 0 ? (double)TrialWork / BaselineWork : 0;
}

/// <summary>Parser work for the Trial's word list and the limits that stopped its searches.</summary>
public sealed record TrialWork(
    int WordCount, long? BaselineWork, long? TrialWorkSteps, double? PercentChange,
    IReadOnlyList<TrialWorkIncrease> IncreasedWords, int WorkLimitStops, int TimeSafetyNetStops)
{
    /// <summary>Whether completed words required more than 5% additional parser work.</summary>
    public bool WorkIncreaseWarning => PercentChange is > 5;

    /// <summary>
    /// Trial words stopped by the work or attempt limit, which stops the same words on every computer. A word where
    /// the time safety net also fired is listed here, because the limit alone would have stopped it.
    /// </summary>
    public IReadOnlyList<string> WorkLimitWords { get; init; } = [];
    /// <summary>Trial words stopped only by the time safety net, which depends on how fast this computer is.</summary>
    public IReadOnlyList<string> TimeSafetyNetWords { get; init; } = [];

    /// <summary>The Baseline's total word time on this computer, in milliseconds, when every Trial word has one.</summary>
    public double? BaselineTimeMs { get; init; }
    /// <summary>The Trial's total word time on this computer, in milliseconds, for the same words.</summary>
    public double? TrialTimeMs { get; init; }
}
