using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host.Assess;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// The one rule for how a parse run is stopped, shared by Assess and Trial: the Selection's deterministic
/// step cap decides, and a wall-clock limit exists only as a generous safety net against a hung parser.
/// </summary>
public static class ParseRunLimits
{
    /// <summary>
    /// How much longer the safety net is than the time the step cap is estimated to need on this computer, on top
    /// of the allowance <see cref="StepLimitEstimator"/> already makes, so a slow or busy computer reaches the
    /// step cap first and the stop does not depend on its speed.
    /// </summary>
    public const int SafetyNetFactor = 10;

    /// <summary>Resolves a Selection's limits into the step cap and the time safety net a run is given.</summary>
    /// <param name="limits">The Selection's saved limits; an explicit time the user chose is kept as typed.</param>
    /// <param name="rate">The measured or typical parser rate used to size an estimated safety net.</param>
    public static TrialLimits Resolve(SelectionParsingLimits limits, ParserStepRate rate)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(rate);
        if (limits.PerWordStepLimit.IsUnbounded) return new TrialLimits(null, limits.PerWordStepLimit);
        var time = limits.TimeMode == SelectionTimeLimitMode.Explicit && limits.ExplicitPerWordLimitMs is { } explicitMs
            ? TimeSpan.FromMilliseconds(explicitMs)
            : SafetyNetTime(limits.PerWordStepLimit, rate);
        return new TrialLimits(time, limits.PerWordStepLimit);
    }

    /// <summary>The estimated-mode safety net for a step cap, or null when the cap is unbounded or too large to size.</summary>
    public static TimeSpan? SafetyNetTime(StepCap stepLimit, ParserStepRate rate)
    {
        var estimate = StepLimitEstimator.Calculate(stepLimit, rate);
        if (estimate?.PerWordTimeLimitMs is not { } milliseconds) return null;
        try { return TimeSpan.FromMilliseconds(checked((double)milliseconds * SafetyNetFactor)); }
        catch (OverflowException) { return null; }
    }

    /// <summary>The parser rate the project's latest parse-time Assessment measured, or the typical rate.</summary>
    internal static ParserStepRate ReadRate(AssessmentRepository assessments)
    {
        var latest = assessments.ListByKind(AssessmentKind.ParseTime.ToStoredKind()).LastOrDefault();
        return StepLimitEstimator.FromAssessment(latest is null ? null : assessments.Get(latest.AssessmentId));
    }
}
